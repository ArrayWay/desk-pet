using System.Text.Json.Serialization;

namespace Fuguang.DesktopPet;

public sealed class PetActionMessage
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;
}