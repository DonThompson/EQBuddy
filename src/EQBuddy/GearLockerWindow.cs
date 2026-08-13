using System.Windows;
using System.Windows.Controls;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// The Gear Locker (#104, Techsteps): everything wearable you OWN, grouped by slot,
/// each slot's items compared against each other — "⬇ outclassed by X" marks a
/// dominance dump candidate, never a taste call, and nothing here is ever "BiS":
/// the Locker ranks your bags, not the game. Stats come from the wiki item cache;
/// the one button that fetches missing pages is explicit, counted, and rate-limited
/// (the same one-fetch-per-request etiquette every wiki surface follows).
/// </summary>
public sealed class GearLockerWindow : Window
{
    private readonly MainWindow _main;
    private readonly StackPanel _panel = new() { Margin = new Thickness(10) };
    private readonly TextBlock _status = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Button _fetch;
    private bool _fetching;

    public GearLockerWindow(MainWindow main)
    {
        _main = main;
        Title = "Gear Locker";
        Width = 470;
        Height = 640;
        Owner = main;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        _status.ToolTip = InventoryWindow.OutputFileTip;

        var bar = new DockPanel { Margin = new Thickness(10, 8, 10, 0) };
        var refresh = Theming.Button("⟳ Refresh");
        refresh.ToolTip = InventoryWindow.OutputFileTip;
        refresh.Click += (_, _) => Render();
        DockPanel.SetDock(refresh, Dock.Right);
        bar.Children.Add(refresh);
        _fetch = Theming.Button("");
        _fetch.FontSize = 11;
        _fetch.Margin = new Thickness(0, 0, 6, 0);
        _fetch.ToolTip = "Fetches the wiki page for each owned item that has no cached stats "
            + "yet — one page at a time, politely spaced, cached for a week. Rows fill in "
            + "as pages arrive.";
        _fetch.Click += async (_, _) => await FetchMissing();
        DockPanel.SetDock(_fetch, Dock.Right);
        bar.Children.Add(_fetch);
        bar.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(new ScrollViewer
        {
            Content = _panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        Content = root;
        Render();
    }

    private List<string> _missing = [];

    private IReadOnlyList<string> MyClassCodes()
    {
        var picked = _main.QuestLedger?.ClassesFor(_main.QuestCharacterKey) ?? [];
        if (picked.Count == 0 && _main.CurrentSnapshot().InferredClass is { Length: > 0 } inf)
            picked = [inf];
        return picked.Select(GearLocker.Code).ToList();
    }

    private void Render()
    {
        _panel.Children.Clear();
        var snap = _main.LatestInventory(refresh: true);
        if (snap is null)
        {
            _status.Text = "No inventory dump found yet — in game, type  /outputfile inventory  "
                + "and click ⟳. (Hover for the full recipe.)";
            _fetch.Visibility = Visibility.Collapsed;
            return;
        }

        var groups = GearLocker.Build(snap.Entries,
            name => _main.WikiItems.CachedInfo(name) is { StatsLines.Count: > 0 } info
                ? ItemStatsBlock.Parse(info.StatsLines) : null,
            MyClassCodes());

        _missing = groups.Where(g => g.Slot == "STATS NOT FETCHED YET")
            .SelectMany(g => g.Rows).Select(r => r.BaseName).ToList();
        _fetch.Visibility = _missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_fetching)
            _fetch.Content = $"⇣ fetch stats for {_missing.Count} item{(_missing.Count == 1 ? "" : "s")}";

        var age = DateTime.Now - snap.WrittenAt;
        _status.Text = $"{System.IO.Path.GetFileName(snap.Path)} — "
            + (age.TotalMinutes < 1 ? "just now" : age.TotalHours < 1
                ? $"{(int)age.TotalMinutes}m ago" : $"{(int)age.TotalHours}h ago")
            + ". Comparisons use wiki BASE stats — a +N raises them in-game, so upgrades "
            + "are shown, never folded in.";

        foreach (var group in groups)
        {
            var header = new TextBlock
            {
                Text = group.Slot is "STATS NOT FETCHED YET"
                    ? $"{group.Slot} ({group.Rows.Count})"
                    : group.Slot,
                Style = (Style)FindResource("SectionLabel"),
                Margin = new Thickness(0, 9, 0, 2),
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            _panel.Children.Add(header);

            foreach (var row in group.Rows)
            {
                var line = new StackPanel { Margin = new Thickness(6, 0, 0, 3) };
                var name = new TextBlock
                {
                    Text = row.Count > 1 ? $"{row.Name} ×{row.Count}" : row.Name,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                name.SetResourceReference(TextBlock.ForegroundProperty,
                    row.OutclassedBy.Length > 0 ? "DimBrush" : "TextBrush");
                if (_main.WikiItems.CachedStatsText(row.BaseName) is { } tip)
                    name.ToolTip = tip;
                line.Children.Add(name);

                var detailParts = new List<string> { row.Where };
                if (row.StatLine.Length > 0) detailParts.Add(row.StatLine);
                if (row.ClassNote.Length > 0) detailParts.Add(row.ClassNote);
                if (row.OutclassedBy.Length > 0) detailParts.Add($"⬇ outclassed by {row.OutclassedBy}");
                var detail = new TextBlock
                {
                    Text = string.Join("  ·  ", detailParts),
                    FontSize = 10.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = string.Join("\n", detailParts),
                };
                detail.SetResourceReference(TextBlock.ForegroundProperty,
                    row.OutclassedBy.Length > 0 ? "WarnBrush" : "DimBrush");
                line.Children.Add(detail);
                _panel.Children.Add(line);
            }
        }

        var foot = new TextBlock
        {
            Text = "\"Outclassed\" = another item you own is at least as good on every stat "
                + "both carry and better on one — a dump candidate by arithmetic, not taste. "
                + "Items class-locked away from you never outclass anything of yours.",
            FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        };
        foot.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        _panel.Children.Add(foot);
    }

    private async Task FetchMissing()
    {
        if (_fetching || _missing.Count == 0) return;
        _fetching = true;
        try
        {
            var total = _missing.Count;
            for (var i = 0; i < _missing.Count; i++)
            {
                _fetch.Content = $"⇣ fetching {i + 1}/{total}…";
                await _main.WikiItems.LookupAsync(_missing[i]);
                await Task.Delay(400);   // polite pacing; the cache makes this one-time
            }
        }
        finally
        {
            _fetching = false;
            Render();
        }
    }
}
