using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;
using WpfBrush = System.Windows.Media.Brush;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace Fuguang.DesktopPet;

public sealed class ThemeMenuWindow : Window
{
    private readonly Action<string> _setTheme;
    private readonly Action _refreshMenu;
    private readonly Dictionary<string, WpfButton> _themeButtons = new(StringComparer.OrdinalIgnoreCase);
    private Func<string> _getFocusStatus;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ScrollViewer? _menuScroll;
    private TextBlock? _focusStatusText;
    private TextBlock? _focusRemainingText;
    private ThemePalette _palette;

    public ThemeMenuWindow(
        string theme,
        string petName,
        bool petVisible,
        bool automaticMovement,
        bool paused,
        string focusStatus,
        Action<string> setTheme,
        Action refreshMenu,
        Action toggleVisibility,
        Action toggleMovement,
        Action togglePause,
        Action renamePet,
        Action changeSkin,
        Action companion,
        IReadOnlyList<(string Label, Action Action)> companionActions,
        IReadOnlyList<(string Label, Action Action)> focusActions,
        IReadOnlyList<(string Label, Action Action)> reminderActions,
        IReadOnlyList<(string Label, Action Action)> growthActions,
        IReadOnlyList<(string Label, Action Action)> systemActions,
        IReadOnlyList<(string Label, Action Action)> vscodeActions,
        Action reset,
        Action quit)
    {
        _setTheme = setTheme;
        _refreshMenu = refreshMenu;
        _getFocusStatus = () => focusStatus;
        Width = 320;
        Height = 620;
        MaxWidth = Math.Max(220, SystemParameters.WorkArea.Width - 16);
        MaxHeight = Math.Max(220, SystemParameters.WorkArea.Height - 16);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowActivated = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _palette = ThemePalette.For(theme);
        Content = BuildContent(petName, petVisible, automaticMovement, paused, focusStatus, toggleVisibility, toggleMovement, togglePause, renamePet, changeSkin, companion, companionActions, focusActions, reminderActions, growthActions, systemActions, vscodeActions, reset, quit);
        ApplyPalette(theme);
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        Deactivated += OnDeactivated;
        _statusTimer.Tick += (_, _) => RefreshFocusStatus();
        _statusTimer.Start();
    }

    public void ShowAtCursor()
    {
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var scale = GetDpiScale();
        var workArea = new Rect(
            screen.WorkingArea.Left / scale,
            screen.WorkingArea.Top / scale,
            screen.WorkingArea.Width / scale,
            screen.WorkingArea.Height / scale);
        var cursorX = cursor.X / scale;
        var cursorY = cursor.Y / scale;
        var menuWidth = ActualWidth > 0 ? ActualWidth : Width;
        var menuHeight = ActualHeight > 0 ? ActualHeight : Height;
        var left = cursorX - menuWidth + 12;
        var top = cursorY - menuHeight - 8;
        if (top < workArea.Top + 8)
        {
            top = cursorY + 8;
        }

        Left = Math.Clamp(left, workArea.Left + 8, Math.Max(workArea.Left + 8, workArea.Right - menuWidth - 8));
        Top = Math.Clamp(top, workArea.Top + 8, Math.Max(workArea.Top + 8, workArea.Bottom - menuHeight - 8));
        if (!IsVisible)
        {
            Show();
        }
        Activate();
    }

    public void ShowAtPosition(double left, double top, double verticalOffset = 0)
    {
        Left = left;
        Top = top;
        if (!IsVisible)
        {
            Show();
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _menuScroll?.ScrollToVerticalOffset(Math.Max(0, verticalOffset));
        }));
        Activate();
    }

    public double GetScrollOffset() => _menuScroll?.VerticalOffset ?? 0;

    public void SetFocusStatusProvider(Func<string> getFocusStatus)
    {
        _getFocusStatus = getFocusStatus;
        RefreshFocusStatus();
    }

    private static double GetDpiScale()
    {
        using var graphics = Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return Math.Max(1, graphics.DpiX / 96.0);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
        DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        base.OnClosed(e);
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is WpfButton) return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    public void ApplyPalette(string theme)
    {
        _palette = ThemePalette.For(theme);
        Background = _palette.WindowBrush;
        if (Content is Border border)
        {
            border.Background = _palette.WindowBrush;
            border.BorderBrush = _palette.BorderBrush;
        }

        foreach (var button in _themeButtons)
        {
            button.Value.Background = button.Key.Equals(theme, StringComparison.OrdinalIgnoreCase)
                ? _palette.ActiveBrush
                : _palette.RowBrush;
            button.Value.Foreground = _palette.ForegroundBrush;
        }

        if (Content is not DependencyObject contentRoot) return;
        foreach (var child in Descendants(contentRoot).OfType<FrameworkElement>())
        {
            if (child is WpfButton button && !_themeButtons.ContainsValue(button))
            {
                button.Background = _palette.RowBrush;
                button.Foreground = Equals(button.Tag, "danger")
                    ? System.Windows.Media.Brushes.DarkRed
                    : _palette.ForegroundBrush;
                button.BorderBrush = _palette.RowBorderBrush;
            }
            else if (child is TextBlock text)
            {
                text.Foreground = IsDangerElement(text)
                    ? System.Windows.Media.Brushes.DarkRed
                    : _palette.MutedBrush;
            }
        }
    }

    private static bool IsDangerElement(DependencyObject element)
    {
        var parent = VisualTreeHelper.GetParent(element);
        while (parent is not null)
        {
            if (parent is WpfButton button && Equals(button.Tag, "danger")) return true;
            parent = VisualTreeHelper.GetParent(parent);
        }

        return false;
    }

    private Border BuildContent(
        string petName,
        bool petVisible,
        bool automaticMovement,
        bool paused,
        string focusStatus,
        Action toggleVisibility,
        Action toggleMovement,
        Action togglePause,
        Action renamePet,
        Action changeSkin,
        Action companion,
        IReadOnlyList<(string Label, Action Action)> companionActions,
        IReadOnlyList<(string Label, Action Action)> focusActions,
        IReadOnlyList<(string Label, Action Action)> reminderActions,
        IReadOnlyList<(string Label, Action Action)> growthActions,
        IReadOnlyList<(string Label, Action Action)> systemActions,
        IReadOnlyList<(string Label, Action Action)> vscodeActions,
        Action reset,
        Action quit)
    {
        var root = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _menuScroll = scroll;
        var stack = new StackPanel();
        root.Child = scroll;
        scroll.Content = stack;

        var header = new StackPanel { Margin = new Thickness(3, 0, 3, 9) };
        header.Children.Add(new TextBlock { Text = "桌宠功能菜单", FontSize = 16, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = $"{petName}  ·  快速控制与桌宠设置", FontSize = 9, Margin = new Thickness(0, 2, 0, 0) });
        stack.Children.Add(header);

        AddGridSection(stack, "快捷操作",
            ActionButton("◉", petVisible ? "隐藏桌宠" : "显示桌宠", "保留后台活动", toggleVisibility),
            ActionButton("Ⅱ", paused ? "继续动画" : "暂停动画", "切换动画状态", togglePause),
            ActionButton("↔", $"自动行走：{(automaticMovement ? "已开启" : "已关闭")}", "切换移动状态", toggleMovement),
            ActionButton("◈", "主宠换装", "切换外观", changeSkin),
            ActionButton("♥", "狗狗玩伴", "召唤与互动", companion),
            ActionButton("✎", "改名与备注…", "修改主宠信息", renamePet));

        var themeTitle = new TextBlock { Text = "界面主题", FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 3, 4) };
        stack.Children.Add(themeTitle);
        var themes = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
        AddTheme(themes, "tech", "科技");
        AddTheme(themes, "cute", "可爱");
        AddTheme(themes, "emerald", "绿野");
        AddTheme(themes, "academy", "魔法");
        stack.Children.Add(themes);

        AddActionList(stack, "玩伴互动", companionActions, "♥");
        AddActionList(stack, "专注计时", focusActions, "◷", focusStatus);
        AddActionList(stack, "提醒", reminderActions, "⌁");
        AddActionList(stack, "养成系统", growthActions, "✧");
        AddActionList(stack, "设置与定位", systemActions, "⚙");
        AddCollapsibleActionList(stack, "VS Code 联动", vscodeActions, "⌘");
        AddSeparator(stack);
        AddSection(stack, "系统",
            ActionButton("↺", "重置设置", "恢复默认配置", reset),
            ActionButton("×", "退出桌宠", "关闭应用", quit, danger: true));

        return root;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void AddTheme(UniformGrid panel, string key, string label)
    {
        var button = new WpfButton { Content = label, Height = 30, Margin = new Thickness(0, 0, 5, 4), FontSize = 10, BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 5, 0) };
        button.Click += (_, _) => _setTheme(key);
        _themeButtons[key] = button;
        panel.Children.Add(button);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Hide();
        e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.XButton1 && e.ChangedButton != MouseButton.XButton2) return;
        Hide();
        e.Handled = true;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current.Windows.OfType<VisitorTitleWindow>().Any(window => window.IsActive)) return;
        if (IsVisible) Hide();
    }

    private void RefreshFocusStatus()
    {
        SetFocusStatus(_getFocusStatus());
    }

    private void SetFocusStatus(string status)
    {
        if (_focusStatusText is null) return;
        var separator = status.LastIndexOf(" · 剩余 ", StringComparison.Ordinal);
        if (separator < 0)
        {
            _focusStatusText.Text = status;
            if (_focusRemainingText is not null) _focusRemainingText.Text = string.Empty;
            return;
        }

        _focusStatusText.Text = status[..separator];
        if (_focusRemainingText is not null) _focusRemainingText.Text = status[(separator + 6)..];
    }

    private WpfButton ActionButton(string glyph, string title, string detail, Action action, bool danger = false)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, Width = 22, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        var labels = new StackPanel();
        labels.Children.Add(new TextBlock { Text = title, FontSize = 11 });
        if (!string.IsNullOrWhiteSpace(detail)) labels.Children.Add(new TextBlock { Text = detail, FontSize = 9, Margin = new Thickness(0, 1, 0, 0) });
        panel.Children.Add(labels);
        var button = new WpfButton { Content = panel, Height = string.IsNullOrWhiteSpace(detail) ? 32 : 36, HorizontalContentAlignment = WpfHorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 3), Padding = new Thickness(7, 2, 7, 2), BorderThickness = new Thickness(1), Tag = danger ? "danger" : null };
        button.Click += (_, _) =>
        {
            action();
            _refreshMenu();
        };
        return button;
    }

    private void AddSection(WpfPanel parent, string title, params WpfButton[] buttons)
    {
        parent.Children.Add(new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 2, 3, 4) });
        foreach (var button in buttons) parent.Children.Add(button);
    }

    private void AddGridSection(WpfPanel parent, string title, params WpfButton[] buttons)
    {
        parent.Children.Add(new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 2, 3, 4) });
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var button in buttons)
        {
            button.Margin = new Thickness(0, 0, 5, 4);
            button.Height = 38;
            grid.Children.Add(button);
        }
        parent.Children.Add(grid);
    }

    private static void AddSeparator(WpfPanel parent)
    {
        parent.Children.Add(new Border { Height = 1, Margin = new Thickness(3, 5, 3, 7), Background = System.Windows.Media.Brushes.Gray, Opacity = 0.35 });
    }

    private void AddActionList(WpfPanel parent, string title, IReadOnlyList<(string Label, Action Action)> actions, string glyph, string? status = null)
    {
        if (actions.Count == 0) return;
        parent.Children.Add(new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 2, 3, 4) });
        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusPanel = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(3, -1, 3, 4) };
            _focusStatusText = new TextBlock { FontSize = 9, FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Center };
            _focusRemainingText = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            statusPanel.Children.Add(_focusStatusText);
            statusPanel.Children.Add(_focusRemainingText);
            parent.Children.Add(statusPanel);
            SetFocusStatus(status);
        }
        foreach (var item in actions)
        {
            parent.Children.Add(ActionButton(glyph, item.Label, string.Empty, item.Action));
        }
    }

    private void AddCollapsibleActionList(WpfPanel parent, string title, IReadOnlyList<(string Label, Action Action)> actions, string glyph)
    {
        if (actions.Count == 0) return;
        var stack = new StackPanel();
        foreach (var item in actions) stack.Children.Add(ActionButton(glyph, item.Label, string.Empty, item.Action));
        parent.Children.Add(new Expander
        {
            Header = new TextBlock { Text = $"{title}  ·  {actions.Count} 项", FontSize = 9, FontWeight = FontWeights.SemiBold },
            Content = stack,
            IsExpanded = false,
            Margin = new Thickness(3, 2, 3, 4),
            Padding = new Thickness(0, 2, 0, 0),
            BorderThickness = new Thickness(0)
        });
    }

    private sealed record ThemePalette(WpfBrush WindowBrush, WpfBrush BorderBrush, WpfBrush RowBrush, WpfBrush RowBorderBrush, WpfBrush ActiveBrush, WpfBrush ForegroundBrush, WpfBrush MutedBrush)
    {
        public static ThemePalette For(string theme) => theme.ToLowerInvariant() switch
        {
            "cute" => New("#FFF8E9", "#FFD1D8", "#FF7380", "#FFFFFF55", "#FF738066", "#FF738044", "#4D3844", "#806A73"),
            "emerald" => New("#F8F2DD", "#B8E2BE", "#0F7A4D", "#FFFFFF66", "#0F7A4D66", "#F0AA3A66", "#291F1A", "#6B5B45"),
            "academy" => New("#0B0E2B", "#263E83", "#C78C3D", "#FFFFFF12", "#C78C3D66", "#293F9A99", "#F4EDDA", "#B9A978"),
            _ => New("#0B1726", "#123D5A", "#4EDCFF", "#FFFFFF0D", "#4EDCFF66", "#287FA099", "#F2F8FF", "#9EB4C8")
        };

        private static ThemePalette New(string backgroundStart, string backgroundEnd, string border, string row, string rowBorder, string active, string foreground, string muted) => new(
            GradientBrush(backgroundStart, backgroundEnd), Brush(border), Brush(row), Brush(rowBorder), Brush(active), Brush(foreground), Brush(muted));

        private static WpfBrush Brush(string value) => new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(value));

        private static WpfBrush GradientBrush(string start, string end)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop((WpfColor)WpfColorConverter.ConvertFromString(start), 0));
            brush.GradientStops.Add(new GradientStop((WpfColor)WpfColorConverter.ConvertFromString(end), 1));
            return brush;
        }
    }
}