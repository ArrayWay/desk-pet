using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Fuguang.DesktopPet;

public partial class MainWindow : Window
{
    private const string PipeName = "fuguang-desktop-pet";
    private const string ActionPipeName = "fuguang-desktop-pet-actions";
    private static readonly string UserDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Fuguang.DesktopPet");
    private readonly string _settingsPath = Path.Combine(UserDataDirectory, "pet-settings.json");
    private readonly string _logPath = Path.Combine(UserDataDirectory, "pet.log");
    private readonly DispatcherTimer _animationTimer = new();
    private readonly DispatcherTimer _movementTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _stateRestoreTimer = new();
    private readonly DispatcherTimer _focusTimer = new();
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _moodTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly NotificationBubbleWindow _bubble = new();
    private readonly StatusBarWindow _mainStatusBar = new(StatusBarTheme.Main);
    private readonly StatusBarWindow _visitorStatusBar = new(StatusBarTheme.Visitor);
    private Forms.ToolStripMenuItem? _growthMenuItem;
    private Forms.ToolStripMenuItem? _statusBarMenuItem;
    private readonly DispatcherTimer _statusBarTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _satietyTimer = new() { Interval = TimeSpan.FromMinutes(8) };
    private Forms.ToolStripMenuItem? _visibilityMenuItem;
    private Forms.ToolStripMenuItem? _visitorCompanionMenuItem;
    private Forms.ToolStripMenuItem? _visitorMenuItem;
    private Forms.ToolStripMenuItem? _visitorFetchMenuItem;
    private Forms.ToolStripMenuItem? _visitorFrisbeeMenuItem;
    private Forms.ToolStripMenuItem? _visitorBugSearchMenuItem;
    private Forms.ToolStripMenuItem? _visitorHandshakeMenuItem;
    private Forms.ToolStripMenuItem? _visitorFeedingMenuItem;
    private Forms.ToolStripMenuItem? _visitorSelectionMenuItem;
    private Forms.ToolStripMenuItem? _visitorIdentityMenuItem;
    private Forms.ToolStripMenuItem? _visitorTitleMenuItem;
    private CompanionWindow? _activeVisitor;
    private FrisbeeThrowWindow? _frisbeeThrowWindow;
    private string? _activeVisitorId;
    private PetSettings _settings = new();
    private PetAnimationConfig _config = new();
    private BitmapSource? _spritesheet;
    private readonly Dictionary<string, BitmapSource> _skinFrameCache = [];
    private PetStateConfig? _currentState;
    private string _currentStateName = "idle";
    private int _frameIndex;
    private int _activePriority;
    private bool _automaticMovement = true;
    private bool _paused;
    private bool _dragging;
    private bool _dragMoved;
    private bool _hovering;
    private DateTimeOffset? _focusEndsAt;
    private bool _focusIsBreak;
    private int _focusDurationMinutes;
    private DateTimeOffset? _oneTimeReminderAt;
    private string _oneTimeReminderMessage = string.Empty;
    private string _lastIdleState = string.Empty;
    private DateTime _lastIdleActionAt = DateTime.MinValue;
    private DateTimeOffset _lastActiveGreetingAt = DateTimeOffset.MinValue;
    private DateTime _lastActivityAt = DateTime.Now;
    private DateTime _lastClickAt = DateTime.MinValue;
    private int _clickComboStep;
    private bool _linkedSleepActive;
    private bool _linkedSleepWaking;
    private DateTime _lastBreakReminderAt = DateTime.Now;
    private DateTime _lastWaterReminderAt = DateTime.Now;
    private DateTime _lastEyeReminderAt = DateTime.Now;
    private System.Windows.Point _dragStart;
    private System.Windows.Point _windowStart;
    private System.Windows.Point _lastDragPoint;
    private DateTime _lastDragAt;
    private DateTime _dragStartedAt;
    private double _dragDistance;
    private double _inertiaX;
    private double _inertiaY;
    private double _velocityX = 1.25;
    private int _lookDirection;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        _trayIcon = CreateTrayIcon();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        _animationTimer.Tick += AnimationTimer_Tick;
        _movementTimer.Tick += MovementTimer_Tick;
        _stateRestoreTimer.Tick += StateRestoreTimer_Tick;
        _focusTimer.Interval = TimeSpan.FromSeconds(1);
        _focusTimer.Tick += OnFocusTick;
        _reminderTimer.Tick += ReminderTimer_Tick;
        _moodTimer.Tick += MoodTimer_Tick;
        _idleTimer.Tick += IdleTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadAssets();
            ApplyMainPetIdentityUi();
            RestorePosition();
            Play("idle");
            _movementTimer.Start();
            _reminderTimer.Start();
            _moodTimer.Start();
            _statusBarTimer.Tick += StatusBarTimer_Tick;
            _statusBarTimer.Start();
            _satietyTimer.Tick += SatietyTimer_Tick;
            _satietyTimer.Start();
            _idleTimer.Start();
            if (_settings.Visitor.Enabled || _settings.Visitor.AutoVisit)
            {
                TryShowVisitor();
                TryStartMorningVisitorVisit();
            }
            await ListenForEventsAsync(_shutdown.Token);
        }
        catch (Exception exception)
        {
            WriteLog("启动失败", exception);
            _trayIcon.BalloonTipTitle = $"{MainPetDisplayName}启动失败";
            _trayIcon.BalloonTipText = exception.Message;
            _trayIcon.ShowBalloonTip(5000);
        }
    }

    private void LoadAssets()
    {
        var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        var configPath = Path.Combine(assetsDirectory, "pet-animation.json");
        _config = JsonSerializer.Deserialize<PetAnimationConfig>(File.ReadAllText(configPath))
            ?? throw new InvalidDataException("动画配置无效。");
        _spritesheet = LoadBitmap(
            Path.Combine(assetsDirectory, _config.Spritesheet.PngFallback),
            Path.Combine(assetsDirectory, _config.Spritesheet.Webp));
        _config.Validate(_spritesheet.PixelWidth, _spritesheet.PixelHeight);
        Width = _config.Spritesheet.CellWidth;
        Height = _config.Spritesheet.CellHeight;
        PetImage.Width = Width;
        PetImage.Height = Height;
    }

    private void LoadSettings()
    {
        MigrateLegacySettings();
        _settings = PetSettings.Load(_settingsPath);
        _settings.ResetDailyStatisticsIfNeeded();
        _automaticMovement = _settings.AutomaticMovement;
        _velocityX = Math.CopySign(_settings.MovementSpeed, _velocityX);
        Topmost = _settings.Topmost;
    }

    private void MigrateLegacySettings()
    {
        if (File.Exists(_settingsPath)) return;
        var legacyPath = Path.Combine(AppContext.BaseDirectory, "Data", "pet-settings.json");
        if (!File.Exists(legacyPath)) return;
        Directory.CreateDirectory(UserDataDirectory);
        File.Copy(legacyPath, _settingsPath);
    }

    private static BitmapSource LoadBitmap(string preferredPath, string fallbackPath)
    {
        foreach (var path in new[] { preferredPath, fallbackPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (NotSupportedException) when (path == preferredPath)
            {
            }
        }

        throw new FileNotFoundException("无法加载桌宠图集。");
    }

    private void Play(string stateName, int durationMs = 0, int priority = 10)
    {
        if (priority < _activePriority) return;
        if (_paused || !_config.States.TryGetValue(stateName, out var state)) return;
        _stateRestoreTimer.Stop();
        _currentStateName = stateName;
        _activePriority = priority;
        _currentState = state;
        _frameIndex = 0;
        _animationTimer.Stop();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(16, state.IntervalMs / _settings.AnimationSpeed));
        RenderFrame();
        _animationTimer.Start();
        if (durationMs > 0)
        {
            _stateRestoreTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(16, durationMs));
            _stateRestoreTimer.Start();
        }
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentState is null) return;
        _frameIndex += 1;
        if (_frameIndex >= _currentState.Frames)
        {
            if (_currentState.Loop)
            {
                _frameIndex = 0;
            }
            else
            {
                if (_stateRestoreTimer.IsEnabled)
                {
                    _frameIndex = Math.Max(0, _currentState.Frames - 1);
                    RenderFrame();
                    _animationTimer.Stop();
                    return;
                }
                _activePriority = 0;
                RestoreAmbientState();
                return;
            }
        }
        RenderFrame();
    }

    private void RenderFrame()
    {
        if (_spritesheet is null || _currentState is null) return;
        if (TryRenderSkinFrame()) return;
        var rectangle = new System.Windows.Int32Rect(
            _frameIndex * _config.Spritesheet.CellWidth,
            _currentState.Row * _config.Spritesheet.CellHeight,
            _config.Spritesheet.CellWidth,
            _config.Spritesheet.CellHeight);
        var frame = new CroppedBitmap(_spritesheet, rectangle);
        frame.Freeze();
        PetImage.Source = frame;
    }

    private bool TryRenderSkinFrame()
    {
        if (!string.Equals(_settings.MainPetSkin, "person2", StringComparison.OrdinalIgnoreCase)
            || _currentState is null)
        {
            return false;
        }

        var skinStateName = _currentStateName switch
        {
            "picked-up" => "idle",
            "landing" => "jumping",
            "stretching" => "idle",
            "sitting" => "idle",
            "sleeping" => "idle",
            "celebrating" => "waving",
            _ => _currentStateName
        };
        var skinState = _config.States.TryGetValue(skinStateName, out var mappedState)
            ? mappedState
            : _currentState;
        var framePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Skins",
            "person2",
            skinStateName,
            $"{_frameIndex % skinState.Frames:00}.png");
        if (!File.Exists(framePath)) return false;
        if (!_skinFrameCache.TryGetValue(framePath, out var frame))
        {
            frame = LoadBitmap(framePath, string.Empty);
            _skinFrameCache[framePath] = frame;
        }

        PetImage.Source = frame;
        return true;
    }

    private void MovementTimer_Tick(object? sender, EventArgs e)
    {
        if (_paused || _dragging || !IsVisible || (_focusEndsAt is not null && !_focusIsBreak)) return;
        var workArea = GetCurrentWorkArea();
        if (Math.Abs(_inertiaX) + Math.Abs(_inertiaY) > 0.2)
        {
            Left = Math.Clamp(Left + _inertiaX, workArea.Left, workArea.Right - Width);
            Top = Math.Clamp(Top + _inertiaY, workArea.Top, workArea.Bottom - Height);
            _inertiaX *= 0.88;
            _inertiaY *= 0.88;
            return;
        }
        _inertiaX = 0;
        _inertiaY = 0;
        if (!_automaticMovement || _hovering) return;
        var nextLeft = Left + _velocityX;
        if (nextLeft <= workArea.Left || nextLeft + Width >= workArea.Right)
        {
            _velocityX *= -1;
            nextLeft = Math.Clamp(nextLeft, workArea.Left, workArea.Right - Width);
        }
        Left = nextLeft;
        Top = Math.Clamp(Top, workArea.Top, workArea.Bottom - Height);
        var movementState = _velocityX > 0 ? "running-right" : "running-left";
        if (_currentStateName is "idle" or "running-left" or "running-right" && _currentStateName != movementState)
        {
            Play(movementState);
        }
    }

    private async Task ListenForEventsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe);
                var line = await reader.ReadLineAsync(cancellationToken);
                var message = line is null ? null : JsonSerializer.Deserialize<PetEventMessage>(line);
                if (message is not null)
                {
                    await Dispatcher.InvokeAsync(() => HandleMessage(message));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
            }
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsOpaqueAt(e.GetPosition(PetImage))) return;
        RegisterActivity();
        _dragging = true;
        _dragMoved = false;
        _dragStart = PointToScreen(e.GetPosition(this));
        _windowStart = new System.Windows.Point(Left, Top);
        _lastDragPoint = _dragStart;
        _lastDragAt = DateTime.UtcNow;
        _dragStartedAt = DateTime.UtcNow;
        _dragDistance = 0;
        Play("picked-up", 0, 65);
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateLookDirection(e.GetPosition(this));
            return;
        }
        var current = PointToScreen(e.GetPosition(this));
        var delta = current - _dragStart;
        _dragDistance = Math.Max(_dragDistance, Math.Abs(delta.X) + Math.Abs(delta.Y));
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 4) _dragMoved = true;
        var elapsed = Math.Max(1, (DateTime.UtcNow - _lastDragAt).TotalMilliseconds);
        _inertiaX = Math.Clamp((current.X - _lastDragPoint.X) * 16 / elapsed, -12, 12);
        _inertiaY = Math.Clamp((current.Y - _lastDragPoint.Y) * 16 / elapsed, -12, 12);
        _lastDragPoint = current;
        _lastDragAt = DateTime.UtcNow;
        Left = _windowStart.X + delta.X;
        Top = _windowStart.Y + delta.Y;
    }

    private void UpdateLookDirection(System.Windows.Point position)
    {
        var normalizedX = Width <= 0 ? 0.5 : position.X / Width;
        var nextDirection = _lookDirection;
        if (_lookDirection <= 0 && normalizedX > 0.62) nextDirection = 1;
        else if (_lookDirection >= 0 && normalizedX < 0.38) nextDirection = -1;
        else if (_lookDirection != 0 && normalizedX is >= 0.44 and <= 0.56) nextDirection = 0;
        if (nextDirection == _lookDirection) return;
        _lookDirection = nextDirection;
        PetImage.RenderTransform = new RotateTransform(_lookDirection * 3);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        KeepInsideWorkArea();
        SnapToNearestEdge();
        SaveSettings();
        if (_dragMoved)
        {
            Play("landing", 1100, 65);
            if (DateTime.UtcNow - _dragStartedAt < TimeSpan.FromMilliseconds(900)
                && _dragDistance < 72)
            {
                Play("waving", 1200, 70);
                ShowBubble("轻轻安抚一下，收到啦。", 1800);
            }
        }
        else
        {
            _inertiaX = 0;
            _inertiaY = 0;
            if (_currentStateName == "failed")
            {
                SendActionToExtension("open-problems");
                return;
            }
            var now = DateTime.UtcNow;
            if (now - _lastClickAt > TimeSpan.FromSeconds(2)) _clickComboStep = 0;
            _lastClickAt = now;
            var clickStates = _clickComboStep switch
            {
                0 => "waving",
                1 => "jumping",
                _ => "review"
            };
            _clickComboStep = (_clickComboStep + 1) % 3;
            var clickResult = GrowthService.ApplyMain(_settings, GrowthAction.MainClick);
            SaveSettings();
            Play(clickStates, 1400, 55);
            MaybeShowGrowthHint(clickResult);
        }
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        RegisterActivity();
        var doubleClickResult = GrowthService.ApplyMain(_settings, GrowthAction.MainDoubleClick);
        SaveSettings();
        Play("jumping");
        MaybeShowGrowthHint(doubleClickResult);
        e.Handled = true;
    }

    private void KeepInsideWorkArea()
    {
        Left = Math.Clamp(Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
    }

    private void SnapToNearestEdge()
    {
        const double snapDistance = 24;
        var workArea = GetCurrentWorkArea();
        var edges = new[]
        {
            (Distance: Math.Abs(Left - workArea.Left), Apply: (Action)(() => Left = workArea.Left)),
            (Distance: Math.Abs(Left - (workArea.Right - Width)), Apply: (Action)(() => Left = workArea.Right - Width)),
            (Distance: Math.Abs(Top - workArea.Top), Apply: (Action)(() => Top = workArea.Top)),
            (Distance: Math.Abs(Top - (workArea.Bottom - Height)), Apply: (Action)(() => Top = workArea.Bottom - Height))
        };
        var nearest = edges.MinBy(edge => edge.Distance);
        if (nearest.Distance <= snapDistance) nearest.Apply();
    }

    private void PlaceNearBottomRight()
    {
        var workArea = GetCurrentWorkArea();
        Left = workArea.Right - Width - 28;
        Top = workArea.Bottom - Height - 20;
        SaveSettings();
    }

    private Rect GetCurrentWorkArea()
    {
        return GetWorkArea(new Rect(Left, Top, Width, Height));
    }

    internal Rect GetCompanionWorkArea()
    {
        return GetCurrentWorkArea();
    }

    private void RestorePosition()
    {
        if (_settings.Left is not double left || _settings.Top is not double top)
        {
            PlaceNearBottomRight();
            return;
        }

        var workArea = GetWorkArea(new Rect(left, top, Width, Height));
        Left = Math.Clamp(left, workArea.Left, workArea.Right - Width);
        Top = Math.Clamp(top, workArea.Top, workArea.Bottom - Height);
        SaveSettings();
    }

    private Rect GetWorkArea(Rect windowBounds)
    {
        var source = PresentationSource.FromVisual(this);
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var deviceTopLeft = toDevice.Transform(windowBounds.TopLeft);
        var deviceBottomRight = toDevice.Transform(windowBounds.BottomRight);
        var deviceBounds = Drawing.Rectangle.FromLTRB(
            (int)Math.Floor(deviceTopLeft.X),
            (int)Math.Floor(deviceTopLeft.Y),
            (int)Math.Ceiling(deviceBottomRight.X),
            (int)Math.Ceiling(deviceBottomRight.Y));
        var deviceWorkArea = Forms.Screen.FromRectangle(deviceBounds).WorkingArea;
        var workAreaTopLeft = fromDevice.Transform(new System.Windows.Point(deviceWorkArea.Left, deviceWorkArea.Top));
        var workAreaBottomRight = fromDevice.Transform(new System.Windows.Point(deviceWorkArea.Right, deviceWorkArea.Bottom));
        return new Rect(workAreaTopLeft, workAreaBottomRight);
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.AutomaticMovement = _automaticMovement;
            _settings.Topmost = Topmost;
            _settings.MovementSpeed = Math.Abs(_velocityX);
            _settings.Save(_settingsPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string CompanionAssetsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Companions");

    private string DefaultMainPetName =>
        string.IsNullOrWhiteSpace(_config.DisplayName) ? "浮光橙仔" : _config.DisplayName;

    private string MainPetDisplayName =>
        string.IsNullOrWhiteSpace(_settings.CustomName) ? DefaultMainPetName : _settings.CustomName;

    private string MainPetNote => _settings.Note ?? string.Empty;

    private string GetVisitorDisplayName(VisitorProfile? profile = null)
    {
        profile ??= ActiveVisitorProfile;
        if (_settings.Visitor.Identities is not null
            && _settings.Visitor.Identities.TryGetValue(profile.Id, out var identity)
            && identity is not null
            && !string.IsNullOrWhiteSpace(identity.CustomName))
        {
            return identity.CustomName;
        }

        return profile.DisplayName;
    }

    private string GetVisitorNote(VisitorProfile? profile = null)
    {
        profile ??= ActiveVisitorProfile;
        if (_settings.Visitor.Identities is not null
            && _settings.Visitor.Identities.TryGetValue(profile.Id, out var identity)
            && identity is not null)
        {
            return identity.Note ?? string.Empty;
        }

        return string.Empty;
    }

    private string GetVisitorSelectionLabel(VisitorProfile profile)
    {
        var name = GetVisitorDisplayName(profile);
        return string.Equals(name, profile.DisplayName, StringComparison.Ordinal)
            ? profile.DisplayName
            : $"{name}（{profile.DisplayName}）";
    }

    private void ApplyMainPetIdentityUi()
    {
        Title = MainPetDisplayName;
        _trayIcon.Text = MainPetDisplayName.Length <= 63 ? MainPetDisplayName : MainPetDisplayName[..63];
        var tip = "单击挥手，双击跳跃，拖动可移动";
        if (!string.IsNullOrWhiteSpace(MainPetNote))
        {
            tip = $"{tip}\n备注：{MainPetNote}";
        }
        PetImage.ToolTip = tip;
    }

    private void ApplyVisitorIdentityUi()
    {
        var profile = ActiveVisitorProfile;
        var name = GetVisitorDisplayName(profile);
        var note = GetVisitorNote(profile);
        _activeVisitor?.SetDisplayTooltip(name, note);
        if (_visitorSelectionMenuItem is not null)
        {
            foreach (Forms.ToolStripMenuItem item in _visitorSelectionMenuItem.DropDownItems)
            {
                if (item.Tag is string id && VisitorProfile.TryGet(id, out var p))
                {
                    item.Text = GetVisitorSelectionLabel(p);
                    item.Checked = string.Equals(id, profile.Id, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        if (_visitorIdentityMenuItem is not null)
        {
            foreach (Forms.ToolStripMenuItem item in _visitorIdentityMenuItem.DropDownItems)
            {
                if (item.Tag is string id && VisitorProfile.TryGet(id, out var p))
                {
                    item.Text = GetVisitorSelectionLabel(p);
                }
            }
        }
    }

    private void PromptRenameMainPet()
    {
        if (!TryPromptIdentity("主宠改名与备注", MainPetDisplayName, MainPetNote, DefaultMainPetName, out var name, out var note))
        {
            return;
        }

        _settings.CustomName = string.Equals(name, DefaultMainPetName, StringComparison.Ordinal) ? string.Empty : name;
        _settings.Note = note;
        SaveSettings();
        ApplyMainPetIdentityUi();
        ShowBubble($"主宠现在叫「{MainPetDisplayName}」。", 2400);
    }

    private void PromptRenameVisitor(VisitorProfile profile)
    {
        var species = profile.DisplayName;
        if (!TryPromptIdentity(
                $"{species}改名与备注",
                GetVisitorDisplayName(profile),
                GetVisitorNote(profile),
                species,
                out var name,
                out var note))
        {
            return;
        }

        var identity = _settings.Visitor.GetOrCreateIdentity(profile.Id);
        identity.CustomName = string.Equals(name, species, StringComparison.Ordinal) ? string.Empty : name;
        identity.Note = note;
        identity.Normalize();
        if (string.IsNullOrEmpty(identity.CustomName) && string.IsNullOrEmpty(identity.Note))
        {
            _settings.Visitor.Identities.Remove(profile.Id);
        }
        else
        {
            _settings.Visitor.Identities[profile.Id] = identity;
        }

        SaveSettings();
        UpdateVisitorMenuItem();
        ApplyVisitorIdentityUi();
        ShowBubble($"{species}现在叫「{GetVisitorDisplayName(profile)}」。", 2400);
    }

    private static bool TryPromptIdentity(
        string title,
        string currentName,
        string currentNote,
        string defaultName,
        out string name,
        out string note)
    {
        name = currentName;
        note = currentNote;
        using var dialog = new Forms.Form
        {
            Text = title,
            Width = 360,
            Height = 230,
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            TopMost = true
        };
        var nameLabel = new Forms.Label { Text = "显示名（最多 16 字，可空恢复默认）", Left = 20, Top = 16, Width = 300 };
        var nameInput = new Forms.TextBox
        {
            Left = 20,
            Top = 40,
            Width = 300,
            MaxLength = 16,
            Text = currentName
        };
        var noteLabel = new Forms.Label { Text = "备注昵称（最多 40 字，可选）", Left = 20, Top = 74, Width = 300 };
        var noteInput = new Forms.TextBox
        {
            Left = 20,
            Top = 98,
            Width = 300,
            MaxLength = 40,
            Text = currentNote
        };
        var resetButton = new Forms.Button { Text = "恢复默认名", Left = 20, Top = 140, Width = 100 };
        resetButton.Click += (_, _) =>
        {
            nameInput.Text = defaultName;
            noteInput.Text = string.Empty;
        };
        var confirmButton = new Forms.Button
        {
            Text = "保存",
            Left = 220,
            Top = 140,
            Width = 100,
            DialogResult = Forms.DialogResult.OK
        };
        var cancelButton = new Forms.Button
        {
            Text = "取消",
            Left = 130,
            Top = 140,
            Width = 80,
            DialogResult = Forms.DialogResult.Cancel
        };
        dialog.Controls.AddRange(new Forms.Control[] { nameLabel, nameInput, noteLabel, noteInput, resetButton, cancelButton, confirmButton });
        dialog.AcceptButton = confirmButton;
        dialog.CancelButton = cancelButton;
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return false;
        name = PetSettings.NormalizeDisplayText(nameInput.Text, 16);
        note = PetSettings.NormalizeDisplayText(noteInput.Text, 40);
        if (string.IsNullOrEmpty(name)) name = defaultName;
        return true;
    }


    private VisitorProfile ActiveVisitorProfile
    {
        get
        {
            VisitorProfile.TryGet(_settings.Visitor.ActiveVisitorId, out var profile);
            return profile;
        }
    }

    private void ToggleVisitor()
    {
        if (_settings.Visitor.Enabled)
        {
            CloseFrisbeeThrowWindow();
            _settings.Visitor.Enabled = false;
            _activeVisitor?.GoHome();
            UpdateVisitorMenuItem();
            SaveSettings();
            return;
        }

        if (!TryShowVisitor()) return;
        _settings.Visitor.Enabled = true;
        UpdateVisitorMenuItem();
        SaveSettings();
    }

    private bool TryShowVisitor()
    {
        var profile = ActiveVisitorProfile;
        if (!profile.TryValidateResources(CompanionAssetsDirectory, out var validationError))
        {
            WriteLog(validationError);
            ShowTrayMessage($"{profile.DisplayName}资源不完整", validationError);
            return false;
        }

        try
        {
            if (_activeVisitor is not null && !string.Equals(_activeVisitorId, profile.Id, StringComparison.OrdinalIgnoreCase))
            {
                _activeVisitor.Stop();
                _activeVisitor = null;
                _activeVisitorId = null;
            }

            if (_activeVisitor is null)
            {
                _activeVisitor = new CompanionWindow(this, CompanionAssetsDirectory, profile);
                _activeVisitorId = profile.Id;
                _activeVisitor.AffectionChanged += OnVisitorAffectionChanged;
                _activeVisitor.ProblemsRequested += () => SendActionToExtension("open-problems");
                _activeVisitor.FetchCompleted += OnVisitorFetchCompleted;
                _activeVisitor.FrisbeeCompleted += OnVisitorFrisbeeCompleted;
                _activeVisitor.DogFoodCompleted += OnVisitorDogFoodCompleted;
                _activeVisitor.TreatCompleted += OnVisitorTreatCompleted;
                _activeVisitor.InteractionCompleted += OnVisitorInteractionCompleted;
                _activeVisitor.EnergyRecoveryRequested += OnVisitorEnergyRecoveryRequested;
                _activeVisitor.Approached += OnVisitorApproached;
            }
            ApplyVisitorIdentityUi();
            _activeVisitor.Visit();
            return true;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            WriteLog($"加载{profile.DisplayName}玩伴失败", exception);
            ShowTrayMessage($"{profile.DisplayName}素材无法加载", $"请检查 {profile.BaseImageName} 是否为有效的透明 PNG。");
            return false;
        }
    }

    private void OnVisitorAffectionChanged(int amount)
    {
        var result = GrowthService.ApplyVisitor(_settings, GrowthAction.VisitorPet);
        UpdateVisitorTitle();
        SaveSettings();
        MaybeShowGrowthHint(result);
    }

    private void OnVisitorFetchCompleted()
    {
        var result = GrowthService.ApplyVisitor(_settings, GrowthAction.VisitorFetch);
        UpdateVisitorTitle();
        SaveSettings();
        ShowBubble($"{GetVisitorDisplayName()}把球带回来了。", 2600);
        MaybeShowGrowthHint(result);
    }

    private void OnVisitorFrisbeeCompleted(bool caught)
    {
        var result = GrowthService.ApplyVisitor(
            _settings,
            caught ? GrowthAction.VisitorFrisbeeSuccess : GrowthAction.VisitorFrisbeeFail,
            success: caught);
        UpdateVisitorTitle();
        SaveSettings();
        ShowBubble(caught
            ? $"{GetVisitorDisplayName()}接住飞盘并带回来了。"
            : $"{GetVisitorDisplayName()}没能接住这次飞盘。", 2600);
        MaybeShowGrowthHint(result);
    }

    private void OnVisitorDogFoodCompleted()
    {
        GrowthService.ApplyVisitor(_settings, GrowthAction.VisitorDogFood);
        UpdateVisitorTitle();
        SaveSettings();
        ShowBubble($"{GetVisitorDisplayName()}吃饱后舔了舔嘴。", 2400);
    }

    private void OnVisitorTreatCompleted()
    {
        GrowthService.ApplyVisitor(_settings, GrowthAction.VisitorTreat);
        UpdateVisitorTitle();
        SaveSettings();
        ShowBubble($"{GetVisitorDisplayName()}开心地吃掉了零食。", 1800);
    }

    private void OnVisitorInteractionCompleted()
    {
        GrowthService.ApplyVisitor(_settings, GrowthAction.VisitorInteraction);
        UpdateVisitorTitle();
        SaveSettings();
    }

    private void OnVisitorEnergyRecoveryRequested()
    {
        if (_settings.Visitor.ActiveStats.Stamina >= 100) return;
        GrowthService.ApplyVisitor(_settings, GrowthAction.VisitorStaminaRecover);
        SaveSettings();
    }

    private void MaybeShowGrowthHint(GrowthResult result)
    {
        if (!result.SoftPenaltyApplied || string.IsNullOrWhiteSpace(result.Hint)) return;
        ShowBubble(result.Hint!, 2200);
    }

    private async void OnVisitorApproached()
    {
        if (!_linkedSleepActive || _linkedSleepWaking) return;
        _linkedSleepWaking = true;
        _activeVisitor?.PlayState(VisitorState.WakingStretch, 1200, 45);
        await Task.Delay(700);
        if (_linkedSleepActive) Play("stretching", 1600, 45);
        _linkedSleepActive = false;
        _linkedSleepWaking = false;
    }

    private async void BeginLinkedSleep()
    {
        _linkedSleepActive = true;
        _linkedSleepWaking = false;
        _activeVisitor?.PlayState(VisitorState.LyingDown, 900, 20);
        await Task.Delay(900);
        if (_linkedSleepActive && _currentStateName == "sleeping")
        {
            _activeVisitor?.PlayState(VisitorState.Sleeping, 0, 20);
        }
    }

    private void UpdateVisitorTitle()
    {
        var stats = _settings.Visitor.ActiveStats;
        var title = GrowthService.ComputeVisitorTitle(stats);
        var previous = stats.Title;
        stats.Title = title;
        _settings.Visitor.SyncLegacyMirrorFromActive();
        if (_visitorTitleMenuItem is not null)
        {
            _visitorTitleMenuItem.Text = $"当前称号：{title}";
        }
        if (title != previous)
        {
            ShowBubble($"{GetVisitorDisplayName()}称号已更新：{title}", 3000);
        }
    }

    private void UpdateVisitorMenuItem()
    {
        var profile = ActiveVisitorProfile;
        var displayName = GetVisitorDisplayName(profile);
        if (_visitorCompanionMenuItem is not null)
        {
            _visitorCompanionMenuItem.Text = $"{displayName}玩伴";
        }
        if (_visitorMenuItem is not null)
        {
            _visitorMenuItem.Text = _settings.Visitor.Enabled ? $"送{displayName}回家" : $"召唤{displayName}";
        }
        if (_visitorFetchMenuItem is not null)
        {
            _visitorFetchMenuItem.Text = $"和{displayName}玩球";
            _visitorFetchMenuItem.Visible = profile.Supports(VisitorCapabilities.Fetch);
        }
        if (_visitorFrisbeeMenuItem is not null)
        {
            _visitorFrisbeeMenuItem.Text = $"和{displayName}玩飞盘";
            _visitorFrisbeeMenuItem.Visible = profile.Supports(VisitorCapabilities.FrisbeeCatch);
        }
        if (_visitorBugSearchMenuItem is not null)
        {
            _visitorBugSearchMenuItem.Text = $"让{displayName}找 Bug";
            _visitorBugSearchMenuItem.Visible = profile.Supports(VisitorCapabilities.BugSearch);
        }
        if (_visitorHandshakeMenuItem is not null)
        {
            _visitorHandshakeMenuItem.Text = $"和{displayName}握手";
            _visitorHandshakeMenuItem.Visible = profile.Supports(VisitorCapabilities.Handshake);
        }
        if (_visitorFeedingMenuItem is not null)
        {
            _visitorFeedingMenuItem.Text = $"给{displayName}喂食";
            _visitorFeedingMenuItem.Visible = profile.Supports(VisitorCapabilities.Feeding);
        }
        ApplyVisitorIdentityUi();
    }

    private void SelectVisitor(string visitorId)
    {
        CloseFrisbeeThrowWindow();
        if (!VisitorProfile.TryGet(visitorId, out var profile) ||
            string.Equals(_settings.Visitor.ActiveVisitorId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!profile.TryValidateResources(CompanionAssetsDirectory, out var validationError))
        {
            WriteLog(validationError);
            ShowTrayMessage($"{profile.DisplayName}资源不完整", validationError);
            return;
        }

        var previousDisplayName = GetVisitorDisplayName();
        _settings.Visitor.ActiveVisitorId = profile.Id;
        UpdateVisitorMenuItem();
        if (_settings.Visitor.Enabled)
        {
            if (TryShowVisitor())
            {
                ShowBubble($"{previousDisplayName}先回家休息，{GetVisitorDisplayName()}来接班。", 3600);
                if (!TryPlayActiveGreeting("换班完成"))
                {
                    _activeVisitor?.PlayState(VisitorState.HappyCelebration, 2200, 45);
                }
            }
        }
        SaveSettings();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var visitorProfile = ActiveVisitorProfile;
        var menu = new Forms.ContextMenuStrip();
        _visibilityMenuItem = new Forms.ToolStripMenuItem("隐藏桌宠");
        _visibilityMenuItem.Click += (_, _) => Dispatcher.Invoke(ToggleVisibility);
        menu.Items.Add(_visibilityMenuItem);
        var automaticMovementItem = new Forms.ToolStripMenuItem("自动行走") { CheckOnClick = true, Checked = _automaticMovement };
        automaticMovementItem.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            _automaticMovement = automaticMovementItem.Checked;
            SaveSettings();
            RestoreAmbientState();
        });
        menu.Items.Add(automaticMovementItem);
        menu.Items.Add("暂停/继续", null, (_, _) => Dispatcher.Invoke(TogglePause));
        menu.Items.Add("主宠改名与备注…", null, (_, _) => Dispatcher.Invoke(PromptRenameMainPet));
        var skinMenu = new Forms.ToolStripMenuItem("主宠换装");
        AddSkinMenuItem(skinMenu, "默认外观", "default");
        AddSkinMenuItem(skinMenu, "人物2", "person2");
        menu.Items.Add(skinMenu);
        var visitorDisplayName = GetVisitorDisplayName(visitorProfile);
        var companionMenu = new Forms.ToolStripMenuItem($"{visitorDisplayName}玩伴");
        _visitorCompanionMenuItem = companionMenu;
        _visitorMenuItem = new Forms.ToolStripMenuItem(_settings.Visitor.Enabled ? $"送{visitorDisplayName}回家" : $"召唤{visitorDisplayName}");
        _visitorMenuItem.Click += (_, _) => Dispatcher.Invoke(ToggleVisitor);
        companionMenu.DropDownItems.Add(_visitorMenuItem);
        if (VisitorProfile.Registered.Count > 1)
        {
            _visitorSelectionMenuItem = new Forms.ToolStripMenuItem("选择访客");
            foreach (var profile in VisitorProfile.Registered)
            {
                var profileItem = new Forms.ToolStripMenuItem(GetVisitorSelectionLabel(profile))
                {
                    Tag = profile.Id,
                    Checked = string.Equals(profile.Id, visitorProfile.Id, StringComparison.OrdinalIgnoreCase)
                };
                profileItem.Click += (_, _) => Dispatcher.Invoke(() => SelectVisitor(profile.Id));
                _visitorSelectionMenuItem.DropDownItems.Add(profileItem);
            }
            companionMenu.DropDownItems.Add(_visitorSelectionMenuItem);
        }
        var autoVisitItem = new Forms.ToolStripMenuItem("自动来访") { CheckOnClick = true, Checked = _settings.Visitor.AutoVisit };
        autoVisitItem.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.Visitor.AutoVisit = autoVisitItem.Checked;
            if (autoVisitItem.Checked && TryShowVisitor())
            {
                _settings.Visitor.Enabled = true;
                UpdateVisitorMenuItem();
            }
            SaveSettings();
        });
        companionMenu.DropDownItems.Add(autoVisitItem);
        _visitorFetchMenuItem = new Forms.ToolStripMenuItem($"和{visitorDisplayName}玩球", null, (_, _) => Dispatcher.Invoke(StartVisitorFetchGame))
        {
            Visible = visitorProfile.Supports(VisitorCapabilities.Fetch)
        };
        companionMenu.DropDownItems.Add(_visitorFetchMenuItem);
        _visitorFrisbeeMenuItem = new Forms.ToolStripMenuItem($"和{visitorDisplayName}玩飞盘", null, (_, _) => Dispatcher.Invoke(StartVisitorFrisbeeGame))
        {
            Visible = visitorProfile.Supports(VisitorCapabilities.FrisbeeCatch)
        };
        companionMenu.DropDownItems.Add(_visitorFrisbeeMenuItem);
        _visitorBugSearchMenuItem = new Forms.ToolStripMenuItem($"让{visitorDisplayName}找 Bug", null, (_, _) => Dispatcher.Invoke(StartVisitorBugSearch))
        {
            Visible = visitorProfile.Supports(VisitorCapabilities.BugSearch)
        };
        companionMenu.DropDownItems.Add(_visitorBugSearchMenuItem);
        _visitorHandshakeMenuItem = new Forms.ToolStripMenuItem($"和{visitorDisplayName}握手", null, (_, _) => Dispatcher.Invoke(StartVisitorHandshake))
        {
            Visible = visitorProfile.Supports(VisitorCapabilities.Handshake)
        };
        companionMenu.DropDownItems.Add(_visitorHandshakeMenuItem);
        _visitorFeedingMenuItem = new Forms.ToolStripMenuItem($"给{visitorDisplayName}喂食")
        {
            Visible = visitorProfile.Supports(VisitorCapabilities.Feeding)
        };
        _visitorFeedingMenuItem.DropDownItems.Add("喂零食（无冷却）", null, (_, _) => Dispatcher.Invoke(StartVisitorTreat));
        _visitorFeedingMenuItem.DropDownItems.Add("喂狗粮", null, (_, _) => Dispatcher.Invoke(StartVisitorDogFood));
        companionMenu.DropDownItems.Add(_visitorFeedingMenuItem);
        var visitorIdentityMenu = new Forms.ToolStripMenuItem("访客改名与昵称备注");
        _visitorIdentityMenuItem = visitorIdentityMenu;
        foreach (var profile in VisitorProfile.Registered)
        {
            var identityItem = new Forms.ToolStripMenuItem(GetVisitorSelectionLabel(profile)) { Tag = profile.Id };
            identityItem.Click += (_, _) => Dispatcher.Invoke(() => PromptRenameVisitor(profile));
            visitorIdentityMenu.DropDownItems.Add(identityItem);
        }
        companionMenu.DropDownItems.Add(visitorIdentityMenu);
        _visitorTitleMenuItem = new Forms.ToolStripMenuItem($"当前称号：{_settings.Visitor.Title}") { Enabled = false };
        companionMenu.DropDownItems.Add(_visitorTitleMenuItem);
        menu.Items.Add(companionMenu);
        var focusMenu = new Forms.ToolStripMenuItem("专注计时");
        focusMenu.DropDownItems.Add("25 分钟专注 / 5 分钟休息", null, (_, _) => Dispatcher.Invoke(() => { _settings.BreakMinutes = 5; StartFocus(25, false); }));
        focusMenu.DropDownItems.Add("50 分钟专注 / 10 分钟休息", null, (_, _) => Dispatcher.Invoke(() => { _settings.BreakMinutes = 10; StartFocus(50, false); }));
        focusMenu.DropDownItems.Add("自定义专注", null, (_, _) => Dispatcher.Invoke(StartCustomFocus));
        focusMenu.DropDownItems.Add("开始休息", null, (_, _) => Dispatcher.Invoke(() => StartFocus(Math.Clamp(_settings.BreakMinutes, 1, 60), true)));
        focusMenu.DropDownItems.Add("停止计时", null, (_, _) => Dispatcher.Invoke(() => StopFocus(true)));
        menu.Items.Add(focusMenu);
        var reminderMenu = new Forms.ToolStripMenuItem("提醒");
        AddReminderToggle(reminderMenu, "久坐提醒", () => _settings.BreakRemindersEnabled, value => _settings.BreakRemindersEnabled = value);
        AddReminderToggle(reminderMenu, "喝水提醒", () => _settings.WaterRemindersEnabled, value => _settings.WaterRemindersEnabled = value);
        AddReminderToggle(reminderMenu, "护眼提醒", () => _settings.EyeRemindersEnabled, value => _settings.EyeRemindersEnabled = value);
        menu.Items.Add(reminderMenu);
        var bubbleItem = new Forms.ToolStripMenuItem("显示气泡") { CheckOnClick = true, Checked = _settings.BubbleEnabled };
        bubbleItem.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.BubbleEnabled = bubbleItem.Checked;
            SaveSettings();
        });
        menu.Items.Add(bubbleItem);
        var growthMenu = new Forms.ToolStripMenuItem("养成系统");
        _growthMenuItem = new Forms.ToolStripMenuItem("开启养成系统") { CheckOnClick = true, Checked = _settings.GrowthEnabled };
        _growthMenuItem.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            var enabled = _growthMenuItem.Checked;
            _settings.GrowthEnabled = enabled;
            if (_statusBarMenuItem is not null)
            {
                _statusBarMenuItem.Enabled = enabled;
            }
            if (!enabled)
            {
                // Seal progress: hide bars and stop further settlement until re-enabled.
                ShowBubble("养成系统已关闭，当前数值已封存。", 2400);
            }
            else
            {
                ShowBubble("养成系统已开启，继续当前进度。", 2200);
            }
            SaveSettings();
            RefreshStatusBars();
        });
        _statusBarMenuItem = new Forms.ToolStripMenuItem("显示状态条")
        {
            CheckOnClick = true,
            Checked = _settings.StatusBarEnabled,
            Enabled = _settings.GrowthEnabled
        };
        _statusBarMenuItem.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.StatusBarEnabled = _statusBarMenuItem.Checked;
            SaveSettings();
            RefreshStatusBars();
        });
        growthMenu.DropDownItems.Add(_growthMenuItem);
        growthMenu.DropDownItems.Add(_statusBarMenuItem);
        menu.Items.Add(growthMenu);
        var mutedItem = new Forms.ToolStripMenuItem("静音") { CheckOnClick = true, Checked = _settings.Muted };
        mutedItem.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.Muted = mutedItem.Checked;
            SaveSettings();
        });
        menu.Items.Add(mutedItem);
        var topmostItem = new Forms.ToolStripMenuItem("窗口置顶") { CheckOnClick = true, Checked = Topmost };
        topmostItem.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            Topmost = topmostItem.Checked;
            SaveSettings();
        });
        menu.Items.Add(topmostItem);
        var animationSpeedMenu = new Forms.ToolStripMenuItem("动画速度");
        foreach (var option in new[] { ("慢", 0.75), ("标准", 1.0), ("快", 1.4) })
        {
            var speedItem = new Forms.ToolStripMenuItem(option.Item1) { Checked = Math.Abs(_settings.AnimationSpeed - option.Item2) < 0.01 };
            speedItem.Click += (_, _) => Dispatcher.Invoke(() =>
            {
                _settings.AnimationSpeed = option.Item2;
                foreach (Forms.ToolStripMenuItem sibling in animationSpeedMenu.DropDownItems) sibling.Checked = ReferenceEquals(sibling, speedItem);
                SaveSettings();
                Play(_currentStateName, 0, _activePriority);
            });
            animationSpeedMenu.DropDownItems.Add(speedItem);
        }
        menu.Items.Add(animationSpeedMenu);
        menu.Items.Add("回到右下角", null, (_, _) => Dispatcher.Invoke(PlaceNearBottomRight));
        menu.Items.Add("回到工作区屏幕", null, (_, _) => Dispatcher.Invoke(ReturnToWorkArea));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("打开当前项目", null, (_, _) => SendActionToExtension("open-project"));
        menu.Items.Add("打开终端", null, (_, _) => SendActionToExtension("open-terminal"));
        menu.Items.Add("打开问题面板", null, (_, _) => SendActionToExtension("open-problems"));
        menu.Items.Add("打开源代码管理", null, (_, _) => SendActionToExtension("open-scm"));
        menu.Items.Add("运行默认构建任务", null, (_, _) => SendActionToExtension("run-build"));
        menu.Items.Add("运行默认测试任务", null, (_, _) => SendActionToExtension("run-test"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("重置设置", null, (_, _) => Dispatcher.Invoke(ResetSettings));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(CloseApplication));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "deskpet.ico");
        return new Forms.NotifyIcon
        {
            Text = MainPetDisplayName.Length <= 63 ? MainPetDisplayName : MainPetDisplayName[..63],
            Icon = File.Exists(iconPath) ? new Drawing.Icon(iconPath) : Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    private void AddSkinMenuItem(Forms.ToolStripMenuItem parent, string label, string skinId)
    {
        var item = new Forms.ToolStripMenuItem(label)
        {
            Checked = string.Equals(_settings.MainPetSkin, skinId, StringComparison.OrdinalIgnoreCase)
        };
        item.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.MainPetSkin = skinId;
            foreach (Forms.ToolStripMenuItem sibling in parent.DropDownItems)
            {
                sibling.Checked = ReferenceEquals(sibling, item);
            }
            SaveSettings();
            Play(_currentStateName, 0, _activePriority);
            ShowBubble(skinId == "default" ? "已换回默认外观。" : "已换上人物2外观。", 2200);
        });
        parent.DropDownItems.Add(item);
    }

    private void StartVisitorFetchGame()
    {
        if (!TryShowVisitor()) return;
        var fetchStats = _settings.Visitor.ActiveStats;
        if (fetchStats.Stamina < GrowthService.SoftFetchStaminaHint || fetchStats.Satiety < GrowthService.SoftFetchSatietyHint)
        {
            _activeVisitor?.PlayState(VisitorState.LyingDown, 1800, 40);
            ShowBubble($"{GetVisitorDisplayName()}有点累或饿了，仍可取球但收益会降低。", 2400);
        }
        var targetWindow = new FetchTargetWindow();
        targetWindow.TargetSelected += (left, top) =>
        {
            if (_activeVisitor?.StartFetchGame(left, top) == true) ShowBubble("去把球捡回来吧。", 2200);
        };
        targetWindow.Show();
        targetWindow.Activate();
    }

    private void StartVisitorFrisbeeGame()
    {
        if (!TryShowVisitor()) return;
        var frisbeeStats = _settings.Visitor.ActiveStats;
        if (frisbeeStats.Stamina < GrowthService.SoftFrisbeeStaminaHint || frisbeeStats.Satiety < GrowthService.SoftFrisbeeSatietyHint)
        {
            _activeVisitor?.PlayState(VisitorState.LyingDown, 1800, 40);
            ShowBubble($"{GetVisitorDisplayName()}状态偏低，仍可玩飞盘但收益会降低。", 2400);
        }
        if (_activeVisitor?.IsBusy == true)
        {
            ShowBubble($"{GetVisitorDisplayName()}正在忙，稍后再玩飞盘。", 1800);
            return;
        }
        if (_frisbeeThrowWindow is not null)
        {
            _frisbeeThrowWindow.Activate();
            return;
        }

        _frisbeeThrowWindow = new FrisbeeThrowWindow();
        _frisbeeThrowWindow.ThrowReleased += (start, drag) =>
        {
            if (_activeVisitor?.StartFrisbeeGame(start, drag) == true)
            {
                ShowBubble($"{GetVisitorDisplayName()}盯住飞盘了。", 1800);
            }
            else
            {
                ShowBubble("拖动距离太短，或者玩伴正在忙。", 1800);
            }
        };
        _frisbeeThrowWindow.Closed += (_, _) => _frisbeeThrowWindow = null;
        _frisbeeThrowWindow.Show();
        _frisbeeThrowWindow.Activate();
    }

    private void CloseFrisbeeThrowWindow()
    {
        var window = _frisbeeThrowWindow;
        _frisbeeThrowWindow = null;
        window?.Close();
    }

    private void StartVisitorHandshake()
    {
        if (!TryShowVisitor()) return;
        if (_activeVisitor?.StartHandshake() == true)
        {
            ShowBubble($"点一下{GetVisitorDisplayName()}抬起的前爪。", 2000);
        }
    }

    private void StartVisitorTreat()
    {
        if (!TryShowVisitor()) return;
        if (_activeVisitor?.StartTreat() != true)
        {
            ShowBubble($"{GetVisitorDisplayName()}正在忙，稍后再喂。", 1800);
        }
    }

    private void StartVisitorDogFood()
    {
        if (!TryShowVisitor()) return;
        if (_activeVisitor?.StartDogFood() == true)
        {
            ShowBubble($"{GetVisitorDisplayName()}闻到狗粮了。", 2000);
        }
        else if (_activeVisitor?.FeedingCooldownSeconds > 0)
        {
            ShowBubble($"{GetVisitorDisplayName()}刚吃饱，再等 {_activeVisitor.FeedingCooldownSeconds} 秒。", 2200);
        }
        else
        {
            ShowBubble($"{GetVisitorDisplayName()}正在忙，稍后再喂。", 1800);
        }
    }

    private async void SendActionToExtension(string action, string value = "")
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", ActionPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
            await pipe.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new PetActionMessage { Action = action, Value = value }));
        }
        catch (OperationCanceledException)
        {
            ShowTrayMessage("VS Code 未连接", "请先启动或重新加载浮光橙仔扩展。");
        }
        catch (IOException)
        {
            ShowTrayMessage("VS Code 未连接", "暂时无法执行此操作。");
        }
    }

    private void ShowTrayMessage(string title, string message)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(2500);
    }

    private void AddReminderToggle(Forms.ToolStripMenuItem parent, string text, Func<bool> read, Action<bool> write)
    {
        var item = new Forms.ToolStripMenuItem(text) { CheckOnClick = true, Checked = read() };
        item.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            write(item.Checked);
            SaveSettings();
        });
        parent.DropDownItems.Add(item);
    }

    private void RegisterActivity()
    {
        // Only track activity time; stamina/mood recover via GrowthService timers.
        _lastActivityAt = DateTime.Now;
    }

    private void ReturnToWorkArea()
    {
        var workArea = GetCurrentWorkArea();
        Left = Math.Clamp(workArea.Left + 24, workArea.Left, workArea.Right - Width);
        Top = Math.Clamp(workArea.Top + 24, workArea.Top, workArea.Bottom - Height);
        SaveSettings();
        ShowBubble("已回到当前工作区屏幕。", 1800);
    }

    private bool IsOpaqueAt(System.Windows.Point point)
    {
        if (PetImage.Source is not BitmapSource frame || point.X < 0 || point.Y < 0 || point.X >= PetImage.ActualWidth || point.Y >= PetImage.ActualHeight) return false;
        var pixelX = Math.Clamp((int)(point.X * frame.PixelWidth / PetImage.ActualWidth), 0, frame.PixelWidth - 1);
        var pixelY = Math.Clamp((int)(point.Y * frame.PixelHeight / PetImage.ActualHeight), 0, frame.PixelHeight - 1);
        var bytesPerPixel = Math.Max(1, (frame.Format.BitsPerPixel + 7) / 8);
        if (bytesPerPixel < 4) return true;
        var pixel = new byte[bytesPerPixel];
        frame.CopyPixels(new Int32Rect(pixelX, pixelY, 1, 1), pixel, bytesPerPixel, 0);
        return pixel[3] >= 24;
    }

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        if (_paused || _dragging || _focusEndsAt is not null || DateTime.Now - _lastActivityAt < TimeSpan.FromSeconds(45) || DateTime.Now - _lastIdleActionAt < TimeSpan.FromSeconds(35)) return;
        TryPlayVisitorEasterEgg();
        var candidates = _settings.Stamina < GrowthService.SoftStaminaThreshold
            ? new[] { "sleeping", "sitting", "idle" }
            : _settings.Mood > 75
                ? new[] { "stretching", "waving", "jumping", "idle" }
                : new[] { "sitting", "stretching", "idle", "review" };
        var choices = candidates.Where(state => state != _lastIdleState).ToArray();
        var next = choices.Length > 0 ? choices[Random.Shared.Next(choices.Length)] : "idle";
        _lastIdleState = next;
        _lastIdleActionAt = DateTime.Now;
        Play(next, next == "sleeping" ? 6000 : 2200, 20);
        if (next == "sleeping") BeginLinkedSleep();
        if (!string.IsNullOrWhiteSpace(MainPetNote) && Random.Shared.Next(5) == 0)
        {
            var note = PetSettings.NormalizeDisplayText(MainPetNote, 24);
            ShowBubble($"{MainPetDisplayName}记得：{note}", 2600);
        }
    }

    private void TryStartMorningVisitorVisit()
    {
        if (!_settings.Visitor.AutoVisit || _activeVisitor is null) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_settings.Visitor.LastMorningVisit == today) return;
        _settings.Visitor.LastMorningVisit = today;
        _settings.Visitor.Enabled = true;
        if (!TryPlayActiveGreeting("早晨见面"))
        {
            _activeVisitor.PlayState(VisitorState.HappyCelebration, 3200, 45);
        }
        UpdateVisitorMenuItem();
        SaveSettings();
        ShowBubble($"{GetVisitorDisplayName()}来陪你开始今天的工作。", 3600);
    }

    private bool TryPlayActiveGreeting(string source)
    {
        if (_activeVisitor is null || !_settings.Visitor.Enabled || _activeVisitor.IsBusy
            || DateTimeOffset.Now - _lastActiveGreetingAt < TimeSpan.FromMinutes(30)) return false;
        if (!_activeVisitor.StartHalfSitPanting()) return false;

        _lastActiveGreetingAt = DateTimeOffset.Now;
        ShowBubble($"{GetVisitorDisplayName()}主动招呼你：{source}。", 2800);
        return true;
    }

    private void TryPlayVisitorEasterEgg()
    {
        if (_activeVisitor is null || !_settings.Visitor.Enabled || _activeVisitor.IsBusy) return;
        if (_settings.Mood < 35)
        {
            _activeVisitor.PlayState(VisitorState.Comforting, 3600, 30);
            return;
        }

        var now = DateTimeOffset.Now;
        var visitorStats = _settings.Visitor.ActiveStats;
        if (_settings.Mood >= 80 && visitorStats.Stamina >= 35
            && (_settings.Visitor.LastChaseAt is null || now - _settings.Visitor.LastChaseAt >= TimeSpan.FromHours(1))
            && Random.Shared.Next(5) == 0
            && _activeVisitor.StartPlayfulChase())
        {
            _settings.Visitor.LastChaseAt = now;
            GrowthService.ApplyVisitor(_settings, GrowthAction.VisitorChase);
            SaveSettings();
            return;
        }
        if (visitorStats.Affection >= 20 && visitorStats.Stamina >= 20
            && (_settings.Visitor.LastConflictAt is null || now - _settings.Visitor.LastConflictAt >= TimeSpan.FromHours(2))
            && Random.Shared.Next(7) == 0
            && _activeVisitor.StartToyTease())
        {
            _settings.Visitor.LastConflictAt = now;
            GrowthService.ApplyVisitor(_settings, GrowthAction.VisitorToyTease);
            SaveSettings();
            ShowBubble($"{GetVisitorDisplayName()}叼着球绕了一圈，像是在邀请{MainPetDisplayName}追它。", 3600);
            return;
        }
        if (_settings.Visitor.LastEasterEggAt is not null && now - _settings.Visitor.LastEasterEggAt < TimeSpan.FromHours(1)) return;
        if (Random.Shared.Next(6) != 0) return;
        _settings.Visitor.LastEasterEggAt = now;
        _activeVisitor.PlayState(VisitorState.Peeking, 4200, 25);
        SaveSettings();
    }

    private void StartVisitorBugSearch()
    {
        StartVisitorBugSearch(string.Empty);
    }

    private void StartVisitorBugSearch(string diagnosticValue)
    {
        if (!TryShowVisitor()) return;
        var stats = _settings.Visitor.ActiveStats;
        stats.BugSearchesToday += 1;
        if (int.TryParse(diagnosticValue, out var baseline)) stats.BugSearchBaseline = Math.Max(0, baseline);
        SaveSettings();
        _activeVisitor?.StartBugSearch();
        SendActionToExtension("bug-search-start");
        ShowBubble($"点击嗅闻中的{GetVisitorDisplayName()}打开 Problems。", 3200);
    }

    private void HandleVisitorBugSearchResult(string value)
    {
        var diagnostics = 0;
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            var baseline = 0;
            if (root.TryGetProperty("baseline", out var baselineValue)) baseline = Math.Max(0, baselineValue.GetInt32());
            if (root.TryGetProperty("diagnostics", out var diagnosticsValue))
            {
                diagnostics = Math.Max(0, diagnosticsValue.GetInt32());
            }
            if (_settings.Visitor.Stats is not null && baseline > 0)
            {
                _settings.Visitor.ActiveStats.BugSearchBaseline = baseline;
            }
        }
        catch (JsonException)
        {
            return;
        }

        if (_activeVisitor is null || !_settings.Visitor.Enabled) return;
        var stats = _settings.Visitor.ActiveStats;
        var succeeded = diagnostics == 0 || diagnostics < stats.BugSearchBaseline;
        var result = GrowthService.ApplyVisitor(
            _settings,
            succeeded ? GrowthAction.VisitorBugSearchSuccess : GrowthAction.VisitorBugSearchFail,
            succeeded);
        stats.BugSearchBaseline = diagnostics;
        UpdateVisitorTitle();
        SaveSettings();
        _activeVisitor.PlayState(succeeded ? VisitorState.HappyCelebration : VisitorState.Comforting, 2400, 70);
        ShowBubble(succeeded
            ? $"问题少了，{GetVisitorDisplayName()}找到了一条线索。"
            : $"先别急，{GetVisitorDisplayName()}还在嗅闻，Problems 里继续看看。", 3200);
        MaybeShowGrowthHint(result);
    }

    private void ReminderTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        if (_oneTimeReminderAt is not null && DateTimeOffset.Now >= _oneTimeReminderAt)
        {
            _oneTimeReminderAt = null;
            Play("waving", 5000, 85);
            ShowBubble(_oneTimeReminderMessage, 6000);
            if (!_settings.Muted) System.Media.SystemSounds.Asterisk.Play();
            return;
        }
        if (_settings.BreakRemindersEnabled && now - _lastBreakReminderAt >= TimeSpan.FromMinutes(_settings.BreakReminderMinutes))
        {
            _lastBreakReminderAt = now;
            var lowEnergy = _settings.Stamina < GrowthService.SoftStaminaThreshold;
            Play(lowEnergy ? "sitting" : "waiting", 4200, 50);
            _activeVisitor?.PlayState(lowEnergy ? VisitorState.LyingDown : VisitorState.CarryingBallRight, 4200, 50);
            ShowBubble(lowEnergy ? "今天有点累，先休息一下。" : "坐久了，起来活动一下。", 5000);
            return;
        }
        if (_settings.WaterRemindersEnabled && now - _lastWaterReminderAt >= TimeSpan.FromMinutes(_settings.WaterReminderMinutes))
        {
            _lastWaterReminderAt = now;
            Play("waving", 4200, 50);
            ShowBubble("喝口水，给自己一点补给。", 5000);
            return;
        }
        if (_settings.EyeRemindersEnabled && now - _lastEyeReminderAt >= TimeSpan.FromMinutes(_settings.EyeReminderMinutes))
        {
            _lastEyeReminderAt = now;
            Play("review", 4200, 50);
            ShowBubble("看看远处，让眼睛休息一下。", 5000);
        }
    }

    private void MoodTimer_Tick(object? sender, EventArgs e)
    {
        if (DateTime.Now - _lastActivityAt > TimeSpan.FromMinutes(10))
        {
            GrowthService.ApplyMain(_settings, GrowthAction.MainIdleDecay);
        }
        else
        {
            // Light recovery while recently active: stamina + mood via unified settlement.
            GrowthService.ApplyMain(_settings, GrowthAction.MainIdleRecover);
        }
        SaveSettings();
    }

    private void SatietyTimer_Tick(object? sender, EventArgs e)
    {
        if (_settings.Satiety > 0)
        {
            GrowthService.ApplyMain(_settings, GrowthAction.TickSatietyDecay);
        }
        if (_settings.Visitor.Enabled)
        {
            GrowthService.ApplyVisitor(_settings, GrowthAction.TickSatietyDecay);
        }
        SaveSettings();
    }

    private void StatusBarTimer_Tick(object? sender, EventArgs e)
    {
        RefreshStatusBars();
    }

    private void RefreshStatusBars()
    {
        if (!_settings.GrowthEnabled || !_settings.StatusBarEnabled || !IsVisible)
        {
            _mainStatusBar.HideBar();
            _visitorStatusBar.HideBar();
            return;
        }

        _mainStatusBar.Topmost = Topmost;
        _mainStatusBar.UpdateContent(
            MainPetDisplayName,
            _settings.Affection,
            _settings.Stamina,
            _settings.Satiety,
            detail: (_settings.Stamina < GrowthService.SoftStaminaThreshold || _settings.Satiety < GrowthService.SoftSatietyThreshold ? "收益降低" : "状态稳定")
                + $"  ·  今日专注 {_settings.FocusSessionsToday} 次/{_settings.FocusMinutesToday} 分钟");
        _mainStatusBar.PlaceNear(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);

        if (_settings.Visitor.Enabled && _activeVisitor is not null && _activeVisitor.IsVisible)
        {
            var stats = _settings.Visitor.ActiveStats;
            _visitorStatusBar.Topmost = Topmost;
            _visitorStatusBar.UpdateContent(
                GetVisitorDisplayName(),
                stats.Affection,
                stats.Stamina,
                stats.Satiety,
                stats.Title);
            _visitorStatusBar.PlaceNear(
                _activeVisitor.Left,
                _activeVisitor.Top,
                _activeVisitor.ActualWidth > 0 ? _activeVisitor.ActualWidth : _activeVisitor.Width,
                _activeVisitor.ActualHeight > 0 ? _activeVisitor.ActualHeight : _activeVisitor.Height);
            SeparateOverlappingStatusBars();
        }
        else
        {
            _visitorStatusBar.HideBar();
        }
    }

    /// <summary>When main/visitor bars collide, push them apart horizontally then vertically.</summary>
    private void SeparateOverlappingStatusBars()
    {
        if (!_mainStatusBar.IsVisible || !_visitorStatusBar.IsVisible) return;

        var main = _mainStatusBar.GetBounds();
        var visitor = _visitorStatusBar.GetBounds();
        const double gap = 8;
        var inflated = main;
        inflated.Inflate(gap / 2, gap / 2);
        if (!inflated.IntersectsWith(visitor)) return;

        // Prefer horizontal separation based on host positions so each bar stays near its pet.
        var mainCenterX = Left + (ActualWidth > 0 ? ActualWidth : Width) / 2;
        var visitorCenterX = _activeVisitor!.Left + (_activeVisitor.ActualWidth > 0 ? _activeVisitor.ActualWidth : _activeVisitor.Width) / 2;
        var overlapX = Math.Min(main.Right, visitor.Right) - Math.Max(main.Left, visitor.Left) + gap;

        if (overlapX > 0 && visitorCenterX >= mainCenterX)
        {
            _mainStatusBar.Nudge(-overlapX / 2, 0);
            _visitorStatusBar.Nudge(overlapX / 2, 0);
        }
        else if (overlapX > 0)
        {
            _mainStatusBar.Nudge(overlapX / 2, 0);
            _visitorStatusBar.Nudge(-overlapX / 2, 0);
        }

        main = _mainStatusBar.GetBounds();
        visitor = _visitorStatusBar.GetBounds();
        inflated = main;
        inflated.Inflate(gap / 2, gap / 2);
        if (!inflated.IntersectsWith(visitor)) return;

        // Still overlapping (stacked hosts): stack visitor above main bar.
        var overlapY = Math.Min(main.Bottom, visitor.Bottom) - Math.Max(main.Top, visitor.Top) + gap;
        if (overlapY > 0)
        {
            _visitorStatusBar.Nudge(0, -overlapY);
        }
    }

    private void ShowAndActivate()
    {
        RestorePosition();
        Show();
        Activate();
        if (_settings.Visitor.Enabled) TryShowVisitor();
        UpdateVisibilityMenuItem();
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            CloseFrisbeeThrowWindow();
            Hide();
            _activeVisitor?.GoHome();
            UpdateVisibilityMenuItem();
            return;
        }

        ShowAndActivate();
    }

    private void UpdateVisibilityMenuItem()
    {
        if (_visibilityMenuItem is not null)
        {
            _visibilityMenuItem.Text = IsVisible ? "隐藏桌宠" : "显示桌宠";
        }
    }

    private void StartCustomFocus()
    {
        using var dialog = new Forms.Form
        {
            Text = "自定义专注",
            Width = 300,
            Height = 170,
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            TopMost = true
        };
        var label = new Forms.Label { Text = "专注时长（分钟）", Left = 20, Top = 20, Width = 240 };
        var minutesInput = new Forms.NumericUpDown
        {
            Left = 20,
            Top = 48,
            Width = 240,
            Minimum = 1,
            Maximum = 180,
            Value = Math.Clamp(_settings.FocusMinutes, 1, 180)
        };
        var confirmButton = new Forms.Button
        {
            Text = "开始专注",
            Left = 155,
            Top = 86,
            Width = 105,
            DialogResult = Forms.DialogResult.OK
        };
        var cancelButton = new Forms.Button
        {
            Text = "取消",
            Left = 65,
            Top = 86,
            Width = 80,
            DialogResult = Forms.DialogResult.Cancel
        };
        dialog.Controls.AddRange(new Forms.Control[] { label, minutesInput, cancelButton, confirmButton });
        dialog.AcceptButton = confirmButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        _settings.FocusMinutes = decimal.ToInt32(minutesInput.Value);
        SaveSettings();
        StartFocus(_settings.FocusMinutes, false);
    }

    private void TogglePause()
    {
        _paused = !_paused;
        if (_paused)
        {
            _animationTimer.Stop();
            _movementTimer.Stop();
        }
        else
        {
            Play("idle");
            _movementTimer.Start();
        }
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hovering = true;
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hovering = false;
    }

    private void StateRestoreTimer_Tick(object? sender, EventArgs e)
    {
        _stateRestoreTimer.Stop();
        if (_currentStateName == "sleeping" && _linkedSleepActive)
        {
            _linkedSleepActive = false;
            _activeVisitor?.PlayState(VisitorState.WakingStretch, 1200, 25);
        }
        RestoreAmbientState();
    }

    private void RestoreAmbientState()
    {
        _activePriority = 0;
        if (_paused) return;
        if (_focusEndsAt is not null && !_focusIsBreak)
        {
            Play("review", 0, 60);
            return;
        }
        var ambientState = _automaticMovement && !_hovering
            ? _velocityX >= 0 ? "running-right" : "running-left"
            : "idle";
        Play(ambientState);
    }

    private void HandleMessage(PetEventMessage message)
    {
        switch (message.Command)
        {
            case "show":
                ShowAndActivate();
                return;
            case "hide":
                CloseFrisbeeThrowWindow();
                Hide();
                _activeVisitor?.GoHome();
                UpdateVisibilityMenuItem();
                return;
            case "toggle-pause":
                TogglePause();
                return;
            case "focus-start":
                var focusMinutes = 25;
                var breakMinutes = _settings.BreakMinutes;
                if (int.TryParse(message.Value, out var legacyMinutes))
                {
                    focusMinutes = legacyMinutes;
                }
                else
                {
                    try
                    {
                        using var document = JsonDocument.Parse(message.Value);
                        var root = document.RootElement;
                        if (root.TryGetProperty("focusMinutes", out var focusValue)) focusMinutes = focusValue.GetInt32();
                        if (root.TryGetProperty("breakMinutes", out var breakValue)) breakMinutes = breakValue.GetInt32();
                    }
                    catch (JsonException)
                    {
                        return;
                    }
                }
                _settings.BreakMinutes = Math.Clamp(breakMinutes, 1, 60);
                StartFocus(Math.Clamp(focusMinutes, 1, 180), false);
                return;
            case "focus-stop":
                StopFocus(true);
                return;
            case "commit-ceremony":
                var commitResult = GrowthService.ApplyMain(_settings, GrowthAction.MainCommitCeremony);
                SaveSettings();
                Play("waving", 2200, 55);
                _activeVisitor?.PlayState(VisitorState.HappyCelebration, 2200, 55);
                ShowBubble("这次提交完成了，进度稳稳落地。", 3000);
                MaybeShowGrowthHint(commitResult);
                return;
            case "long-session":
                if (_paused || _dragging || _focusEndsAt is not null || _activeVisitor?.IsBusy == true) return;
                Play("sitting", 2600, 35);
                TryPlayActiveGreeting("连续工作提醒");
                ShowBubble("已经连续工作一阵子了，抬头活动一下吧。", 3200);
                return;
            case "bug-search-start":
                StartVisitorBugSearch(message.Value);
                return;
            case "bug-search-result":
                HandleVisitorBugSearchResult(message.Value);
                return;
            case "morning-check":
                Play("waving", 1800, 35);
                ShowBubble("早上好，今天也一起工作吧。", 3200);
                return;
            case "remind":
                if (int.TryParse(message.Value, out var reminderMinutes))
                {
                    SetOneTimeReminder(Math.Clamp(reminderMinutes, 1, 1440), message.Source);
                }
                return;
            case "notify":
                ShowBubble(message.Source);
                return;
            case "exit":
                CloseApplication();
                return;
        }

        if (!string.IsNullOrWhiteSpace(message.State))
        {
            Play(message.State, message.DurationMs, message.Priority);
            PlayVisitorEvent(message);
            if (!string.IsNullOrWhiteSpace(message.Source)) ShowBubble(message.Source, Math.Max(1800, message.DurationMs));
        }
    }

    private void PlayVisitorEvent(PetEventMessage message)
    {
        if (_activeVisitor is null || !_settings.Visitor.Enabled) return;
        var visitorState = message.State switch
        {
            "failed" => VisitorState.Sad,
            "waiting" => VisitorState.Waiting,
            "waving" or "jumping" or "celebrating" => VisitorState.HappyCelebration,
            "review" => VisitorState.Guarding,
            "running" => VisitorState.SniffingRight,
            _ => VisitorState.Idle
        };
        var priority = message.State == "failed" ? 80 : Math.Clamp(message.Priority, 0, 70);
        var duration = message.DurationMs > 0
            ? message.DurationMs
            : message.State == "failed" ? 5000 : message.State == "waiting" ? 0 : 1800;
        _activeVisitor.PlayState(visitorState, duration, priority);
    }

    private void StartFocus(int minutes, bool isBreak)
    {
        _focusTimer.Stop();
        _focusEndsAt = DateTimeOffset.Now.AddMinutes(minutes);
        _focusIsBreak = isBreak;
        _focusDurationMinutes = minutes;
        _focusTimer.Start();
        SendActionToExtension("focus-state", JsonSerializer.Serialize(new
        {
            state = isBreak ? "break" : "started",
            focusMinutes = isBreak ? 0 : minutes,
            breakMinutes = _settings.BreakMinutes
        }));
        Play(isBreak ? "waiting" : "review", isBreak ? 1800 : 0, 60);
        _activeVisitor?.PlayState(isBreak ? VisitorState.LyingDown : VisitorState.Guarding, isBreak ? 1800 : 0, 60);
        ShowBubble(isBreak ? $"休息开始，{minutes} 分钟后提醒你。" : $"专注开始，{minutes} 分钟后提醒你。", 4200);
    }

    private void SetOneTimeReminder(int minutes, string message)
    {
        _oneTimeReminderAt = DateTimeOffset.Now.AddMinutes(minutes);
        _oneTimeReminderMessage = string.IsNullOrWhiteSpace(message) ? "你设置的提醒时间到了。" : message;
        ShowBubble($"已设置 {minutes} 分钟后提醒。", 2600);
    }

    private void StopFocus(bool notify)
    {
        _focusTimer.Stop();
        _focusEndsAt = null;
        _focusIsBreak = false;
        _focusDurationMinutes = 0;
        SendActionToExtension("focus-state", JsonSerializer.Serialize(new { state = "stopped" }));
        if (notify) ShowBubble("计时已停止。", 2200);
        _activeVisitor?.RestoreAmbientState();
        RestoreAmbientState();
    }

    private void OnFocusTick(object? sender, EventArgs e)
    {
        if (_focusEndsAt is null || DateTimeOffset.Now < _focusEndsAt) return;
        _focusTimer.Stop();
        _focusEndsAt = null;
        if (_focusIsBreak)
        {
            Play("waving", 5000, 90);
            _activeVisitor?.PlayState(VisitorState.WakingStretch, 5000, 90);
            ShowBubble("休息结束，准备继续吧。", 6000);
            SendActionToExtension("focus-state", JsonSerializer.Serialize(new { state = "completed" }));
        }
        else
        {
            _settings.FocusSessionsToday += 1;
            _settings.FocusMinutesToday += _focusDurationMinutes;
            GrowthService.ApplyMain(_settings, GrowthAction.MainFocusComplete);
            SaveSettings();
            Play("celebrating", 5000, 90);
            _activeVisitor?.PlayState(VisitorState.HappyCelebration, 5000, 90);
            ShowBubble("专注完成，去喝口水、活动一下。", 6000);
            StartFocus(_settings.BreakMinutes, true);
        }
        if (!_settings.Muted) System.Media.SystemSounds.Asterisk.Play();
        _focusDurationMinutes = 0;
    }

    private void ShowBubble(string message, int durationMs = 4000)
    {
        if (!_settings.BubbleEnabled || string.IsNullOrWhiteSpace(message)) return;
        _bubble.ShowMessage(message, Left, Top, ActualWidth > 0 ? ActualWidth : Width, durationMs);
    }

    private void ResetSettings()
    {
        _settings = new PetSettings();
        _automaticMovement = _settings.AutomaticMovement;
        _velocityX = _settings.MovementSpeed;
        Topmost = _settings.Topmost;
        PlaceNearBottomRight();
        SaveSettings();
        ApplyMainPetIdentityUi();
        UpdateVisitorMenuItem();
        ShowTrayMessage("设置已重置", "托盘勾选状态将在下次启动时同步更新。");
    }

    private void CloseApplication()
    {
        _statusBarTimer.Stop();
        _satietyTimer.Stop();
        _mainStatusBar.HideBar();
        _visitorStatusBar.HideBar();
        _mainStatusBar.Close();
        _visitorStatusBar.Close();
        _shutdown.Cancel();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        CloseFrisbeeThrowWindow();
        _shutdown.Cancel();
        _animationTimer.Stop();
        _movementTimer.Stop();
        _stateRestoreTimer.Stop();
        _focusTimer.Stop();
        _reminderTimer.Stop();
        _moodTimer.Stop();
        _idleTimer.Stop();
        _bubble.Stop();
        _activeVisitor?.Stop();
        _activeVisitor = null;
        _activeVisitorId = null;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        SaveSettings();
    }

    private void WriteLog(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            var detail = exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";
            File.AppendAllText(_logPath, $"{DateTimeOffset.Now:O} | {message}{detail}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}