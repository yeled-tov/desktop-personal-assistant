using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopAssistant.Core;
using DesktopAssistant.Offline;

namespace DesktopAssistant.UI;

/// <summary>Settings → Offline: choose, download (with exact sizes and warnings) and remove offline packs.</summary>
public sealed class OfflinePanel : StackPanel
{
    private readonly AppHost _host;
    private readonly StackPanel _speechList = new();
    private readonly StackPanel _brainList = new();
    private readonly Border _progressBox;
    private readonly ProgressBar _progress = new() { Height = 8, Minimum = 0, Maximum = 1000, Margin = new Thickness(0, 6, 0, 4) };
    private readonly TextBlock _progressText = new() { FontSize = 12.5 };
    private CancellationTokenSource? _download;

    public event Action? Changed;

    public OfflinePanel(AppHost host)
    {
        _host = host;
        var ram = OfflineManager.TotalRamGb();
        var disk = OfflineManager.FreeDiskBytes();
        Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Text =
                "במצב אופליין הכל רץ על המחשב שלך: בלי אינטרנט, בלי מפתח API, בלי מכסה, ושום דבר לא יוצא מהמחשב.\n" +
                "החבילות לא כלולות בהתקנה — מורידים רק אם רוצים. שימו לב: הן כבדות (גיגה-בייטים), תופסות זיכרון בזמן עבודה, " +
                "ובמחשב בלי כרטיס מסך חזק הן איטיות יותר מהענן. מילת ההפעלה (\"היי שיר\") עובדת רק עם זיהוי הדיבור האופליין."
        });
        Children.Add(new TextBlock { Text = $"במחשב הזה: זיכרון {ram}GB · מקום פנוי בדיסק {OfflineCatalog.Size(disk)}", Foreground = Brushes.DimGray, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 12) });

        Children.Add(Section("🎙 זיהוי דיבור אופליין", "הכתבה, דיבור עם העוזרת ומילת הפעלה — בלי אינטרנט."));
        Children.Add(_speechList);
        Children.Add(Section("🧠 מוח אופליין (AI מקומי)", "עונה, מפעיל את המחשב, רואה את המסך ומתרגם — בלי אינטרנט."));
        Children.Add(_brainList);

        var cancel = new Button { Content = "בטל הורדה", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
        cancel.Click += (_, _) => _download?.Cancel();
        _progressBox = new Border
        {
            Visibility = Visibility.Collapsed, Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(232, 240, 254)), Child = new StackPanel { Children = { _progressText, _progress, cancel } }
        };
        Children.Add(_progressBox);
        Render();
    }

    private static TextBlock Section(string title, string subtitle) => new()
    {
        Margin = new Thickness(0, 14, 0, 4), TextWrapping = TextWrapping.Wrap,
        Inlines = { new System.Windows.Documents.Run(title) { FontWeight = FontWeights.SemiBold, FontSize = 15 }, new System.Windows.Documents.LineBreak(), new System.Windows.Documents.Run(subtitle) { Foreground = Brushes.DimGray, FontSize = 12.5 } }
    };

    private void Render()
    {
        _speechList.Children.Clear();
        foreach (var pack in OfflineCatalog.Speech) _speechList.Children.Add(PackRow(pack, _host.Controller.Settings.OfflineSpeechModel));
        _brainList.Children.Clear();
        foreach (var pack in OfflineCatalog.Brains) _brainList.Children.Add(PackRow(pack, _host.Controller.Settings.OfflineBrainModel));
    }

    private UIElement PackRow(OfflinePack pack, string activeId)
    {
        var installed = OfflineManager.IsInstalled(pack.Id);
        var active = installed && activeId == pack.Id;
        var ram = OfflineManager.TotalRamGb();
        var tooBig = ram > 0 && ram < pack.RamGb + 2;
        var remaining = OfflineManager.RemainingBytes(pack);

        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = pack.Title + (active ? "   ✓ פעיל" : installed ? "   ✓ מותקן" : ""), FontWeight = FontWeights.SemiBold, Foreground = active ? new SolidColorBrush(Color.FromRgb(30, 142, 62)) : Brushes.Black });
        info.Children.Add(new TextBlock { Text = pack.Description, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Brushes.DimGray });
        info.Children.Add(new TextBlock
        {
            Text = $"הורדה: {OfflineCatalog.Size(pack.TotalBytes)} · זיכרון בזמן עבודה: כ-{pack.RamGb}GB" + (tooBig ? $"  ⚠ במחשב יש רק {ram}GB — עלול להיות איטי מאוד" : ""),
            FontSize = 12, Foreground = tooBig ? new SolidColorBrush(Color.FromRgb(197, 34, 31)) : Brushes.DimGray
        });

        var action = new Button { MinWidth = 96, Padding = new Thickness(10, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
        if (!installed)
        {
            action.Content = remaining < pack.TotalBytes ? $"⬇ השלם ({OfflineCatalog.Size(remaining)})" : $"⬇ הורד";
            action.Style = (Style)Application.Current.Resources["AccentButton"];
            action.Click += async (_, _) => await DownloadAsync(pack);
        }
        else if (!active)
        {
            action.Content = "השתמש בזה";
            action.Click += (_, _) => { Activate(pack); };
        }
        else
        {
            action.Content = "🗑 הסר";
            action.Click += (_, _) =>
            {
                if (MessageBox.Show(Window.GetWindow(this), $"להסיר את \"{pack.Title}\" ולפנות {OfflineCatalog.Size(pack.TotalBytes)}?", "אופליין", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                _host.Offline.Remove(pack);
                Changed?.Invoke();
                Render();
            };
        }
        action.IsEnabled = _download is null;

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(action, 1);
        grid.Children.Add(info);
        grid.Children.Add(action);
        return new Border { Child = grid, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(8), Background = active ? new SolidColorBrush(Color.FromRgb(237, 247, 240)) : new SolidColorBrush(Color.FromRgb(246, 247, 249)), Margin = new Thickness(0, 3, 0, 3) };
    }

    private void Activate(OfflinePack pack)
    {
        var s = _host.Controller.Settings;
        if (pack.Kind == OfflineKind.Speech) s.OfflineSpeechModel = pack.Id; else s.OfflineBrainModel = pack.Id;
        Settings.SettingsStore.Save(s);
        Changed?.Invoke();
        Render();
    }

    private async Task DownloadAsync(OfflinePack pack)
    {
        var remaining = OfflineManager.RemainingBytes(pack);
        var ram = OfflineManager.TotalRamGb();
        var warning = $"להוריד את \"{pack.Title}\"?\n\n" +
                      $"• גודל ההורדה: {OfflineCatalog.Size(remaining)} (יישמר במחשב: {OfflineManager.Root})\n" +
                      $"• זיכרון בזמן עבודה: כ-{pack.RamGb}GB (במחשב יש {ram}GB)\n" +
                      "• ההורדה ממשיכה מאיפה שעצרה אם האינטרנט נקטע.\n" +
                      "• מקורות: whisper.cpp / llama.cpp (קוד פתוח, GitHub) והמודלים מ-Hugging Face.";
        if (ram > 0 && ram < pack.RamGb + 2) warning += $"\n\n⚠ למחשב הזה אין הרבה זיכרון — זה עלול לעבוד לאט מאוד. כדאי לבחור את האפשרות הקלה.";
        if (MessageBox.Show(Window.GetWindow(this), warning, "הורדה למצב אופליין", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;

        _download = new CancellationTokenSource();
        _progressBox.Visibility = Visibility.Visible;
        _progress.Value = 0;
        Render();
        var progress = new Progress<DownloadProgress>(p =>
        {
            _progress.Value = p.Total > 0 ? 1000.0 * p.Done / p.Total : 0;
            var eta = p.BytesPerSecond > 0 ? TimeSpan.FromSeconds((p.Total - p.Done) / p.BytesPerSecond) : TimeSpan.Zero;
            _progressText.Text = $"מוריד {p.File}…  {OfflineCatalog.Size(p.Done)} / {OfflineCatalog.Size(p.Total)}  ·  {p.BytesPerSecond / 1_000_000:0.0}MB/s" + (eta > TimeSpan.Zero ? $"  ·  עוד כ-{(eta.TotalMinutes >= 1 ? $"{eta.TotalMinutes:0} דק'" : $"{eta.Seconds} שנ'")}" : "");
        });
        try
        {
            _progressText.Text = "מתחיל…";
            await _host.Offline.InstallAsync(pack, progress, _download.Token);
            _progressText.Text = $"✓ \"{pack.Title}\" הותקן ופעיל.";
            Changed?.Invoke();
        }
        catch (OperationCanceledException) { _progressText.Text = "ההורדה נעצרה. אפשר להמשיך מאותה נקודה מאוחר יותר."; }
        catch (Exception ex)
        {
            AppLog.Error("Offline download failed", ex);
            _progressText.Text = "⚠ " + ex.Message + "  (אפשר לנסות שוב — ההורדה תמשיך מאיפה שעצרה)";
        }
        finally
        {
            _download.Dispose();
            _download = null;
            Render();
        }
    }
}
