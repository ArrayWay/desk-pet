namespace Fuguang.DesktopPet;

public sealed class VisitorIdentitySettings
{
    /// <summary>用户自定义访客显示名；空则回退 Profile.DisplayName。</summary>
    public string CustomName { get; set; } = string.Empty;
    /// <summary>访客备注（仅本地展示）。</summary>
    public string Note { get; set; } = string.Empty;

    public void Normalize()
    {
        CustomName = PetSettings.NormalizeDisplayText(CustomName, 16);
        Note = PetSettings.NormalizeDisplayText(Note, 40);
    }
}

public sealed class VisitorSettings
{
    public string ActiveVisitorId { get; set; } = VisitorProfile.Dog.Id;
    public bool Enabled { get; set; }
    public bool AutoVisit { get; set; }

    /// <summary>按访客 Profile Id 分桶的成长统计。</summary>
    public Dictionary<string, VisitorGrowthStats> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>旧扁平字段，仅迁移读取；保存前会从 active stats 镜像写回以便过渡期兼容。</summary>
    public int Affection { get; set; }
    public int Energy { get; set; } = 80;
    public int Interactions { get; set; }
    public int MoodGainToday { get; set; }
    public string Title { get; set; } = "新朋友";

    public DateOnly? LastMorningVisit { get; set; }
    public DateTimeOffset? LastEasterEggAt { get; set; }
    public DateTimeOffset? LastChaseAt { get; set; }
    public DateTimeOffset? LastConflictAt { get; set; }

    /// <summary>按访客 Profile Id 分桶的改名/备注，切换访客互不影响。</summary>
    public Dictionary<string, VisitorIdentitySettings> Identities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private bool _legacyMigrated;

    public void ResetDailyStatistics()
    {
        foreach (var stats in Stats.Values)
        {
            stats.MoodGainToday = 0;
            stats.BugSearchesToday = 0;
            stats.BugSearchBaseline = 0;
        }
        MoodGainToday = 0;
    }

    public VisitorIdentitySettings GetOrCreateIdentity(string visitorId)
    {
        Identities ??= new Dictionary<string, VisitorIdentitySettings>(StringComparer.OrdinalIgnoreCase);
        if (!Identities.TryGetValue(visitorId, out var identity) || identity is null)
        {
            identity = new VisitorIdentitySettings();
            Identities[visitorId] = identity;
        }

        identity.Normalize();
        return identity;
    }

    public VisitorGrowthStats GetOrCreateStats(string visitorId)
    {
        Stats ??= new Dictionary<string, VisitorGrowthStats>(StringComparer.OrdinalIgnoreCase);
        if (!VisitorProfile.TryGet(visitorId, out var profile))
        {
            profile = VisitorProfile.Dog;
        }

        if (!Stats.TryGetValue(profile.Id, out var stats) || stats is null)
        {
            stats = new VisitorGrowthStats();
            Stats[profile.Id] = stats;
        }

        stats.Normalize();
        return stats;
    }

    public VisitorGrowthStats ActiveStats => GetOrCreateStats(ActiveVisitorId);

    /// <summary>
    /// 旧访客扁平进度 50/50 分给 dog 与 training-dog。
    /// 取整向下；剩余差值由 dog 承担。
    /// </summary>
    public void MigrateLegacySharedProgress()
    {
        if (_legacyMigrated) return;
        Stats ??= new Dictionary<string, VisitorGrowthStats>(StringComparer.OrdinalIgnoreCase);

        var hasBucketed = Stats.Values.Any(s => s is not null && HasMeaningfulProgress(s));
        var hasLegacy = Affection > 0 || Interactions > 0 || Energy != 80 || !string.Equals(Title, "新朋友", StringComparison.Ordinal);

        if (!hasBucketed && hasLegacy)
        {
            var dog = SplitLegacyHalf(primary: true);
            var training = SplitLegacyHalf(primary: false);
            Stats[VisitorProfile.Dog.Id] = dog;
            Stats[VisitorProfile.TrainingDog.Id] = training;
        }

        // 确保两个已知访客至少有 stats 条目
        _ = GetOrCreateStats(VisitorProfile.Dog.Id);
        _ = GetOrCreateStats(VisitorProfile.TrainingDog.Id);

        SyncLegacyMirrorFromActive();
        _legacyMigrated = true;
    }

    private VisitorGrowthStats SplitLegacyHalf(bool primary)
    {
        // primary=dog 承担余数；secondary 取 floor(n/2)
        int Half(int value) => primary ? value - value / 2 : value / 2;

        return new VisitorGrowthStats
        {
            Affection = Half(Math.Max(0, Affection)),
            Stamina = Half(Math.Clamp(Energy, 0, 100)),
            Satiety = GrowthService.DefaultSatiety,
            Interactions = Half(Math.Max(0, Interactions)),
            MoodGainToday = 0,
            Title = primary ? (string.IsNullOrWhiteSpace(Title) ? "新朋友" : Title) : "新朋友"
        };
    }

    private static bool HasMeaningfulProgress(VisitorGrowthStats stats)
    {
        return stats.Affection > 0
            || stats.Interactions > 0
            || stats.Stamina != 80
            || stats.Satiety != GrowthService.DefaultSatiety
            || !string.Equals(stats.Title, "新朋友", StringComparison.Ordinal);
    }

    public void SyncLegacyMirrorFromActive()
    {
        var stats = ActiveStats;
        Affection = stats.Affection;
        Energy = stats.Stamina;
        Interactions = stats.Interactions;
        MoodGainToday = stats.MoodGainToday;
        Title = stats.Title;
    }

    public void Normalize()
    {
        if (!VisitorProfile.TryGet(ActiveVisitorId, out var profile))
        {
            profile = VisitorProfile.Dog;
        }

        ActiveVisitorId = profile.Id;
        Stats ??= new Dictionary<string, VisitorGrowthStats>(StringComparer.OrdinalIgnoreCase);
        var normalizedStats = new Dictionary<string, VisitorGrowthStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Stats)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null) continue;
            if (!VisitorProfile.TryGet(key, out var known)) continue;
            value.Normalize();
            normalizedStats[known.Id] = value;
        }
        Stats = normalizedStats;
        _ = GetOrCreateStats(ActiveVisitorId);
        SyncLegacyMirrorFromActive();

        Identities ??= new Dictionary<string, VisitorIdentitySettings>(StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, VisitorIdentitySettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Identities)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null) continue;
            if (!VisitorProfile.TryGet(key, out var known)) continue;
            value.Normalize();
            if (string.IsNullOrEmpty(value.CustomName) && string.IsNullOrEmpty(value.Note)) continue;
            normalized[known.Id] = value;
        }

        Identities = normalized;
    }
}
