using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fuguang.DesktopPet;

public sealed class NotificationBubbleWindow : Window
{
    private readonly TextBlock _message;
    private readonly DispatcherTimer _closeTimer = new();

    public NotificationBubbleWindow()
    {
        Width = 240;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;

        _message = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 208
        };
        Content = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(232, 35, 39, 44)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 152, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Child = _message
        };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Hide();
        };
    }

    public void ShowMessage(string message, double petLeft, double petTop, double petWidth, int durationMs = 4000)
    {
        _message.Text = message;
        Left = Math.Max(SystemParameters.VirtualScreenLeft, petLeft + petWidth - Width);
        Top = Math.Max(SystemParameters.VirtualScreenTop, petTop - 78);
        Show();
        _closeTimer.Stop();
        _closeTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, durationMs));
        _closeTimer.Start();
    }

    public void Stop()
    {
        _closeTimer.Stop();
        Close();
    }
}