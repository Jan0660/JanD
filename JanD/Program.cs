using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

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
        public const string Version = ThisAssembly.Constants.Version;

        /// <summary>
        /// e.g. JanD v0.7.0
        /// </summary>
        public static readonly string NameFormatted = $"JanD v{Version}";

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

            if (args.Length == 0)
            {
                // force info command
                args = new[] { "info" };
            }

            var parserResult = await new Parser(config =>
                {
                    config.IgnoreUnknownArguments = false;
                    config.AutoHelp = true;
                    config.AutoVersion = true;
                    config.HelpWriter = null;
                }).ParseArguments(args,
                    typeof(Commands).GetNestedTypes())
                .WithParsedAsync(async obj =>
                {
                    if (obj is ICommand command)
                        await command.Run();
                    else
                        Console.WriteLine("error");
                });
            await parserResult.WithNotParsedAsync(async errors =>
            {
                var error = errors.First();
                if (error is VersionRequestedError)
                {
                    await new Commands.InfoCommand().Run();
                }
                else if (error is HelpVerbRequestedError || error is HelpRequestedError)
                {
                    var helpText = HelpText.AutoBuild(parserResult, h =>
                    {
                        h.MaximumDisplayWidth = Int32.MaxValue;
                        h.AutoVersion = true;
                        h.AdditionalNewLineAfterOption = false;
                        h.Heading = String.Format(GetResourceString("info.txt").TrimEnd(), TextLogo, Version);
                        h.Copyright = "";
                        // when getting help for a specific command
                        if (error is HelpRequestedError)
                        {
                            var verb = parserResult.TypeInfo.Current.GetCustomAttribute<VerbAttribute>();
                            h.Heading = verb!.Name;
                            h.Copyright = "";
                            if (verb.HelpText != null)
                                h.AddPreOptionsLine(verb.HelpText);
                            var examples = parserResult.TypeInfo.Current.GetCustomAttribute<ExamplesAttribute>()?.Examples;
                            if (examples != null)
                            {
                                h.AddPostOptionsLine("If an option ends with $, that means you can use Regex.");
                                h.AddPostOptionsLine("Examples:");
                                foreach (var example in examples)
                                    h.AddPostOptionsLine(example);
                            }
                            h.AutoHelp = false;
                            h.AutoVersion = false;
                        }
                        else
                        {
                            h.AddPostOptionsText(@"For more documentation visit https://jand.jan0660.dev");
                        }

                        h.AddNewLineBetweenHelpSections = false;
                        h.AddDashesToOption = true;
                        h.AddPreOptionsLine(error is HelpVerbRequestedError ? "Commands:" : "Options:");
                        return h;
                    }, e => e, true);
                    Console.WriteLine(helpText.ToString());
                }
                else
                {
                    var helpText = HelpText.AutoBuild(parserResult, h =>
                    {
                        h.AutoHelp = false; // hides --help
                        h.AutoVersion = false; // hides --version
                        h.Heading = NameFormatted;
                        h.Copyright = "";
                        h.AdditionalNewLineAfterOption = false;
                        return HelpText.DefaultParsingErrorsHandler(parserResult, h);
                    }, e => e);
                    Console.WriteLine(helpText);
                }
            });
        }

        public static void DoProcessListIfEnabled(IpcClient client)
        {
            var env = Environment.GetEnvironmentVariable("JAND_PROCESS_LIST");
            if (env == null | env == "1")
                DoProcessList(client);
        }

        /// <summary>
        /// Shows process list.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="matchRegex">Show only processes that match this regex.</param>
        public static void DoProcessList(IpcClient client, Regex matchRegex = null)
        {
            var json = client.RequestString("get-processes", "");
            var status = client.GetStatus();
            var processes = JsonSerializer.Deserialize<JanDRuntimeProcess[]>(json);

            int maxNameLength = 0;
            int maxIndexLength = 0;
            foreach (var process in processes!)
            {
                if (process.Name.Length > maxNameLength)
                    maxNameLength = process.Name.Length;
                if (process.SafeIndex.ToString().Length > maxIndexLength)
                    maxIndexLength = process.SafeIndex.ToString().Length;
            }
            maxNameLength = maxNameLength < 12 ? 14 : maxNameLength;
            var nameFormatString = $"{{0,-{maxNameLength + 2}}}";
            var indexFormatString = $"{{0,-{maxIndexLength + 2}}}";
            Console.Write("{0,-6}", "R|E|A");
            Console.Write(indexFormatString, "Id");
            Console.Write(nameFormatString, "Name");
            Console.Write("{0,-5}", "↺");
            Console.Write("{0,-10}", "PID");
            Console.Write("{0,-7}", "Mem");
            Console.Write("{0,-7}", "Uptime");
            Console.Write("{0,-12}", "Cmd");
            Console.WriteLine();
            foreach (var process in processes)
            {
                if (matchRegex != null && !matchRegex.IsMatch(process.Name))
                    continue;
                Console.Write((process.Running ? TrueMark : FalseMark) + "|" +
                              (process.Enabled ? TrueMark : FalseMark) + "|"
                              + (process.AutoRestart ? TrueMark : FalseMark) + " ");
                Console.Write(indexFormatString, process.SafeIndex);
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
                        > (int)1e9 => (mem / (int)1e9) + "GB",
                        > (int)1e6 => (mem / (int)1e6) + "MB",
                        > (int)1e3 => (mem / (int)1e3) + "KB",
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
            if (Version != status.Version)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine("Daemon is running an outdated version of JanD: " + status.Version);
                Console.ResetColor();
            }
        }

        public static string GetResourceString(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream("JanD.Resources." + name))
            using (StreamReader reader = new StreamReader(stream!))
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
            public string Filename { get; set; }
            public string[] Arguments { get; set; }
            public string WorkingDirectory { get; set; }
            public int ProcessId { get; set; }
            public bool Stopped { get; set; }
            public int ExitCode { get; set; }
            public int RestartCount { get; set; }
            public bool Enabled { get; set; }
            public bool AutoRestart { get; set; }
            public bool Running { get; set; }
            public bool Watch { get; set; }
            public int SafeIndex { get; set; }
        }
    }
}