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
    /// <summary>When the last copy happened, per command — surfaces that rebuild
    /// their buttons every render (the Raids card) consult this so the ✓ survives
    /// the rebuild instead of blinking out on the next tick.</summary>
    private static readonly Dictionary<string, DateTime> _copiedAt = new();
    private static readonly TimeSpan CopiedLinger = TimeSpan.FromSeconds(2.5);

    public static Button WireCopyCommand(Button b, string command,
        string? label = null, string? copied = null)
    {
        var copiedText = copied ?? "✓ copied — paste in game chat";
        b.Content = _copiedAt.TryGetValue(command, out var at)
            && DateTime.Now - at < CopiedLinger
            ? copiedText
            : label ?? $"⧉ copy  {command}";
        b.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(command);
                b.Content = copiedText;
                _copiedAt[command] = DateTime.Now;
            }
            catch { /* clipboard momentarily held by another app */ }
        };
        return b;
    }
}
