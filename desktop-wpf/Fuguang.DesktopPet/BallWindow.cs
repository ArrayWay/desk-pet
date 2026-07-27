using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fuguang.DesktopPet;

public sealed class BallWindow : Window
{
    public BallWindow(string imagePath)
    {
        Width = 54;
        Height = 54;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        var image = new System.Windows.Controls.Image { Source = bitmap, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Content = image;
    }

    public void Place(double left, double top)
    {
        Left = left + (168 - Width) / 2;
        Top = top + 182 - Height;
        Show();
    }
}