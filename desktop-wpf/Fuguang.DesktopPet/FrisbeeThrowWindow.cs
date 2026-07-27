using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Fuguang.DesktopPet;

public sealed class FrisbeeThrowWindow : Window
{
    private System.Windows.Point? _throwStart;

    public event Action<System.Windows.Point, Vector>? ThrowReleased;

    public FrisbeeThrowWindow()
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
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        KeyDown += OnKeyDown;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _throwStart = PointToScreen(e.GetPosition(this));
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_throwStart is not System.Windows.Point start) return;
        var end = PointToScreen(e.GetPosition(this));
        if (IsMouseCaptured) ReleaseMouseCapture();
        _throwStart = null;
        ThrowReleased?.Invoke(start, end - start);
        Close();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}