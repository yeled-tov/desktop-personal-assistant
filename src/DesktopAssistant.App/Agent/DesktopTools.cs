using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;
using DesktopAssistant.Settings;
using Forms = System.Windows.Forms;

namespace DesktopAssistant.Agent;

/// <summary>The agent's hands: every action it can take on the computer.</summary>
public static class DesktopTools
{
    private static readonly Regex SensitiveWords = new(@"\b(send|submit|buy|purchase|pay|checkout|order|delete|remove|confirm payment|transfer)\b|שלח|שליחה|קנה|קנייה|רכוש|רכישה|תשלום|שלם|הזמן|הזמנה|מחק|מחיקה|העבר כסף|אשר תשלום", RegexOptions.IgnoreCase);

    public static IReadOnlyList<AgentTool> All { get; } = Build();

    private static List<AgentTool> Build() =>
    [
        new()
        {
            Name = "open_app", ActivityText = "פותח אפליקציה…",
            Description = "Open an installed application by name (Hebrew or English names work: Word/וורד, Excel, Chrome, WhatsApp, Calculator, Settings, Spotify, Phone Link…), a website, or a URI. Waits briefly for its window.",
            Schema = S.Obj(("name", S.Str("App name, e.g. 'Word', 'וואטסאפ', 'Chrome'"), true), ("arguments", S.Str("Optional command-line arguments"), false)),
            Run = async (a, ctx, ct) =>
            {
                var name = a.GetString("name");
                var result = await AppLauncher.OpenAsync(name, a.GetOptionalString("arguments"), ct);
                var window = await WindowManager.WaitForAsync(name, TimeSpan.FromSeconds(6), ct);
                if (window is not null) { WindowManager.Focus(window.Handle); return new ToolOutput($"{result} החלון '{window.Title}' פתוח וממוקד."); }
                return new ToolOutput(result + " (החלון עדיין נטען — אפשר לבדוק עם list_windows)");
            }
        },
        new()
        {
            Name = "list_installed_apps", ActivityText = "בודק אילו אפליקציות מותקנות…",
            Description = "List applications installed on this PC (as in the Start menu). Use a filter to search.",
            Schema = S.Obj(("filter", S.Str("Optional text to filter by"), false)),
            Run = async (a, ctx, ct) =>
            {
                var filter = a.GetString("filter");
                var apps = await AppLauncher.GetInstalledAppsAsync(ct);
                var list = apps.Where(x => filter.Length == 0 || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).Select(x => x.Name).Take(200).ToList();
                return new ToolOutput(list.Count == 0 ? "לא נמצאו אפליקציות מתאימות." : string.Join("\n", list));
            }
        },
        new()
        {
            Name = "open_url", ActivityText = "פותח אתר…",
            Description = "Open a web page in the default browser.",
            Schema = S.Obj(("url", S.Str("Full URL"), true)),
            Run = (a, ctx, ct) => { var url = a.GetString("url"); AppLauncher.OpenUrl(url.Contains("://") ? url : "https://" + url); return Task.FromResult(new ToolOutput("האתר נפתח.")); }
        },
        new()
        {
            Name = "web_search", ActivityText = "מחפש…",
            Description = "Open a search in the browser (Google by default; also YouTube, Google Maps, Google Images, Wikipedia, Google Translate).",
            Schema = S.Obj(("query", S.Str("What to search"), true), ("engine", S.Enum("Where to search", "google", "youtube", "maps", "images", "wikipedia", "translate", "bing"), false)),
            Run = (a, ctx, ct) => Task.FromResult(new ToolOutput(AppLauncher.Search(a.GetString("query"), a.GetString("engine", "google"))))
        },
        new()
        {
            Name = "open_path", ActivityText = "פותח קובץ/תיקייה…",
            Description = "Open a file or folder. Accepts absolute paths or known folders like 'Downloads', 'Desktop', 'Documents', 'הורדות', 'Documents\\report.docx'.",
            Schema = S.Obj(("path", S.Str("Path or known folder"), true)),
            Run = (a, ctx, ct) =>
            {
                var path = AppLauncher.ResolveKnownFolder(a.GetString("path"));
                if (!File.Exists(path) && !Directory.Exists(path)) return Task.FromResult(ToolOutput.Error($"הנתיב לא קיים: {path}"));
                AppLauncher.ShellOpen(path, null);
                return Task.FromResult(new ToolOutput($"נפתח: {path}"));
            }
        },
        new()
        {
            Name = "list_files", ActivityText = "קורא תיקייה…",
            Description = "List files and folders in a directory (known folders like 'Downloads' allowed). Newest first.",
            Schema = S.Obj(("path", S.Str("Folder path"), true), ("pattern", S.Str("Optional pattern like *.pdf"), false)),
            Run = (a, ctx, ct) =>
            {
                var path = AppLauncher.ResolveKnownFolder(a.GetString("path"));
                if (!Directory.Exists(path)) return Task.FromResult(ToolOutput.Error($"התיקייה לא קיימת: {path}"));
                var dir = new DirectoryInfo(path);
                var pattern = a.GetString("pattern", "*");
                var entries = dir.EnumerateFileSystemInfos(string.IsNullOrWhiteSpace(pattern) ? "*" : pattern).OrderByDescending(e => e.LastWriteTime).Take(150)
                    .Select(e => e is DirectoryInfo ? $"[תיקייה] {e.Name}" : $"{e.Name}  ({((FileInfo)e).Length / 1024} KB, {e.LastWriteTime:dd/MM/yyyy HH:mm})");
                return Task.FromResult(new ToolOutput($"{path}\n{string.Join("\n", entries)}"));
            }
        },
        new()
        {
            Name = "read_text_file", ActivityText = "קורא קובץ…",
            Description = "Read a text file (txt, md, csv, json, code…). Up to 20,000 characters.",
            Schema = S.Obj(("path", S.Str("File path"), true)),
            Run = async (a, ctx, ct) =>
            {
                var path = AppLauncher.ResolveKnownFolder(a.GetString("path"));
                if (!File.Exists(path)) return ToolOutput.Error($"הקובץ לא קיים: {path}");
                var text = await File.ReadAllTextAsync(path, ct);
                return new ToolOutput(text.Length > 20000 ? text[..20000] + "\n…(נחתך)" : text);
            }
        },
        new()
        {
            Name = "write_text_file", ActivityText = "שומר קובץ…",
            Description = "Create or overwrite a text file (e.g. save notes to Desktop\\notes.txt). Relative paths go to Documents.",
            Schema = S.Obj(("path", S.Str("File path"), true), ("content", S.Str("Full file content"), true)),
            NeedsConfirmation = (a, ctx) => { var p = AppLauncher.ResolveKnownFolder(a.GetString("path")); return File.Exists(p) ? $"לדרוס את הקובץ הקיים?\n{p}" : null; },
            Run = async (a, ctx, ct) =>
            {
                var path = AppLauncher.ResolveKnownFolder(a.GetString("path"));
                if (!Path.IsPathRooted(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, a.GetString("content"), new UTF8Encoding(true), ct);
                return new ToolOutput($"נשמר: {path}");
            }
        },
        new()
        {
            Name = "list_windows", ActivityText = "בודק חלונות פתוחים…",
            Description = "List open windows (title and app). Use to find the window to work in.",
            Schema = S.Obj(),
            Run = (a, ctx, ct) =>
            {
                var windows = WindowManager.List();
                var foreground = Core.Native.GetForegroundWindow();
                var lines = windows.Select(w => $"- \"{w.Title}\" ({w.ProcessName}){(w.Minimized ? " [ממוזער]" : "")}{(w.Handle == foreground ? " [פעיל]" : "")}");
                return Task.FromResult(new ToolOutput(windows.Count == 0 ? "אין חלונות פתוחים." : string.Join("\n", lines)));
            }
        },
        new()
        {
            Name = "window_action", ActivityText = "מטפל בחלון…", TouchesDesktop = true,
            Description = "Focus, minimize, maximize, restore or close a window found by part of its title or its app name (e.g. 'Word', 'Chrome').",
            Schema = S.Obj(("window", S.Str("Part of the window title or the app name"), true), ("action", S.Enum("What to do", "focus", "minimize", "maximize", "restore", "close"), true)),
            Run = (a, ctx, ct) =>
            {
                var w = WindowManager.Find(a.GetString("window"));
                if (w is null) return Task.FromResult(ToolOutput.Error("לא מצאתי חלון כזה. השתמש ב-list_windows."));
                switch (a.GetString("action"))
                {
                    case "minimize": WindowManager.Minimize(w.Handle); break;
                    case "maximize": WindowManager.Maximize(w.Handle); WindowManager.Focus(w.Handle); break;
                    case "restore": WindowManager.Restore(w.Handle); WindowManager.Focus(w.Handle); break;
                    case "close": WindowManager.Close(w.Handle); break;
                    default: WindowManager.Focus(w.Handle); break;
                }
                return Task.FromResult(new ToolOutput($"בוצע '{a.GetString("action")}' על \"{w.Title}\"."));
            }
        },
        new()
        {
            Name = "type_text", ActivityText = "מקליד…", TouchesDesktop = true,
            Description = "Type text (any language, including Hebrew) into the focused window, or into a given window after focusing it. Newlines become Enter. For long text use method='paste'.",
            Schema = S.Obj(("text", S.Str("Exact text to type"), true), ("window", S.Str("Optional window title/app to focus first"), false), ("method", S.Enum("keyboard (default) or paste via clipboard", "keyboard", "paste"), false)),
            Run = async (a, ctx, ct) =>
            {
                var target = a.GetOptionalString("window");
                if (target is not null)
                {
                    var w = WindowManager.Find(target);
                    if (w is null) return ToolOutput.Error($"לא מצאתי חלון '{target}'.");
                    WindowManager.Focus(w.Handle);
                    await Task.Delay(150, ct);
                }
                InputSimulator.ReleaseModifiers();
                var text = a.GetString("text");
                if (a.GetString("method") == "paste" || text.Length > 600)
                {
                    await ctx.RunOnUiThread(() => { System.Windows.Clipboard.SetText(text); return null; });
                    await Task.Delay(80, ct);
                    InputSimulator.PressCombo("CTRL+V");
                }
                else await Task.Run(() => InputSimulator.TypeText(text, ct), ct);
                var fg = Core.Native.GetWindowTitle(Core.Native.GetForegroundWindow());
                return new ToolOutput($"הוקלדו {text.Length} תווים בחלון \"{fg}\".");
            }
        },
        new()
        {
            Name = "press_keys", ActivityText = "לוחץ על מקשים…", TouchesDesktop = true,
            Description = "Press a key or shortcut in the focused window, e.g. ENTER, ESC, TAB, CTRL+S, CTRL+N, ALT+F4, WIN+D, CTRL+SHIFT+T, F5, DOWN, VOLUME_UP, MEDIA_PLAY_PAUSE.",
            Schema = S.Obj(("keys", S.Str("Key combination with '+'"), true), ("repeat", S.Int("How many times (default 1)"), false), ("window", S.Str("Optional window to focus first"), false)),
            Run = async (a, ctx, ct) =>
            {
                var target = a.GetOptionalString("window");
                if (target is not null && WindowManager.Find(target) is { } w) { WindowManager.Focus(w.Handle); await Task.Delay(120, ct); }
                InputSimulator.ReleaseModifiers();
                InputSimulator.PressCombo(a.GetString("keys"), Math.Max(1, a.GetInt("repeat", 1)));
                return new ToolOutput($"נלחץ: {a.GetString("keys")}");
            }
        },
        new()
        {
            Name = "list_ui_elements", ActivityText = "סורק את החלון…", TouchesDesktop = true,
            Description = "List clickable/editable controls in a window (buttons, fields, links, menu items) with numbers. Then use click_element / set_element_text with that number. Best way to operate normal Windows apps.",
            Schema = S.Obj(("window", S.Str("Window title/app; empty = the active window"), false)),
            Run = async (a, ctx, ct) =>
            {
                var w = WindowManager.Find(a.GetOptionalString("window"));
                if (w is null) return ToolOutput.Error("לא נמצא חלון.");
                WindowManager.Focus(w.Handle);
                var text = await Task.Run(() => ctx.Ui.ListElements(w.Handle), ct);
                return new ToolOutput($"חלון: \"{w.Title}\"\n{text}");
            }
        },
        new()
        {
            Name = "click_element", ActivityText = "לוחץ…", TouchesDesktop = true,
            Description = "Click/activate a control by its number from the latest list_ui_elements.",
            Schema = S.Obj(("index", S.Int("Element number"), true), ("element_name", S.Str("The element's name as listed (used for safety checks)"), false)),
            NeedsConfirmation = (a, ctx) => SensitiveWords.IsMatch(a.GetString("element_name")) ? $"ללחוץ על \"{a.GetString("element_name")}\"?" : null,
            Run = (a, ctx, ct) => Task.Run(() => new ToolOutput(ctx.Ui.Click(a.GetInt("index"))), ct)
        },
        new()
        {
            Name = "set_element_text", ActivityText = "ממלא שדה…", TouchesDesktop = true,
            Description = "Put text into an edit field by its number from list_ui_elements (replaces the content unless append=true).",
            Schema = S.Obj(("index", S.Int("Element number"), true), ("text", S.Str("Text"), true), ("append", S.Bool("Append instead of replace"), false)),
            Run = (a, ctx, ct) => Task.Run(() => new ToolOutput(ctx.Ui.SetText(a.GetInt("index"), a.GetString("text"), a.GetBool("append"))), ct)
        },
        new()
        {
            Name = "take_screenshot", ActivityText = "מצלם את המסך…", TouchesDesktop = true,
            Description = "See the screen. Returns an image of the monitor with the active (or given) window. Coordinates used by click_at/scroll/drag refer to pixels of the latest screenshot image.",
            Schema = S.Obj(("window", S.Str("Optional window to focus and capture"), false)),
            Run = async (a, ctx, ct) =>
            {
                nint handle = 0;
                if (a.GetOptionalString("window") is { } name && WindowManager.Find(name) is { } w) { WindowManager.Focus(w.Handle); handle = w.Handle; await Task.Delay(200, ct); }
                else handle = Core.Native.GetForegroundWindow();
                await ctx.SetOverlayVisible(false);
                try
                {
                    await Task.Delay(120, ct);
                    var shot = ScreenCapture.CaptureForAgent(handle);
                    ctx.LastScreenshot = shot;
                    return new ToolOutput($"צילום מסך {shot.Width}x{shot.Height}. חלון פעיל: \"{Core.Native.GetWindowTitle(Core.Native.GetForegroundWindow())}\".", new ImagePart(shot.Jpeg));
                }
                finally { await ctx.SetOverlayVisible(true); }
            }
        },
        new()
        {
            Name = "click_at", ActivityText = "לוחץ במסך…", TouchesDesktop = true,
            Description = "Mouse click at a point of the latest take_screenshot image (x,y in that image's pixels). Use when there is no accessible element.",
            Schema = S.Obj(("x", S.Num("X in screenshot pixels"), true), ("y", S.Num("Y in screenshot pixels"), true), ("button", S.Enum("Mouse button", "left", "right", "middle"), false), ("clicks", S.Int("1 = click, 2 = double click"), false), ("target_description", S.Str("What you are clicking (for the user)"), false)),
            NeedsConfirmation = (a, ctx) => SensitiveWords.IsMatch(a.GetString("target_description")) ? $"ללחוץ על \"{a.GetString("target_description")}\"?" : null,
            Run = (a, ctx, ct) =>
            {
                if (ctx.LastScreenshot is null) return Task.FromResult(ToolOutput.Error("צריך קודם take_screenshot."));
                var (x, y) = ctx.LastScreenshot.ToScreen(a.GetNumber("x"), a.GetNumber("y"));
                InputSimulator.Click(x, y, a.GetString("button", "left"), Math.Max(1, a.GetInt("clicks", 1)));
                return Task.FromResult(new ToolOutput($"לחצתי ב-({x},{y})."));
            }
        },
        new()
        {
            Name = "scroll", ActivityText = "גולל…", TouchesDesktop = true,
            Description = "Scroll with the mouse wheel. Optional x,y (screenshot pixels) to scroll a specific area; default = center of the active window.",
            Schema = S.Obj(("direction", S.Enum("Direction", "up", "down", "left", "right"), true), ("amount", S.Int("Wheel notches (default 5)"), false), ("x", S.Num("X in screenshot pixels"), false), ("y", S.Num("Y in screenshot pixels"), false)),
            Run = (a, ctx, ct) =>
            {
                int x, y;
                if (a["x"] is not null && a["y"] is not null && ctx.LastScreenshot is not null) (x, y) = ctx.LastScreenshot.ToScreen(a.GetNumber("x"), a.GetNumber("y"));
                else { Core.Native.GetWindowRect(Core.Native.GetForegroundWindow(), out var r); x = r.Left + r.Width / 2; y = r.Top + r.Height / 2; }
                var amount = Math.Clamp(a.GetInt("amount", 5), 1, 30);
                var direction = a.GetString("direction", "down");
                InputSimulator.Scroll(x, y, direction is "down" or "right" ? -amount : amount, horizontal: direction is "left" or "right");
                return Task.FromResult(new ToolOutput($"גללתי {direction}."));
            }
        },
        new()
        {
            Name = "drag", ActivityText = "גורר…", TouchesDesktop = true,
            Description = "Drag with the left mouse button between two points of the latest screenshot.",
            Schema = S.Obj(("from_x", S.Num("Start X"), true), ("from_y", S.Num("Start Y"), true), ("to_x", S.Num("End X"), true), ("to_y", S.Num("End Y"), true)),
            Run = (a, ctx, ct) =>
            {
                if (ctx.LastScreenshot is null) return Task.FromResult(ToolOutput.Error("צריך קודם take_screenshot."));
                var (fx, fy) = ctx.LastScreenshot.ToScreen(a.GetNumber("from_x"), a.GetNumber("from_y"));
                var (tx, ty) = ctx.LastScreenshot.ToScreen(a.GetNumber("to_x"), a.GetNumber("to_y"));
                InputSimulator.Drag(fx, fy, tx, ty);
                return Task.FromResult(new ToolOutput("בוצעה גרירה."));
            }
        },
        new()
        {
            Name = "read_window_text", ActivityText = "קורא את תוכן החלון…",
            Description = "Read the text content of a window (documents, web pages, chats, dialogs) without a screenshot.",
            Schema = S.Obj(("window", S.Str("Window title/app; empty = active window"), false)),
            Run = async (a, ctx, ct) =>
            {
                var w = WindowManager.Find(a.GetOptionalString("window"));
                if (w is null) return ToolOutput.Error("לא נמצא חלון.");
                var text = await Task.Run(() => UiAutomationHelper.ReadWindowText(w.Handle), ct);
                return new ToolOutput($"חלון: \"{w.Title}\"\n{text}");
            }
        },
        new()
        {
            Name = "word_write", ActivityText = "כותב ב-Word…", TouchesDesktop = true,
            Description = "Write text into Microsoft Word (opens Word if needed). Use for 'open Word and write…', dictation, letters, summaries. Handles Hebrew right-to-left. Can also save the document.",
            Schema = S.Obj(("text", S.Str("Body text; use \\n between paragraphs"), true), ("new_document", S.Bool("Start a new document (default true)"), false), ("title", S.Str("Optional heading at the top"), false), ("save_path", S.Str("Optional: save as, e.g. 'Documents\\letter.docx' or 'Desktop\\summary'"), false)),
            Run = async (a, ctx, ct) =>
            {
                try { return new ToolOutput(await WordAutomation.WriteAsync(a.GetString("text"), a.GetBool("new_document", true), a.GetOptionalString("title"), a.GetOptionalString("save_path"), ct)); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Warn("Word COM failed: " + ex.Message);
                    return ToolOutput.Error($"לא הצלחתי לשלוט ב-Word דרך COM ({ex.Message}). אפשר במקום: open_app 'Word', להמתין, CTRL+N ו-type_text.");
                }
            }
        },
        new()
        {
            Name = "word_read", ActivityText = "קורא את מסמך ה-Word…",
            Description = "Read the text of the document open in Microsoft Word.",
            Schema = S.Obj(),
            Run = async (a, ctx, ct) => new ToolOutput(await WordAutomation.ReadActiveDocumentAsync(ct))
        },
        new()
        {
            Name = "clipboard", ActivityText = "לוח העתקה…",
            Description = "Read the clipboard text (action=get) or put text on the clipboard (action=set).",
            Schema = S.Obj(("action", S.Enum("get or set", "get", "set"), true), ("text", S.Str("Text for set"), false)),
            Run = async (a, ctx, ct) =>
            {
                if (a.GetString("action") == "set")
                {
                    var text = a.GetString("text");
                    await ctx.RunOnUiThread(() => { System.Windows.Clipboard.SetText(text); return null; });
                    return new ToolOutput("הטקסט הועתק ללוח.");
                }
                var value = await ctx.RunOnUiThread(() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null) as string;
                return new ToolOutput(string.IsNullOrEmpty(value) ? "(הלוח ריק או לא מכיל טקסט)" : value.Length > 10000 ? value[..10000] + "…" : value);
            }
        },
        new()
        {
            Name = "make_call", ActivityText = "מחייג…",
            Description = "Start a phone call to a number through the PC's calling app (Phone Link connected to the user's phone, Skype, Teams…).",
            Schema = S.Obj(("phone_number", S.Str("Number, e.g. 0501234567 or +972501234567"), true), ("contact_name", S.Str("Who is being called"), false)),
            NeedsConfirmation = (a, ctx) => $"להתקשר ל-{a.GetString("contact_name", "")} {a.GetString("phone_number")}?".Replace("  ", " "),
            Run = (a, ctx, ct) =>
            {
                var number = Regex.Replace(a.GetString("phone_number"), @"[^\d+*#]", "");
                if (number.Length < 3) return Task.FromResult(ToolOutput.Error("מספר טלפון לא תקין."));
                AppLauncher.ShellOpen($"tel:{number}", null);
                return Task.FromResult(new ToolOutput($"פתחתי חיוג ל-{number}. אם לא נפתחה אפליקציה — צריך לחבר את הטלפון דרך 'קישור לטלפון' (Phone Link)."));
            }
        },
        new()
        {
            Name = "whatsapp_message", ActivityText = "פותח וואטסאפ…",
            Description = "Open a WhatsApp chat with a phone number with the message pre-filled (the user presses send). Uses WhatsApp Desktop, or WhatsApp Web.",
            Schema = S.Obj(("phone_number", S.Str("International format preferred, e.g. 972501234567"), true), ("text", S.Str("Message text"), false)),
            Run = (a, ctx, ct) =>
            {
                var number = Regex.Replace(a.GetString("phone_number"), @"\D", "");
                if (number.StartsWith("0")) number = "972" + number[1..];
                var text = Uri.EscapeDataString(a.GetString("text"));
                try { AppLauncher.ShellOpen($"whatsapp://send?phone={number}&text={text}", null); }
                catch { AppLauncher.OpenUrl($"https://wa.me/{number}?text={text}"); }
                return Task.FromResult(new ToolOutput("הצ'אט נפתח עם ההודעה מוכנה — נשאר רק ללחוץ שליחה."));
            }
        },
        new()
        {
            Name = "compose_email", ActivityText = "מכין מייל…",
            Description = "Open a new email draft in the default mail app with recipient, subject and body filled in.",
            Schema = S.Obj(("to", S.Str("Recipient address"), false), ("subject", S.Str("Subject"), false), ("body", S.Str("Body"), false)),
            Run = (a, ctx, ct) =>
            {
                var uri = $"mailto:{Uri.EscapeDataString(a.GetString("to"))}?subject={Uri.EscapeDataString(a.GetString("subject"))}&body={Uri.EscapeDataString(a.GetString("body"))}";
                AppLauncher.ShellOpen(uri, null);
                return Task.FromResult(new ToolOutput("טיוטת המייל נפתחה."));
            }
        },
        new()
        {
            Name = "media_control", ActivityText = "שולט במדיה…",
            Description = "Control music/video playback and system volume.",
            Schema = S.Obj(("action", S.Enum("Action", "play_pause", "next", "previous", "stop", "volume_up", "volume_down", "mute"), true), ("steps", S.Int("For volume: how many steps (each ≈2%)"), false)),
            Run = (a, ctx, ct) =>
            {
                var key = a.GetString("action") switch { "next" => "MEDIA_NEXT", "previous" => "MEDIA_PREV", "stop" => "MEDIA_STOP", "volume_up" => "VOLUME_UP", "volume_down" => "VOLUME_DOWN", "mute" => "VOLUME_MUTE", _ => "MEDIA_PLAY_PAUSE" };
                InputSimulator.PressCombo(key, key.StartsWith("VOLUME_U") || key.StartsWith("VOLUME_D") ? Math.Clamp(a.GetInt("steps", 5), 1, 50) : 1);
                return Task.FromResult(new ToolOutput("בוצע."));
            }
        },
        new()
        {
            Name = "run_powershell", ActivityText = "מריץ פקודה…",
            Description = "Run a PowerShell script on this PC and get its output (for system info, files, settings that have no UI path). Always shown to the user for approval.",
            Schema = S.Obj(("script", S.Str("PowerShell code"), true), ("purpose", S.Str("One line: what it does, in the user's language"), true)),
            NeedsConfirmation = (a, ctx) => $"{a.GetString("purpose")}\n\nהפקודה:\n{a.GetString("script")}",
            Run = async (a, ctx, ct) =>
            {
                var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -")
                { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                using var process = Process.Start(info)!;
                await process.StandardInput.WriteLineAsync("[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'");
                await process.StandardInput.WriteLineAsync(a.GetString("script"));
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEndAsync(ct);
                var error = process.StandardError.ReadToEndAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { try { process.Kill(true); } catch { } return ToolOutput.Error("הפקודה נעצרה אחרי 60 שניות."); }
                var text = (await output).Trim();
                var err = (await error).Trim();
                var combined = $"{text}{(err.Length > 0 ? "\nשגיאות:\n" + err : "")}".Trim();
                if (combined.Length > 8000) combined = combined[..8000] + "…";
                return new ToolOutput(combined.Length == 0 ? $"הסתיים (קוד {process.ExitCode}) ללא פלט." : combined, null, process.ExitCode != 0 && text.Length == 0);
            }
        },
        new()
        {
            Name = "set_timer", ActivityText = "מגדיר טיימר…",
            Description = "Set a reminder/timer that pops up a notification after some minutes (while the assistant runs).",
            Schema = S.Obj(("minutes", S.Num("Minutes from now"), true), ("message", S.Str("Reminder text"), true)),
            Run = (a, ctx, ct) =>
            {
                var minutes = Math.Clamp(a.GetNumber("minutes", 1), 0.1, 24 * 60);
                var message = a.GetString("message", "תזכורת");
                _ = Task.Delay(TimeSpan.FromMinutes(minutes)).ContinueWith(_ =>
                {
                    ctx.Notify("⏰ תזכורת", message);
                    System.Media.SystemSounds.Exclamation.Play();
                });
                return Task.FromResult(new ToolOutput($"הטיימר הוגדר ל-{minutes:0.#} דקות ({DateTime.Now.AddMinutes(minutes):HH:mm})."));
            }
        },
        new()
        {
            Name = "remember", ActivityText = "שומר בזיכרון…",
            Description = "Save a lasting fact or preference about the user (name, preferences, people's phone numbers) for future conversations. Do not store passwords.",
            Schema = S.Obj(("fact", S.Str("The fact, one short sentence"), true)),
            Run = (a, ctx, ct) =>
            {
                var fact = a.GetString("fact").Trim();
                if (Regex.IsMatch(fact, "password|סיסמ|api key|token|אשראי|credit card", RegexOptions.IgnoreCase)) return Task.FromResult(ToolOutput.Error("לא שומרים סיסמאות או פרטי תשלום."));
                if (fact.Length > 0 && !ctx.Settings.Memory.Contains(fact)) { ctx.Settings.Memory.Add(fact); if (ctx.Settings.Memory.Count > 200) ctx.Settings.Memory.RemoveAt(0); SettingsStore.Save(ctx.Settings); }
                return Task.FromResult(new ToolOutput("נשמר."));
            }
        },
        new()
        {
            Name = "forget", ActivityText = "מוחק מהזיכרון…",
            Description = "Remove saved memory facts containing the given text (or everything with all=true).",
            Schema = S.Obj(("contains", S.Str("Text to match"), false), ("all", S.Bool("Forget everything"), false)),
            Run = (a, ctx, ct) =>
            {
                var removed = a.GetBool("all") ? ctx.Settings.Memory.RemoveAll(_ => true) : ctx.Settings.Memory.RemoveAll(m => m.Contains(a.GetString("contains"), StringComparison.OrdinalIgnoreCase) && a.GetString("contains").Length > 0);
                SettingsStore.Save(ctx.Settings);
                return Task.FromResult(new ToolOutput($"נמחקו {removed} פריטים."));
            }
        },
        new()
        {
            Name = "translate_screen", ActivityText = "פותח תרגום מסך…",
            Description = "Open the live screen translation overlay (translates everything visible on screen in place).",
            Schema = S.Obj(),
            Run = async (a, ctx, ct) => { await ctx.OpenScreenTranslation(); return new ToolOutput("תרגום המסך נפתח."); }
        },
        new()
        {
            Name = "system_info", ActivityText = "בודק מצב המחשב…",
            Description = "Date/time, battery, screens and basic PC info.",
            Schema = S.Obj(),
            Run = (a, ctx, ct) =>
            {
                var power = Forms.SystemInformation.PowerStatus;
                var battery = power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery) ? "אין סוללה (מחשב נייח)" : $"{power.BatteryLifePercent:P0}{(power.PowerLineStatus == Forms.PowerLineStatus.Online ? " (בטעינה)" : "")}";
                var screens = string.Join(", ", Forms.Screen.AllScreens.Select(s => $"{s.Bounds.Width}x{s.Bounds.Height}{(s.Primary ? " ראשי" : "")}"));
                return Task.FromResult(new ToolOutput($"תאריך ושעה: {DateTime.Now:dddd dd/MM/yyyy HH:mm}\nסוללה: {battery}\nמסכים: {screens}\nמשתמש: {Environment.UserName}\nמחשב: {Environment.MachineName}\nWindows: {Environment.OSVersion.Version}"));
            }
        },
        new()
        {
            Name = "wait", ActivityText = "ממתין…",
            Description = "Wait for something to load (seconds, max 15).",
            Schema = S.Obj(("seconds", S.Num("Seconds"), true)),
            Run = async (a, ctx, ct) => { var s = Math.Clamp(a.GetNumber("seconds", 1), 0.2, 15); await Task.Delay(TimeSpan.FromSeconds(s), ct); return new ToolOutput($"המתנתי {s:0.#} שניות."); }
        },
    ];
}
