using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EQBuddy.Core;

namespace EQBuddy;

/// <summary>
/// Session drops grouped by source creature, with export (discussion #55 — LeBigNasty
/// tracking Plane of Sky and feeding corrected drop tables back to eqlwiki for revamped
/// zones). Read-only view over StatsSnapshot.Mobs; the filter narrows both the display
/// and what the export buttons emit, so "just the golems" is one filter away.
/// </summary>
public partial class DropsWindow : Window
{
    private readonly MainWindow _main;
    private StatsSnapshot _snapshot = new();
    private string _signature = "";
    private DateTime _lastRefresh = DateTime.MinValue;

    public DropsWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
    }

    /// <summary>Called on open and from MainWindow's tick while visible.</summary>
    public void Update(StatsSnapshot s)
    {
        _lastRefresh = DateTime.Now;
        _snapshot = s;
        Render();
    }

    public void MaybeRefresh()
    {
        if ((DateTime.Now - _lastRefresh).TotalSeconds >= 3) Update(_main.CurrentSnapshot());
    }

    private List<MobSummary> Filtered()
    {
        var filter = FilterBox.Text.Trim();
        var mobs = _snapshot.Mobs.Where(m => m.Loot.Count > 0);
        if (filter.Length > 0)
            mobs = mobs.Where(m =>
                m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || m.Loot.Any(l => l.Item.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        return mobs.ToList();
    }

    private void Render()
    {
        var mobs = Filtered();
        var sig = string.Join("|", mobs.Select(m =>
            $"{m.Name}:{m.Kills}:{string.Join(",", m.Loot.Select(l => $"{l.Item}{l.Count}"))}"));
        if (sig == _signature) return;
        _signature = sig;

        MobsPanel.Children.Clear();
        var (character, _) = _main.Identity;
        Title = character.Length > 0
            ? $"EQBuddy — Drops by Creature — {character}"
            : "EQBuddy — Drops by Creature";
        foreach (var mob in mobs)
        {
            var header = new TextBlock
            {
                Text = $"{mob.Name} — {mob.Kills} kill{(mob.Kills == 1 ? "" : "s")}",
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 2),
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            MobsPanel.Children.Add(header);

            foreach (var l in mob.Loot)
            {
                var row = new TextBlock
                {
                    Text = $"{l.Item} ×{l.Count}" +
                           (l.DropRatePct is { } pct ? $"  ·  {pct:0.#}% of {mob.Kills}" : ""),
                    FontSize = 12, Margin = new Thickness(14, 1, 0, 1),
                };
                row.SetResourceReference(TextBlock.ForegroundProperty,
                    _main.QuestCatalog.IsTurnInItem(l.Item) ? "GoodBrush" : "TextBrush");
                MobsPanel.Children.Add(row);
            }
        }
        if (mobs.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = FilterBox.Text.Trim().Length > 0
                    ? "Nothing matches that filter."
                    : "No drops recorded this session yet — loot lines name their corpse,\nso every kill you loot shows up here.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            MobsPanel.Children.Add(empty);
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        _signature = "";
        Render();
    }

    private void OnCopyText(object sender, RoutedEventArgs e)
    {
        var (character, server) = _main.Identity;
        TryClipboard(DropsReport.ToText(Filtered(), character, server, _snapshot.SessionStart));
    }

    private void OnCopyCsv(object sender, RoutedEventArgs e) =>
        TryClipboard(DropsReport.ToCsv(Filtered()));

    private static void TryClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { CoreLog.Error(ex); }   // clipboard contention: rare, retry by hand
    }

    private void OnSaveCsv(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"eqbuddy-drops-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, DropsReport.ToCsv(Filtered())); }
        catch (Exception ex) { CoreLog.Error(ex); }
    }
}
