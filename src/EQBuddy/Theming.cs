using System.Windows;
using System.Windows.Controls;

namespace EQBuddy;

/// <summary>
/// Shared bits for code-built windows. Default WPF buttons render pale-gray-on-
/// pale-gray against the dark themes (David's contrast pass, 2026-08-10) — every
/// button in a code-built window goes through here so it pulls from the same
/// palette as the widget, and repaints on a live theme switch.
/// </summary>
internal static class Theming
{
    public static Button Button(string label, bool isDefault = false, bool isCancel = false)
    {
        var b = new Button
        {
            Content = label,
            Padding = new Thickness(12, 2, 12, 2),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        b.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        b.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        b.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
        return b;
    }

    /// <summary>The ⧉ command-copy wiring (David, 2026-08-14: every "run this in
    /// game" surface offers its command as one click): the click puts EXACTLY the
    /// command on the clipboard and the label flips to confirm — clipboard only,
    /// never focus, never the game itself. Wired here so the label, the ✓ flip,
    /// and the clipboard call behave identically on every surface; the caller
    /// styles the button to fit its own layout.</summary>
    public static Button WireCopyCommand(Button b, string command,
        string? label = null, string? copied = null)
    {
        b.Content = label ?? $"⧉ copy  {command}";
        b.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(command);
                b.Content = copied ?? "✓ copied — paste in game chat";
            }
            catch { /* clipboard momentarily held by another app */ }
        };
        return b;
    }
}
