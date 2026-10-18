using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopAssistant.UI;

public static class Theme
{
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI, Arial");
    public static readonly Color AccentColor = Color.FromRgb(76, 141, 246);

    public static System.Windows.Media.ImageSource? AppIcon
    {
        get
        {
            try { return System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico")); }
            catch { return null; }
        }
    }

    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static Button PillButton(string text) => new()
    {
        Content = text,
        Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
        Foreground = Brushes.White,
        Padding = new Thickness(14, 6, 14, 6),
        Margin = new Thickness(3, 0, 3, 0),
        FontSize = 13.5
    };

    public static Button Chip(string text) => new()
    {
        Content = text,
        Background = new SolidColorBrush(Color.FromRgb(241, 243, 244)),
        Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
        Padding = new Thickness(14, 7, 14, 7),
        Margin = new Thickness(0, 0, 8, 8),
        FontSize = 13.5
    };

    public static Effect Shadow(double blur = 28, double opacity = 0.35) => new DropShadowEffect { BlurRadius = blur, ShadowDepth = 3, Opacity = opacity, Color = Colors.Black };

    public static bool ContainsHebrew(string text) => text.Any(c => c >= 'א' && c <= 'ת');
}
