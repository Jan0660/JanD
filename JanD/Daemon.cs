using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JanD
{
    public static class Daemon
    {
        public static bool NotSaved = false;
        public static List<JanDProcess> Processes;
        public static Config Config;

        public static async Task Start()
        {
            void DaemonLog(string str)
                => Console.WriteLine(Ansi.ForegroundColor(str, 0, 247, 247));

            DaemonLog("Starting daemon in " + Directory.GetCurrentDirectory());
            try
            {
                var json = File.ReadAllText("./config.json");
                Config = JsonSerializer.Deserialize<Config>(json);
            }
            catch
            {
                Config = new()
                {
                    Processes = new JanDProcess[0]
                };
            }

            DaemonLog($"Starting with {Config.Processes.Length} processes.");
            if (!Directory.Exists("./logs"))
                Directory.CreateDirectory("./logs");
            foreach (var proc in Config.Processes)
            {
                if (proc.Enabled)
                    proc.Start();
            }

            JanDProcess GetProcess(string name, bool throwOnNotFound = true)
            {
                var proc = Processes.FirstOrDefault(p => p.Name == name);
                if (proc == null)
                {
                    if (throwOnNotFound)
                        throw new("invalid-process");
                }

                return proc;
            }


            void NewPipeServer()
            {
                var pipeServer = new NamedPipeServerStream(Program.PipeName, PipeDirection.InOut, 250);
                // pipeServer.ReadMode = PipeTransmissionMode.Message;
                pipeServer.BeginWaitForConnection(state =>
                {
                    DaemonLog("IPC CONNECTION h!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                    var bytes = new byte[10000];
                    AsyncCallback callback = null;
                    callback = state =>
                    {
                        var count = pipeServer.EndRead(state);
                        if (count == 0)
                        {
                            Console.WriteLine("Read returned zero bytes, disconnecting connection.");
                            pipeServer.Disconnect();
                            pipeServer.Dispose();
                            return;
                        }

                        try
                        {
                            DaemonLog("receive'd ip[[c: " + Encoding.UTF8.GetString(bytes.AsSpan()[..count]));
                            var packet = JsonSerializer.Deserialize<IpcPacket>(bytes.AsSpan()[..count]);
                            switch (packet.Type)
                            {
                                case "write":
                                    Console.WriteLine(packet.Data);
                                    break;
                                case "exit":
                                    Environment.Exit(0);
                                    break;
                                case "status":
                                {
                                    var j = new Utf8JsonWriter(pipeServer);
                                    j.WriteStartObject();
                                    j.WriteNumber("Processes", Processes.Count);
                                    j.WriteBoolean("NotSaved", NotSaved);
                                    j.WriteString("Directory", Directory.GetCurrentDirectory());
                                    j.WriteEndObject();
                                    j.Flush();
                                    break;
                                }
                                case "set-enabled":
                                {
                                    var separatorIndex = packet.Data.IndexOf(':');
                                    var proc = Processes.FirstOrDefault(p =>
                                        p.Name == packet.Data[..separatorIndex]);
                                    if (proc == null)
                                    {
                                        pipeServer.Write("ERR:invalid-process");
                                        return;
                                    }

                                    proc.Enabled = Boolean.Parse(packet.Data[(separatorIndex + 1)..]);
                                    pipeServer.Write(proc.Enabled.ToString());
                                    NotSaved = true;
                                    break;
                                }
                                case "get-process-info":
                                {
                                    var proc = GetProcess(packet.Data);

                                    var j = new Utf8JsonWriter(pipeServer);
                                    j.WriteStartObject();
                                    j.WriteProcessInfo(proc);
                                    j.WriteEndObject();
                                    j.Flush();
                                    break;
                                }
                                case "stop-process":
                                {
                                    var proc = GetProcess(packet.Data);

                                    if (proc.Process == null)
                                    {
                                        pipeServer.Write("already-stopped");
                                    }
                                    else
                                    {
                                        NotSaved = true;
                                        proc.Stopped = true;
                                        proc.Process.Kill();
                                        pipeServer.Write("killed");
                                    }

                                    break;
                                }
                                case "restart-process":
                                {
                                    var proc = GetProcess(packet.Data);

                                    if (proc.Process != null)
                                        proc.Stop();
                                    proc.Start();
                                    proc.Stopped = false;
                                    pipeServer.Write(Encoding.UTF8.GetBytes("done"));
                                    break;
                                }
                                case "new-process":
                                {
                                    var def = JsonSerializer.Deserialize<JanDNewProcess>(packet.Data);
                                    if (Processes.Any(p => p.Name == def.Name))
                                    {
                                        pipeServer.Write("ERR:already-exists");
                                        return;
                                    }

                                    JanDProcess proc = new()
                                    {
                                        Name = def.Name,
                                        Command = def.Command,
                                        WorkingDirectory = def.WorkingDirectory,
                                        AutoRestart = true,
                                        Enabled = true
                                    };
                                    Processes.Add(proc);
                                    NotSaved = true;
                                    pipeServer.Write("added");
                                    break;
                                }
                                case "start-process":
                                {
                                    var proc = GetProcess(packet.Data);

                                    if (proc.Process != null)
                                    {
                                        pipeServer.Write("ERR:already-started");
                                        return;
                                    }

                                    proc.Stopped = false;
                                    proc.Start();
                                    pipeServer.Write("done");
                                    break;
                                }
                                case "save-config":
                                {
                                    Config.Processes = Processes.ToArray();
                                    var json = JsonSerializer.Serialize(Config);
                                    File.WriteAllText("./config.json", json);
                                    NotSaved = false;
                                    pipeServer.Write("done");
                                    break;
                                }
                                case "get-process-list":
                                {
                                    pipeServer.Write(JsonSerializer.SerializeToUtf8Bytes(Processes.ToArray()));
                                    break;
                                }
                                case "get-processes":
                                {
                                    var writer = new Utf8JsonWriter(pipeServer);
                                    writer.WriteStartArray();
                                    foreach (var process in Processes)
                                    {
                                        writer.WriteStartObject();
                                        writer.WriteProcessInfo(process);
                                        writer.WriteEndObject();
                                    }

                                    writer.WriteEndArray();
                                    writer.Flush();
                                    break;
                                }
                                case "delete-process":
                                {
                                    var proc = GetProcess(packet.Data);

                                    proc.Stopped = true;
                                    proc.Process?.Kill();
                                    Processes.Remove(proc);
                                    NotSaved = true;
                                    pipeServer.Write("done");
                                    break;
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            pipeServer.Write("ERR:" + exception.Message + '\n' + exception.StackTrace);
                        }

                        PipeRead();
                    };

                    void PipeRead()
                    {
                        if (!pipeServer.IsConnected)
                        {
                            Console.WriteLine("IPC disconnected.");
                            return;
                        }

                        pipeServer.BeginRead(bytes, 0, bytes.Length, callback, pipeServer);
                    }

                    PipeRead();
                    NewPipeServer();
                }, new object());
            }

            Processes = Config.Processes.ToList();
            NewPipeServer();
            await Task.Delay(-1);
        }

        public class JanDNewProcess
        {
            public string Name { get; set; }
            public string Command { get; set; }
            public string WorkingDirectory { get; set; }

            public JanDNewProcess(string name, string command, string workingDirectory) =>
                (Name, Command, WorkingDirectory) = (name, command, workingDirectory);
        }
    }

    public class DaemonStatus
    {
        public int Processes { get; set; }
        public bool NotSaved { get; set; }
        public string Directory { get; set; }
    }
}