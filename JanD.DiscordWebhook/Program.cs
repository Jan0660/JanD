using System.Collections.Generic;
using System.IO;
using Discord.Webhook;
using JanD;
using System.Text.Json;
using Discord;

var config = JsonSerializer.Deserialize<Config>(await File.ReadAllTextAsync("./config.json"));
var discord = new DiscordWebhookClient(config!.WebhookId, config.WebhookToken);
var eventClient = new IpcClient(System.Environment.GetEnvironmentVariable("JAND_PIPE") ?? Program.DefaultPipeName);
var client = new IpcClient(System.Environment.GetEnvironmentVariable("JAND_PIPE") ?? Program.DefaultPipeName);
eventClient.RequestString("subscribe-events", "255");
byte[] bytes = new byte[10_000];
while (true)
{
    var count = eventClient.Stream.Read(bytes, 0, bytes.Length);
    var ev = JsonSerializer.Deserialize<IpcClient.DaemonClientEvent>(bytes[..count]);
    Program.JanDRuntimeProcess info;
    try
    {
        info = client.RequestJson<Program.JanDRuntimeProcess>("get-process-info", ev.Process);
    }
    catch
    {
        info = null;
    }

    discord.SendMessageAsync(embeds: new List<Embed>
    {
        new EmbedBuilder
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
                "procstart" => Color.Green,
                "procdel" => Color.Red,
                "procadd" => Color.Blue,
                "procstop" => Color.Red,
                _ => Color.Default
            }
        }.Build()
    });
}

public class Config
{
    public ulong WebhookId { get; set; }
    public string WebhookToken { get; set; }
}