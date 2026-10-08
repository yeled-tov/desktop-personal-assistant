using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopAssistant.ScreenTranslation;

public sealed class TranslationOverlay
{
    private readonly IScreenCaptureProvider _capture = new ScreenCaptureProvider();
    private readonly IGeminiVisionProvider _gemini;
    private readonly bool _useGemini;
    private readonly string _targetLanguage;
    private readonly CancellationToken _cancellationToken;

    public TranslationOverlay(IGeminiVisionProvider gemini, bool useGemini, string targetLanguage = "he", CancellationToken cancellationToken = default)
    { _gemini = gemini; _useGemini = useGemini; _targetLanguage = targetLanguage; _cancellationToken = cancellationToken; }

    public async Task ShowAsync(Window owner)
    {
        using var overlayCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var grid = new Grid { Background = Brushes.Black };
        var image = new Image { Stretch = Stretch.Fill };
        var translationLayer = new Canvas { IsHitTestVisible = false };
        var status = new TextBlock { Text = "מכין תרגום…", Foreground = Brushes.White, Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };
        var statusBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 24, 28, 40)), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12) };
        statusBorder.Child = status;
        var originalButton = new Button { Content = "מקור / תרגום", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(3) };
        var growButton = new Button { Content = "+", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(3) };
        var shrinkButton = new Button { Content = "−", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(3) };
        var refreshButton = new Button { Content = "F5 · תרגם שוב", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(3) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { originalButton, growButton, shrinkButton, refreshButton } };
        var toolbar = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 24, 28, 40)), CornerRadius = new CornerRadius(7), Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12), Child = buttons };
        grid.Children.Add(image); grid.Children.Add(translationLayer); grid.Children.Add(statusBorder); grid.Children.Add(toolbar);

        var overlay = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, Content = grid, Cursor = Cursors.Arrow, FlowDirection = FlowDirection.RightToLeft, Background = Brushes.Black };
        IReadOnlyList<NormalizedScreenRegion> visibleRegions = [];
        double fontScale = 1;
        var showTranslations = true;
        var refreshing = false;

        void RenderRegions()
        {
            translationLayer.Children.Clear();
            if (!showTranslations || image.Source is not BitmapSource bitmap) return;
            foreach (var region in visibleRegions)
            {
                if (!IsValid(region)) continue;
                var x = overlay.Width * region.Left; var y = overlay.Height * region.Top;
                var width = Math.Max(42, overlay.Width * (region.Right - region.Left));
                var height = Math.Max(20, overlay.Height * (region.Bottom - region.Top));
                var fontSize = Math.Clamp(height * .65 * fontScale, 10, 48);
                var label = new Border { Width = width, Height = height, Padding = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(238, 255, 255, 255)), ClipToBounds = true, Child = new TextBlock { Text = region.TranslatedText, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = fontSize, Foreground = Brushes.Black, FontWeight = FontWeights.Medium, ToolTip = region.OriginalText } };
                Canvas.SetLeft(label, x); Canvas.SetTop(label, y); translationLayer.Children.Add(label);
            }
        }

        async Task RefreshAsync(bool recapture)
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                status.Text = "מצלם את המסך…";
                if (recapture) { overlay.Hide(); await Task.Delay(180, overlayCts.Token); }
                var frame = await _capture.CaptureAsync(overlayCts.Token);
                overlay.Left = frame.VirtualBounds.Left; overlay.Top = frame.VirtualBounds.Top; overlay.Width = frame.VirtualBounds.Width; overlay.Height = frame.VirtualBounds.Height;
                image.Source = frame.Image; visibleRegions = [];
                RenderRegions();
                if (!overlay.IsVisible) { overlay.Show(); overlay.Activate(); }
                if (!_useGemini) status.Text = "צילום המסך קפוא. מצב OCR מקומי עדיין לא הותקן.";
                else
                {
                    try
                    {
                        status.Text = "Gemini מזהה ומתרגם טקסט…";
                        var result = await _gemini.TranslateScreenshotAsync(frame, _targetLanguage, overlayCts.Token);
                        visibleRegions = result.Regions;
                        status.Text = $"התרגום מוכן · {visibleRegions.Count} אזורי טקסט · Esc לסגירה · F5 לרענון";
                    }
                    catch (OperationCanceledException) when (overlayCts.IsCancellationRequested) { return; }
                    catch (Exception ex) { status.Text = "לא ניתן לתרגם כרגע: " + ex.Message; }
                }
                RenderRegions();
            }
            finally { refreshing = false; }
        }

        originalButton.Click += (_, _) => { showTranslations = !showTranslations; RenderRegions(); };
        growButton.Click += (_, _) => { fontScale = Math.Min(2.2, fontScale + .1); RenderRegions(); };
        shrinkButton.Click += (_, _) => { fontScale = Math.Max(.6, fontScale - .1); RenderRegions(); };
        refreshButton.Click += async (_, _) => await RefreshAsync(true);
        overlay.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; overlay.Close(); }
            else if (e.Key == Key.F5) { e.Handled = true; await RefreshAsync(true); }
            else if (e.Key == Key.O) { e.Handled = true; showTranslations = !showTranslations; RenderRegions(); }
            else if (e.Key == Key.Add || e.Key == Key.OemPlus) { e.Handled = true; fontScale = Math.Min(2.2, fontScale + .1); RenderRegions(); }
            else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) { e.Handled = true; fontScale = Math.Max(.6, fontScale - .1); RenderRegions(); }
        };
        overlay.Closed += (_, _) => { overlayCts.Cancel(); closed.TrySetResult(); };
        await RefreshAsync(false);
        if (overlay.IsVisible) await closed.Task;
    }

    private static bool IsValid(NormalizedScreenRegion region) => !string.IsNullOrWhiteSpace(region.TranslatedText) &&
        double.IsFinite(region.Left) && double.IsFinite(region.Top) && double.IsFinite(region.Right) && double.IsFinite(region.Bottom) &&
        region.Left >= 0 && region.Top >= 0 && region.Right <= 1 && region.Bottom <= 1 && region.Right > region.Left && region.Bottom > region.Top;
}
