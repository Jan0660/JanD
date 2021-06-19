using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Jan0660.DiscordWebhook;

namespace JanD.DiscordWebhook
{
    public class Program
    {
        public static async Task Main()
        {
            var config = JsonSerializer.Deserialize<Config>(await File.ReadAllTextAsync("./config.json"));
            Console.WriteLine("g");
            var discord = new DiscordWebhookClient(config!.WebhookId, config.WebhookToken);
            Console.WriteLine("r");
            var eventClient = new IpcClient(Environment.GetEnvironmentVariable("JAND_PIPE") ??
                                            JanD.Program.DefaultPipeName);
            var client = new IpcClient(Environment.GetEnvironmentVariable("JAND_PIPE") ??
                                       JanD.Program.DefaultPipeName);
            Console.WriteLine("b");
            Console.WriteLine(eventClient.RequestString("subscribe-events", "255"));
            Console.WriteLine("h");
            eventClient.ListenEvents(ev =>
            {
                Console.WriteLine(ev.Event);
                JanD.Program.JanDRuntimeProcess info;
                try
                {
                    info = client.RequestJson<JanD.Program.JanDRuntimeProcess>("get-process-info", ev.Process);
                }
                catch
                {
                    info = null;
                }

#pragma warning disable 4014
                discord.SendMessageAsync(embeds: new List<DiscordEmbed>
#pragma warning restore 4014
                {
                    new()
                    {
                        Title = ev!.Event switch
                        {
                            "procstart" => "Process Started",
                            "procdel" => "Process Deleted",
                            "procadd" => "Process Added",
                            "procstop" => "Process Stopped",
                            "procren" => "Process Renamed",
                            "procprop" => "Process Property Updated",
                            _ => ev.Event
                        },
                        Description = ev.Event switch
                        {
                            "procstart" => $"`{ev.Process}` has started.",
                            "procdel" => $"`{ev.Process}` has been deleted.",
                            "procadd" => @$"`{ev.Process}` has been added.
        **Command:** `{info.Command}`
        **Directory:** `{info.WorkingDirectory}`",
                            "procstop" => @$"`{ev.Process}` has stopped.
        **Exit Code:** `{info.ExitCode}`",
                            "procren" => $@"`{ev.Process}` => `{ev.Value}`",
                            "procprop" => $@"`{ev.Process}`
Property `{ev.Value[..ev.Value.IndexOf(':')]}` changed to `{ev.Value[(ev.Value.IndexOf(':') + 1)..]}`",
                            _ => ev.Process
                        },
                        Color = ev.Event switch
                        {
                            "procstart" => 0x002ECC71,
                            "procdel" => 0x00E74C3C,
                            "procadd" => 0x003498DB,
                            "procstop" => 0x00E74C3C,
                            "procren" => 0x003498DB,
                            "procprop" => 0x003498DB,
                            _ => 0x002F3136
                        }
                    }
                });
            });
        }
    }

    public class Config
    {
        public ulong WebhookId { get; set; }
        public string WebhookToken { get; set; }
    }
}