using System.IO;

namespace Fuguang.DesktopPet;

[Flags]
public enum VisitorCapabilities
{
    None = 0,
    Petting = 1,
    Fetch = 2,
    BugSearch = 4,
    PlayfulChase = 8,
    ToyTease = 16,
    Handshake = 32,
    Feeding = 64,
    FrisbeeCatch = 128,
    ActiveGreeting = 256,
    All = Petting | Fetch | BugSearch | PlayfulChase | ToyTease | ActiveGreeting
}

public enum VisitorState
{
    Idle,
    RunningRight,
    RunningLeft,
    Sitting,
    LyingDown,
    Sleeping,
    WakingStretch,
    HappyCelebration,
    Sad,
    PettingResponse,
    ConfusedDodge,
    Waiting,
    Guarding,
    Comforting,
    SniffingRight,
    Peeking,
    CarryingBallRight,
    CarryingBallLeft,
    HalfSitPanting,
    HandshakeOffer,
    HandshakeSuccess,
    HandshakeSpin,
    FoodSniff,
    Eating,
    LickingThanks,
    TreatEating,
    FrisbeeWatch,
    FrisbeeRunRight,
    FrisbeeRunLeft,
    FrisbeeCatchRight,
    FrisbeeCatchLeft,
    FrisbeeLanding,
    FrisbeeReturnRight,
    FrisbeeReturnLeft,
    FrisbeeMiss,
    FrisbeeShowoffWithDisc
}

public sealed class VisitorProfile
{
    private VisitorProfile(
        string id,
        string displayName,
        string assetDirectoryName,
        string baseImageName,
        string? ballImageName,
        string? foodBowlImageName,
        string? frisbeeImageName,
        VisitorCapabilities capabilities,
        IReadOnlyDictionary<VisitorState, string> states)
    {
        Id = id;
        DisplayName = displayName;
        AssetDirectoryName = assetDirectoryName;
        BaseImageName = baseImageName;
        BallImageName = ballImageName;
        FoodBowlImageName = foodBowlImageName;
        FrisbeeImageName = frisbeeImageName;
        Capabilities = capabilities;
        States = states;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string AssetDirectoryName { get; }
    public string BaseImageName { get; }
    public string? BallImageName { get; }
    public string? FoodBowlImageName { get; }
    public string? FrisbeeImageName { get; }
    public VisitorCapabilities Capabilities { get; }
    public IReadOnlyDictionary<VisitorState, string> States { get; }

    public string Tooltip => $"{DisplayName}玩伴";

    public bool Supports(VisitorCapabilities capability)
    {
        return (Capabilities & capability) == capability;
    }

    public bool TryGetStateName(VisitorState state, out string stateName)
    {
        return States.TryGetValue(state, out stateName!);
    }

    public string GetBaseImagePath(string companionsDirectory)
    {
        return Path.Combine(companionsDirectory, BaseImageName);
    }

    public string GetAssetDirectory(string companionsDirectory)
    {
        return Path.Combine(companionsDirectory, AssetDirectoryName);
    }

    public string? GetBallImagePath(string companionsDirectory)
    {
        return BallImageName is null ? null : Path.Combine(GetAssetDirectory(companionsDirectory), BallImageName);
    }

    public string? GetFoodBowlImagePath(string companionsDirectory)
    {
        return FoodBowlImageName is null ? null : Path.Combine(GetAssetDirectory(companionsDirectory), FoodBowlImageName);
    }

    public string? GetFrisbeeImagePath(string companionsDirectory)
    {
        return FrisbeeImageName is null ? null : Path.Combine(GetAssetDirectory(companionsDirectory), FrisbeeImageName);
    }

    public bool TryValidateResources(string companionsDirectory, out string error)
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(AssetDirectoryName))
        {
            error = $"访客 Profile '{Id}' 缺少有效的 ID 或资源目录名。";
            return false;
        }

        if (!TryGetStateName(VisitorState.Idle, out _))
        {
            error = $"访客 '{Id}' 未声明必需状态 {VisitorState.Idle}。";
            return false;
        }

        var baseImagePath = GetBaseImagePath(companionsDirectory);
        if (!File.Exists(baseImagePath))
        {
            error = $"访客 '{Id}' 缺少基础图：{baseImagePath}";
            return false;
        }

        var assetDirectory = GetAssetDirectory(companionsDirectory);
        foreach (var (state, stateName) in States)
        {
            var stateDirectory = Path.Combine(assetDirectory, stateName);
            if (!Directory.Exists(stateDirectory) || !Directory.EnumerateFiles(stateDirectory, "*.png").Any())
            {
                error = $"访客 '{Id}' 的状态 {state} 缺少 PNG 帧：{stateDirectory}";
                return false;
            }
        }

        var ballImagePath = GetBallImagePath(companionsDirectory);
        if (Supports(VisitorCapabilities.Fetch) && (ballImagePath is null || !File.Exists(ballImagePath)))
        {
            error = $"访客 '{Id}' 声明了 Fetch 能力，但缺少球素材：{ballImagePath ?? "<未配置>"}";
            return false;
        }

        var foodBowlImagePath = GetFoodBowlImagePath(companionsDirectory);
        if (Supports(VisitorCapabilities.Feeding) && (foodBowlImagePath is null || !File.Exists(foodBowlImagePath)))
        {
            error = $"访客 '{Id}' 声明了 Feeding 能力，但缺少食盆素材：{foodBowlImagePath ?? "<未配置>"}";
            return false;
        }

        var frisbeeImagePath = GetFrisbeeImagePath(companionsDirectory);
        if (Supports(VisitorCapabilities.FrisbeeCatch) && (frisbeeImagePath is null || !File.Exists(frisbeeImagePath)))
        {
            error = $"访客 '{Id}' 声明了 FrisbeeCatch 能力，但缺少飞盘素材：{frisbeeImagePath ?? "<未配置>"}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static VisitorProfile Dog { get; } = new(
        id: "dog",
        displayName: "狗狗",
        assetDirectoryName: "dog",
        baseImageName: "dog.png",
        ballImageName: "ball.png",
        foodBowlImageName: null,
        frisbeeImageName: null,
        capabilities: VisitorCapabilities.All,
        states: new Dictionary<VisitorState, string>
        {
            [VisitorState.Idle] = "idle",
            [VisitorState.RunningRight] = "running-right",
            [VisitorState.RunningLeft] = "running-left",
            [VisitorState.Sitting] = "sitting",
            [VisitorState.LyingDown] = "lying-down",
            [VisitorState.Sleeping] = "sleeping",
            [VisitorState.WakingStretch] = "waking-stretch",
            [VisitorState.HappyCelebration] = "happy-celebration",
            [VisitorState.Sad] = "sad",
            [VisitorState.PettingResponse] = "petting-response",
            [VisitorState.ConfusedDodge] = "confused-dodge",
            [VisitorState.Waiting] = "waiting",
            [VisitorState.Guarding] = "guarding",
            [VisitorState.Comforting] = "comforting",
            [VisitorState.SniffingRight] = "sniffing-right",
            [VisitorState.Peeking] = "peeking",
            [VisitorState.CarryingBallRight] = "carrying-ball-right",
            [VisitorState.CarryingBallLeft] = "carrying-ball-left"
        });

    public static VisitorProfile TrainingDog { get; } = new(
        id: "training-dog",
        displayName: "训练犬",
        assetDirectoryName: "training-dog",
        baseImageName: Path.Combine("training-dog", "training-dog.png"),
        ballImageName: null,
        foodBowlImageName: "food-bowl.png",
        frisbeeImageName: "frisbee.png",
        capabilities: VisitorCapabilities.Petting | VisitorCapabilities.Handshake | VisitorCapabilities.Feeding | VisitorCapabilities.FrisbeeCatch | VisitorCapabilities.ActiveGreeting,
        states: new Dictionary<VisitorState, string>
        {
            [VisitorState.Idle] = "half-sit-panting",
            [VisitorState.RunningRight] = "running-right",
            [VisitorState.RunningLeft] = "running-left",
            [VisitorState.HalfSitPanting] = "half-sit-panting",
            [VisitorState.HandshakeOffer] = "handshake-offer",
            [VisitorState.HandshakeSuccess] = "handshake-success",
            [VisitorState.HandshakeSpin] = "handshake-spin",
            [VisitorState.PettingResponse] = "petting-response",
            [VisitorState.FoodSniff] = "food-sniff",
            [VisitorState.Eating] = "eating",
            [VisitorState.LickingThanks] = "licking-thanks",
            [VisitorState.TreatEating] = "treat-eating",
            [VisitorState.FrisbeeWatch] = "frisbee-watch",
            [VisitorState.FrisbeeRunRight] = "frisbee-run-right",
            [VisitorState.FrisbeeRunLeft] = "frisbee-run-left",
            [VisitorState.FrisbeeCatchRight] = "frisbee-catch-right",
            [VisitorState.FrisbeeCatchLeft] = "frisbee-catch-left",
            [VisitorState.FrisbeeLanding] = "frisbee-landing",
            [VisitorState.FrisbeeReturnRight] = "frisbee-return-right",
            [VisitorState.FrisbeeReturnLeft] = "frisbee-return-left",
            [VisitorState.FrisbeeMiss] = "frisbee-miss",
            [VisitorState.FrisbeeShowoffWithDisc] = "frisbee-showoff-with-disc"
        });

    public static IReadOnlyCollection<VisitorProfile> Registered { get; } = Array.AsReadOnly([Dog, TrainingDog]);

    private static readonly IReadOnlyDictionary<string, VisitorProfile> RegisteredProfiles =
        Registered.ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string? id, out VisitorProfile profile)
    {
        if (id is not null && RegisteredProfiles.TryGetValue(id, out profile!))
        {
            return true;
        }

        profile = Dog;
        return false;
    }
}