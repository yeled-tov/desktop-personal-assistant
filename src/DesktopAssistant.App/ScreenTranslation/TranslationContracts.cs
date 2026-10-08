namespace DesktopAssistant.ScreenTranslation;

public sealed record OcrTextBlock(string Text, System.Windows.Rect Bounds, string? Language = null, double? Confidence = null);
public sealed record TranslationOverlayItem(string Original, string Translation, System.Windows.Rect Bounds);
public sealed record NormalizedScreenRegion(string OriginalText, string TranslatedText, double Left, double Top, double Right, double Bottom, double Confidence = 1);
public sealed record ScreenTranslationResult(string SourceLanguage, string TargetLanguage, IReadOnlyList<NormalizedScreenRegion> Regions);

public interface IScreenCaptureProvider { Task<ScreenFrame> CaptureAsync(CancellationToken cancellationToken = default); }
public interface IOcrProvider { Task<IReadOnlyList<OcrTextBlock>> RecognizeAsync(ScreenFrame frame, CancellationToken cancellationToken = default); }
public interface ITranslationProvider { Task<IReadOnlyList<TranslationOverlayItem>> TranslateAsync(IReadOnlyList<OcrTextBlock> blocks, string targetLanguage, CancellationToken cancellationToken = default); }
public interface IGeminiVisionProvider
{
    Task<ScreenTranslationResult> TranslateScreenshotAsync(ScreenFrame frame, string targetLanguage, CancellationToken cancellationToken = default);
    Task<string> AnalyzeScreenshotAsync(ScreenFrame frame, string question, CancellationToken cancellationToken = default);
}
public interface ITranslationOverlayRenderer { System.Windows.Window Create(ScreenFrame frame, IReadOnlyList<TranslationOverlayItem> items); }

public sealed record ScreenFrame(System.Windows.Media.Imaging.BitmapSource Image, System.Windows.Rect VirtualBounds);

public static class TranslationResultValidator
{
    public static IReadOnlyList<NormalizedScreenRegion> ValidateRegions(IEnumerable<NormalizedScreenRegion> regions) => regions
        .Where(region => !string.IsNullOrWhiteSpace(region.OriginalText) && !string.IsNullOrWhiteSpace(region.TranslatedText) && region.OriginalText.Length <= 2000 && region.TranslatedText.Length <= 3000)
        .Where(region => double.IsFinite(region.Left) && double.IsFinite(region.Top) && double.IsFinite(region.Right) && double.IsFinite(region.Bottom))
        .Where(region => region.Left >= 0 && region.Top >= 0 && region.Right <= 1 && region.Bottom <= 1 && region.Right > region.Left && region.Bottom > region.Top)
        .Take(250).ToArray();
}

public sealed class UnsupportedOcrProvider : IOcrProvider
{
    public Task<IReadOnlyList<OcrTextBlock>> RecognizeAsync(ScreenFrame frame, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("לא נמצא מנוע OCR זמין. ספק OCR יתחבר בהמשך דרך הממשק הנתמך.");
}
