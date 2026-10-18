using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;
using DesktopAssistant.UI;

namespace DesktopAssistant.Overlay;

/// <summary>One frozen monitor. Draw around anything to search it.</summary>
public sealed class CircleSearchWindow : Window
{
    private readonly CircleSearchSession _session;
    private readonly MonitorShot _shot;
    private readonly Grid _root = new();
    private readonly Image _frozen = new() { Stretch = Stretch.Fill };
    private readonly Rectangle _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Path _spotlight = new() { Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Canvas _drawLayer = new() { Background = Brushes.Transparent };
    private readonly Canvas _translationLayer = new() { IsHitTestVisible = false };
    private readonly Polyline _stroke;
    private readonly Border _selectionFrame = new() { BorderBrush = Brushes.White, BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(14), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border _hint;
    private readonly TextBlock _hintText = new() { Foreground = Brushes.White, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
    private readonly BottomSheet _sheet;
    private readonly Border _glow;

    private bool _drawing;
    private Rect _selection = Rect.Empty;
    private BitmapSource? _selectionImage;
    private CancellationTokenSource? _work;
    private IReadOnlyList<TranslatedRegion> _regions = [];
    private Rect _regionsArea;
    private bool _showOriginal;

    internal CircleSearchWindow(CircleSearchSession session, MonitorShot shot)
    {
        _session = session;
        _shot = shot;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        FlowDirection = FlowDirection.LeftToRight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = shot.Bounds.X; Top = shot.Bounds.Y; Width = Math.Max(100, shot.Bounds.Width); Height = Math.Max(100, shot.Bounds.Height);
        Opacity = 0;
        Cursor = Cursors.Cross;
        FontFamily = Theme.Font;

        _frozen.Source = shot.Image;
        _stroke = new Polyline
        {
            Stroke = Brushes.White, StrokeThickness = 6, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Effect = new DropShadowEffect { Color = Color.FromRgb(66, 133, 244), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 }, IsHitTestVisible = false
        };
        _drawLayer.Children.Add(_selectionFrame);
        _drawLayer.Children.Add(_stroke);

        _glow = BuildGlowBorder();
        _hint = BuildTopBar();
        _hint.FlowDirection = FlowDirection.RightToLeft;
        _sheet = new BottomSheet { Visibility = Visibility.Collapsed, FlowDirection = FlowDirection.RightToLeft };
        _sheet.SearchClicked += () => SearchGoogle();
        _sheet.LensClicked += () => OpenLens();
        _sheet.TranslateClicked += () => _ = TranslateSelectionAsync();
        _sheet.CopyClicked += () => _ = CopyTextAsync();
        _sheet.AskAssistantClicked += question => AskAssistant(question);
        _sheet.FollowUp += question => _ = FollowUpAsync(question);

        _root.Children.Add(_frozen);
        _root.Children.Add(_dim);
        _root.Children.Add(_spotlight);
        _root.Children.Add(_translationLayer);
        _root.Children.Add(_drawLayer);
        _root.Children.Add(_glow);
        _root.Children.Add(_hint);
        _root.Children.Add(_sheet);
        Content = _root;

        _drawLayer.MouseLeftButtonDown += OnDown;
        _drawLayer.MouseMove += OnMove;
        _drawLayer.MouseLeftButtonUp += OnUp;
        _drawLayer.MouseRightButtonUp += (_, _) => { if (_selection.IsEmpty) Close(); else ClearSelection(); };
        KeyDown += OnKey;
        SourceInitialized += (_, _) => PlaceExactly();
        Loaded += (_, _) => FadeIn();
    }

    public bool Contains(int x, int y) => x >= _shot.Bounds.X && x < _shot.Bounds.X + _shot.Bounds.Width && y >= _shot.Bounds.Y && y < _shot.Bounds.Y + _shot.Bounds.Height;

    /// <summary>Cover the monitor exactly in physical pixels (independent of display scaling).</summary>
    private void PlaceExactly()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _shot.Bounds.X, _shot.Bounds.Y, _shot.Bounds.Width, _shot.Bounds.Height, Native.SWP_SHOWWINDOW);
    }

    private void FadeIn()
    {
        PlaceExactly();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
    }

    // ---------------- drawing ----------------

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_regions.Count > 0 && _selection.IsEmpty) ClearTranslation();
        _session.OnSelectionStarted(this);
        ClearSelection();
        _drawing = true;
        _stroke.Points.Clear();
        _stroke.Opacity = 1;
        _stroke.Points.Add(e.GetPosition(_drawLayer));
        _drawLayer.CaptureMouse();
        _hint.Visibility = Visibility.Collapsed;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_drawing) return;
        var p = e.GetPosition(_drawLayer);
        var last = _stroke.Points[^1];
        if ((p - last).Length > 2) _stroke.Points.Add(p);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        _drawLayer.ReleaseMouseCapture();
        var points = _stroke.Points;
        if (points.Count == 0) return;
        var bounds = new Rect(points[0], points[0]);
        foreach (var p in points) bounds.Union(p);
        if (bounds.Width < 14 && bounds.Height < 14)
        {
            // A tap: select a reasonable area around it, like tapping an object on the phone.
            var c = points[0];
            bounds = new Rect(c.X - 160, c.Y - 110, 320, 220);
        }
        bounds.Inflate(6, 6);
        bounds.Intersect(new Rect(0, 0, _drawLayer.ActualWidth, _drawLayer.ActualHeight));
        if (bounds.IsEmpty || bounds.Width < 8 || bounds.Height < 8) return;
        _stroke.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450)));
        Select(bounds);
    }

    private void Select(Rect bounds)
    {
        _selection = bounds;
        _selectionImage = Crop(bounds);
        Canvas.SetLeft(_selectionFrame, bounds.X); Canvas.SetTop(_selectionFrame, bounds.Y);
        _selectionFrame.Width = bounds.Width; _selectionFrame.Height = bounds.Height;
        _selectionFrame.Visibility = Visibility.Visible;
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var hole = new RectangleGeometry(bounds, 14, 14);
        _spotlight.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
        _spotlight.Visibility = Visibility.Visible;
        _dim.Visibility = Visibility.Collapsed;
        _sheet.ShowFor(bounds, ActualHeight);
        _ = DescribeAsync();
    }

    public void ClearSelection()
    {
        _work?.Cancel();
        _selection = Rect.Empty;
        _selectionImage = null;
        _selectionFrame.Visibility = Visibility.Collapsed;
        _spotlight.Visibility = Visibility.Collapsed;
        _dim.Visibility = Visibility.Visible;
        _sheet.Visibility = Visibility.Collapsed;
        _stroke.Points.Clear();
        if (_regions.Count > 0 && _regionsArea != new Rect(0, 0, ActualWidth, ActualHeight)) ClearTranslation();
    }

    private BitmapSource Crop(Rect dipRect)
    {
        var sx = _shot.Image.PixelWidth / Math.Max(1, ActualWidth);
        var sy = _shot.Image.PixelHeight / Math.Max(1, ActualHeight);
        var x = (int)Math.Clamp(dipRect.X * sx, 0, _shot.Image.PixelWidth - 1);
        var y = (int)Math.Clamp(dipRect.Y * sy, 0, _shot.Image.PixelHeight - 1);
        var w = (int)Math.Clamp(dipRect.Width * sx, 1, _shot.Image.PixelWidth - x);
        var h = (int)Math.Clamp(dipRect.Height * sy, 1, _shot.Image.PixelHeight - y);
        var crop = new CroppedBitmap(_shot.Image, new Int32Rect(x, y, w, h));
        crop.Freeze();
        return crop;
    }

    // ---------------- actions ----------------

    private IChatModel? ModelOrExplain()
    {
        var model = _session.GetModel();
        if (model is null) _sheet.ShowMessage("כדי לזהות ולתרגם צריך לחבר ספק AI (Gemini, OpenAI או Claude). פתח הגדרות והדבק מפתח API.", showSettings: true, onSettings: () => { Close(); _session.OpenSettings(); });
        return model;
    }

    private CancellationToken NewWork()
    {
        _work?.Cancel();
        _work = new CancellationTokenSource();
        return _work.Token;
    }

    private SelectionInsight? _insight;

    private async Task DescribeAsync()
    {
        _insight = null;
        var model = ModelOrExplain();
        if (model is null || _selectionImage is null) return;
        var token = NewWork();
        _sheet.ShowLoading("מזהה מה סימנת…");
        try
        {
            _insight = await ScreenVision.DescribeAsync(model, _selectionImage, _shot.Image, null, token);
            if (!token.IsCancellationRequested) _sheet.ShowAnswer(_insight.Answer, _insight.SearchQuery);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) _sheet.ShowMessage(ex.Message); }
    }

    private async Task FollowUpAsync(string question)
    {
        var model = ModelOrExplain();
        if (model is null || _selectionImage is null) return;
        var token = NewWork();
        _sheet.ShowLoading("חושב…");
        try
        {
            var answer = await ScreenVision.FollowUpAsync(model, _selectionImage, question, token);
            if (!token.IsCancellationRequested) _sheet.ShowAnswer(answer, _insight?.SearchQuery ?? "");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) _sheet.ShowMessage(ex.Message); }
    }

    private void SearchGoogle()
    {
        var query = _insight?.SearchQuery;
        if (string.IsNullOrWhiteSpace(query)) query = _insight?.Text;
        if (string.IsNullOrWhiteSpace(query)) { OpenLens(); return; }
        AppLauncher.Search(query);
        Close();
    }

    /// <summary>Google Lens can't be fed an image by URL, so the crop goes to the clipboard and Lens opens ready for Ctrl+V.</summary>
    private void OpenLens()
    {
        if (_selectionImage is null) return;
        try { Clipboard.SetImage(_selectionImage); } catch (Exception ex) { AppLog.Warn("Clipboard image failed: " + ex.Message); }
        AppLauncher.OpenUrl("https://lens.google.com/");
        Close();
        _ = Task.Run(() => System.Windows.Application.Current.Dispatcher.Invoke(() =>
            UI.Toast.Show("Google Lens", "התמונה הועתקה — בדף של Lens לחץ Ctrl+V כדי להדביק אותה.")));
    }

    private async Task CopyTextAsync()
    {
        if (_selectionImage is null) return;
        _sheet.ShowLoading("מחלץ טקסט…");
        var text = _insight?.Text;
        if (string.IsNullOrWhiteSpace(text)) text = await ScreenVision.OcrAsync(_selectionImage);
        if (string.IsNullOrWhiteSpace(text))
        {
            var model = ModelOrExplain();
            if (model is null) return;
            try
            {
                var token = NewWork();
                var reply = await model.CompleteAsync("", [ChatMessage.User("Transcribe all text in this image exactly, preserving line breaks. Output only the text.", new ImagePart(ScreenCapture.EncodeForModel(_selectionImage, 1600)))], [], new ModelOptions(), token);
                text = reply.Text;
            }
            catch (Exception ex) { _sheet.ShowMessage(ex.Message); return; }
        }
        if (string.IsNullOrWhiteSpace(text)) { _sheet.ShowMessage("לא נמצא טקסט בבחירה."); return; }
        try { Clipboard.SetText(text); } catch { }
        _sheet.ShowAnswer("✓ הועתק ללוח:\n\n" + text, _insight?.SearchQuery ?? "");
    }

    private void AskAssistant(string question)
    {
        if (_selectionImage is null) return;
        var image = _selectionImage;
        Close();
        _session.AskAssistant(image, question);
    }

    // ---------------- translation ----------------

    public async Task TranslateWholeScreenAsync()
    {
        ClearSelection();
        await TranslateAreaAsync(new Rect(0, 0, Math.Max(1, ActualWidth > 0 ? ActualWidth : Width), Math.Max(1, ActualHeight > 0 ? ActualHeight : Height)), _shot.Image, fromSelection: false);
    }

    private async Task TranslateSelectionAsync()
    {
        if (_selectionImage is null) return;
        await TranslateAreaAsync(_selection, _selectionImage, fromSelection: true);
    }

    private async Task TranslateAreaAsync(Rect area, BitmapSource image, bool fromSelection)
    {
        var model = _session.GetModel();
        if (model is null)
        {
            _hintText.Text = "כדי לתרגם צריך לחבר ספק AI בהגדרות";
            _hint.Visibility = Visibility.Visible;
            if (fromSelection) ModelOrExplain();
            return;
        }
        var token = NewWork();
        _hintText.Text = "🌐  מתרגם את המסך…";
        _hint.Visibility = Visibility.Visible;
        if (fromSelection) _sheet.ShowLoading("מתרגם…");
        try
        {
            var regions = await ScreenVision.TranslateAsync(model, image, _session.TranslationTarget, token);
            if (token.IsCancellationRequested) return;
            _regions = regions;
            _regionsArea = area;
            _showOriginal = false;
            RenderTranslation();
            _hintText.Text = regions.Count == 0 ? "לא נמצא טקסט לתרגום" : $"תורגמו {regions.Count} קטעים · לחץ O להצגת המקור · Esc ליציאה";
            if (fromSelection) _sheet.ShowAnswer(string.Join("\n", regions.Select(r => r.Translated)), _insight?.SearchQuery ?? "");
            if (!fromSelection) { _dim.Visibility = Visibility.Collapsed; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _hintText.Text = "התרגום נכשל: " + ex.Message;
            if (fromSelection) _sheet.ShowMessage(ex.Message);
        }
    }

    private void RenderTranslation()
    {
        _translationLayer.Children.Clear();
        if (_showOriginal) return;
        foreach (var region in _regions)
        {
            var x = _regionsArea.X + region.Left * _regionsArea.Width;
            var y = _regionsArea.Y + region.Top * _regionsArea.Height;
            var w = Math.Max(30, (region.Right - region.Left) * _regionsArea.Width);
            var h = Math.Max(14, (region.Bottom - region.Top) * _regionsArea.Height);
            var (background, foreground) = SampleColors(new Rect(x, y, w, h));
            var text = new TextBlock
            {
                Text = region.Translated, TextWrapping = TextWrapping.Wrap, Foreground = foreground, FontWeight = FontWeights.Medium,
                FlowDirection = region.Translated.Any(c => c >= '֐' && c <= 'ۿ') ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = region.Original
            };
            var label = new Border { Width = w, Height = h, Background = background, CornerRadius = new CornerRadius(3), Child = new Viewbox { Child = text, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly } };
            text.FontSize = Math.Clamp(h * 0.72, 9, 40);
            text.MaxWidth = Math.Max(w * 1.6, 40);
            Canvas.SetLeft(label, x); Canvas.SetTop(label, y);
            _translationLayer.Children.Add(label);
        }
    }

    private void ClearTranslation()
    {
        _regions = [];
        _translationLayer.Children.Clear();
    }

    /// <summary>Average color of the region, so translated labels blend into the original UI.</summary>
    private (Brush Background, Brush Foreground) SampleColors(Rect dipRect)
    {
        try
        {
            var crop = Crop(dipRect);
            var small = new TransformedBitmap(crop, new ScaleTransform(Math.Min(1, 24.0 / crop.PixelWidth), Math.Min(1, 24.0 / crop.PixelHeight)));
            var converted = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            // Background = the most common brightness group; text is whatever contrasts.
            long r = 0, g = 0, b = 0; var n = 0;
            var lum = new List<double>();
            for (var i = 0; i < pixels.Length; i += 4) lum.Add(0.299 * pixels[i + 2] + 0.587 * pixels[i + 1] + 0.114 * pixels[i]);
            var median = lum.OrderBy(v => v).ElementAt(lum.Count / 2);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var l = 0.299 * pixels[i + 2] + 0.587 * pixels[i + 1] + 0.114 * pixels[i];
                if (Math.Abs(l - median) > 40) continue;
                b += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2]; n++;
            }
            if (n == 0) throw new InvalidOperationException();
            var color = Color.FromArgb(245, (byte)(r / n), (byte)(g / n), (byte)(b / n));
            var luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
            return (new SolidColorBrush(color), luminance > 140 ? new SolidColorBrush(Color.FromRgb(20, 20, 24)) : Brushes.White);
        }
        catch { return (new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), Brushes.Black); }
    }

    // ---------------- chrome ----------------

    private Border BuildGlowBorder()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = { new GradientStop(Color.FromRgb(66, 133, 244), 0), new GradientStop(Color.FromRgb(234, 67, 53), 0.33), new GradientStop(Color.FromRgb(251, 188, 5), 0.66), new GradientStop(Color.FromRgb(52, 168, 83), 1) }
        };
        var rotate = new RotateTransform(0, 0.5, 0.5);
        brush.RelativeTransform = rotate;
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });
        return new Border
        {
            BorderBrush = brush, BorderThickness = new Thickness(5), IsHitTestVisible = false,
            Effect = new BlurEffect { Radius = 8 }
        };
    }

    private Border BuildTopBar()
    {
        _hintText.Text = "הקף, סמן או הקש על משהו כדי לחפש";
        var translate = Theme.PillButton("🌐  תרגם מסך");
        translate.Click += (_, _) => _ = TranslateWholeScreenAsync();
        var close = Theme.PillButton("✕");
        close.Click += (_, _) => Close();
        close.ToolTip = "סגור (Esc)";
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "🔍", FontSize = 16, Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        row.Children.Add(_hintText);
        row.Children.Add(new Border { Width = 18 });
        row.Children.Add(translate);
        row.Children.Add(close);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 32, 33, 36)), CornerRadius = new CornerRadius(26), Padding = new Thickness(18, 8, 10, 8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 28, 0, 0),
            Child = row, Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.45 }, Cursor = Cursors.Arrow
        };
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (!_selection.IsEmpty) ClearSelection();
            else if (_regions.Count > 0) { ClearTranslation(); _hintText.Text = "הקף, סמן או הקש על משהו כדי לחפש"; _dim.Visibility = Visibility.Visible; }
            else Close();
        }
        else if (e.Key == Key.O && _regions.Count > 0 && Keyboard.FocusedElement is not TextBox) { _showOriginal = !_showOriginal; RenderTranslation(); e.Handled = true; }
        else if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBox) { _ = TranslateWholeScreenAsync(); e.Handled = true; }
    }

    // ---------------- used by the smoke test to render the UI states ----------------

    internal void DemoSelection()
    {
        var w = ActualWidth; var h = ActualHeight;
        Select(new Rect(w * 0.36, h * 0.22, w * 0.28, h * 0.22));
        _work?.Cancel();
        _sheet.ShowAnswer("זהו לוגו של העוזר האישי. זה מצב הדגמה של \"הקף וחפש\": כאן תופיע תשובת ה-AI על מה שסימנת, עם כפתורים לחיפוש בגוגל, Lens, תרגום והעתקת טקסט.", "Desktop assistant logo");
    }

    internal void DemoTranslation()
    {
        ClearSelection();
        _regions =
        [
            new TranslatedRegion("File", "קובץ", 0.02, 0.02, 0.08, 0.05),
            new TranslatedRegion("Settings", "הגדרות", 0.10, 0.02, 0.18, 0.05),
            new TranslatedRegion("Welcome to the application", "ברוכים הבאים לאפליקציה", 0.30, 0.40, 0.70, 0.46),
        ];
        _regionsArea = new Rect(0, 0, ActualWidth, ActualHeight);
        RenderTranslation();
        _hintText.Text = "תורגמו 3 קטעים · לחץ O להצגת המקור · Esc ליציאה";
        _hint.Visibility = Visibility.Visible;
    }

    protected override void OnClosed(EventArgs e)
    {
        _work?.Cancel();
        base.OnClosed(e);
    }
}
