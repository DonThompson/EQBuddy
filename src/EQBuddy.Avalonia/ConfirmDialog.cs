using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EQBuddy.Avalonia;

/// <summary>
/// WPF's MessageBox.Show(…, OKCancel/YesNo, defaultResult: the safe one), which
/// Avalonia has no equivalent for — so it's the SessionPicker's shape instead: a
/// small owned dialog, awaited for its answer.
///
/// Cancel carries BOTH Enter and Esc, which is what WPF's default-to-the-safe-button
/// bought us: nothing destructive is ever one stray keypress away. Every caller here
/// guards something that overwrites or erases the player's own record, so that
/// default is the whole point of the helper rather than a detail of one caller.
/// </summary>
internal static class ConfirmDialog
{
    public static async Task<bool> Ask(Window owner, string title, string message, string confirmLabel)
    {
        var answer = false;
        var dialog = new Window
        {
            Title = title,
            CanResize = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = AppTheme.BgBrush,
        };
        var text = new TextBlock
        {
            Text = message, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380, Foreground = AppTheme.TextBrush,
        };
        var go = ZoneTheming.Button(confirmLabel);
        go.Click += (_, _) => { answer = true; dialog.Close(); };
        var cancel = ZoneTheming.Button("Cancel", isDefault: true, isCancel: true);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => dialog.Close();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(go);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(text);
        root.Children.Add(buttons);
        dialog.Content = root;
        await dialog.ShowDialog(owner);
        return answer;
    }
}
