using System.Text.Json.Serialization;

namespace Fuguang.DesktopPet;

public sealed class PetEventMessage
{
    [JsonPropertyName("state")]
    public string State { get; init; } = "idle";

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("durationMs")]
    public int DurationMs { get; init; }

    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("priority")]
    public int Priority { get; init; }
}