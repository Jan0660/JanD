using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

#pragma warning disable 1998
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace JanD
{
    public static class Commands
    {
        // Logging helper methods
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

        // commands

        [Verb("start", aliases: new[]
        {
            "up"
        }, HelpText = "Start a process or create a new process and start it.", Hidden = false)]
        public class Start : ICommand
        {
            [Value(0, Required = true, HelpText = "The process name.", MetaName = "Name")]
            public string Name { get; set; }

            [Value(1, Default = null, HelpText = "The command to start the new process with.", MetaName = "Command")]
            public string Command { get; set; }

            [Value(2, Default = null, HelpText = "The arguments to execute the command with.", MetaName = "Arguments")]
            public IEnumerable<string> Arguments { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                if (Command == null && !Arguments.Any())
                    client.DoRequests(client.GetProcessNames(new[] { Name }), "start-process");
                else
                {
                    var str = client.RequestString("new-process",
                        JsonSerializer.Serialize(new Daemon.JanDNewProcess(Name,
                            Command.ToFullPath(), Arguments?.ToArray() ?? Array.Empty<string>(),
                            Directory.GetCurrentDirectory())));
                    Console.WriteLine(str);
                    str = client.RequestString("start-process", Name);
                    Console.WriteLine(str);
                }

                Program.DoProcessListIfEnabled(client);
            }
        }

        [Verb("enable", aliases: new[]
        {
            "disable"
        }, HelpText = "Enable/Disable a process.")]
        public class Enable : ICommand
        {
            [Value(0, MetaName = "Processes$", Required = true)]
            public IEnumerable<string> Processes { get; set; }

            public async Task Run()
            {
                var processes = Processes.ToArray();
                var client = new IpcClient();
                for (var i = 0; i < processes.Length; i++)
                    processes[i] = processes[i] + ":" +
                                   (processes[0].ToLower() == "disable" ? "false" : "true");

                client.DoRequests(client.GetProcessNames(processes[1..]), "set-enabled");
            }
        }

        [Verb("list", aliases: new[]
        {
            "ls", "l"
        }, HelpText = "List current processes.")]
        [Examples("list", "list /[0-9]/")]
        public class List : ICommand
        {
            [Value(0, MetaName = "Processes$")] public IEnumerable<string> Processes { get; set; }

            public async Task Run()
            {
                var args = Processes.ToArray();
                var client = new IpcClient();
                Program.DoProcessList(client,
                    args.Length > 1 && args[1].StartsWith('/') && args[1].EndsWith('/')
                        ? new Regex(args[1][1..^1])
                        : null);
            }
        }

        [Verb("stop", aliases: new[]
        {
            "down"
        }, HelpText = "Stop a process.")]
        public class StopProcess : ICommand
        {
            [Value(0, MetaName = "Processes$", Required = true)]
            public IEnumerable<string> Processes { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                client.DoRequests(client.GetProcessNames(Processes.ToArray()), "stop-process");
            }
        }

        [Verb("restart", HelpText = "Restart a process.")]
        public class RestartProcess : ICommand
        {
            [Value(0, MetaName = "Processes$", Required = true)]
            public IEnumerable<string> Processes { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                client.DoRequests(client.GetProcessNames(Processes.ToArray()), "restart-process");
            }
        }

        [Verb("info", aliases: new[]
        {
            "i"
        }, HelpText = "Get JanD information of information about a process.")]
        public class InfoCommand : ICommand
        {
            [Value(0, MetaName = "Processes$", Default = null)]
            public IEnumerable<string> Processes { get; set; }

            public async Task Run()
            {
                if (Processes == null || !Processes.Any())
                {
                    Console.Write(Program.GetResourceString("info.txt"), Program.TextLogo, Program.Version);
                }
                else
                {
                    var client = new IpcClient();
                    foreach (var process in client.GetProcessNames(Processes.ToArray()))
                    {
                        var proc = client.RequestJson<Program.JanDRuntimeProcess>("get-process-info", process);

                        Console.WriteLine(proc!.Name);
                        Info("Filename", proc.Filename);
                        Info("Arguments", string.Join(' ', proc.Arguments));
                        Info("WorkingDirectory", proc.WorkingDirectory);
                        Info("PID", proc.ProcessId.ToString());
                        Info("ExitCode", proc.ExitCode.ToString());
                        Info("RestartCount", proc.RestartCount.ToString());
                        InfoBool("Stopped", proc.Stopped);
                        InfoBool("Enabled", proc.Enabled);
                        InfoBool("AutoRestart", proc.AutoRestart);
                        InfoBool("Running", proc.Running);
                        InfoBool("Watch", proc.Watch);
                    }
                }
            }
        }

        [Verb("new", aliases: new[]
        {
            "add"
        }, HelpText = "Create a new process.")]
        public class New : ICommand
        {
            [Value(0, Required = true, HelpText = "The process name.", MetaName = "Name")]
            public string Name { get; set; }

            [Value(1, Required = true, HelpText = "The command to start the new process with.", MetaName = "Command")]
            public string Command { get; set; }

            [Value(2, Required = true, HelpText = "The arguments to execute the command with.", MetaName = "Arguments")]
            public IEnumerable<string> Arguments { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                var str = client.RequestString("new-process",
                    JsonSerializer.Serialize(new Daemon.JanDNewProcess(Name,
                        Command.ToFullPath(), Arguments?.ToArray() ?? Array.Empty<string>(),
                        Directory.GetCurrentDirectory())));
                Console.WriteLine(str);
                Program.DoProcessListIfEnabled(client);
            }
        }

        [Verb("kill", HelpText = "Stop all processes (and their children) and then kill JanD.")]
        public class Kill : ICommand
        {
            public async Task Run()
            {
                var client = new IpcClient();
                client.SendString("exit", "");
                Console.WriteLine("Sent exit.");
            }
        }

        [Verb("save", HelpText = "Save JanD processes and configuration.")]
        public class Save : ICommand
        {
            public async Task Run()
            {
                var client = new IpcClient();
                var str = client.RequestString("save-config", "");
                Console.WriteLine(str);
            }
        }

        [Verb("start-daemon", HelpText = "Start the daemon.")]
        public class StartDaemon : ICommand
        {
            public async Task Run()
            {
                await Daemon.Start();
            }
        }

        [Verb("status", HelpText = "Get daemon status.")]
        public class Status : ICommand
        {
            public async Task Run()
            {
                var client = new IpcClient();
                var status = client.GetStatus();
                Info("Directory", status.Directory);
                Info("Processes", status.Processes.ToString());
                InfoBool("NotSaved", status.NotSaved);
                Info("Version", status.Version);
                Program.DoChecks(client);
            }
        }

        [Verb("logs", HelpText = "Get recent logs for a process and watch for new logs.")]
        public class Logs : ICommand
        {
            [Value(0, Default = null, MetaName = "Process", HelpText = "The process to get logs of.")]
            public string Process { get; set; }

            public async Task Run()
            {
                var events = DaemonEvents.ErrLog | DaemonEvents.OutLog;
                var client = new IpcClient();
                if (Process == null)
                {
                    // full logs
                    client.RequestString("subscribe-events", "255");
                    var processes = client.RequestJson<Program.JanDRuntimeProcess[]>("get-processes", "");
                    foreach (var proc in processes)
                    {
                        client.RequestString("subscribe-outlog-event", proc.Name);
                        client.RequestString("subscribe-errlog-event", proc.Name);
                    }

                    client.ListenEvents(ev =>
                    {
                        if (ev.Event == "outlog" || ev.Event == "errlog")
                            Console.Write(ev.Value);
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine(":: " + ev.Event switch
                            {
                                "procstop" => $"The process `{ev.Process}` has stopped.",
                                "procstart" => $"The process `{ev.Process}` has started.",
                                "procadd" => $"The process `{ev.Process}` was created.",
                                "procdel" => $"The process `{ev.Process}` was removed.",
                                "procren" => $"The process `{ev.Process}` was renamed to `{ev.Value}`.",
                                _ => $"Process: {ev.Process}; Event: {ev.Event}; Value: {ev.Value}"
                            });
                            Console.ResetColor();
                        }

                        if (ev.Event == "procren")
                            client.RequestString("subscribe-outlog-event", ev.Value);
                        if (ev.Event == "procadd")
                            client.RequestString("subscribe-outlog-event", ev.Value);
                    });
                }

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
                    var filename = Path.Combine(status.Directory, "logs/" + Process + $"-{whichStd}.log");
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

                if (events.HasFlag(DaemonEvents.OutLog))
                    TailLog("out");
                if (events.HasFlag(DaemonEvents.ErrLog))
                    TailLog("err");
                Console.WriteLine();

                client.RequestString("subscribe-events", ((int)events).ToString());
                if (events.HasFlag(DaemonEvents.OutLog))
                    client.RequestString("subscribe-outlog-event", Process);
                if (events.HasFlag(DaemonEvents.ErrLog))
                    client.RequestString("subscribe-errlog-event", Process);
                client.ListenEvents(ev => Console.Write(ev!.Value));
            }
        }

        [Verb("delete", aliases: new[]
        {
            "remove", "rm"
        }, HelpText = "Stop a process and delete it.")]
        public class Delete : ICommand
        {
            [Value(0, MetaName = "Processes$", Required = true)]
            public IEnumerable<string> Processes { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                client.DoRequests(client.GetProcessNames(Processes.ToArray()), "delete-process");
                Program.DoProcessListIfEnabled(client);
            }
        }

        [Verb("startup", HelpText = "Add JanD to your system's startup.")]
        public class Startup : ICommand
        {
            [Value(0, MetaName = "Username", Default = null)]
            public string Username { get; set; }

            [Value(1, MetaName = "HomePath", Default = null)]
            public string HomePath { get; set; }

            public async Task Run()
            {
                if (!OperatingSystem.IsLinux())
                {
                    Console.WriteLine("Startup services are currently only available on Linux.");
                    return;
                }

                if (Program.getuid() != 0 || Username == null)
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
                            var service = Program.GetResourceString("systemd-template.service");
                            service = String.Format(service, Username, Environment.GetEnvironmentVariable("PATH"),
                                HomePath,
                                Program.PipeName, Process.GetCurrentProcess().MainModule?.FileName ?? "jand");
                            var location = "/etc/systemd/system/jand-" + Username + ".service";
                            File.WriteAllText(location, service);
                            Console.WriteLine($"SystemD service file installed in {location}");
                            Console.WriteLine("Enable and start the service using the following command:");
                            Console.WriteLine($"systemctl enable --now jand-{Username}");
                            break;
                        }
                        case "runit":
                        {
                            Console.WriteLine("Detected runit...");
                            // check for Artix's folder structure
                            if (Directory.Exists("/etc/runit/sv"))
                            {
                                var path = $"/etc/runit/sv/jand-{Username}";
                                Console.WriteLine("Creating...");
                                Directory.CreateDirectory(path);
                                await Task.WhenAll(File.WriteAllTextAsync($"{path}/conf",
                                    String.Format(Program.GetResourceString("runit-conf-template"), Username,
                                        HomePath)), File.WriteAllTextAsync($"{path}/run",
                                    String.Format(Program.GetResourceString("runit-run"),
                                        Process.GetCurrentProcess().MainModule?.FileName ?? "jand")));
                                await Task.WhenAll(Process.Start("chmod", $"755 \"{path}/run\"")!.WaitForExitAsync(),
                                    Process.Start("chmod", $"755 \"{path}/conf\"")!.WaitForExitAsync());
                                Console.WriteLine($"Installed service file in {path}");
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
                                @"Unknown init. If you want to see support added, please open an issue about it on GitHub!");
                            break;
                        }
                    }
                }
            }
        }

        [Verb("events-json", HelpText = "View events in raw JSON.")]
        public class EventsJson : ICommand
        {
            public async Task Run()
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
        }

        [Verb("flush", HelpText = "Ensure all logs are written to disk. (May be redundant)")]
        public class Flush : ICommand
        {
            public async Task Run()
            {
                var client = new IpcClient();
                Console.WriteLine(client.RequestString("flush-all-logs", ""));
            }
        }

        [Verb("rename", HelpText = "Rename a process.")]
        public class Rename : ICommand
        {
            [Value(0, Required = true, MetaName = "Old Name")]
            public string OldName { get; set; }

            [Value(1, Required = true, MetaName = "New Name")]
            public string NewName { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                Console.WriteLine(client.RequestString("rename-process", OldName + ':' + NewName));
                Program.DoProcessListIfEnabled(client);
            }
        }

        [Verb("send", HelpText = "Send line of text to process' stdin.")]
        public class Send : ICommand
        {
            [Value(0, Required = true, MetaName = "Process")]
            public string Process { get; set; }

            [Value(1, Required = true, MetaName = "Data")]
            public IEnumerable<string> Data { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                Console.WriteLine(client.RequestString("send-process-stdin-line",
                    Process + ":" + String.Join(' ', Data)));
            }
        }

        [Verb("config", HelpText = "View and edit configuration.")]
        public class Config : ICommand
        {
            [Value(0, Default = null, MetaName = "Name")]
            public string Name { get; set; }

            [Value(1, Default = null, MetaName = "Value")]
            public string Value { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                if (Name == null)
                {
                    var config = client.RequestJson<Config>("get-config", "");
                    var type = config.GetType();
                    foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    {
                        if (property.PropertyType == typeof(bool))
                            InfoBool(property.Name, (bool)property.GetValue(config)!);
                        else if (property.PropertyType == typeof(int))
                            Info(property.Name, property.GetValue(config)!.ToString());

                        Program.LogDescription(property);
                    }
                }
                else if (Name != null && Value == null)
                {
                    var res = client.RequestString("set-config", Name + ":" + Value);
                    if (res == "done")
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write(Name);
                        Console.ResetColor();
                        Console.Write(" has been set to ");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write(Value);
                        Console.ResetColor();
                        Console.WriteLine(".");
                        Program.DoChecks(client);
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
                    var property = type.GetPropertyCaseInsensitive(Name);
                    if (property != null)
                    {
                        Console.WriteLine(property.GetValue(config)!.ToString());

                        Program.LogDescription(property);
                    }
                    else
                    {
                        Console.WriteLine("Invalid property.");
                    }
                }
            }
        }

        [Verb("group start", aliases: new[]
        {
            "group up"
        }, HelpText = "Start a group of processes.")]
        public class Group_Start : ICommand
        {
            [Value(0, Default = null, MetaName = "Group Name")]
            public string GroupName { get; set; }

            public async Task Run()
            {
                var groupFile = JsonSerializer.Deserialize<GroupFile>(File.ReadAllText("./jand-group.json"),
                    new JsonSerializerOptions()
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    });
                var client = new IpcClient();
                var list = new List<String>();
                foreach (var proc in groupFile!.Processes)
                {
                    proc.WorkingDirectory = Path.GetFullPath(proc.WorkingDirectory);
                    proc.Filename = proc.Filename.StartsWith('.')
                        ? Path.GetFullPath(proc.Filename)
                        : proc.Filename;
                    proc.Name = String.Format(proc.Name, GroupName);
                    Console.Write("new " + proc.Name + ": ");
                    Console.WriteLine(client.RequestString("new-process",
                        JsonSerializer.Serialize(proc)));
                    var defaultValues = new JanDProcess();
                    foreach (var prop in new[] { "watch", "autoRestart", "enabled" })
                    {
                        // don't set default values
                        var val = typeof(GroupFileProcess).GetPropertyCaseInsensitive(prop).GetValue(proc)!
                            .ToString();
                        var defVal = typeof(JanDProcess).GetPropertyCaseInsensitive(prop)
                            .GetValue(defaultValues)!.ToString();
                        if (val != defVal)
                        {
                            client.RequestString("set-process-property", JsonSerializer.Serialize(
                                new SetPropertyIpcPacket
                                {
                                    Process = proc.Name,
                                    Property = prop,
                                    Data = val
                                }));
                        }
                    }

                    list.Add(proc.Name);
                }

                Console.WriteLine("Starting processes...");
                client.DoRequests(list.ToArray(), "start-process");
            }
        }

        [Verb("request", HelpText = "Send a raw request to the daemon.")]
        public class Request : ICommand
        {
            [Value(0, MetaName = "Type", Required = true, HelpText = "The request type.")]
            public string Type { get; set; }

            [Value(1, MetaName = "Data", Default = "", HelpText = "The request data.")]
            public string Data { get; set; }

            [Option("echo", Default = false, HelpText = "If the request should be echoed back.")]
            public bool Echo { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                if (Echo)
                    Console.WriteLine($"Sending request: Type: `{Type}`; Data: `{Data}`");
                Console.WriteLine(client.RequestString(Type, Data));
            }
        }

        [Verb("spp", Hidden = true)]
        public class SetProcessProperty : ICommand
        {
            [Value(0, MetaName = "Process")] public string Process { get; set; }
            [Value(1, MetaName = "Property")] public string Property { get; set; }
            [Value(2, MetaName = "Data")] public string Data { get; set; }

            public async Task Run()
            {
                var client = new IpcClient();
                Console.WriteLine(client.RequestString("set-process-property", JsonSerializer.Serialize(
                    new SetPropertyIpcPacket
                    {
                        Process = Process,
                        Property = Property,
                        Data = Data
                    })));
            }
        }

        [Verb("compgen-proc-list", Hidden = true)]
        public class CompletionGeneration_ProcessList : ICommand
        {
            public async Task Run()
            {
                var client = new IpcClient();
                var processes = client.RequestJson<Program.JanDRuntimeProcess[]>("get-processes", "");
                for (var i = 0; i < processes.Length; i++)
                {
                    Console.Write(processes[i].Name);
                    if (i != processes.Length - 1)
                        Console.Write(' ');
                }
            }
        }
    }

    public interface ICommand
    {
        public Task Run();
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class ExamplesAttribute : Attribute
    {
        public readonly string[] Examples;

        public ExamplesAttribute(params string[] examples)
            => Examples = examples;
    }
}
