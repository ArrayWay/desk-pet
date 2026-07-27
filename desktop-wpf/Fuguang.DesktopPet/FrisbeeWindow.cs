using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fuguang.DesktopPet;

public sealed class FrisbeeWindow : Window
{
    public FrisbeeWindow(string imagePath)
    {
        Width = 62;
        Height = 40;
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

    public void Place(double centerX, double centerY)
    {
        Left = centerX - Width / 2;
        Top = centerY - Height / 2;
        if (!IsVisible) Show();
    }
}