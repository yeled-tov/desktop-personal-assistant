using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopAssistant.Input;

namespace DesktopAssistant.UI;

/// <summary>One shortcut row: name · current keys · ✏ change · ↺ default.</summary>
public sealed class HotkeyEditor : Grid
{
    private readonly TextBlock _keys = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Border _keysBox;
    private readonly Button _edit;
    private readonly string _default;
    private bool _capturing;

    public string Value { get; private set; }
    public string Label { get; }

    public HotkeyEditor(string label, string description, string value, string defaultValue)
    {
        Label = label; Value = value; _default = defaultValue;
        Margin = new Thickness(0, 4, 0, 4);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap });
        Children.Add(text);

        _keysBox = new Border { Child = _keys, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(238, 240, 244)), Margin = new Thickness(8, 0, 8, 0), Focusable = true, Cursor = Cursors.Hand, ToolTip = "לחצו ✏ כדי לשנות" };
        SetColumn(_keysBox, 1);
        Children.Add(_keysBox);

        _edit = new Button { Content = "✏", FontSize = 14, Width = 34, Height = 32, Padding = new Thickness(0), ToolTip = "שנה קיצור" };
        var reset = new Button { Content = "↺", FontSize = 14, Width = 34, Height = 32, Padding = new Thickness(0), ToolTip = $"ברירת מחדל ({defaultValue})" };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _edit, reset } };
        SetColumn(buttons, 2);
        Children.Add(buttons);

        _edit.Click += (_, _) => { if (_capturing) EndCapture(); else BeginCapture(); };
        _keysBox.MouseLeftButtonUp += (_, _) => BeginCapture();
        reset.Click += (_, _) => { Value = _default; EndCapture(); };
        _keysBox.PreviewKeyDown += OnKey;
        _keysBox.LostKeyboardFocus += (_, _) => { if (_capturing) EndCapture(); };
        Render();
    }

    private void BeginCapture()
    {
        _capturing = true;
        _keys.Text = "הקישו צירוף…  (Esc ביטול · Backspace כיבוי)";
        _keys.FontWeight = FontWeights.Normal;
        _keysBox.Background = new SolidColorBrush(Color.FromRgb(232, 240, 254));
        _keysBox.BorderBrush = new SolidColorBrush(Theme.AccentColor);
        _keysBox.BorderThickness = new Thickness(1.5);
        _edit.Content = "✓";
        _keysBox.Focus();
        Keyboard.Focus(_keysBox);
    }

    private void EndCapture()
    {
        _capturing = false;
        _keysBox.BorderThickness = new Thickness(0);
        _keysBox.Background = new SolidColorBrush(Color.FromRgb(238, 240, 244));
        _edit.Content = "✏";
        Render();
    }

    private void Render()
    {
        _keys.FontWeight = FontWeights.SemiBold;
        _keys.Text = string.IsNullOrWhiteSpace(Value) ? "— כבוי —" : Value.Replace("+", " + ");
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { EndCapture(); return; }
        if (key is Key.Back or Key.Delete) { Value = ""; EndCapture(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var mods = Keyboard.Modifiers;
        var win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        var name = key switch
        {
            Key.Space => "Space", Key.Enter => "Enter", Key.Tab => "Tab",
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "NUM" + (int)(key - Key.NumPad0),
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.OemPlus => "PLUS", Key.OemMinus => "MINUS", Key.OemPeriod => "PERIOD", Key.OemComma => "COMMA",
            _ => key.ToString().ToUpperInvariant()
        };
        // A key without Ctrl/Alt/Win would block normal typing in every app — require a modifier (F-keys excepted).
        if (mods == ModifierKeys.None && !win && !(key >= Key.F1 && key <= Key.F24)) { _keys.Text = "צריך גם Ctrl / Alt / Win  (או מקש F)"; return; }
        var combo = Hotkey.Format(mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt), mods.HasFlag(ModifierKeys.Shift), win, name);
        if (!Hotkey.TryParse(combo, out _)) { _keys.Text = "המקש הזה לא נתמך, נסו אחר"; return; }
        Value = combo;
        EndCapture();
    }
}
