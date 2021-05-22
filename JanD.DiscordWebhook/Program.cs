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
            var discord = new DiscordWebhookClient(config!.WebhookId, config.WebhookToken);
            var eventClient = new IpcClient(System.Environment.GetEnvironmentVariable("JAND_PIPE") ??
                                            JanD.Program.DefaultPipeName);
            var client = new IpcClient(System.Environment.GetEnvironmentVariable("JAND_PIPE") ??
                                       JanD.Program.DefaultPipeName);
            eventClient.RequestString("subscribe-events", "255");
            byte[] bytes = new byte[10_000];
            AsyncCallback callback = null;
            callback = state =>
            {
                Task.Factory.StartNew(() =>
                {
                    var count = eventClient.Stream.EndRead(state);
                    var ev = JsonSerializer.Deserialize<IpcClient.DaemonClientEvent>(bytes[..count]);
                    JanD.Program.JanDRuntimeProcess info;
                    try
                    {
                        info = client.RequestJson<JanD.Program.JanDRuntimeProcess>("get-process-info", ev.Process);
                    }
                    catch
                    {
                        info = null;
                    }

                    discord.SendMessageAsync(embeds: new List<DiscordEmbed>
                    {
                        new()
                        {
                            Title = ev!.Event switch
                            {
                                "procstart" => "Process Started",
                                "procdel" => "Process Deleted",
                                "procadd" => "Process Added",
                                "procstop" => "Process Stopped",
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
                                _ => ev.Process
                            },
                            Color = ev.Event switch
                            {
                                "procstart" => 0x002ECC71,
                                "procdel" => 0x00E74C3C,
                                "procadd" => 0x003498DB,
                                "procstop" => 0x00E74C3C,
                                _ => 0x002F3136
                            }
                        }
                    });
                    eventClient.Stream.BeginRead(bytes, 0, bytes.Length, callback, client.Stream);
                });
            };
            eventClient.Stream.BeginRead(bytes, 0, bytes.Length, callback, client.Stream);

            await Task.Delay(-1);
        }
    }

    public class Config
    {
        public ulong WebhookId { get; set; }
        public string WebhookToken { get; set; }
    }
}