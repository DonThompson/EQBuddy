using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using EQBuddy.Core;

namespace EQBuddy;

public partial class HistoryWindow : Window
{
    private readonly SessionRepository _repo;
    private List<SessionRow> _rows = [];
    private SessionRow? _selected;
    private StatsSnapshot? _selectedSnapshot;

    public HistoryWindow(SessionRepository repo)
    {
        InitializeComponent();
        _repo = repo;
        RefreshFilters();
        RefreshList();
    }

    private void RefreshFilters()
    {
        var current = CharFilter.SelectedItem as string;
        CharFilter.Items.Clear();
        CharFilter.Items.Add("All characters");
        foreach (var (server, character) in _repo.Characters())
            CharFilter.Items.Add($"{character} ({server})");
        CharFilter.SelectedItem = current is not null && CharFilter.Items.Contains(current)
            ? current : "All characters";
    }

    private (string? Server, string? Character) SelectedFilter()
    {
        if (CharFilter.SelectedItem is not string sel || sel == "All characters")
            return (null, null);
        var open = sel.LastIndexOf(" (", StringComparison.Ordinal);
        return (sel[(open + 2)..^1], sel[..open]);
    }

    private void RefreshList()
    {
        var (server, character) = SelectedFilter();
        _rows = _repo.Query(server, character, SearchBox.Text);
        SessionList.Items.Clear();
        foreach (var r in _rows)
        {
            var dur = TimeSpan.FromSeconds(r.ElapsedSeconds);
            SessionList.Items.Add(
                $"{r.StartLocal:MMM d h:mm tt} · {r.Character}\n" +
                $"   {(r.PrimaryZone.Length > 0 ? r.PrimaryZone : "—")} · {(int)dur.TotalHours}h {dur.Minutes}m · " +
                $"{r.Kills} kills · {r.XpPercent:0.#}% xp · {StatsSnapshot.FormatCoin(r.Copper)}" +
                (r.EndReason == "RecoveredAfterCrash" ? " · (recovered)" : "") +
                (r.EndReason == "Active" ? " · (in progress)" : ""));
        }
        CountText.Text = $"{_rows.Count} session{(_rows.Count == 1 ? "" : "s")}";
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => RefreshList();
    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void OnSessionSelected(object sender, SelectionChangedEventArgs e)
    {
        var i = SessionList.SelectedIndex;
        if (i < 0 || i >= _rows.Count) { _selected = null; return; }
        _selected = _rows[i];
        _selectedSnapshot = _repo.LoadSnapshot(_selected.Id);
        NoteBox.Text = _selected.Note;
        TagsBox.Text = _selected.Tags;
        DetailText.Text = _selectedSnapshot is null
            ? "Could not load session detail."
            : BuildOverview(_selected, _selectedSnapshot);
    }

    internal static string BuildOverview(SessionRow r, StatsSnapshot s)
    {
        var sb = new StringBuilder();
        var dur = TimeSpan.FromSeconds(r.ElapsedSeconds);
        var act = TimeSpan.FromSeconds(r.ActiveSeconds);
        sb.AppendLine($"{r.Character} ({r.Server}) — {r.StartLocal:dddd MMM d, h:mm tt}");
        sb.AppendLine($"Duration {(int)dur.TotalHours}h {dur.Minutes}m · active {(int)act.TotalMinutes}m · ended: {r.EndReason}");
        sb.AppendLine();
        sb.AppendLine($"Kills      {s.YourKillCount} (+{s.PartyKillCount} group) · {s.KillsPerHour:0.0}/hr");
        sb.AppendLine($"XP         {s.XpPercent:0.0}% · {s.XpPerHour:0.0}%/hr" +
                      (s.Levels.Count > 0 ? $" · {string.Join(", ", s.Levels.Select(l => l.Text))}" : "") +
                      (s.AaGained > 0 ? $" · {s.AaGained} AA" : ""));
        sb.AppendLine($"Damage     {s.DamageDealt:N0} dealt · {s.SessionDps:0.0} dps · taken {s.DamageTaken:N0}");
        if (s.HealingDone > 0)
            sb.AppendLine($"Healing    {s.HealingDone:N0} done · {s.Hps:0.#} hps");
        sb.AppendLine($"Money      {StatsSnapshot.FormatCoin(s.Copper)} ({StatsSnapshot.FormatCoin(s.CopperPerHour)}/hr)");
        sb.AppendLine($"Deaths     {s.Deaths.Count}");
        sb.AppendLine();
        if (s.DamageBySource.Count > 0)
        {
            sb.AppendLine("Top damage sources:");
            foreach (var d in s.DamageBySource.Take(8))
                sb.AppendLine($"  {d.Name,-28} {d.Total,8:N0} · {d.Hits} hits · avg {(double)d.Total / d.Hits:0.#}");
            sb.AppendLine();
        }
        if (s.YourKills.Count > 0)
        {
            sb.AppendLine("Kills by creature:");
            foreach (var k in s.YourKills.Take(10))
                sb.AppendLine($"  {k.Name,-28} ×{k.Count}");
            sb.AppendLine();
        }
        if (s.Loot.Count > 0)
        {
            sb.AppendLine("Loot:");
            foreach (var l in s.Loot.Take(15))
                sb.AppendLine($"  {l.Item,-34} ×{l.Count}");
            sb.AppendLine();
        }
        if (s.Zones.Count > 0)
            sb.AppendLine("Zones: " + string.Join(" → ", s.Zones.Select(z => z.Text)));
        if (s.Markers.Count > 0)
            sb.AppendLine("Markers: " + string.Join(" · ", s.Markers.Select(m => $"{m.Text} ({m.Time:h:mm tt})")));
        return sb.ToString();
    }

    private void OnSaveMeta(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _repo.SetNoteTags(_selected.Id, NoteBox.Text.Trim(), TagsBox.Text.Trim());
        RefreshList();
    }

    private void OnCopySummary(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selectedSnapshot is null) return;
        Clipboard.SetText(BuildOverview(_selected, _selectedSnapshot));
    }

    private void OnExportJson(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selectedSnapshot is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"eqbuddy-{_selected.Character}-{_selected.StartLocal:yyyyMMdd-HHmm}.json",
            Filter = "JSON|*.json",
        };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName,
            System.Text.Json.JsonSerializer.Serialize(_selectedSnapshot,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (MessageBox.Show(this,
                $"Delete the {_selected.StartLocal:MMM d h:mm tt} session for {_selected.Character}? This cannot be undone.",
                "Delete session", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _repo.Delete(_selected.Id);
        _selected = null;
        _selectedSnapshot = null;
        DetailText.Text = "Select a session.";
        RefreshFilters();
        RefreshList();
    }
}
