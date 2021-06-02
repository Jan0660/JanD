﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JanD
{
    public static class Program
    {
        [DllImport("libc")]
        public static extern uint getuid();

        public const string DefaultPipeName = "jand";
        public static string PipeName;
        public const string TrueMark = "[38;2;0;255;0m√[0m";
        public const string FalseMark = "[38;2;255;0;0mx[0m";

        public const string TextLogo =
            "[1m[38;2;216;160;223m[[22m[38;2;0;255;255mJanD[38;2;216;160;223m[1m][22m[39m";

        static async Task Main(string[] args)
        {
            PipeName = Environment.GetEnvironmentVariable("JAND_PIPE") ?? DefaultPipeName;
            var home = Environment.GetEnvironmentVariable("JAND_HOME");
            if (home != null)
            {
                if (!Directory.Exists(home))
                    Directory.CreateDirectory(home);
                Directory.SetCurrentDirectory(home);
            }

            Console.WriteLine($"JanD v{ThisAssembly.Info.Version}");
            if (args.Length == 0)
            {
                Console.WriteLine("No argument given. For a list of commands see the `help` command.");
                return;
            }

            void ProcessRelativePath()
            {
                if (args[2].StartsWith('.'))
                    args[2] = Path.GetFullPath(args[2]);
            }

            switch (args[0].ToLower())
            {
                case "up":
                case "start":
                {
                    var client = new IpcClient();
                    if (args.Length == 2)
                    {
                        var str = client.RequestString("start-process", args[1]);
                        Console.WriteLine(str);
                    }
                    else if (args.Length > 2)
                    {
                        ProcessRelativePath();
                        var str = client.RequestString("new-process",
                            JsonSerializer.Serialize(new Daemon.JanDNewProcess(args[1],
                                String.Join(' ', args[2..]), Directory.GetCurrentDirectory())));
                        Console.WriteLine(str);
                        str = client.RequestString("start-process", args[1]);
                        Console.WriteLine(str);
                    }

                    DoProcessListIfEnabled(client);

                    break;
                }
                case "enable":
                case "disable":
                {
                    var client = new IpcClient();
                    for (var i = 0; i < args.Length - 1; i++)
                        args[i + 1] = args[i + 1] + ":" + (args[0].ToLower() == "disable" ? "false" : "true");

                    client.DoRequests(client.GetProcessNames(args[1..]), "set-enabled");
                    break;
                }
                case "l":
                case "ls":
                case "list":
                {
                    var client = new IpcClient();
                    DoProcessList(client);

                    break;
                }
                case "down":
                case "stop":
                {
                    var client = new IpcClient();
                    client.DoRequests(client.GetProcessNames(args[1..]), "stop-process");
                    break;
                }
                case "restart":
                {
                    var client = new IpcClient();
                    client.DoRequests(client.GetProcessNames(args[1..]), "restart-process");
                    break;
                }
                case "i":
                case "info":
                {
                    if (args.Length == 1)
                    {
                        Console.WriteLine(GetResourceString("info.txt"), TextLogo);
                    }
                    else
                    {
                        var client = new IpcClient();
                        foreach (var process in client.GetProcessNames(args[1..]))
                        {
                            var proc = JsonSerializer.Deserialize<JanDRuntimeProcess>(
                                client.RequestString("get-process-info", process));

                            Console.WriteLine(proc.Name);
                            Info("Command", proc.Command);
                            Info("WorkingDirectory", proc.WorkingDirectory);
                            Info("PID", proc.ProcessId.ToString());
                            Info("ExitCode", proc.ExitCode.ToString());
                            Info("RestartCount", proc.RestartCount.ToString());
                            InfoBool("Stopped", proc.Stopped);
                            InfoBool("Enabled", proc.Enabled);
                            InfoBool("AutoRestart", proc.AutoRestart);
                            InfoBool("Running", proc.Running);
                        }
                    }

                    break;
                }
                case "add":
                case "new":
                {
                    var client = new IpcClient();
                    ProcessRelativePath();
                    var str = client.RequestString("new-process",
                        JsonSerializer.Serialize(new Daemon.JanDNewProcess(args[1],
                            String.Join(' ', args[2..]), Directory.GetCurrentDirectory())));
                    Console.WriteLine(str);
                    DoProcessListIfEnabled(client);
                    break;
                }
                case "write-daemon":
                {
                    var client = new IpcClient();
                    client.Write(String.Join(' ', args[1..]));
                    break;
                }
                case "kill":
                {
                    var client = new IpcClient();
                    client.SendString("exit", "");
                    Console.WriteLine("Sent exit.");
                    break;
                }
                case "save":
                {
                    var client = new IpcClient();
                    var str = client.RequestString("save-config", "");
                    Console.WriteLine(str);
                    break;
                }
                case "start-daemon":
                    await Daemon.Start();
                    break;
                case "status":
                {
                    var client = new IpcClient();
                    var status = client.GetStatus();
                    Info("Directory", status.Directory);
                    Info("Processes", status.Processes.ToString());
                    InfoBool("NotSaved", status.NotSaved);
                    Info("Version", status.Version);
                    DoChecks(client);
                    break;
                }
                case "logs":
                case "errlogs":
                case "outlogs":
                {
                    var events = args[0].ToLower() switch
                    {
                        "logs" => Daemon.DaemonEvents.ErrLog | Daemon.DaemonEvents.OutLog,
                        "outlogs" => Daemon.DaemonEvents.OutLog,
                        "errlogs" => Daemon.DaemonEvents.ErrLog,
                        _ => Daemon.DaemonEvents.ErrLog | Daemon.DaemonEvents.OutLog
                    };
                    var client = new IpcClient();
                    if (Environment.GetEnvironmentVariable("JAND_AUTOFLUSH") != null
                        | Environment.GetEnvironmentVariable("JAND_AUTOFLUSH") == "1")
                    {
                        Console.WriteLine("Flushing logs...");
                        Console.WriteLine(client.RequestString("flush-all-logs", ""));
                    }

                    var status = client.GetStatus();

                    void TailLog(string whichStd, int lineCount = 15)
                    {
                        Console.WriteLine($"Getting last 15 lines of std{whichStd} logs...");
                        var filename = Path.Combine(status.Directory, "logs/" + args[1] + $"-{whichStd}.log");
                        if (!File.Exists(filename))
                        {
                            Console.WriteLine("Log files not found.");
                            Environment.Exit(1);
                        }

                        var fs = new FileStream(filename,
                            FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var reader = new StreamReader(fs);
                        var lines = reader.Tail(lineCount);
                        reader.Close();
                        foreach (var line in lines)
                            Console.WriteLine(line);
                        Console.ResetColor();
                    }

                    if (events.HasFlag(Daemon.DaemonEvents.OutLog))
                        TailLog("out");
                    if (events.HasFlag(Daemon.DaemonEvents.ErrLog))
                        TailLog("err");
                    Console.WriteLine();
                    if (OperatingSystem.IsWindows())
                    {
                        Console.WriteLine(
                            "Woops, watching logs is not available on W*ndows because of cringeness issues with IPC.");
                        return;
                    }

                    client.RequestString("subscribe-events", ((int) events).ToString());
                    if (events.HasFlag(Daemon.DaemonEvents.OutLog))
                        client.RequestString("subscribe-outlog-event", args[1]);
                    if (events.HasFlag(Daemon.DaemonEvents.ErrLog))
                        client.RequestString("subscribe-errlog-event", args[1]);
                    client.ListenEvents(ev => Console.Write(ev!.Value));
                    break;
                }
                case "remove":
                case "rm":
                case "delete":
                {
                    var client = new IpcClient();
                    client.DoRequests(client.GetProcessNames(args[1..]), "delete-process");
                    DoProcessListIfEnabled(client);
                    break;
                }
                case "startup":
                {
                    if (!OperatingSystem.IsLinux())
                    {
                        Console.WriteLine("Startup services are only available on Linux with SystemD or runit.");
                        return;
                    }

                    if (getuid() != 0 || args.Length == 1)
                    {
                        Console.WriteLine("Run the following command as root to install the service file:");
                        Console.WriteLine(
                            $"{Environment.GetCommandLineArgs()[0]} startup {Environment.UserName} {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jand")}");
                    }
                    else
                    {
                        var pid1 = Process.GetProcessById(1);
                        switch (pid1.ProcessName)
                        {
                            case "systemd":
                            {
                                Console.WriteLine("Detected SystemD...");
                                var service = GetResourceString("systemd-template.service");
                                service = String.Format(service, args[1], Environment.GetEnvironmentVariable("PATH"),
                                    args[2],
                                    PipeName, Process.GetCurrentProcess().MainModule?.FileName ?? "jand");
                                var location = "/etc/systemd/system/jand-" + args[1] + ".service";
                                File.WriteAllText(location, service);
                                Console.WriteLine($"SystemD service file installed in {location}");
                                Console.WriteLine("Enable and start the service using the following command:");
                                Console.WriteLine($"systemctl enable --now jand-{args[1]}");
                                break;
                            }
                            case "runit":
                            {
                                Console.WriteLine("Detected runit...");
                                // check for Artix's folder structure
                                if (Directory.Exists("/etc/runit/sv"))
                                {
                                    Console.WriteLine("Detected Artix's folder structure.");
                                    var path = $"/etc/runit/sv/jand-{args[1]}";
                                    Console.WriteLine("Creating...");
                                    Directory.CreateDirectory(path);
                                    File.WriteAllText($"{path}/run",
                                        String.Format(GetResourceString("runit-run"),
                                            Process.GetCurrentProcess().MainModule?.FileName ?? "jand"));
                                    File.WriteAllText($"{path}/conf",
                                        String.Format(GetResourceString("runit-conf-template"), args[1], args[2]));
                                    Console.WriteLine($"Installed service file in {path}");
                                    Process.Start("chmod", $"755 \"{path}/run\"")!.WaitForExit();
                                    Process.Start("chmod", $"755 \"{path}/conf\"")!.WaitForExit();
                                    if (File.Exists("/usr/share/libalpm/scripts/runit-hook"))
                                        Process.Start("/usr/share/libalpm/scripts/runit-hook", "add")!.WaitForExit();
                                }
                                else
                                    Console.WriteLine("Unknown folder structure.");

                                break;
                            }
                            default:
                            {
                                Console.WriteLine(
                                    @"Unknown init. If you are using OpenRC or s6 please wait for support to be added.
Or you can contribute on GitHub!");
                                break;
                            }
                        }
                    }

                    break;
                }
                case "h":
                case "-h":
                case "help":
                case "-help":
                case "--help":
                {
                    Console.Write(GetResourceString("info.txt"), TextLogo);
                    Console.WriteLine(GetResourceString("help.txt"));

                    break;
                }
                case "events":
                {
                    var client = new IpcClient();
                    client.RequestString("subscribe-events", "255");
                    byte[] bytes = new byte[100_000];
                    while (true)
                    {
                        var count = client.Stream.Read(bytes, 0, bytes.Length);
                        Console.WriteLine(Encoding.UTF8.GetString(bytes[..count]));
                    }
                }
                case "flush":
                {
                    var client = new IpcClient();
                    Console.WriteLine(client.RequestString("flush-all-logs", ""));
                    break;
                }
                case "rename":
                {
                    var client = new IpcClient();
                    Console.WriteLine(client.RequestString("rename-process", args[1] + ':' + args[2]));
                    DoProcessListIfEnabled(client);
                    break;
                }
                case "config":
                {
                    var client = new IpcClient();
                    if (args.Length == 1)
                    {
                        var config = client.RequestJson<Config>("get-config", "");
                        var type = config.GetType();
                        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                        {
                            if (property.PropertyType == typeof(bool))
                                InfoBool(property.Name, (bool) property.GetValue(config)!);
                            else if (property.PropertyType == typeof(int))
                                Info(property.Name, property.GetValue(config)!.ToString());

                            LogDescription(property);
                        }
                    }
                    else if (args.Length > 2)
                    {
                        var val = String.Join(' ', args[2..]);
                        var res = client.RequestString("set-config", args[1] + ":" + val);
                        if (res == "done")
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write(args[1]);
                            Console.ResetColor();
                            Console.Write(" has been set to ");
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write(val);
                            Console.ResetColor();
                            Console.WriteLine(".");
                            DoChecks(client);
                        }
                        else
                        {
                            Console.BackgroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine("Setting option failed.");
                            Console.ResetColor();
                            Console.WriteLine(res);
                        }
                    }
                    else
                    {
                        var config = client.RequestJson<Config>("get-config", "");
                        var type = config.GetType();
                        var property = type.GetPropertyCaseInsensitive(args[1]);
                        if (property != null)
                        {
                            Console.WriteLine(property.GetValue(config)!.ToString());

                            LogDescription(property);
                        }
                        else
                        {
                            Console.WriteLine("Invalid property.");
                        }
                    }

                    break;
                }
                case "raw-request":
                {
                    var client = new IpcClient();
                    var type = args[1];
                    var data = string.Join(' ', args[2..]);
                    Console.WriteLine($"Sending request: Type: `{type}`; Data: `{data}`");
                    Console.WriteLine(client.RequestString(type, data));
                    break;
                }
                case "spp":
                {
                    // spp process property data...
                    var client = new IpcClient();
                    Console.WriteLine(client.RequestString("set-process-property", JsonSerializer.Serialize(
                        new SetPropertyIpcPacket()
                        {
                            Process = args[1],
                            Property = args[2],
                            Data = String.Join(' ', args[3..])
                        })));
                    break;
                }
                default:
                    Console.WriteLine("Unknown command. For a list of commands see the `help` command.");
                    return;
            }
        }

        private static void DoProcessListIfEnabled(IpcClient client)
        {
            var env = Environment.GetEnvironmentVariable("JAND_PROCESS_LIST");
            if (env == null | env == "1")
                DoProcessList(client);
        }

        private static void DoProcessList(IpcClient client)
        {
            var json = client.RequestString("get-processes", "");
            var status = client.GetStatus();
            var processes = JsonSerializer.Deserialize<JanDRuntimeProcess[]>(json);

            int maxNameLength = 0;
            foreach (var process in processes)
                if (process.Name.Length > maxNameLength)
                    maxNameLength = process.Name.Length;
            maxNameLength = maxNameLength < 12 ? 14 : maxNameLength;
            var nameFormatString = $"{{0,-{maxNameLength + 2}}}";
            Console.Write("{0,-6}", "R|E|A");
            Console.Write(nameFormatString, "Name");
            Console.Write("{0,-5}", "↺");
            Console.Write("{0,-10}", "PID");
            Console.Write("{0,-7}", "Mem");
            Console.Write("{0,-7}", "Uptime");
            Console.Write("{0,-12}", "Cmd");
            Console.WriteLine();
            foreach (var process in processes)
            {
                Console.Write((process.Running ? TrueMark : FalseMark) + "|" +
                              (process.Enabled ? TrueMark : FalseMark) + "|"
                              + (process.AutoRestart ? TrueMark : FalseMark) + " ");
                Console.Write(nameFormatString, process.Name);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("{0,-5}",
                    process.RestartCount.ToString().Length > 3
                        ? process.RestartCount.ToString()[..3] + '-'
                        : process.RestartCount.ToString());
                Console.ForegroundColor = ConsoleColor.DarkMagenta;
                Console.Write("{0,-10}", process.ProcessId);
                Console.ResetColor();
                if (process.ProcessId != -1 && !process.Stopped)
                {
                    Process proc;
                    try
                    {
                        proc = Process.GetProcessById(process.ProcessId);
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.Write("ERR");
                        Console.ResetColor();
                        goto InvalidPid;
                    }

                    // Mem
                    var mem = proc.WorkingSet64;
                    string memString = mem switch
                    {
                        > (int) 1e9 => (mem / (int) 1e9) + "GB",
                        > (int) 1e6 => (mem / (int) 1e6) + "MB",
                        > (int) 1e3 => (mem / (int) 1e3) + "KB",
                        _ => mem.ToString()
                    };
                    Console.Write("{0,-7}", memString);
                    // Uptime
                    var uptime = (DateTime.Now - proc.StartTime);
                    string uptimeString = uptime.TotalSeconds switch
                    {
                        >= (86_400 * 2) => Math.Floor(uptime.TotalMinutes / 60 / 24) + "D",
                        >= 3_600 => Math.Floor(uptime.TotalMinutes / 60) + "h",
                        >= 60 => Math.Floor(uptime.TotalMinutes) + "m",
                        _ => Math.Floor(uptime.TotalSeconds) + "s"
                    };
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("{0,-7}", uptimeString);
                    // Cmd
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.Write("{0,-12}", proc.ProcessName);
                    Console.ResetColor();
                }

                InvalidPid: ;

                Console.WriteLine();
            }

            DoChecks(status);
        }

        public static void LogDescription(PropertyInfo property)
        {
            var att = property.GetCustomAttribute<DescriptionAttribute>();
            if (att != null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("└→ " + att.Value);
                Console.ResetColor();
            }
        }

        public static void Info(string name, string value)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(name);
            Console.ResetColor();
            Console.WriteLine(": " + value);
        }

        public static void InfoBool(string name, bool value)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(name);
            Console.ResetColor();
            Console.Write(": ");
            if (value)
                Console.ForegroundColor = ConsoleColor.Green;
            else
                Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine((value ? "true" : "false"));
            Console.ResetColor();
        }

        public static void DoChecks(IpcClient client) => DoChecks(client.GetStatus());

        public static void DoChecks(DaemonStatus status)
        {
            NotSavedCheck(status);
            DaemonVersionCheck(status);
        }

        public static void NotSavedCheck(DaemonStatus status)
        {
            if (status.NotSaved)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine("Process list or configuration not saved, use the `save` command to save it.");
                Console.ResetColor();
            }
        }

        public static void DaemonVersionCheck(DaemonStatus status)
        {
            if (ThisAssembly.Info.Version != status.Version)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine("Daemon is running an outdated version of JanD: " + status.Version);
                Console.ResetColor();
            }
        }

        public static string GetResourceString(string name)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream("JanD.Resources." + name))
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        // stolen from https://stackoverflow.com/a/4619770/12520276
        ///<summary>Returns the end of a text reader.</summary>
        ///<param name="reader">The reader to read from.</param>
        ///<param name="lineCount">The number of lines to return.</param>
        ///<returns>The last lneCount lines from the reader.</returns>
        public static string[] Tail(this TextReader reader, int lineCount)
        {
            var buffer = new List<string>(lineCount);
            string line;
            for (int i = 0; i < lineCount; i++)
            {
                line = reader.ReadLine();
                if (line == null) return buffer.ToArray();
                buffer.Add(line);
            }

            int lastLine =
                lineCount - 1; //The index of the last line read from the buffer.  Everything > this index was read earlier than everything <= this indes

            while (null != (line = reader.ReadLine()))
            {
                lastLine++;
                if (lastLine == lineCount) lastLine = 0;
                buffer[lastLine] = line;
            }

            if (lastLine == lineCount - 1) return buffer.ToArray();
            var retVal = new string[lineCount];
            buffer.CopyTo(lastLine + 1, retVal, 0, lineCount - lastLine - 1);
            buffer.CopyTo(0, retVal, lineCount - lastLine - 1, lastLine + 1);
            return retVal;
        }

        public class JanDRuntimeProcess
        {
            public string Name { get; set; }
            public string Command { get; set; }
            public string WorkingDirectory { get; set; }
            public int ProcessId { get; set; }
            public bool Stopped { get; set; }
            public int ExitCode { get; set; }
            public int RestartCount { get; set; }
            public bool Enabled { get; set; }
            public bool AutoRestart { get; set; }
            public bool Running { get; set; }
        }
    }
}