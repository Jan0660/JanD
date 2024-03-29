#pragma warning disable 8632
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

// not finished and not released as NuGet package yet ?
namespace Jan0660.DiscordWebhook
{
    public class DiscordWebhookClient
    {
        public const string DiscordUrl = "https://discord.com/api";
        private string _url;
        private HttpClient _httpClient = new();

        public DiscordWebhookClient(ulong id, string token)
        {
            _url = DiscordUrl + "/webhooks/" + id + "/" + token;
            Console.WriteLine(_url);
        }

        public async Task SendMessageAsync(string content = null, string username = null, string avatarUrl = null,
            List<DiscordEmbed> embeds = null)
        {
            HttpRequestMessage req = new(HttpMethod.Post, _url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(
                        new DiscordWebhookRequest()
                        {
                            Content = content, Username = username, AvatarUrl = avatarUrl, EmbedsList = embeds
                        }, new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase}),
                    Encoding.UTF8, "application/json")
            };
            var res = await _httpClient.SendAsync(req);
            Console.WriteLine(res.StatusCode + ": " + res.Content);
            req.Dispose();
            res.Dispose();
        }
    }

    public class DiscordWebhookRequest
    {
        public string Content { get; set; }
        public string Username { get; set; }
        [JsonPropertyName("avatar_url")] public string AvatarUrl { get; set; }

        public bool Tts { get; set; } = false;

        // todo: file
        public DiscordEmbed[] Embeds => EmbedsList.ToArray();

        [JsonIgnore] public List<DiscordEmbed> EmbedsList { get; set; } = new();
        // todo: allowed_mentions
    }

    public class DiscordEmbed
    {
        public string? Title { get; set; }
        public string? Type { get; set; } = "rich";
        public string? Description { get; set; }

        public string? Url { get; set; }

        // todo: timestamp
        public int Color { get; set; }

        // todo: footer
        [JsonIgnore] public string? ImageUrl { get; set; }
        public DiscordImage Image => new(ImageUrl);
        [JsonIgnore] public string? ThumbnailUrl { get; set; }

        public DiscordImage Thumbnail => new(ThumbnailUrl);
        // todo: provider
        // todo: author
        // todo: fields
    }

    public class DiscordImage
    {
        public string Url;
        public DiscordImage(string url) => Url = url;
    }
}