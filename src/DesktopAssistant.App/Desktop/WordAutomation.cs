using System.Runtime.InteropServices;
using DesktopAssistant.Core;

namespace DesktopAssistant.Desktop;

/// <summary>Drives Microsoft Word through its COM object model (late bound, so no Office SDK is needed).</summary>
public static class WordAutomation
{
    public static Task<string> WriteAsync(string text, bool newDocument, string? title, string? savePath, CancellationToken cancellationToken) =>
        StaRunner.RunAsync(() =>
        {
            dynamic word = GetOrStartWord(out var started);
            word.Visible = true;
            dynamic doc;
            int count = word.Documents.Count;
            if (newDocument || count == 0) doc = word.Documents.Add();
            else doc = word.ActiveDocument;
            doc.Activate();
            dynamic selection = word.Selection;
            if (!newDocument && count > 0) selection.EndKey(6); // wdStory: continue at the end of the document

            var hebrew = text.Any(c => c >= '֐' && c <= '׿');
            if (hebrew) { try { selection.RtlPara(); } catch { } }
            if (!string.IsNullOrWhiteSpace(title))
            {
                try { selection.Style = doc.Styles[-63]; } catch { } // wdStyleHeading1
                selection.TypeText(title);
                selection.TypeParagraph();
                try { selection.Style = doc.Styles[-1]; } catch { } // wdStyleNormal
                if (hebrew) { try { selection.RtlPara(); } catch { } }
            }
            var paragraphs = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < paragraphs.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                selection.TypeText(paragraphs[i]);
                if (i < paragraphs.Length - 1) selection.TypeParagraph();
            }

            var saved = "";
            if (!string.IsNullOrWhiteSpace(savePath))
            {
                var path = AppLauncher.ResolveKnownFolder(savePath);
                if (!Path.HasExtension(path)) path += ".docx";
                if (!Path.IsPathRooted(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                doc.SaveAs2(path);
                saved = $" המסמך נשמר ב: {path}";
            }
            try { word.Activate(); } catch { }
            try { var hwnd = new nint((long)word.ActiveWindow.Hwnd); WindowManager.Focus(hwnd); } catch { }
            return $"כתבתי ב-Word ({text.Length} תווים){(started ? ", לאחר שפתחתי את Word" : "")}.{saved}";
        }, cancellationToken);

    public static Task<string> ReadActiveDocumentAsync(CancellationToken cancellationToken) =>
        StaRunner.RunAsync(() =>
        {
            dynamic word = GetRunningWord() ?? throw new InvalidOperationException("Word לא פתוח.");
            int count = word.Documents.Count;
            if (count == 0) return "אין מסמך פתוח ב-Word.";
            string content = word.ActiveDocument.Content.Text;
            string name = word.ActiveDocument.Name;
            return $"מסמך: {name}\n{(content.Length > 8000 ? content[..8000] + "…" : content)}";
        }, cancellationToken);

    private static object GetOrStartWord(out bool started)
    {
        started = false;
        var running = GetRunningWord();
        if (running is not null) return running;
        var type = Type.GetTypeFromProgID("Word.Application") ?? throw new InvalidOperationException("Microsoft Word לא מותקן במחשב הזה.");
        started = true;
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("לא הצלחתי להפעיל את Word.");
    }

    private static object? GetRunningWord()
    {
        try
        {
            if (CLSIDFromProgID("Word.Application", out var clsid) != 0) return null;
            return GetActiveObject(ref clsid, 0, out var instance) == 0 ? instance : null;
        }
        catch (Exception ex) { AppLog.Warn("GetActiveObject(Word) failed: " + ex.Message); return null; }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)] private static extern int CLSIDFromProgID(string progId, out Guid clsid);
    [DllImport("oleaut32.dll")] private static extern int GetActiveObject(ref Guid clsid, nint reserved, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}
