using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
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
                case "start":
                {
                    if (args.Length == 2)
                    {
                        var client = new IpcClient();
                        var str = client.RequestString("start-process", args[1]);
                        Console.WriteLine(str);
                    }
                    else if (args.Length > 2)
                    {
                        var client = new IpcClient();
                        ProcessRelativePath();
                        var str = client.RequestString("new-process",
                            JsonSerializer.Serialize(new Daemon.JanDNewProcess(args[1],
                                String.Join(' ', args[2..]), Directory.GetCurrentDirectory())));
                        Console.WriteLine(str);
                        str = client.RequestString("start-process", args[1]);
                        Console.WriteLine(str);
                    }

                    break;
                }
                case "enable":
                case "disable":
                {
                    var client = new IpcClient();
                    var response = client.RequestString("set-enabled",
                        args[1] + ":" + (args[0].ToLower() == "disable" ? "false" : "true"));
                    Console.WriteLine($"Enabled set to {response}.");
                    break;
                }
                case "l":
                case "ls":
                case "list":
                {
                    var client = new IpcClient();
                    var json = client.RequestString("get-processes", "");
                    var status = client.GetStatus();
                    var processes = JsonSerializer.Deserialize<JanDRuntimeProcess[]>(json);

                    Console.Write("{0,-6}", "R|E|A");
                    Console.Write("{0,-14}", "Name");
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
                        Console.Write("{0,-14}", process.Name);
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
                            // Mem
                            var proc = Process.GetProcessById(process.ProcessId);
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

                        Console.WriteLine();
                    }

                    NotSavedCheck(status);

                    break;
                }
                case "stop":
                {
                    var client = new IpcClient();
                    var str = client.RequestString("stop-process", args[1]);
                    Console.WriteLine(str);
                    break;
                }
                case "restart":
                {
                    var client = new IpcClient();
                    var str = client.RequestString("restart-process", args[1]);
                    Console.WriteLine(str);
                    break;
                }
                case "info":
                {
                    var client = new IpcClient();
                    var proc = JsonSerializer.Deserialize<JanDRuntimeProcess>(client.RequestString("get-process-info", args[1]));

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
                    break;
                }
                case "new":
                {
                    var client = new IpcClient();
                    ProcessRelativePath();
                    var str = client.RequestString("new-process",
                        JsonSerializer.Serialize(new Daemon.JanDNewProcess(args[1],
                            String.Join(' ', args[2..]), Directory.GetCurrentDirectory())));
                    Console.WriteLine(str);
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
                    NotSavedCheck(status);
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
                    };
                    var client = new IpcClient();
                    var status = client.GetStatus();

                    void TailLog(string whichStd, int lineCount = 15)
                    {
                        Console.WriteLine($"Getting last 15 lines of std{whichStd} logs...");
                        var fs = new FileStream(Path.Combine(status.Directory, "logs/" + args[1] + $"-{whichStd}.log"),
                            FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var reader = new StreamReader(fs);
                        var lines = reader.Tail(lineCount);
                        reader.Close();
                        foreach (var line in lines)
                            Console.WriteLine(line);
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
                    LogWatch(client);

                    break;
                }
                case "delete":
                {
                    var client = new IpcClient();
                    var str = client.RequestString("delete-process", args[1]);
                    Console.WriteLine(str);
                    break;
                }
                case "startup":
                {
                    if (!OperatingSystem.IsLinux())
                    {
                        Console.WriteLine("SystemD startup services are only available on Linux with SystemD.");
                        return;
                    }
                    if (getuid() != 0)
                    {
                        Console.WriteLine("Run the following command as root to install the SystemD service file:");
                        Console.WriteLine(
                            $"{Environment.GetCommandLineArgs()[0]} startup {Environment.UserName} {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jand")}");
                    }
                    else
                    {
                        var service = GetResourceString("systemd-template.service");
                        service = String.Format(service, args[1], Environment.GetEnvironmentVariable("PATH"), args[2],
                            PipeName, Process.GetCurrentProcess().MainModule?.FileName ?? "jand");
                        var location = "/etc/systemd/system/jand-" + args[1] + ".service";
                        File.WriteAllText(location, service);
                        Console.WriteLine($"SystemD service file installed in {location}");
                        Console.WriteLine("Enable and start the service using the following command:");
                        Console.WriteLine($"systemctl enable --now jand-{args[1]}");
                    }

                    break;
                }
                case "help":
                case "-help":
                case "--help":
                {
                    Console.WriteLine(GetResourceString("help.txt"));

                    break;
                }
                case "flush":
                {
                    var client = new IpcClient();
                    Console.WriteLine(client.RequestString("flush-all-logs", ""));
                    break;
                }
                default:
                    Console.WriteLine("Unknown command. For a list of commands see the `help` command.");
                    return;
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

        public static void NotSavedCheck(DaemonStatus status)
        {
            if (status.NotSaved)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine("Process list not saved, use the `save` command to save it.");
                Console.ResetColor();
            }
        }
        public static void LogWatch(IpcClient client)
        {
            byte[] bytes = new byte[100_000];
            AsyncCallback callback = null;
            while (true)
            {
                var count = client.Stream.Read(bytes, 0, bytes.Length);
                var ev = JsonSerializer.Deserialize<IpcClient.DaemonClientEvent>(bytes[..count]);
                Console.Write(ev!.Value);
            }
        }

        public static string GetResourceString(string name)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream("JanD." + name))
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