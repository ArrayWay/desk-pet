using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Fuguang.DesktopPet;

public sealed class FetchTargetWindow : Window
{
    public event Action<double, double>? TargetSelected;

    public FetchTargetWindow()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = System.Windows.Input.Cursors.Cross;
        MouseLeftButtonDown += OnTargetSelected;
        KeyDown += OnKeyDown;
    }

    private void OnTargetSelected(object sender, MouseButtonEventArgs e)
    {
        var point = PointToScreen(e.GetPosition(this));
        TargetSelected?.Invoke(point.X, point.Y);
        Close();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}