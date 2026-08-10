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
}
