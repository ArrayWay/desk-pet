using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuguang.DesktopPet;

public sealed class PetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public string ScreenDeviceName { get; set; } = string.Empty;
    public bool AutomaticMovement { get; set; } = true;
    public bool Topmost { get; set; } = true;
    public bool Muted { get; set; }
    public double MovementSpeed { get; set; } = 1.25;
    public double AnimationSpeed { get; set; } = 1;
    public bool BubbleEnabled { get; set; } = true;
    /// <summary>是否启用养成系统。关闭后数值封存不再结算，直到再次开启。</summary>
    public bool GrowthEnabled { get; set; } = true;
    public bool StatusBarEnabled { get; set; } = true;
    public bool BreakRemindersEnabled { get; set; } = true;
    public bool WaterRemindersEnabled { get; set; }
    public bool EyeRemindersEnabled { get; set; }
    public int BreakReminderMinutes { get; set; } = 60;
    public int WaterReminderMinutes { get; set; } = 45;
    public int EyeReminderMinutes { get; set; } = 30;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int FocusSessionsToday { get; set; }
    public int FocusMinutesToday { get; set; }
    public DateOnly StatisticsDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int Affection { get; set; }
    public int Mood { get; set; } = 70;
    /// <summary>精力（游玩/互动消耗）。旧档 Energy 会迁移到此字段。</summary>
    public int Stamina { get; set; } = 80;
    /// <summary>饱食。喂食回升、时间缓慢衰减。v1 主宠不喂食。</summary>
    public int Satiety { get; set; } = GrowthService.DefaultSatiety;
    /// <summary>兼容旧 JSON 的 Energy 字段；仅用于迁移读取，保存时不再写出。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? Energy { get; set; }
    /// <summary>用户自定义主宠显示名；空则回退动画配置 displayName。</summary>
    public string CustomName { get; set; } = string.Empty;
    /// <summary>主宠备注（仅本地展示，不参与玩法逻辑）。</summary>
    public string Note { get; set; } = string.Empty;
    public string MainPetSkin { get; set; } = "default";
    public VisitorSettings Visitor { get; set; } = new();

    public static PetSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new PetSettings();
            var settings = JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(path)) ?? new PetSettings();
            settings.MigrateFromLegacy();
            settings.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            return new PetSettings();
        }
        catch (IOException)
        {
            return new PetSettings();
        }
    }

    public void Save(string path)
    {
        Normalize();
        // 保存时不再写旧 Energy 字段。
        Energy = null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, path, true);
    }

    public void ResetDailyStatisticsIfNeeded()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (StatisticsDate == today) return;
        StatisticsDate = today;
        FocusSessionsToday = 0;
        FocusMinutesToday = 0;
        Visitor.ResetDailyStatistics();
        // When growth is sealed off, keep mood/stamina frozen at sealed values.
        if (GrowthEnabled)
        {
            Mood = Math.Max(Mood, 65);
            Stamina = Math.Max(Stamina, 75);
        }
    }

    /// <summary>一次性兼容：旧 Energy → Stamina；缺省 Satiety；访客 50/50 分桶。</summary>
    private void MigrateFromLegacy()
    {
        if (Energy is int legacyEnergy)
        {
            // 仅当 Stamina 仍是默认且旧 Energy 存在时，优先采用旧值。
            if (Stamina == 80 || Stamina == 0)
            {
                Stamina = legacyEnergy;
            }
        }

        if (Satiety <= 0)
        {
            Satiety = GrowthService.DefaultSatiety;
        }

        Visitor ??= new VisitorSettings();
        Visitor.MigrateLegacySharedProgress();
    }

    private void Normalize()
    {
        MovementSpeed = Math.Clamp(MovementSpeed, 0.25, 5);
        AnimationSpeed = Math.Clamp(AnimationSpeed, 0.5, 2);
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        BreakMinutes = Math.Clamp(BreakMinutes, 1, 60);
        BreakReminderMinutes = Math.Clamp(BreakReminderMinutes, 10, 240);
        WaterReminderMinutes = Math.Clamp(WaterReminderMinutes, 10, 240);
        EyeReminderMinutes = Math.Clamp(EyeReminderMinutes, 10, 240);
        Affection = Math.Clamp(Affection, 0, 10000);
        Mood = Math.Clamp(Mood, 0, 100);
        Stamina = Math.Clamp(Stamina, 0, 100);
        Satiety = Math.Clamp(Satiety, 0, 100);
        CustomName = NormalizeDisplayText(CustomName, 16);
        Note = NormalizeDisplayText(Note, 40);
        MainPetSkin = string.Equals(MainPetSkin, "person2", StringComparison.OrdinalIgnoreCase)
            ? "person2"
            : "default";
        Visitor ??= new VisitorSettings();
        Visitor.Normalize();
        FocusSessionsToday = Math.Max(0, FocusSessionsToday);
        FocusMinutesToday = Math.Max(0, FocusMinutesToday);
    }

    internal static string NormalizeDisplayText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim().Replace("\r", " ").Replace("\n", " ");
        while (trimmed.Contains("  ", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("  ", " ", StringComparison.Ordinal);
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
