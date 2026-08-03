using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EQBuddy.Core;

namespace EQBuddy;

/// <summary>
/// The once-per-update "What's new" popup (NOTES-001). Shown at launch when the
/// running version is newer than the last one whose notes were seen; lists every
/// skipped version, newest first, then never again. Fresh installs never see it —
/// onboarding belongs to the tutorial.
/// </summary>
public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow(MainWindow main, IReadOnlyList<WhatsNewEntry> entries)
    {
        InitializeComponent();
        Owner = main;
        TitleText.Text = entries.Count == 1
            ? $"What's new in EQBuddy {entries[0].Version}"
            : $"What's new since your last version";

        foreach (var entry in entries)
        {
            if (entries.Count > 1)
            {
                var header = new TextBlock
                {
                    Text = $"EQBuddy {entry.Version}", FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 2),
                };
                header.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                NotesPanel.Children.Add(header);
            }
            foreach (var line in entry.Highlights)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var bullet = new TextBlock { Text = "•", FontSize = 12, Margin = new Thickness(2, 0, 8, 0) };
                bullet.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                var text = new TextBlock { Text = line, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                Grid.SetColumn(text, 1);
                row.Children.Add(bullet);
                row.Children.Add(text);
                NotesPanel.Children.Add(row);
            }
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
