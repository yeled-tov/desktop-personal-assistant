using System.Windows.Automation;

namespace DesktopAssistant.Desktop;

/// <summary>Reads and operates controls through Windows UI Automation (no screen coordinates needed).</summary>
public sealed class UiAutomationHelper
{
    private static readonly ControlType[] Interactive =
    [
        ControlType.Button, ControlType.Edit, ControlType.Document, ControlType.Hyperlink, ControlType.ListItem, ControlType.TabItem, ControlType.MenuItem,
        ControlType.CheckBox, ControlType.RadioButton, ControlType.ComboBox, ControlType.TreeItem, ControlType.SplitButton, ControlType.DataItem, ControlType.Slider
    ];

    private readonly List<AutomationElement> _lastElements = [];

    public string ListElements(nint window, int max = 120)
    {
        _lastElements.Clear();
        var root = AutomationElement.FromHandle(window);
        var lines = new List<string>();
        var walker = TreeWalker.ControlViewWalker;
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;
        while (queue.Count > 0 && _lastElements.Count < max && visited < 4000)
        {
            var (element, depth) = queue.Dequeue();
            visited++;
            try
            {
                var info = element.Current;
                if (depth > 0 && Interactive.Contains(info.ControlType) && !info.IsOffscreen && info.IsEnabled)
                {
                    var name = Clean(info.Name);
                    var type = info.ControlType.ProgrammaticName.Replace("ControlType.", "");
                    var value = ReadValue(element);
                    if (name.Length > 0 || value.Length > 0 || type is "Edit" or "Document" or "ComboBox")
                    {
                        _lastElements.Add(element);
                        var r = info.BoundingRectangle;
                        var extra = value.Length > 0 ? $" value=\"{Truncate(value, 80)}\"" : "";
                        lines.Add($"[{_lastElements.Count}] {type} \"{Truncate(name, 80)}\"{extra} @({(int)(r.Left + r.Width / 2)},{(int)(r.Top + r.Height / 2)})");
                    }
                }
                if (depth < 25)
                {
                    var child = walker.GetFirstChild(element);
                    while (child is not null) { queue.Enqueue((child, depth + 1)); child = walker.GetNextSibling(child); }
                }
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
        }
        if (lines.Count == 0) return "לא נמצאו רכיבים נגישים בחלון (ייתכן שזו אפליקציה שמציירת את עצמה — השתמש ב-take_screenshot ו-click_at).";
        return string.Join("\n", lines) + "\n(מספרי האלמנטים תקפים עד הקריאה הבאה ל-list_ui_elements. הקואורדינטות הן פיקסלים במסך.)";
    }

    public string Click(int index)
    {
        var element = Get(index);
        var info = element.Current;
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var p)) { ((InvokePattern)p).Invoke(); return $"לחצתי על '{info.Name}'."; }
            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out p)) { ((TogglePattern)p).Toggle(); return $"שיניתי את '{info.Name}'."; }
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out p)) { ((SelectionItemPattern)p).Select(); return $"בחרתי '{info.Name}'."; }
            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out p)) { ((ExpandCollapsePattern)p).Expand(); return $"פתחתי את '{info.Name}'."; }
        }
        catch (Exception) { /* fall back to a real click */ }
        var r = info.BoundingRectangle;
        if (r.IsEmpty) throw new InvalidOperationException("לא ניתן ללחוץ על הרכיב הזה.");
        InputSimulator.Click((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2));
        return $"לחצתי (עכבר) על '{info.Name}'.";
    }

    public string SetText(int index, string text, bool append)
    {
        var element = Get(index);
        try
        {
            if (!append && element.TryGetCurrentPattern(ValuePattern.Pattern, out var p) && !((ValuePattern)p).Current.IsReadOnly)
            {
                ((ValuePattern)p).SetValue(text);
                return "הטקסט הוזן.";
            }
        }
        catch (Exception) { }
        try { element.SetFocus(); } catch { var r = element.Current.BoundingRectangle; InputSimulator.Click((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2)); }
        Thread.Sleep(80);
        if (!append) { InputSimulator.PressCombo("CTRL+A"); InputSimulator.PressCombo("DELETE"); }
        InputSimulator.TypeText(text);
        return "הטקסט הוקלד.";
    }

    public static string ReadWindowText(nint window, int maxChars = 6000)
    {
        var root = AutomationElement.FromHandle(window);
        var builder = new StringBuilder();
        // Documents (Word, Notepad, browsers) expose their full text through TextPattern.
        try
        {
            var doc = root.FindFirst(TreeScope.Descendants, new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
            if (doc is not null && doc.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
            {
                var text = ((TextPattern)tp).DocumentRange.GetText(maxChars);
                if (!string.IsNullOrWhiteSpace(text)) builder.AppendLine(text.Trim());
            }
        }
        catch { }
        if (builder.Length < maxChars / 2)
        {
            var seen = new HashSet<string>();
            var walker = TreeWalker.ControlViewWalker;
            var stack = new Stack<(AutomationElement, int)>();
            stack.Push((root, 0));
            var visited = 0;
            while (stack.Count > 0 && builder.Length < maxChars && visited < 3000)
            {
                var (element, depth) = stack.Pop();
                visited++;
                try
                {
                    var info = element.Current;
                    if (info.IsOffscreen || info.IsPassword) continue;
                    var name = Clean(info.Name);
                    if (name.Length > 0 && seen.Add(name)) builder.AppendLine(name);
                    var value = ReadValue(element);
                    if (value.Length > 0 && seen.Add(value)) builder.AppendLine(value);
                    if (depth < 30)
                    {
                        var children = new List<AutomationElement>();
                        var child = walker.GetFirstChild(element);
                        while (child is not null) { children.Add(child); child = walker.GetNextSibling(child); }
                        for (var i = children.Count - 1; i >= 0; i--) stack.Push((children[i], depth + 1));
                    }
                }
                catch { }
            }
        }
        var result = builder.ToString().Trim();
        return result.Length == 0 ? "(לא נמצא טקסט נגיש בחלון)" : Truncate(result, maxChars);
    }

    private AutomationElement Get(int index)
    {
        if (index < 1 || index > _lastElements.Count) throw new ArgumentException($"אין רכיב מספר {index}. קרא קודם ל-list_ui_elements.");
        return _lastElements[index - 1];
    }

    private static string ReadValue(AutomationElement element)
    {
        try
        {
            if (element.Current.IsPassword) return "";
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var p)) return Clean(((ValuePattern)p).Current.Value);
        }
        catch { }
        return "";
    }

    private static string Clean(string? s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
