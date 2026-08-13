using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace EQBuddy.Avalonia;

/// <summary>
/// "How do I get there from here?" — the ZoneGraph has answered this for quest
/// sorting since 1.39; this window finally says it out loud (competitive gap #2,
/// 2026-08-10: the data was shipped, only the presentation was missing). Pick a
/// destination, get the hop list from wherever the log last saw you. Zone lines
/// come from the eqltools atlas (client-mined walking connections) plus the wiki's
/// boat and port adjacencies, so a hop may be a zone line, a boat, or a port —
/// the wiki page for a zone names which.
/// </summary>
public sealed class TravelWindow : Window
{
    private readonly IZoneHost _host;
    // WPF uses an editable ComboBox; Avalonia's isn't editable, and AutoCompleteBox
    // is the same gesture (type or pick) with the filtering built in.
    private readonly AutoCompleteBox _dest = new()
    {
        FontSize = 12, MinWidth = 240,
        FilterMode = AutoCompleteFilterMode.Contains,
        MinimumPrefixLength = 0,
        PlaceholderText = "Destination zone…",
    };
    private readonly TextBlock _fromLabel = new()
    {
        FontSize = 12, Margin = new Thickness(0, 0, 0, 6), Foreground = AppTheme.TextBrush,
    };
    private readonly StackPanel _route = new();

    public TravelWindow(IZoneHost host)
    {
        _host = host;
        Title = "Travel route";
        CanResize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = AppTheme.BgBrush;

        _dest.ItemsSource = _host.ZoneGraph.Zones;
        _dest.SelectionChanged += (_, _) => RenderRoute();
        _dest.KeyDown += (_, e) => { if (e.Key == Key.Enter) RenderRoute(); };
        var go = ZoneTheming.Button("Route", isDefault: true);
        go.Margin = new Thickness(6, 0, 0, 0);
        go.Click += (_, _) => RenderRoute();

        var pickRow = new StackPanel { Orientation = Orientation.Horizontal };
        pickRow.Children.Add(_dest);
        pickRow.Children.Add(go);

        var root = new StackPanel { Margin = new Thickness(12), MinWidth = 300 };
        root.Children.Add(_fromLabel);
        root.Children.Add(pickRow);
        root.Children.Add(_route);
        Content = root;
        RenderRoute();
    }

    /// <summary>Re-run against the current zone — called on open and when you zone.</summary>
    public void RenderRoute()
    {
        var from = _host.CurrentZoneName;
        _fromLabel.Text = from.Length > 0 ? $"From: {from}" : "From: (no zone seen in the log yet)";
        _route.Children.Clear();
        var dest = (_dest.SelectedItem as string) ?? (_dest.Text ?? "").Trim();
        if (from.Length == 0 || dest.Length == 0) return;

        var note = new TextBlock
        {
            FontSize = 12, Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 340,
        };
        if (_host.ZoneGraph.Distance(from, dest) is not { } route)
        {
            note.Text = $"No known route from {from} to \"{dest}\" — if a connection is missing, " +
                        "its wiki zone page probably lacks the adjacency.";
            note.Foreground = AppTheme.WarnBrush;
            _route.Children.Add(note);
            return;
        }

        note.Text = route.Hops == 0 ? "You're already there." : $"{route.Hops} zone{(route.Hops == 1 ? "" : "s")} away:";
        note.Foreground = AppTheme.AccentBrush;
        _route.Children.Add(note);
        for (var i = 0; i < route.Path.Count; i++)
        {
            var step = new TextBlock
            {
                Text = i == 0 ? $"  📍 {route.Path[i]}" : $"  {i}. {route.Path[i]}",
                FontSize = 12, Margin = new Thickness(4, 2, 0, 0),
                Foreground = i == route.Path.Count - 1 ? AppTheme.GoodBrush : AppTheme.TextBrush,
            };
            _route.Children.Add(step);
        }
    }
}
