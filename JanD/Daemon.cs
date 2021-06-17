using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable 4014

namespace JanD
{
    public static class Daemon
    {
        public static bool NotSaved;
        public static List<JanDProcess> Processes;
        public static Config Config;
        public static StreamWriter DaemonLogWriter;
        public static readonly CancellationTokenSource CancellationTokenSource = new();
        public static readonly List<DaemonConnection> Connections = new();

        public static readonly Regex ProcessNameValidationRegex =
            new("^(?!(-|[0-9]|\\/))([A-z]|[0-9]|_|-|\\.|@|#|\\/)+$");

        #region util

        public static JanDProcess GetProcess(string name, bool throwOnNotFound = true)
        {
            var proc = Processes.FirstOrDefault(p => p.Name == name);
            if (proc == null)
            {
                if (throwOnNotFound)
                    throw new DaemonException("invalid-process");
            }

            return proc;
        }

        /// <summary>
        /// write in cyan and write to file if enabled
        /// </summary>
        /// <param name="str"></param>
        public static void DaemonLog(string str)
        {
            str = Ansi.ForegroundColor(str, 0, 247, 247);
            DaemonLogWriter?.WriteLine(str);
            Console.WriteLine(str);
        }

        #endregion

        public static async Task Start()
        {
            DaemonLog("Starting daemon in " + Directory.GetCurrentDirectory());
            DaemonLog($"With JAND_PIPE: {Program.PipeName}");
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

            if (Config!.DaemonLogSave)
                DaemonLogWriter = new StreamWriter(new FileStream("./daemon.log", FileMode.OpenOrCreate,
                    FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };

            DaemonLog($"Starting with {Config.Processes.Length} processes.");
            if (!Directory.Exists("./logs"))
                Directory.CreateDirectory("./logs");
            foreach (var proc in Config.Processes)
            {
                try
                {
                    if (proc.Enabled)
                        proc.Start();
                }
                catch (Exception exc)
                {
                    Console.WriteLine($"Starting {proc.Name} failed: {exc.Message}");
                }
            }


            void NewPipeServer()
            {
                var pipeServer = new NamedPipeServerStream(Program.PipeName, PipeDirection.InOut, 250,
                    OperatingSystem.IsWindows() ? PipeTransmissionMode.Message : PipeTransmissionMode.Byte);
                pipeServer.BeginWaitForConnection(_ =>
                    {
                        if (Config.LogIpc)
                            DaemonLog("IPC connected.");
                        var connection = new DaemonConnection(pipeServer);
                        Connections.Add(connection);
                        var bytes = new byte[10000];
                        AsyncCallback callback = null;
                        callback = state =>
                        {
                            int count = 0;
                            try
                            {
                                count = pipeServer.EndRead(state);
                            }
                            catch
                            {
                                // death has happened, the (count == 0) if statement will take care of cleanup
                            }

                            if (count == 0)
                            {
                                pipeServer.Disconnect();
                                Connections.TryRemove(connection);
                                pipeServer.Dispose();
                                return;
                            }

                            try
                            {
                                try
                                {
                                    HandlePacket(pipeServer, bytes, count, connection);
                                }
                                catch (DaemonException exception)
                                {
                                    pipeServer.Write("ERR:" + exception.Message);
                                }
                                catch (Exception exception)
                                {
                                    pipeServer.Write("ERR:" + exception.Message + '\n' + exception.StackTrace);
                                }
                            }
                            catch
                            {
                                // pipe broke and exception writes above threw an exception...
                            }

                            PipeRead();
                        };

                        void PipeRead()
                        {
                            if (!pipeServer.IsConnected)
                            {
                                Connections.TryRemove(connection);
                                pipeServer.Dispose();
                                return;
                            }

                            pipeServer.BeginRead(bytes, 0, bytes.Length, callback, pipeServer);
                        }

                        PipeRead();
                        NewPipeServer();
                    },
                    new object());
            }

            Processes = Config.Processes.ToList();
            NewPipeServer();
            try
            {
                await Task.Delay(-1, CancellationTokenSource.Token);
            }
            finally
            {
                Console.WriteLine("Exit requested. Killing all processes.");
                foreach (var process in Processes)
                    process?.Process?.Kill(true);
            }
        }

        public static void HandlePacket(NamedPipeServerStream pipeServer, byte[] bytes, int count,
            DaemonConnection connection)
        {
            if (Config.LogIpc)
                DaemonLog("Received IPC: " + Encoding.UTF8.GetString(bytes.AsSpan()[..count]));
            var packet = JsonSerializer.Deserialize<IpcPacket>(bytes.AsSpan()[..count]);
            switch (packet!.Type)
            {
                case "ping":
                    Console.WriteLine("ping");
                    pipeServer.Write("pong");
                    break;
                case "write":
                    Console.WriteLine(packet.Data);
                    break;
                case "exit":
                    Console.WriteLine("Exit requested. Killing all processes.");
                    foreach (var process in Processes)
                        process?.Process?.Kill(true);
                    Environment.Exit(0);
                    break;
                case "status":
                {
                    var j = new Utf8JsonWriter(pipeServer);
                    j.WriteStartObject();
                    j.WriteNumber("Processes", Processes.Count);
                    j.WriteBoolean("NotSaved", NotSaved);
                    j.WriteString("Directory", Directory.GetCurrentDirectory());
                    j.WriteString("Version", ThisAssembly.Info.Version);
                    j.WriteEndObject();
                    j.Flush();
                    break;
                }
                case "rename-process":
                {
                    var val1 = packet.Data[..packet.Data.IndexOf(':')];
                    var val2 = packet.Data[(packet.Data.IndexOf(':') + 1)..];
                    if (Processes.Any(p => p.Name == val2))
                        throw new DaemonException("already-exists");
                    if (!ProcessNameValidationRegex.IsMatch(val2))
                        throw new DaemonException("Process name doesn't pass verification regex.");

                    var proc = GetProcess(val1);
                    proc.Name = val2;
                    NotSaved = true;
                    pipeServer.Write("done");
                    ProcessEventAsync(DaemonEvents.ProcessRenamed, val1, val2);
                    break;
                }
                case "send-process-stdin-line":
                    GetProcess(packet.Data[..packet.Data.IndexOf(':')]).Process.StandardInput
                        .WriteLine(packet.Data[(packet.Data.IndexOf(':') + 1)..]);
                    pipeServer.Write("done");
                    break;
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
                    ProcessPropertyUpdated(proc.Name, "Enabled", packet.Data[(separatorIndex + 1)..]);
                    break;
                }
                case "set-process-property":
                {
                    var req = JsonSerializer.Deserialize<SetPropertyIpcPacket>(packet.Data);
                    var process = GetProcess(req!.Process);
                    var property = typeof(JanDProcess).GetPropertyCaseInsensitive(req.Property);
                    if (property == null)
                    {
                        pipeServer.Write("Invalid property.");
                        return;
                    }

                    property.SetValueString(process, req.Data);
                    pipeServer.Write("done");
                    NotSaved = true;
                    ProcessPropertyUpdated(req.Process, req.Property, req.Data);
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

                    if (proc.Process == null && proc.Stopped)
                    {
                        pipeServer.Write("already-stopped");
                    }
                    else
                    {
                        proc.Stop();
                        pipeServer.Write("killed");
                    }

                    break;
                }
                case "restart-process":
                {
                    var proc = GetProcess(packet.Data);

                    if (proc.Process != null)
                        proc.Stop();
                    proc.CurrentUnstableRestarts = 0;
                    proc.Start();
                    proc.Stopped = false;
                    pipeServer.Write(Encoding.UTF8.GetBytes("done"));
                    break;
                }
                case "new-process":
                {
                    var def = JsonSerializer.Deserialize<JanDNewProcess>(packet.Data);
                    if (Processes.Any(p => p.Name == def!.Name))
                        throw new DaemonException("already-exists");

                    if (!ProcessNameValidationRegex.IsMatch(def!.Name))
                        throw new DaemonException("Process name doesn't pass verification regex.");

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
                    ProcessEventAsync(DaemonEvents.ProcessAdded, proc.Name);
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

                    proc.CurrentUnstableRestarts = 0;
                    proc.Stopped = false;
                    proc.Start();
                    pipeServer.Write("done");
                    break;
                }
                case "save-config":
                {
                    Config.Processes = Processes.ToArray();
                    var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions
                    {
                        WriteIndented = Config.FormatConfig
                    });
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
                    ProcessEventAsync(DaemonEvents.ProcessDeleted, proc.Name);
                    break;
                }
                case "subscribe-events":
                {
                    connection.Events =
                        (DaemonEvents)((int)connection.Events | int.Parse(packet.Data));
                    pipeServer.Write("done");
                    break;
                }
                case "unsubscribe-events":
                {
                    connection.Events =
                        (DaemonEvents)((int)connection.Events ^ int.Parse(packet.Data));
                    pipeServer.Write("done");
                    break;
                }
                case "subscribe-outlog-event":
                {
                    connection.OutLogSubs.Add(packet.Data);
                    pipeServer.Write("done");
                    break;
                }
                case "subscribe-errlog-event":
                {
                    connection.ErrLogSubs.Add(packet.Data);
                    pipeServer.Write("done");
                    break;
                }
                case "get-config":
                {
                    var writer = new Utf8JsonWriter(pipeServer);
                    writer.WriteStartObject();
                    writer.WriteBoolean("LogIpc", Config.LogIpc);
                    writer.WriteBoolean("FormatConfig", Config.FormatConfig);
                    writer.WriteNumber("MaxRestarts", Config.MaxRestarts);
                    writer.WriteBoolean("LogProcessOutput", Config.LogProcessOutput);
                    writer.WriteEndObject();
                    writer.Flush();
                    break;
                }
                case "set-config":
                {
                    var name = packet.Data[..packet.Data.IndexOf(':')];
                    var value = packet.Data[(packet.Data.IndexOf(':') + 1)..];
                    var property = typeof(Config).GetPropertyCaseInsensitive(name);
                    if (property == null)
                    {
                        pipeServer.Write("Option not found.");
                        return;
                    }

                    property.SetValueString(Config, value);
                    pipeServer.Write("done");
                    NotSaved = true;
                    break;
                }

                case "flush-all-logs":
                    foreach (var proc in Processes)
                    {
                        proc.OutWriter?.Flush();
                        proc.ErrWriter?.Flush();
                    }

                    pipeServer.Write("done");
                    break;
            }
        }

        public static Task ProcessPropertyUpdated(string processName, string propertyName, string newValue)
            => ProcessEventAsync(DaemonEvents.ProcessPropertyUpdated, processName, propertyName + ":" + newValue);

        public static async Task ProcessEventAsync(DaemonEvents daemonEvent, string processName, string data = null)
        {
            foreach (var connection in Connections)
            {
                if (!connection.Events.HasFlag(daemonEvent))
                    continue;
                try
                {
                    var writer = new Utf8JsonWriter(connection.Stream);
                    writer.WriteStartObject();
                    writer.WriteString("Event", daemonEvent.ToIpcString());
                    writer.WriteString("Process", processName);
                    if (data != null)
                        writer.WriteString("Value", data);
                    writer.WriteEndObject();
                    await writer.FlushAsync();
                }
                catch
                {
                    // connection interrupted, rest of the code will handle the cleanup...
                }
            }
        }

        public class JanDNewProcess
        {
            public string Name { get; set; }
            public string Command { get; set; }
            public string WorkingDirectory { get; set; }

            public JanDNewProcess(string name, string command, string workingDirectory) =>
                (Name, Command, WorkingDirectory) = (name, command, workingDirectory);
        }

        public class DaemonConnection
        {
            public NamedPipeServerStream Stream;
            public DaemonEvents Events;
            public List<string> OutLogSubs;
            public List<string> ErrLogSubs;

            public DaemonConnection(NamedPipeServerStream stream)
            {
                Stream = stream;
                Events = 0;
                OutLogSubs = new();
                ErrLogSubs = new();
            }
        }
    }

    public class DaemonStatus
    {
        public int Processes { get; set; }
        public bool NotSaved { get; set; }
        public string Directory { get; set; }
        public string Version { get; set; }
    }

    [Flags]
    public enum DaemonEvents
    {
        // outlog
        OutLog = 0b0000_0001,

        // errlog
        ErrLog = 0b0000_0010,

        // procstop
        ProcessStopped = 0b0000_0100,

        // procstart
        ProcessStarted = 0b0000_1000,

        // procadd
        ProcessAdded = 0b0001_0000,

        // procdel
        ProcessDeleted = 0b0010_0000,

        // procren
        ProcessRenamed = 0b0100_0000,

        // procprop
        ProcessPropertyUpdated = 0b1000_0000,
    }

    public class DaemonException : Exception
    {
        public DaemonException(string message) : base(message)
        {
        }
    }
}