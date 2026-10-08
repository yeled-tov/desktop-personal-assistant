using System.Windows;
using System.Windows.Controls;
using Window = System.Windows.Window;

namespace DesktopAssistant;

public sealed class WelcomeWindow : Window
{
    public bool ConnectGemini { get; private set; }
    public WelcomeWindow()
    {
        Title = "ברוך הבא"; Width = 480; Height = 280; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; FlowDirection = FlowDirection.RightToLeft;
        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = "ברוך הבא לעוזר האישי למחשב", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = "אפשר לחבר Gemini כדי לקבל עוזר חכם שמבין שאלות, מסך ופקודות מורכבות. החיבור אופציונלי; הפעולות המקומיות זמינות גם בלעדיו.", Margin = new Thickness(0,12,0,12), TextWrapping = TextWrapping.Wrap });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var connect = new Button { Content = "חבר Gemini", Margin = new Thickness(0,0,8,0) };
        connect.Click += (_, _) => { ConnectGemini = true; DialogResult = true; };
        var continueButton = new Button { Content = "המשך ללא Gemini", Background = System.Windows.Media.Brushes.LightGray, Foreground = System.Windows.Media.Brushes.Black };
        continueButton.Click += (_, _) => { ConnectGemini = false; DialogResult = true; };
        row.Children.Add(connect); row.Children.Add(continueButton); stack.Children.Add(row); Content = stack;
    }
}
