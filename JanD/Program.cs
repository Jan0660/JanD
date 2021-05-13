using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Resources;
using System.Text.Json;
using System.Threading.Tasks;

namespace JanD
{
    public static class Program
    {
        public const string DefaultPipeName = "jand";
        public static string PipeName;
        public const string TrueMark = "[38;2;0;255;0m√[0m";
        public const string FalseMark = "[38;2;255;0;0mx[0m";

        static async Task Main(string[] args)
        {
            PipeName = Environment.GetEnvironmentVariable("JAND_PIPE") ?? DefaultPipeName;
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
                        Console.Write("{0,-5}",
                            process.RestartCount.ToString().Length > 3
                                ? process.RestartCount.ToString()[..3] + '-'
                                : process.RestartCount.ToString());
                        Console.Write("{0,-10}", process.ProcessId);
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
                            Console.Write("{0,-7}", uptimeString);
                            // Cmd
                            Console.Write("{0,-12}", proc.ProcessName);
                        }

                        Console.WriteLine();
                    }

                    if (status.NotSaved)
                        Console.WriteLine("Process list not saved, use the `save` command to save it.");

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
                    var str = client.RequestString("get-process-info", args[1]);
                    Console.WriteLine(str);
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
                    var str = client.RequestString("status", "");
                    Console.WriteLine(str);
                    break;
                }
                case "logs":
                    Console.WriteLine("Use `outlogs` or `errlogs`.");
                    break;
                case "outlogs":
                {
                    var status = new IpcClient().GetStatus();
                    var lines =
                        new StreamReader(Path.Combine(status.Directory, "logs/" + args[1] + "-out.log")).Tail(15);
                    foreach (var line in lines)
                        Console.WriteLine(line);
                    break;
                }
                case "errlogs":
                {
                    var status = new IpcClient().GetStatus();
                    var lines =
                        new StreamReader(Path.Combine(status.Directory, "logs/" + args[1] + "-err.log")).Tail(15);
                    foreach (var line in lines)
                        Console.WriteLine(line);
                    break;
                }
                case "delete":
                {
                    var client = new IpcClient();
                    var str = client.RequestString("delete-process", args[1]);
                    Console.WriteLine(str);
                    break;
                }
                case "help":
                case "-help":
                case "--help":
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var resourceName = "JanD.help.txt";

                    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string result = reader.ReadToEnd();
                        Console.WriteLine(result);
                    }

                    break;
                }
                default:
                    Console.WriteLine("Unknown command. For a list of commands see the `help` command.");
                    return;
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