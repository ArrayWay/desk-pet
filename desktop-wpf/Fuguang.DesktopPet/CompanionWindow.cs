using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Fuguang.DesktopPet;

public sealed class CompanionWindow : Window
{
    private const double HostGap = 10;
    private readonly Window _host;
    private readonly VisitorProfile _profile;
    private readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromMilliseconds(24) };
    private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _stateRestoreTimer = new();
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(18) };
    private readonly DispatcherTimer _fetchTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _frisbeeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _feedingTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _responseTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer _pettingTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly System.Windows.Controls.Image _companionImage;
    private readonly string? _ballImagePath;
    private readonly string? _frisbeeImagePath;
    private readonly Dictionary<string, BitmapSource[]> _animations = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<int>? _activeFrameIntervalsMs;
    private readonly RotateTransform _responseRotation = new();
    private readonly ScaleTransform _responseScale = new(1, 1);
    private readonly TranslateTransform _responseTranslation = new();
    private byte[] _alphaPixels = [];
    private static readonly TimeSpan EnergyRecoveryInterval = TimeSpan.FromSeconds(2);
    private int _pixelWidth;
    private int _pixelHeight;
    private string _currentState = "idle";
    private int _frameIndex;
    private int _activePriority;
    private int _idleStateIndex;
    private double _targetLeft;
    private double _targetTop;
    private double _approachOffset;
    private int _rapidClickCount;
    private DateTime _rapidClickStartedAt = DateTime.MinValue;
    private DateTime _lastAffectionAt = DateTime.MinValue;
    private DateTime _handshakeCooldownEndsAt = DateTime.MinValue;
    private DateTime _lastEnergyRecoveryAt = DateTime.UtcNow;
    private DateTime _chaseStartedAt = DateTime.MinValue;
    private DateTime _chaseEndsAt = DateTime.MinValue;
    private bool _chaseCarriesBall;
    private bool _pettingCandidate;
    private bool _awaitingHandshake;
    private bool _playHandshakeSpin;
    private bool _playFrisbeeShowoff;
    private bool _opensProblems;
    private int _fetchPhase;
    private double _fetchTargetLeft;
    private double _fetchTargetTop;
    private bool _frisbeeReturnOnLeft;
    private BallWindow? _ballWindow;
    private FrisbeeWindow? _frisbeeWindow;
    private int _frisbeePhase;
    private DateTime _frisbeeFlightStartedAt = DateTime.MinValue;
    private DateTime _frisbeePhaseStartedAt = DateTime.MinValue;
    private System.Windows.Point _frisbeeStart;
    private System.Windows.Point _frisbeeEnd;
    private double _frisbeeArcHeight;
    private double _frisbeeFlightSeconds;
    private bool _frisbeeWillCatch;
    private double _frisbeeInterceptLeft;
    private double _frisbeeInterceptTop;
    private int _feedingPhase;
    private DateTime _feedingPhaseEndsAt = DateTime.MinValue;
    private DateTime _feedingCooldownEndsAt = DateTime.MinValue;

    public event Action<int>? AffectionChanged;
    public event Action? InteractionCompleted;
    public event Action? EnergyRecoveryRequested;
    public event Action? ProblemsRequested;
    public event Action? FetchCompleted;
    public event Action<bool>? FrisbeeCompleted;
    public event Action? DogFoodCompleted;
    public event Action? TreatCompleted;
    public event Action? Approached;

    public bool IsBusy => _activePriority > 0 || _fetchPhase != 0 || _frisbeePhase != 0 || _feedingPhase != 0;
    public int FeedingCooldownSeconds => Math.Max(0, (int)Math.Ceiling((_feedingCooldownEndsAt - DateTime.UtcNow).TotalSeconds));
    public VisitorProfile Profile => _profile;

    public CompanionWindow(Window host, string companionsDirectory, VisitorProfile profile)
    {
        _host = host;
        _profile = profile;
        var imagePath = profile.GetBaseImagePath(companionsDirectory);
        var assetDirectory = profile.GetAssetDirectory(companionsDirectory);
        _ballImagePath = profile.GetBallImagePath(companionsDirectory);
        _frisbeeImagePath = profile.GetFrisbeeImagePath(companionsDirectory);
        Width = 168;
        Height = 182;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = host.Topmost;
        IsHitTestVisible = true;

        LoadAnimations(assetDirectory);
        var image = TryGetFrames(VisitorState.Idle, out var idleFrames)
            ? idleFrames[0]
            : LoadBitmap(imagePath);

        _companionImage = new System.Windows.Controls.Image
        {
            Source = image,
            Width = Width,
            Height = Height,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            ToolTip = profile.Tooltip
        };
        var responseTransform = new TransformGroup();
        responseTransform.Children.Add(_responseScale);
        responseTransform.Children.Add(_responseRotation);
        responseTransform.Children.Add(_responseTranslation);
        _companionImage.RenderTransform = responseTransform;
        _companionImage.RenderTransformOrigin = new System.Windows.Point(0.5, 0.8);
        RenderOptions.SetBitmapScalingMode(_companionImage, BitmapScalingMode.HighQuality);
        Content = _companionImage;
        _followTimer.Tick += FollowTimer_Tick;
        _animationTimer.Tick += AnimationTimer_Tick;
        _stateRestoreTimer.Tick += StateRestoreTimer_Tick;
        _idleTimer.Tick += IdleTimer_Tick;
        _fetchTimer.Tick += FetchTimer_Tick;
        _frisbeeTimer.Tick += FrisbeeTimer_Tick;
        _feedingTimer.Tick += FeedingTimer_Tick;
        _responseTimer.Tick += ResponseTimer_Tick;
        _pettingTimer.Tick += PettingTimer_Tick;
        MouseLeftButtonDown += CompanionWindow_MouseLeftButtonDown;
        MouseLeftButtonUp += CompanionWindow_MouseLeftButtonUp;
        MouseRightButtonDown += CompanionWindow_MouseRightButtonDown;
        MouseEnter += (_, _) => Approached?.Invoke();
        SetFrame(image);
    }

    public void SetDisplayTooltip(string? displayName, string? note = null)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? _profile.DisplayName : displayName.Trim();
        var tip = $"{name}玩伴";
        if (!string.IsNullOrWhiteSpace(note))
        {
            tip = $"{tip}\n备注：{note.Trim()}";
        }

        _companionImage.ToolTip = tip;
    }

    public void Visit()
    {
        UpdateTarget();
        if (double.IsNaN(Left)) Left = _targetLeft;
        if (double.IsNaN(Top)) Top = _targetTop;
        RestoreAmbientState();
        Show();
        _followTimer.Start();
        _animationTimer.Start();
        _idleTimer.Start();
    }

    public void GoHome()
    {
        _followTimer.Stop();
        _animationTimer.Stop();
        _stateRestoreTimer.Stop();
        _idleTimer.Stop();
        CancelFetchBehavior();
        CancelFrisbeeBehavior();
        CancelFeedingBehavior();
        _responseTimer.Stop();
        _pettingTimer.Stop();
        ClearMovingBehavior();
        _awaitingHandshake = false;
        _playHandshakeSpin = false;
        _playFrisbeeShowoff = false;
        _activePriority = 0;
        _opensProblems = false;
        Hide();
    }

    public void Stop()
    {
        _followTimer.Stop();
        _animationTimer.Stop();
        _stateRestoreTimer.Stop();
        _idleTimer.Stop();
        CancelFetchBehavior(closeBallWindow: true);
        _ballWindow = null;
        CancelFrisbeeBehavior(closeFrisbeeWindow: true);
        _frisbeeWindow = null;
        CancelFeedingBehavior();
        _responseTimer.Stop();
        _pettingTimer.Stop();
        ClearMovingBehavior();
        _awaitingHandshake = false;
        _playHandshakeSpin = false;
        _playFrisbeeShowoff = false;
        Close();
    }

    private void ClearMovingBehavior()
    {
        _chaseStartedAt = DateTime.MinValue;
        _chaseEndsAt = DateTime.MinValue;
        _chaseCarriesBall = false;
    }

    private void CancelFetchBehavior(bool closeBallWindow = false)
    {
        _fetchTimer.Stop();
        _fetchPhase = 0;
        if (closeBallWindow)
        {
            _ballWindow?.Close();
        }
        else
        {
            _ballWindow?.Hide();
        }
    }

    private void CancelFrisbeeBehavior(bool closeFrisbeeWindow = false)
    {
        _frisbeeTimer.Stop();
        _frisbeePhase = 0;
        _frisbeeFlightStartedAt = DateTime.MinValue;
        _frisbeePhaseStartedAt = DateTime.MinValue;
        if (closeFrisbeeWindow)
        {
            _frisbeeWindow?.Close();
        }
        else
        {
            _frisbeeWindow?.Hide();
        }
    }

    private void CancelFeedingBehavior()
    {
        _feedingTimer.Stop();
        _feedingPhase = 0;
        _feedingPhaseEndsAt = DateTime.MinValue;
    }

    private void CompanionWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var clickPosition = e.GetPosition(_companionImage);
        if (!IsOpaqueAt(clickPosition)) return;

        if (_opensProblems)
        {
            ProblemsRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (_profile.Supports(VisitorCapabilities.Handshake) && _awaitingHandshake)
        {
            HandleHandshakeClick(clickPosition);
            e.Handled = true;
            return;
        }

        if (_profile.Id == "training-dog" && e.ClickCount == 1)
        {
            StartHandshake();
            e.Handled = true;
            return;
        }

        if (!_profile.Supports(VisitorCapabilities.Petting))
        {
            if (_profile.Supports(VisitorCapabilities.Handshake))
            {
                HandleHandshakeClick(clickPosition);
                e.Handled = true;
            }
            return;
        }

        _pettingCandidate = true;
        _pettingTimer.Stop();
        _pettingTimer.Start();
        CaptureMouse();

        var now = DateTime.UtcNow;
        if (now - _rapidClickStartedAt > TimeSpan.FromSeconds(2))
        {
            _rapidClickStartedAt = now;
            _rapidClickCount = 0;
        }

        _rapidClickCount += 1;
        if (_rapidClickCount >= 5)
        {
            PlayState(VisitorState.ConfusedDodge, 1400, 55);
            ShowResponse(rotation: 7, scale: 0.96, verticalOffset: 2, durationMs: 900);
            _rapidClickCount = 0;
        }
        else if (e.ClickCount >= 2)
        {
            _approachOffset = Width * 0.34;
            PlayState(VisitorState.HappyCelebration, 1400, 55);
            ShowResponse(rotation: 0, scale: 1.05, verticalOffset: -5, durationMs: 1100);
            TryIncreaseAffection();
            InteractionCompleted?.Invoke();
        }
        else
        {
            PlayState(VisitorState.PettingResponse, 900, 50);
            ShowResponse(rotation: 0, scale: 1, verticalOffset: 0, durationMs: 520);
        }

        e.Handled = true;
    }

    private void CompanionWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_profile.Id != "training-dog" || !IsOpaqueAt(e.GetPosition(_companionImage))) return;
        if (_opensProblems || _awaitingHandshake) return;

        PlayState(VisitorState.PettingResponse, 900, 50);
        ShowResponse(rotation: 0, scale: 1.15, verticalOffset: 0, durationMs: 900, originY: 0.42);
        TryIncreaseAffection();
        InteractionCompleted?.Invoke();
        e.Handled = true;
    }

    private void HandleHandshakeClick(System.Windows.Point position)
    {
        if (!_awaitingHandshake)
        {
            StartHandshake();
            return;
        }

        if (!IsHandshakePawAt(position))
        {
            ShowResponse(rotation: 5, scale: 0.98, verticalOffset: 1, durationMs: 650);
            return;
        }

        _awaitingHandshake = false;
        _handshakeCooldownEndsAt = DateTime.UtcNow.AddSeconds(8);
        PlayState(VisitorState.HandshakeSuccess, 1500, 60);
        _playHandshakeSpin = Random.Shared.Next(6) == 0;
        ShowResponse(rotation: 0, scale: 1.04, verticalOffset: -4, durationMs: 900);
        TryIncreaseAffection();
        InteractionCompleted?.Invoke();
    }

    public bool StartHandshake()
    {
        if (DateTime.UtcNow < _handshakeCooldownEndsAt || IsBusy || !_profile.Supports(VisitorCapabilities.Handshake)) return false;
        _awaitingHandshake = PlayState(VisitorState.HandshakeOffer, 2000, 45);
        return _awaitingHandshake;
    }

    private bool IsHandshakePawAt(System.Windows.Point position)
    {
        var viewWidth = _companionImage.ActualWidth > 0 ? _companionImage.ActualWidth : Width;
        var viewHeight = _companionImage.ActualHeight > 0 ? _companionImage.ActualHeight : Height;
        var scale = Math.Min(viewWidth / _pixelWidth, viewHeight / _pixelHeight);
        var renderedWidth = _pixelWidth * scale;
        var renderedHeight = _pixelHeight * scale;
        var normalizedX = (position.X - (viewWidth - renderedWidth) / 2) / renderedWidth;
        var normalizedY = (position.Y - (viewHeight - renderedHeight) / 2) / renderedHeight;
        return normalizedX is >= 0.08 and <= 0.48 && normalizedY is >= 0.42 and <= 0.76;
    }

    private void CompanionWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pettingCandidate = false;
        _pettingTimer.Stop();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PettingTimer_Tick(object? sender, EventArgs e)
    {
        if (!_pettingCandidate || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _pettingTimer.Stop();
            return;
        }
        PlayState(VisitorState.PettingResponse, 1200, 50);
        ShowResponse(rotation: 0, scale: 1, verticalOffset: 0, durationMs: 1200);
        TryIncreaseAffection();
    }

    private void TryIncreaseAffection()
    {
        var now = DateTime.UtcNow;
        if (now - _lastAffectionAt < TimeSpan.FromSeconds(2)) return;
        _lastAffectionAt = now;
        AffectionChanged?.Invoke(1);
    }

    private bool IsOpaqueAt(System.Windows.Point position)
    {
        var viewWidth = _companionImage.ActualWidth > 0 ? _companionImage.ActualWidth : Width;
        var viewHeight = _companionImage.ActualHeight > 0 ? _companionImage.ActualHeight : Height;
        var scale = Math.Min(viewWidth / _pixelWidth, viewHeight / _pixelHeight);
        var renderedWidth = _pixelWidth * scale;
        var renderedHeight = _pixelHeight * scale;
        var imageX = position.X - (viewWidth - renderedWidth) / 2;
        var imageY = position.Y - (viewHeight - renderedHeight) / 2;
        if (imageX < 0 || imageY < 0 || imageX >= renderedWidth || imageY >= renderedHeight) return false;

        var pixelX = Math.Clamp((int)(imageX / scale), 0, _pixelWidth - 1);
        var pixelY = Math.Clamp((int)(imageY / scale), 0, _pixelHeight - 1);
        return _alphaPixels[(pixelY * _pixelWidth + pixelX) * 4 + 3] >= 24;
    }

    private void ShowResponse(double rotation, double scale, double verticalOffset, int durationMs, double originY = 0.8)
    {
        _responseTimer.Stop();
        _responseRotation.Angle = rotation;
        _responseScale.ScaleX = scale;
        _responseScale.ScaleY = scale;
        _responseTranslation.Y = verticalOffset;
        _companionImage.RenderTransformOrigin = new System.Windows.Point(0.5, originY);
        _responseTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _responseTimer.Start();
    }

    private void ResponseTimer_Tick(object? sender, EventArgs e)
    {
        _responseTimer.Stop();
        _responseRotation.Angle = 0;
        _responseScale.ScaleX = 1;
        _responseScale.ScaleY = 1;
        _responseTranslation.Y = 0;
        _companionImage.RenderTransformOrigin = new System.Windows.Point(0.5, 0.8);
        _approachOffset = 0;
    }

    public void PlayState(
        string state,
        int durationMs = 0,
        int priority = 40,
        int frameIntervalMs = 150,
        IReadOnlyList<int>? frameIntervalsMs = null)
    {
        if (!_animations.ContainsKey(state) || priority < _activePriority) return;
        if (priority > _activePriority)
        {
            _playHandshakeSpin = false;
            _playFrisbeeShowoff = false;
        }
        if (_fetchPhase != 0 && priority > _activePriority)
        {
            CancelFetchBehavior();
        }
        if (_frisbeePhase != 0 && priority > _activePriority)
        {
            CancelFrisbeeBehavior();
        }
        if (_feedingPhase != 0 && priority > _activePriority)
        {
            CancelFeedingBehavior();
        }
        if (_chaseEndsAt != DateTime.MinValue && priority > 35)
        {
            ClearMovingBehavior();
        }
        _opensProblems = state == "sad" && priority >= 80;
        _currentState = state;
        _activePriority = priority;
        _frameIndex = 0;
        _activeFrameIntervalsMs = frameIntervalsMs;
        _animationTimer.Interval = TimeSpan.FromMilliseconds(GetFrameIntervalMs(0, frameIntervalMs));
        SetFrame(_animations[state][0]);
        _animationTimer.Start();
        _stateRestoreTimer.Stop();
        if (durationMs <= 0) return;
        _stateRestoreTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _stateRestoreTimer.Start();
    }

    public bool PlayState(
        VisitorState state,
        int durationMs = 0,
        int priority = 40,
        int frameIntervalMs = 150,
        IReadOnlyList<int>? frameIntervalsMs = null)
    {
        if (!_profile.TryGetStateName(state, out var stateName) || !_animations.ContainsKey(stateName)) return false;
        PlayState(stateName, durationMs, priority, frameIntervalMs, frameIntervalsMs);
        return true;
    }

    private int GetFrameIntervalMs(int frameIndex, int fallbackIntervalMs = 150)
    {
        if (_activeFrameIntervalsMs is null || frameIndex >= _activeFrameIntervalsMs.Count) return fallbackIntervalMs;
        return _activeFrameIntervalsMs[frameIndex];
    }

    private bool TryGetFrames(VisitorState state, out BitmapSource[] frames)
    {
        if (_profile.TryGetStateName(state, out var stateName) && _animations.TryGetValue(stateName, out frames!)) return true;
        frames = [];
        return false;
    }

    public void RestoreAmbientState()
    {
        if (_fetchPhase != 0 || _frisbeePhase != 0 || _feedingPhase != 0) return;
        _awaitingHandshake = false;
        _stateRestoreTimer.Stop();
        _activePriority = 0;
        _opensProblems = false;
        PlayState(VisitorState.Idle, priority: 0);
    }

    public void StartBugSearch()
    {
        if (!_profile.Supports(VisitorCapabilities.BugSearch)) return;
        PlayState(VisitorState.SniffingRight, 5000, 80);
        _opensProblems = true;
    }

    public bool StartHalfSitPanting(int durationMs = 2400, int priority = 30)
    {
        if (IsBusy || !_profile.Supports(VisitorCapabilities.ActiveGreeting)) return false;
        return PlayState(VisitorState.HalfSitPanting, durationMs, priority);
    }

    public bool StartPlayfulChase()
    {
        if (IsBusy || !_profile.Supports(VisitorCapabilities.PlayfulChase)) return false;
        _chaseStartedAt = DateTime.UtcNow;
        _chaseEndsAt = _chaseStartedAt.AddSeconds(6);
        _chaseCarriesBall = false;
        PlayState(VisitorState.RunningRight, priority: 35);
        return true;
    }

    public bool StartToyTease()
    {
        if (IsBusy || !_profile.Supports(VisitorCapabilities.ToyTease) || !TryGetFrames(VisitorState.CarryingBallRight, out _)) return false;
        _chaseStartedAt = DateTime.UtcNow;
        _chaseEndsAt = _chaseStartedAt.AddSeconds(5);
        _chaseCarriesBall = true;
        PlayState(VisitorState.CarryingBallRight, priority: 35);
        return true;
    }

    public bool StartFetchGame(double targetX, double targetY)
    {
        if (_fetchPhase != 0 || !_profile.Supports(VisitorCapabilities.Fetch)
            || !TryGetFrames(VisitorState.RunningRight, out _) || !TryGetFrames(VisitorState.CarryingBallRight, out _)) return false;
        _fetchTargetLeft = Math.Clamp(targetX - Width / 2, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        _fetchTargetTop = Math.Clamp(targetY - Height, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        _fetchPhase = 1;
        _activePriority = 75;
        if (_ballImagePath is not null && File.Exists(_ballImagePath))
        {
            _ballWindow ??= new BallWindow(_ballImagePath);
            _ballWindow.Place(_fetchTargetLeft, _fetchTargetTop);
        }
        PlayState(VisitorState.RunningRight, priority: 75);
        _fetchTimer.Start();
        return true;
    }

    public bool StartFrisbeeGame(System.Windows.Point start, Vector drag)
    {
        if (IsBusy || !_profile.Supports(VisitorCapabilities.FrisbeeCatch)
            || _frisbeeImagePath is null || !File.Exists(_frisbeeImagePath)
            || !TryGetFrames(VisitorState.FrisbeeWatch, out _)
            || !TryGetFrames(VisitorState.FrisbeeRunRight, out _)
            || !TryGetFrames(VisitorState.FrisbeeCatchRight, out _)
            || !TryGetFrames(VisitorState.FrisbeeLanding, out _)
            || !TryGetFrames(VisitorState.FrisbeeReturnLeft, out _)
            || !TryGetFrames(VisitorState.FrisbeeMiss, out _)) return false;

        var power = Math.Clamp(drag.Length, 70, 620);
        if (drag.Length < 36) return false;

        var direction = drag / drag.Length;
        var rawEnd = start + direction * (120 + power * 0.92);
        var landingPoint = new System.Drawing.Point((int)Math.Round(rawEnd.X), (int)Math.Round(rawEnd.Y + 90));
        var workArea = System.Windows.Forms.Screen.FromPoint(landingPoint).WorkingArea;
        var left = workArea.Left + 34;
        var right = workArea.Right - 34;
        var top = workArea.Top + 28;
        var bottom = workArea.Bottom - 34;
        _frisbeeStart = new System.Windows.Point(Math.Clamp(start.X, left, right), Math.Clamp(start.Y, top, bottom));
        _frisbeeEnd = new System.Windows.Point(Math.Clamp(rawEnd.X, left, right), Math.Clamp(rawEnd.Y + 90, top + 80, bottom));
        _frisbeeArcHeight = 80 + power * 0.34 + Math.Max(0, -direction.Y) * 120;
        _frisbeeFlightSeconds = 1.05 + power / 620 * 1.15;

        var peakY = Math.Min(_frisbeeStart.Y, _frisbeeEnd.Y) - _frisbeeArcHeight;
        var endpointWasClamped = Math.Abs(rawEnd.X - _frisbeeEnd.X) > 28 || rawEnd.Y + 90 > bottom + 28;
        _frisbeeWillCatch = Math.Abs(_frisbeeEnd.X - _frisbeeStart.X) >= 85
            && !endpointWasClamped
            && peakY >= top;

        const double interceptProgress = 0.76;
        var intercept = GetFrisbeePosition(interceptProgress);
        _frisbeeInterceptLeft = Math.Clamp(intercept.X - Width * 0.52, left, right - Width);
        _frisbeeInterceptTop = Math.Clamp(intercept.Y - Height * 0.72, top, bottom - Height);
        _frisbeeWindow ??= new FrisbeeWindow(_frisbeeImagePath);
        _frisbeeWindow.Place(_frisbeeStart.X, _frisbeeStart.Y);
        _frisbeePhase = 1;
        _frisbeeFlightStartedAt = DateTime.UtcNow;
        _frisbeePhaseStartedAt = _frisbeeFlightStartedAt;
        _activePriority = 76;
        PlayState(VisitorState.FrisbeeWatch, priority: 76, frameIntervalMs: 115);
        _frisbeeTimer.Start();
        return true;
    }

    public bool StartTreat()
    {
        if (IsBusy || !_profile.Supports(VisitorCapabilities.Feeding)
            || !TryGetFrames(VisitorState.TreatEating, out _)) return false;

        int[] frameIntervalsMs = [180, 300, 1000, 1000, 300, 250];
        if (!PlayState(
            VisitorState.TreatEating,
            frameIntervalsMs.Sum(),
            70,
            frameIntervalsMs: frameIntervalsMs)) return false;
        TreatCompleted?.Invoke();
        return true;
    }

    public bool StartDogFood()
    {
        if (IsBusy || DateTime.UtcNow < _feedingCooldownEndsAt || !_profile.Supports(VisitorCapabilities.Feeding)
            || !TryGetFrames(VisitorState.FoodSniff, out _) || !TryGetFrames(VisitorState.Eating, out _)
            || !TryGetFrames(VisitorState.LickingThanks, out _)) return false;

        _feedingPhase = 1;
        _feedingPhaseEndsAt = DateTime.UtcNow.AddMilliseconds(1000);
        _activePriority = 75;
        PlayState(VisitorState.FoodSniff, priority: 75);
        _feedingTimer.Start();
        return true;
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (!_animations.TryGetValue(_currentState, out var frames) || frames.Length == 0) return;
        _frameIndex = (_frameIndex + 1) % frames.Length;
        _animationTimer.Interval = TimeSpan.FromMilliseconds(GetFrameIntervalMs(_frameIndex));
        SetFrame(frames[_frameIndex]);
    }

    private void StateRestoreTimer_Tick(object? sender, EventArgs e)
    {
        if (_playHandshakeSpin)
        {
            _playHandshakeSpin = false;
            PlayState(VisitorState.HandshakeSpin, 1050, 62, frameIntervalMs: 100);
            return;
        }
        if (_playFrisbeeShowoff)
        {
            _playFrisbeeShowoff = false;
            PlayState(VisitorState.FrisbeeShowoffWithDisc, 1050, 71, frameIntervalMs: 100);
            return;
        }
        RestoreAmbientState();
    }

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        if (_activePriority > 0) return;
        var idleStates = new[] { VisitorState.Sitting, VisitorState.LyingDown, VisitorState.Sleeping, VisitorState.Idle };
        PlayState(idleStates[_idleStateIndex++ % idleStates.Length], priority: 0);
    }

    private void FetchTimer_Tick(object? sender, EventArgs e)
    {
        if (_fetchPhase == 0) return;
        UpdateTarget();
        if (Math.Abs(Left - _targetLeft) + Math.Abs(Top - _targetTop) > 18) return;
        if (_fetchPhase == 1)
        {
            _fetchPhase = 2;
            _ballWindow?.Hide();
            _fetchTargetLeft = GetSideBySideLeft();
            _fetchTargetTop = _host.Top + (_host.ActualHeight > 0 ? _host.ActualHeight : _host.Height) - Height;
            PlayState(_fetchTargetLeft < Left ? VisitorState.CarryingBallLeft : VisitorState.CarryingBallRight, 0, 75);
            return;
        }

        _fetchPhase = 0;
        _fetchTimer.Stop();
        _activePriority = 0;
        PlayState(VisitorState.HappyCelebration, 1800, 70);
        FetchCompleted?.Invoke();
    }

    private void FrisbeeTimer_Tick(object? sender, EventArgs e)
    {
        if (_frisbeePhase == 0) return;
        var now = DateTime.UtcNow;
        var flightElapsed = (now - _frisbeeFlightStartedAt).TotalSeconds;
        var phaseElapsed = (now - _frisbeePhaseStartedAt).TotalSeconds;
        var flightProgress = Math.Clamp(flightElapsed / _frisbeeFlightSeconds, 0, 1);
        if ((_frisbeePhase <= 2 || (_frisbeePhase == 3 && !_frisbeeWillCatch)) && flightProgress < 1)
        {
            var position = GetFrisbeePosition(flightProgress);
            _frisbeeWindow?.Place(position.X, position.Y);
        }

        if (_frisbeePhase == 1 && flightElapsed >= 0.48)
        {
            _frisbeePhase = 2;
            _fetchTargetLeft = _frisbeeInterceptLeft;
            _fetchTargetTop = _frisbeeInterceptTop;
            PlayState(_frisbeeInterceptLeft >= Left ? VisitorState.FrisbeeRunRight : VisitorState.FrisbeeRunLeft, priority: 76, frameIntervalMs: 95);
        }

        if (_frisbeePhase == 2 && flightProgress >= 0.68)
        {
            _frisbeePhase = 3;
            var catchState = _frisbeeInterceptLeft >= Left ? VisitorState.FrisbeeCatchRight : VisitorState.FrisbeeCatchLeft;
            if (_frisbeeWillCatch)
            {
                _frisbeeWindow?.Hide();
            }
            PlayState(_frisbeeWillCatch ? catchState : VisitorState.FrisbeeMiss, priority: 76, frameIntervalMs: 120);
        }

        if (flightProgress < 1) return;
        _frisbeeWindow?.Hide();
        if (_frisbeePhase <= 3)
        {
            _frisbeePhase = _frisbeeWillCatch ? 4 : 6;
            _frisbeePhaseStartedAt = DateTime.UtcNow;
            PlayState(_frisbeeWillCatch ? VisitorState.FrisbeeLanding : VisitorState.FrisbeeMiss, priority: 76, frameIntervalMs: 125);
            return;
        }

        phaseElapsed = (DateTime.UtcNow - _frisbeePhaseStartedAt).TotalSeconds;
        if (_frisbeePhase == 4)
        {
            if (phaseElapsed < 0.72) return;
            _frisbeePhase = 5;
            var workArea = GetCompanionWorkArea();
            var hostWidth = _host.ActualWidth > 0 ? _host.ActualWidth : _host.Width;
            var leftTarget = _host.Left - Width - HostGap;
            var rightTarget = _host.Left + hostWidth + HostGap;
            var maximumLeft = Math.Max(workArea.Left, workArea.Right - Width);
            _frisbeeReturnOnLeft = Left + Width / 2 < _host.Left + hostWidth / 2
                && leftTarget >= workArea.Left;
            _fetchTargetLeft = Math.Clamp(
                _frisbeeReturnOnLeft ? leftTarget : rightTarget,
                workArea.Left,
                maximumLeft);
            _fetchTargetTop = _host.Top + (_host.ActualHeight > 0 ? _host.ActualHeight : _host.Height) - Height;
            PlayState(_frisbeeReturnOnLeft ? VisitorState.FrisbeeReturnLeft : VisitorState.FrisbeeReturnRight, priority: 76, frameIntervalMs: 105);
            return;
        }

        if (_frisbeePhase == 5 && Math.Abs(Left - _targetLeft) + Math.Abs(Top - _targetTop) > 20) return;
        if (_frisbeePhase == 6 && phaseElapsed < 1.4) return;

        var caught = _frisbeeWillCatch;
        CancelFrisbeeBehavior();
        _activePriority = 0;
        _playFrisbeeShowoff = false;
        PlayState(
            caught ? VisitorState.FrisbeeShowoffWithDisc : VisitorState.FrisbeeMiss,
            caught ? 720 : 900,
            caught ? 71 : 70,
            frameIntervalMs: caught ? 100 : 70);
        FrisbeeCompleted?.Invoke(caught);
    }

    private System.Windows.Point GetFrisbeePosition(double progress)
    {
        var x = _frisbeeStart.X + (_frisbeeEnd.X - _frisbeeStart.X) * progress;
        var linearY = _frisbeeStart.Y + (_frisbeeEnd.Y - _frisbeeStart.Y) * progress;
        var y = linearY - _frisbeeArcHeight * 4 * progress * (1 - progress);
        return new System.Windows.Point(x, y);
    }

    private void FeedingTimer_Tick(object? sender, EventArgs e)
    {
        if (_feedingPhase == 0) return;
        if (_feedingPhase == 1)
        {
            if (DateTime.UtcNow < _feedingPhaseEndsAt) return;
            _feedingPhase = 2;
            _feedingPhaseEndsAt = DateTime.UtcNow.AddMilliseconds(2200);
            PlayState(VisitorState.Eating, priority: 75);
            return;
        }
        if (_feedingPhase == 2 && DateTime.UtcNow < _feedingPhaseEndsAt) return;

        _feedingTimer.Stop();
        _feedingPhase = 0;
        _feedingPhaseEndsAt = DateTime.MinValue;
        _feedingCooldownEndsAt = DateTime.UtcNow.AddSeconds(30);
        _activePriority = 0;
        PlayState(VisitorState.LickingThanks, 1600, 70);
        DogFoodCompleted?.Invoke();
    }

    private void FollowTimer_Tick(object? sender, EventArgs e)
    {
        if (!_host.IsVisible)
        {
            Hide();
            return;
        }

        if (!IsVisible) Show();
        Topmost = _host.Topmost;
        if (_chaseEndsAt != DateTime.MinValue && DateTime.UtcNow >= _chaseEndsAt)
        {
            ClearMovingBehavior();
            RestoreAmbientState();
        }
        UpdateTarget();
        var horizontalDistance = _targetLeft - Left;
        if (_chaseEndsAt != DateTime.MinValue)
        {
            var state = _chaseCarriesBall
                ? horizontalDistance >= 0 ? VisitorState.CarryingBallRight : VisitorState.CarryingBallLeft
                : horizontalDistance >= 0 ? VisitorState.RunningRight : VisitorState.RunningLeft;
            if (!_profile.TryGetStateName(state, out var stateName) || _currentState != stateName)
            {
                PlayState(state, priority: 35);
            }
        }
        else if (Math.Abs(horizontalDistance) > 24 && _activePriority <= 10)
        {
            var state = horizontalDistance > 0 ? VisitorState.RunningRight : VisitorState.RunningLeft;
            if (!_profile.TryGetStateName(state, out var stateName) || _currentState != stateName)
            {
                PlayState(state, priority: 10);
            }
        }
        else if (Math.Abs(horizontalDistance) < 3 && _activePriority == 10)
        {
            _activePriority = 0;
            PlayState(VisitorState.Idle, priority: 0);
        }
        var nextLeft = Left + (_targetLeft - Left) * 0.12;
        var nextTop = Top + (_targetTop - Top) * 0.12;
        var workArea = GetCompanionWorkArea();
        var safePosition = KeepOutsideHost(nextLeft, nextTop, workArea);
        Left = safePosition.X;
        Top = safePosition.Y;
        if (_activePriority == 0 && DateTime.UtcNow - _lastEnergyRecoveryAt >= EnergyRecoveryInterval)
        {
            _lastEnergyRecoveryAt = DateTime.UtcNow;
            EnergyRecoveryRequested?.Invoke();
        }
    }

    private void UpdateTarget()
    {
        var workArea = GetCompanionWorkArea();
        var maximumLeft = Math.Max(workArea.Left, workArea.Right - Width);
        var maximumTop = Math.Max(workArea.Top, workArea.Bottom - Height);
        if (_fetchPhase != 0 || _frisbeePhase >= 2)
        {
            _targetLeft = Math.Clamp(_fetchTargetLeft, workArea.Left, maximumLeft);
            _targetTop = Math.Clamp(_fetchTargetTop, workArea.Top, maximumTop);
            return;
        }
        if (_frisbeePhase == 1)
        {
            _targetLeft = Left;
            _targetTop = Top;
            return;
        }
        var hostWidth = _host.ActualWidth > 0 ? _host.ActualWidth : _host.Width;
        var hostHeight = _host.ActualHeight > 0 ? _host.ActualHeight : _host.Height;
        if (_chaseEndsAt != DateTime.MinValue)
        {
            var elapsed = (DateTime.UtcNow - _chaseStartedAt).TotalSeconds;
            var direction = Math.Sin(elapsed * Math.PI * 0.8) >= 0 ? 1 : -1;
            _targetLeft = Math.Clamp(
                _host.Left + hostWidth / 2 + direction * (hostWidth * 0.72) - Width / 2,
                workArea.Left,
                maximumLeft);
            _targetTop = Math.Clamp(
                _host.Top + hostHeight - Height + Math.Sin(elapsed * Math.PI * 1.6) * 18,
                workArea.Top,
                maximumTop);
            return;
        }
        _targetLeft = Math.Clamp(
            GetSideBySideLeft(workArea, hostWidth),
            workArea.Left,
            maximumLeft);
        _targetTop = Math.Clamp(
            _host.Top + hostHeight - Height,
            workArea.Top,
            maximumTop);
    }

    private Rect GetCompanionWorkArea()
    {
        return _host is MainWindow mainWindow
            ? mainWindow.GetCompanionWorkArea()
            : new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
    }

    private double GetSideBySideLeft()
    {
        var hostWidth = _host.ActualWidth > 0 ? _host.ActualWidth : _host.Width;
        return GetSideBySideLeft(GetCompanionWorkArea(), hostWidth);
    }

    private double GetSideBySideLeft(Rect workArea, double hostWidth)
    {
        var leftCandidate = _host.Left - Width - HostGap;
        if (leftCandidate >= workArea.Left) return leftCandidate;
        return _host.Left + hostWidth + HostGap;
    }

    private System.Windows.Point KeepOutsideHost(double left, double top, Rect workArea)
    {
        var maximumLeft = Math.Max(workArea.Left, workArea.Right - Width);
        var maximumTop = Math.Max(workArea.Top, workArea.Bottom - Height);
        left = Math.Clamp(left, workArea.Left, maximumLeft);
        top = Math.Clamp(top, workArea.Top, maximumTop);

        var hostWidth = _host.ActualWidth > 0 ? _host.ActualWidth : _host.Width;
        var hostHeight = _host.ActualHeight > 0 ? _host.ActualHeight : _host.Height;
        var hostBounds = new Rect(_host.Left - HostGap, _host.Top - HostGap, hostWidth + HostGap * 2, hostHeight + HostGap * 2);
        var companionBounds = new Rect(left, top, Width, Height);
        if (!hostBounds.IntersectsWith(companionBounds)) return new System.Windows.Point(left, top);

        var leftCandidate = _host.Left - Width - HostGap;
        var rightCandidate = _host.Left + hostWidth + HostGap;
        var preferLeft = left + Width / 2 < _host.Left + hostWidth / 2;
        if (preferLeft && leftCandidate >= workArea.Left)
        {
            return new System.Windows.Point(leftCandidate, top);
        }

        if (!preferLeft && rightCandidate <= maximumLeft)
        {
            return new System.Windows.Point(rightCandidate, top);
        }

        if (leftCandidate >= workArea.Left)
        {
            return new System.Windows.Point(leftCandidate, top);
        }

        if (rightCandidate <= maximumLeft)
        {
            return new System.Windows.Point(rightCandidate, top);
        }

        return new System.Windows.Point(left, top);
    }

    private void LoadAnimations(string assetDirectory)
    {
        if (!Directory.Exists(assetDirectory)) return;
        foreach (var stateDirectory in Directory.EnumerateDirectories(assetDirectory))
        {
            var frames = Directory.EnumerateFiles(stateDirectory, "*.png")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(LoadBitmap)
                .ToArray();
            if (frames.Length > 0) _animations[Path.GetFileName(stateDirectory)] = frames;
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void SetFrame(BitmapSource frame)
    {
        _companionImage.Source = frame;
        var pixelSource = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        pixelSource.Freeze();
        _pixelWidth = pixelSource.PixelWidth;
        _pixelHeight = pixelSource.PixelHeight;
        _alphaPixels = new byte[_pixelWidth * _pixelHeight * 4];
        pixelSource.CopyPixels(_alphaPixels, _pixelWidth * 4, 0);
    }
}