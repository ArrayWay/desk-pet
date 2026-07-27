using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Fuguang.DesktopPet;

public enum StatusBarTheme
{
    Main,
    Visitor
}

/// <summary>Desktop numeric status bar for affection/stamina/satiety.</summary>
public sealed class StatusBarWindow : Window
{
    private const double PreferredGap = 6;
    private readonly Border _shell;
    private readonly Border _innerHighlight;
    private readonly TextBlock _titleText;
    private readonly TextBlock _statsText;
    private StatusBarTheme _theme = StatusBarTheme.Main;

    public StatusBarWindow(StatusBarTheme theme = StatusBarTheme.Main)
    {
        _theme = theme;
        Width = 300;
        Height = 58;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 220;
        MaxWidth = 360;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Focusable = false;

        _titleText = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 15
        };
        _statsText = new TextBlock
        {
            FontSize = 10.5,
            FontWeight = FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 14,
            Margin = new Thickness(0, 2, 0, 0),
            Opacity = 0.92
        };

        var stack = new StackPanel();
        stack.Children.Add(_titleText);
        stack.Children.Add(_statsText);

        _innerHighlight = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 6, 10, 6),
            BorderThickness = new Thickness(1),
            Child = stack
        };

        _shell = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Child = _innerHighlight,
            Effect = new DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 2,
                Opacity = 0.28,
                Direction = 270
            }
        };

        Content = _shell;
        ApplyTheme(_theme);
    }

    public void SetTheme(StatusBarTheme theme)
    {
        if (_theme == theme) return;
        _theme = theme;
        ApplyTheme(theme);
    }

    private void ApplyTheme(StatusBarTheme theme)
    {
        if (theme == StatusBarTheme.Main)
        {
            // Soft light orange glass for main pet.
            _shell.Background = new LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(235, 255, 196, 120),
                System.Windows.Media.Color.FromArgb(228, 255, 168, 82),
                90);
            _shell.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 236, 210));
            _innerHighlight.Background = new LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(70, 255, 255, 255),
                System.Windows.Media.Color.FromArgb(18, 255, 255, 255),
                90);
            _innerHighlight.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 255, 255, 255));
            var ink = new SolidColorBrush(System.Windows.Media.Color.FromRgb(92, 48, 12));
            _titleText.Foreground = ink;
            _statsText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 92, 48, 12));
        }
        else
        {
            // Cool blue glass for visitor.
            _shell.Background = new LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(235, 110, 168, 255),
                System.Windows.Media.Color.FromArgb(228, 64, 126, 232),
                90);
            _shell.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 210, 230, 255));
            _innerHighlight.Background = new LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(75, 255, 255, 255),
                System.Windows.Media.Color.FromArgb(18, 255, 255, 255),
                90);
            _innerHighlight.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(95, 255, 255, 255));
            var ink = new SolidColorBrush(System.Windows.Media.Color.FromRgb(12, 36, 84));
            _titleText.Foreground = ink;
            _statsText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(235, 12, 36, 84));
        }
    }

    public void UpdateContent(string name, int affection, int stamina, int satiety, string? title = null, string? detail = null)
    {
        var titlePart = string.IsNullOrWhiteSpace(title) ? string.Empty : (" · " + title);
        _titleText.Text = name + titlePart;
        var statsPart = "亲密度 " + affection + "  ·  精力 " + stamina + "  ·  饱食 " + satiety;
        _statsText.Text = string.IsNullOrWhiteSpace(detail) ? statsPart : statsPart + "\n" + detail;
        if (IsVisible)
        {
            UpdateLayout();
        }
    }

    /// <summary>
    /// Place the bar above the host when possible; fall back below if near the top edge.
    /// Does not cover the character body.
    /// </summary>
    public void PlaceNear(double hostLeft, double hostTop, double hostWidth, double hostHeight)
    {
        UpdateLayout();
        var barWidth = ActualWidth > 1 ? ActualWidth : Width;
        var barHeight = ActualHeight > 1 ? ActualHeight : Height;

        var left = hostLeft + (hostWidth - barWidth) / 2;
        var topAbove = hostTop - barHeight - PreferredGap;
        var topBelow = hostTop + hostHeight + PreferredGap;
        var top = topAbove >= SystemParameters.VirtualScreenTop
            ? topAbove
            : topBelow;

        var minLeft = SystemParameters.VirtualScreenLeft;
        var maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - barWidth;
        Left = Math.Clamp(left, minLeft, Math.Max(minLeft, maxLeft));
        Top = top;

        if (!IsVisible)
        {
            Show();
        }
    }

    public Rect GetBounds()
    {
        var w = ActualWidth > 1 ? ActualWidth : Width;
        var h = ActualHeight > 1 ? ActualHeight : Height;
        return new Rect(Left, Top, w, h);
    }

    public void Nudge(double dx, double dy)
    {
        Left += dx;
        Top += dy;
        var barWidth = ActualWidth > 1 ? ActualWidth : Width;
        var barHeight = ActualHeight > 1 ? ActualHeight : Height;
        var minLeft = SystemParameters.VirtualScreenLeft;
        var maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - barWidth;
        Left = Math.Clamp(Left, minLeft, Math.Max(minLeft, maxLeft));
        var minTop = SystemParameters.VirtualScreenTop;
        var maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - barHeight;
        Top = Math.Clamp(Top, minTop, Math.Max(minTop, maxTop));
    }

    public void HideBar()
    {
        if (IsVisible)
        {
            Hide();
        }
    }
}
