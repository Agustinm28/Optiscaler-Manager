using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OptiscalerClient.Models.Help
{
    public class HelpPageConfig
    {
        [JsonPropertyName("pages")]
        public List<HelpPage> Pages { get; set; } = new();
    }

    public class HelpPage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("icon")]
        public string Icon { get; set; } = "&#xE8A5;";

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("fontSize")]
        public double? FontSize { get; set; }

        [JsonPropertyName("sections")]
        public List<HelpSection> Sections { get; set; } = new();
    }

    public class HelpSection
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("fontSize")]
        public double? FontSize { get; set; }

        [JsonPropertyName("items")]
        public List<HelpContentItem>? Items { get; set; }
    }

    public class HelpContentItem
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("fontSize")]
        public double? FontSize { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("items")]
        public List<HelpContentItem>? Items { get; set; }
    }
}
