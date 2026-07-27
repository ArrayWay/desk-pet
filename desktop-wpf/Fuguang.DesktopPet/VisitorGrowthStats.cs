namespace Fuguang.DesktopPet;

/// <summary>按访客 Profile Id 分桶的成长统计。</summary>
public sealed class VisitorGrowthStats
{
    public int Affection { get; set; }
    public int Stamina { get; set; } = 80;
    public int Satiety { get; set; } = 75;
    public int Interactions { get; set; }
    public int MoodGainToday { get; set; }
    public int BugSearchesToday { get; set; }
    public int BugSearchBaseline { get; set; }
    public string Title { get; set; } = "新朋友";

    public void Normalize()
    {
        Affection = Math.Clamp(Affection, 0, 10000);
        Stamina = Math.Clamp(Stamina, 0, 100);
        Satiety = Math.Clamp(Satiety, 0, 100);
        Interactions = Math.Max(0, Interactions);
        MoodGainToday = Math.Clamp(MoodGainToday, 0, 12);
        BugSearchesToday = Math.Max(0, BugSearchesToday);
        BugSearchBaseline = Math.Max(0, BugSearchBaseline);
        Title = string.IsNullOrWhiteSpace(Title) ? "新朋友" : Title.Trim();
    }

    public VisitorGrowthStats Clone()
    {
        return new VisitorGrowthStats
        {
            Affection = Affection,
            Stamina = Stamina,
            Satiety = Satiety,
            Interactions = Interactions,
            MoodGainToday = MoodGainToday,
            BugSearchesToday = BugSearchesToday,
            BugSearchBaseline = BugSearchBaseline,
            Title = Title
        };
    }
}
