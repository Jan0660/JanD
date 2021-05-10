using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace JanD
{
    public static class Program
    {
        public const string DefaultPipeName = "jand";
        public static string PipeName;

        static async Task Main(string[] args)
        {
            PipeName = Environment.GetEnvironmentVariable("JAND_PIPE") ?? DefaultPipeName;
            Console.WriteLine($"JanD v{ThisAssembly.Info.Version}");
            if (args.Length == 0)
            {
                Console.WriteLine("No argument given.");
                return;
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
                    // Console.WriteLine(json);
                    var processes = JsonSerializer.Deserialize<JanDRuntimeProcess[]>(json);

                    Console.Write("{0,-14}", "Name");
                    Console.Write("{0,-10}", "PID");
                    Console.Write("{0,-14}", "Mem");
                    Console.WriteLine();
                    foreach (var process in processes)
                    {
                        // var proc = Process.GetProcessById(process.ProcessId);
                        Console.Write("{0,-14}", process.Name);
                        Console.Write("{0,-9}", process.ProcessId);
                        if (process.ProcessId != -1 && !process.Stopped)
                        {
                            var proc = Process.GetProcessById(process.ProcessId);
                            var mem = proc.WorkingSet64;
                            string memString = mem switch
                            {
                                > (int)1e9 => (mem / (int) 1e9) + "GB",
                                > (int)1e6 => (mem / (int) 1e6) + "MB",
                                > (int)1e3 => (mem / (int) 1e3) + "KB",
                                _ => mem.ToString()
                            };
                            Console.Write("{0,-7}", memString);
                        }

                        Console.WriteLine();
                    }

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
                    var lines = new StreamReader(Path.Combine(status.Directory, "logs/" + args[1] + "-out.log")).Tail(15);
                    foreach (var line in lines)
                        Console.WriteLine(line);
                    break;
                }
                case "errlogs":
                {
                    var status = new IpcClient().GetStatus();
                    var lines = new StreamReader(Path.Combine(status.Directory, "logs/" + args[1] + "-err.log")).Tail(15);
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
                default:
                    Console.WriteLine("Unknown command.");
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
        }
    }
}