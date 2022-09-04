using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable AccessToModifiedClosure

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
        public static int LastSafeIndex = -1;

        public static readonly Regex ProcessNameValidationRegex =
            new("^(?!(-|[0-9]|\\/))([A-z]|[0-9]|_|-|\\.|@|#|\\/)+$");

        #region util

        public static JanDProcess GetProcess(string name, bool throwOnNotFound = true)
        {
            var proc = Processes.FirstOrDefault(p => p.Data.Name == name);
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
                Config = (Config)JsonSerializer.Deserialize(json, typeof(Config), ConfigJsonContext.Default);
                // redundant check for past versions since this is the first migration ever
                if (Config!.SavedVersion == "0.6.0" || Config.SavedVersion == "0.5.2" ||
                    Config.SavedVersion == "0.5.1" || Config.SavedVersion == "0.5.0")
                {
                    Console.WriteLine("Running migration from version 0.6.0 and lower");
                    foreach (var process in Config.Processes)
                    {
#pragma warning disable 612
                        var split = process.Command.Split(' ');
                        process.Filename = split[0];
                        process.Arguments = split.Length == 1 ? Array.Empty<string>() : split[1..];
                        process.Command = null;
#pragma warning restore 612
                    }

                    NotSaved = true;
                }

                if (NotSaved)
                    Console.WriteLine(
                        "Seems like migrations from past versions were done, please backup and save the configuration manually.");
            }
            catch
            {
                Config = new()
                {
                    Processes = Array.Empty<JanDProcessData>()
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

            Processes = new();
            foreach (var processData in Config.Processes)
            {
                var proc = new JanDProcess()
                {
                    Data = processData
                };
                Processes.Add(proc);
                proc.SafeIndex = ++LastSafeIndex;
                try
                {
                    if (proc.Data.Enabled)
                        proc.Start();
                }
                catch (Exception exc)
                {
                    Console.WriteLine($"Starting {proc.Data.Name} failed: {exc.Message}");
                }
            }


            void NewPipeServer()
            {
                var pipeServer = new NamedPipeServerStream(Program.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
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
                    null);
            }

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
            var packet = (IpcPacket)JsonSerializer.Deserialize(bytes.AsSpan()[..count], typeof(IpcPacket), MyJsonContext.Default);
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
                    j.WriteString("Version", Program.Version);
                    j.WriteEndObject();
                    j.Flush();
                    break;
                }
                case "rename-process":
                {
                    var val1 = packet.Data[..packet.Data.IndexOf(':')];
                    var val2 = packet.Data[(packet.Data.IndexOf(':') + 1)..];
                    if (Processes.Any(p => p.Data.Name == packet.Data.AsSpan()[(packet.Data.IndexOf(':') + 1)..]))
                        throw new DaemonException("process-already-exists");
                    if (!ProcessNameValidationRegex.IsMatch(val2))
                        throw new DaemonException("Process name doesn't pass verification regex.");

                    var proc = GetProcess(val1);
                    proc.Data.Name = val2;
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
                        p.Data.Name == packet.Data[..separatorIndex]);
                    if (proc == null)
                    {
                        pipeServer.Write("ERR:invalid-process");
                        return;
                    }

                    proc.Data.Enabled = Boolean.Parse(packet.Data[(separatorIndex + 1)..]);
                    pipeServer.Write(proc.Data.Enabled.ToString());
                    NotSaved = true;
                    ProcessPropertyUpdated(proc.Data.Name, "Enabled", packet.Data[(separatorIndex + 1)..]);
                    break;
                }
                case "set-process-property":
                {
                    var req = Util.DeserializeJson<SetPropertyIpcPacket>(packet.Data);
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
                    var def = Util.DeserializeJson<JanDNewProcess>(packet.Data);
                    if (Processes.Any(p => p.Data.Name == def!.Name))
                        throw new DaemonException("process-already-exists");

                    if (!ProcessNameValidationRegex.IsMatch(def!.Name))
                        throw new DaemonException("Process name doesn't pass verification regex.");

                    JanDProcess proc = new()
                    {
                        Data = new()
                        {
                            Name = def.Name,
                            Filename = def.Filename,
                            Arguments = def.Arguments,
                            WorkingDirectory = def.WorkingDirectory,
                        },
                        SafeIndex = ++LastSafeIndex
                    };
                    Processes.Add(proc);
                    NotSaved = true;
                    pipeServer.Write("added");
                    ProcessEventAsync(DaemonEvents.ProcessAdded, proc.Data.Name);
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
                    var processes = new JanDProcessData[Processes.Count];
                    for (var i = 0; i < Processes.Count; i++)
                    {
                        processes[i] = Processes[i].Data;
                    }
                    Config.Processes = processes;
                    Config.SavedVersion = Program.Version;
                    var json = JsonSerializer.Serialize(Config, typeof(Config), ConfigJsonContext.Default);
                    File.WriteAllText("./config.json", json);
                    NotSaved = false;
                    pipeServer.Write("done");
                    break;
                }
                case "get-process-list":
                {
                    var writer = new Utf8JsonWriter(pipeServer);
                    writer.WriteStartArray();
                    foreach (var process in Processes)
                    {
                        JsonSerializer.Serialize(writer, process, typeof(JanDProcessData), MyJsonContext.Default);
                    }
                    writer.WriteEndArray();
                    writer.Flush();
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
                    ProcessEventAsync(DaemonEvents.ProcessDeleted, proc.Data.Name);
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
                // todo: don't separate stdout and stderr at all
                case "subscribe-log-event":
                {
                    connection.OutLogSubs.Add(packet.Data);
                    connection.ErrLogSubs.Add(packet.Data);
                    pipeServer.Write("done");
                    break;
                }
                case "unsubscribe-log-event":
                {
                    connection.OutLogSubs.Remove(packet.Data);
                    connection.ErrLogSubs.Remove(packet.Data);
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
                    connection.Stream.WriteByte((byte)'\n');
                    await connection.Stream.FlushAsync();
                }
                catch
                {
                    // connection interrupted, rest of the code will handle the cleanup...
                }
            }
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

    public class DaemonException : Exception
    {
        public DaemonException(string message) : base(message)
        {
        }
    }
}