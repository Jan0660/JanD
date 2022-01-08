global using JanD.Lib;
global using JanD.Lib.Objects;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace JanD
{
    public static class Program
    {
        public static string PipeName;
        public const string Version = ThisAssembly.Constants.Version;

        /// <summary>
        /// e.g. <c>JanD v0.7.0</c>
        /// </summary>
        public static readonly string NameFormatted = $"JanD v{Version}";

        public const string TextLogo =
            "[1m[38;2;216;160;223m[[22m[38;2;0;255;255mJanD[38;2;216;160;223m[1m][22m[39m";

        static async Task Main(string[] args)
        {
            PipeName = Environment.GetEnvironmentVariable("JAND_PIPE") ?? IpcClient.DefaultPipeName;
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
                    {
                        try
                        {
                            await command.Run();
                        }
                        catch (JanDClientException e)
                        {
                            Console.WriteLine("Daemon error: " + e.Message switch
                            {
                                "process-already-exists" => "Process already exists.",
                                _ => e.Message,
                            });
                        }
                    }
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
                else if (error is HelpVerbRequestedError or HelpRequestedError)
                {
                    var helpText = HelpText.AutoBuild(parserResult, h =>
                    {
                        h.MaximumDisplayWidth = Int32.MaxValue;
                        h.AutoVersion = true;
                        h.AdditionalNewLineAfterOption = false;
                        h.Heading = String.Format(Util.GetResourceString("info.txt").TrimEnd(), TextLogo, Version);
                        h.Copyright = "";
                        // when getting help for a specific command
                        if (error is HelpRequestedError)
                        {
                            var verb = parserResult.TypeInfo.Current.GetCustomAttribute<VerbAttribute>();
                            h.Heading = verb!.Name;
                            h.Copyright = "";
                            if (verb.HelpText != null)
                                h.AddPreOptionsLine(verb.HelpText);
                            var examples = parserResult.TypeInfo.Current.GetCustomAttribute<ExamplesAttribute>()
                                ?.Examples;
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
    }
}