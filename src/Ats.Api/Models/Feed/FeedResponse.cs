using System.Text.Json.Serialization;

namespace Ats.Api.Models.Feed;

public sealed class FeedResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("_embedded")] public FeedEmbedded Embedded { get; set; } = new();
}

public sealed class FeedEmbedded
{
    [JsonPropertyName("jobs")] public List<FeedJob> Jobs { get; set; } = new();
}

public sealed class FeedJob
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "H";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("location")] public FeedLocation Location { get; set; } = new();
    [JsonPropertyName("_embedded")] public FeedJobEmbedded Embedded { get; set; } = new();
}

public sealed class FeedLocation
{
    [JsonPropertyName("city")] public string? City { get; set; }
}

public sealed class FeedJobEmbedded
{
    [JsonPropertyName("status")] public FeedStatus Status { get; set; } = new();
}

public sealed class FeedStatus
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
}
