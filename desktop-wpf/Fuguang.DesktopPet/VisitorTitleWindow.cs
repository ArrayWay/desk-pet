using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fuguang.DesktopPet;

public sealed class VisitorTitleWindow : Window
{
    private readonly VisitorTitleProgress _progress;
    private readonly StackPanel _historyPanel = new();
    private readonly System.Windows.Media.Brush _mutedBrush;
    private readonly System.Windows.Media.Brush _accentBrush;

    public VisitorTitleWindow(string visitorName, VisitorTitleProgress progress, string theme)
    {
        _progress = progress;
        Title = $"{visitorName}的称号历程";
        Width = 330;
        SizeToContent = SizeToContent.Height;
        MinHeight = 210;
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 80);
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        (_mutedBrush, _accentBrush) = CreateThemeBrushes(theme);
        ApplyTheme(theme);
        Content = BuildContent(visitorName);
    }

    private static (System.Windows.Media.Brush Muted, System.Windows.Media.Brush Accent) CreateThemeBrushes(string theme)
    {
        var values = theme.ToLowerInvariant() switch
        {
            "cute" => ("#806A73", "#FF7380"),
            "emerald" => ("#6B5B45", "#0F7A4D"),
            "academy" => ("#B9A978", "#C78C3D"),
            _ => ("#9EB4C8", "#4EDCFF")
        };
        return (new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(values.Item1)),
            new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(values.Item2)));
    }

    private void ApplyTheme(string theme)
    {
        var (background, foreground) = theme.ToLowerInvariant() switch
        {
            "cute" => ("#FFF8E9", "#4D3844"),
            "emerald" => ("#F8F2DD", "#291F1A"),
            "academy" => ("#0B0E2B", "#F4EDDA"),
            _ => ("#0B1726", "#F2F8FF")
        };
        Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(background));
        Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(foreground));
    }

    private UIElement BuildContent(string visitorName)
    {
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = $"{visitorName} · 称号阶段", FontSize = 16, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = $"当前称号：{_progress.CurrentTitle}", Margin = new Thickness(0, 14, 0, 4), FontSize = 13 });
        root.Children.Add(new TextBlock { Text = _progress.CurrentRequirement, Foreground = _mutedBrush, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = $"亲密度 {_progress.CurrentAffection} · 互动 {_progress.CurrentInteractions} 次", Margin = new Thickness(0, 5, 0, 12), Foreground = _mutedBrush });
        root.Children.Add(new TextBlock { Text = $"下一阶段：{_progress.NextTitle}", FontSize = 13 });
        root.Children.Add(new TextBlock { Text = _progress.NextRequirement, Margin = new Thickness(0, 4, 0, 14), Foreground = _mutedBrush, TextWrapping = TextWrapping.Wrap });

        var more = new System.Windows.Controls.Button
        {
            Content = "更多：查看全部称号进阶历程",
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Background = _accentBrush,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = _accentBrush
        };
        more.Click += (_, _) =>
        {
            _historyPanel.Children.Clear();
            foreach (var (title, index) in _progress.History.Select((title, index) => (title, index)))
            {
                _historyPanel.Children.Add(new TextBlock { Text = $"{index + 1}. {title}", Margin = new Thickness(0, 3, 0, 3) });
            }
            more.Visibility = Visibility.Collapsed;
        };
        root.Children.Add(more);
        root.Children.Add(_historyPanel);
        return root;
    }
}