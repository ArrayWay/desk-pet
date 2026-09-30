namespace Fuguang.DesktopPet;

public enum GrowthAction
{
    MainClick,
    MainDoubleClick,
    MainFocusComplete,
    MainCommitCeremony,
    MainIdleRecover,
    MainIdleDecay,
    VisitorPet,
    VisitorFetch,
    VisitorFrisbeeSuccess,
    VisitorFrisbeeFail,
    VisitorDogFood,
    VisitorTreat,
    VisitorInteraction,
    VisitorStaminaRecover,
    VisitorChase,
    VisitorToyTease,
    VisitorBugSearchSuccess,
    VisitorBugSearchFail,
    TickSatietyDecay
}

public readonly record struct GrowthResult(
    int AffectionDelta,
    int StaminaDelta,
    int SatietyDelta,
    int MoodDelta,
    int InteractionsDelta,
    bool SoftPenaltyApplied,
    string? Hint);

public readonly record struct VisitorTitleProgress(
    string CurrentTitle,
    string NextTitle,
    int CurrentAffection,
    int CurrentInteractions,
    string CurrentRequirement,
    string NextRequirement,
    IReadOnlyList<string> History);

/// <summary>统一养成结算入口。窗口层只报事件，不直接散落加减数值。</summary>
public static class GrowthService
{
    public const int SoftStaminaThreshold = 25;
    public const int SoftSatietyThreshold = 25;
    public const int DefaultSatiety = 75;
    // Play-specific soft tips (still allow play; only warn earlier than global soft gate).
    public const int SoftFetchStaminaHint = 12;
    public const int SoftFetchSatietyHint = 20;
    public const int SoftFrisbeeStaminaHint = 20;
    public const int SoftFrisbeeSatietyHint = SoftSatietyThreshold;

    public static GrowthResult ApplyMain(PetSettings settings, GrowthAction action)
    {
        // Growth disabled: seal current values (no deltas applied).
        if (!settings.GrowthEnabled)
        {
            return new GrowthResult(0, 0, 0, 0, 0, false, null);
        }

        var result = ResolveMain(action);
        ApplyToMain(settings, result);
        return result;
    }

    public static GrowthResult ApplyVisitor(PetSettings settings, GrowthAction action, bool success = true)
    {
        if (!settings.GrowthEnabled)
        {
            return new GrowthResult(0, 0, 0, 0, 0, false, null);
        }

        var stats = settings.Visitor.GetOrCreateStats(settings.Visitor.ActiveVisitorId);
        var result = Resolve(action, stats.Stamina, stats.Satiety, success);
        ApplyToVisitor(settings, stats, result);
        return result;
    }

    public static string ComputeVisitorTitle(VisitorGrowthStats stats)
    {
        if (stats.Affection >= 100 || stats.Interactions >= 30) return "最佳搭档";
        if (stats.Affection >= 25 || stats.Interactions >= 8) return "好伙伴";
        return "新朋友";
    }

    public static VisitorTitleProgress GetVisitorTitleProgress(VisitorGrowthStats stats)
    {
        var current = ComputeVisitorTitle(stats);
        var next = current switch
        {
            "新朋友" => "好伙伴",
            "好伙伴" => "最佳搭档",
            _ => "已达最高阶段"
        };
        var currentRequirement = current switch
        {
            "新朋友" => "尚未达到好伙伴：亲密度 25 或互动 8 次",
            "好伙伴" => "已达到好伙伴：亲密度 25 或互动 8 次",
            _ => "已达到最高阶段：亲密度 100 或互动 30 次"
        };
        var nextRequirement = current switch
        {
            "新朋友" => "亲密度 25 或互动 8 次",
            "好伙伴" => "亲密度 100 或互动 30 次",
            _ => "继续陪伴，称号历程已完成"
        };
        return new VisitorTitleProgress(
            current,
            next,
            stats.Affection,
            stats.Interactions,
            currentRequirement,
            nextRequirement,
            new[] { "新朋友", "好伙伴", "最佳搭档" });
    }

    public static bool IsSoftLow(int stamina, int satiety) =>
        stamina < SoftStaminaThreshold || satiety < SoftSatietyThreshold;

    private static GrowthResult ResolveMain(GrowthAction action)
    {
        return action switch
        {
            GrowthAction.MainClick => new GrowthResult(+1, 0, 0, +1, 0, false, null),
            GrowthAction.MainDoubleClick => new GrowthResult(+1, 0, 0, +1, 0, false, null),
            GrowthAction.MainFocusComplete => new GrowthResult(+2, 0, 0, +2, 0, false, null),
            GrowthAction.MainCommitCeremony => new GrowthResult(+2, 0, 0, +1, 0, false, null),
            GrowthAction.MainIdleRecover => new GrowthResult(0, 0, 0, +1, 0, false, null),
            GrowthAction.MainIdleDecay => new GrowthResult(0, 0, 0, -1, 0, false, null),
            _ => new GrowthResult(0, 0, 0, 0, 0, false, null)
        };
    }

    private static GrowthResult Resolve(GrowthAction action, int stamina, int satiety, bool success)
    {
        var soft = IsSoftLow(stamina, satiety);
        var affectionMul = soft ? 0.5 : 1.0;
        string? hint = null;
        if (soft)
        {
            hint = stamina < SoftStaminaThreshold && satiety < SoftSatietyThreshold
                ? "有点累又有点饿，收益会降低。"
                : stamina < SoftStaminaThreshold
                    ? "精力偏低，建议先休息。"
                    : "有点饿了，喂点东西会更好。";
        }

        return action switch
        {
            GrowthAction.VisitorPet => new GrowthResult(
                Scale(+1, affectionMul), -2, -1, +1, 0, soft, hint),
            GrowthAction.VisitorFetch => new GrowthResult(
                Scale(+3, affectionMul), -12, -4, +2, 1, soft, hint),
            GrowthAction.VisitorFrisbeeSuccess => new GrowthResult(
                Scale(+4, affectionMul), -20, -6, +3, 1, soft, hint),
            GrowthAction.VisitorFrisbeeFail => new GrowthResult(
                Scale(+1, affectionMul), -20, -6, 0, 1, soft, hint),
            GrowthAction.VisitorDogFood => new GrowthResult(+2, +4, +18, +1, 1, false, null),
            GrowthAction.VisitorTreat => new GrowthResult(+1, +2, +10, +1, 1, false, null),
            GrowthAction.VisitorInteraction => new GrowthResult(0, 0, 0, 0, 1, false, null),
            GrowthAction.VisitorStaminaRecover => new GrowthResult(0, +2, 0, 0, 0, false, null),
            GrowthAction.VisitorChase => new GrowthResult(0, -5, -1, 0, 0, false, null),
            GrowthAction.VisitorToyTease => new GrowthResult(0, -4, -1, 0, 0, false, null),
            GrowthAction.VisitorBugSearchSuccess => new GrowthResult(Scale(+3, affectionMul), -1, 0, +1, 1, soft, hint),
            GrowthAction.VisitorBugSearchFail => new GrowthResult(Scale(+1, affectionMul), 0, 0, 1, 1, false, null),
            GrowthAction.TickSatietyDecay => new GrowthResult(0, 0, -1, 0, 0, false, null),
            _ => new GrowthResult(0, 0, 0, 0, 0, false, null)
        };
    }

    private static int Scale(int value, double mul)
    {
        if (value == 0 || mul >= 0.999) return value;
        // Soft penalty may reduce +1 actions to 0 so the gate is actually felt.
        if (value > 0) return Math.Max(0, (int)Math.Floor(value * mul));
        return value;
    }

    private static void ApplyToMain(PetSettings settings, GrowthResult result)
    {
        settings.Affection = Math.Clamp(settings.Affection + result.AffectionDelta, 0, 10000);
        settings.Stamina = Math.Clamp(settings.Stamina + result.StaminaDelta, 0, 100);
        settings.Satiety = Math.Clamp(settings.Satiety + result.SatietyDelta, 0, 100);
        if (result.MoodDelta != 0)
        {
            settings.Mood = Math.Clamp(settings.Mood + result.MoodDelta, 0, 100);
        }
    }

    private static void ApplyToVisitor(PetSettings settings, VisitorGrowthStats stats, GrowthResult result)
    {
        stats.Affection = Math.Clamp(stats.Affection + result.AffectionDelta, 0, 10000);
        stats.Stamina = Math.Clamp(stats.Stamina + result.StaminaDelta, 0, 100);
        stats.Satiety = Math.Clamp(stats.Satiety + result.SatietyDelta, 0, 100);
        stats.Interactions = Math.Max(0, stats.Interactions + result.InteractionsDelta);

        if (result.MoodDelta > 0)
        {
            var granted = Math.Min(result.MoodDelta, 12 - stats.MoodGainToday);
            if (granted > 0)
            {
                stats.MoodGainToday += granted;
                settings.Mood = Math.Min(100, settings.Mood + granted);
            }
        }

        var title = ComputeVisitorTitle(stats);
        stats.Title = title;
        settings.Visitor.SyncLegacyMirrorFromActive();
    }
}
