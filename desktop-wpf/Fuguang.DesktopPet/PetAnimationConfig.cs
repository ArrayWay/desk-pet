using System.IO;
using System.Text.Json.Serialization;

namespace Fuguang.DesktopPet;

public sealed class PetAnimationConfig
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "浮光橙仔";

    [JsonPropertyName("spritesheet")]
    public SpritesheetConfig Spritesheet { get; init; } = new();

    [JsonPropertyName("states")]
    public Dictionary<string, PetStateConfig> States { get; init; } = [];

    public void Validate(int spritesheetWidth, int spritesheetHeight)
    {
        if (Spritesheet.CellWidth <= 0 || Spritesheet.CellHeight <= 0)
        {
            throw new InvalidDataException("动画配置中的单元格尺寸必须大于零。");
        }

        if (spritesheetWidth % Spritesheet.CellWidth != 0 || spritesheetHeight % Spritesheet.CellHeight != 0)
        {
            throw new InvalidDataException("图集尺寸无法按动画单元格尺寸整除。");
        }

        if (States.Count == 0)
        {
            throw new InvalidDataException("动画配置未定义任何状态。");
        }

        var columns = spritesheetWidth / Spritesheet.CellWidth;
        var rows = spritesheetHeight / Spritesheet.CellHeight;
        foreach (var (stateName, state) in States)
        {
            if (state.Frames <= 0 || state.Frames > columns)
            {
                throw new InvalidDataException($"状态“{stateName}”的帧数超出图集列数。");
            }

            if (state.Row < 0 || state.Row >= rows)
            {
                throw new InvalidDataException($"状态“{stateName}”的行号超出图集范围。");
            }

            if (state.IntervalMs <= 0)
            {
                throw new InvalidDataException($"状态“{stateName}”的帧间隔必须大于零。");
            }
        }
    }
}

public sealed class SpritesheetConfig
{
    [JsonPropertyName("webp")]
    public string Webp { get; init; } = "spritesheet.webp";

    [JsonPropertyName("pngFallback")]
    public string PngFallback { get; init; } = "spritesheet.png";

    [JsonPropertyName("cellWidth")]
    public int CellWidth { get; init; } = 192;

    [JsonPropertyName("cellHeight")]
    public int CellHeight { get; init; } = 208;
}

public sealed class PetStateConfig
{
    [JsonPropertyName("row")]
    public int Row { get; init; }

    [JsonPropertyName("frames")]
    public int Frames { get; init; }

    [JsonPropertyName("intervalMs")]
    public int IntervalMs { get; init; }

    [JsonPropertyName("loop")]
    public bool Loop { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;
}