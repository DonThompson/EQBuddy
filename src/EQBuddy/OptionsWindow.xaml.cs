using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// Thin WPF view over the shared OptionsViewModel (EQBuddy.UI.Shared) — all
/// mappings/mutations live there; this class builds controls, forwards input, and
/// applies the visual side effects (scale/opacity/layout) to the main window.
/// </summary>
public partial class OptionsWindow : Window
{
    private readonly MainWindow _main;
    private readonly OptionsViewModel _vm;
    private bool _ready;

    public OptionsWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        _vm = new OptionsViewModel(main.Settings, main.PersistSettings);
        Owner = main;
        Width = Math.Clamp(_vm.OptionsWidth, MinWidth, MaxWidth);
        // The handle only exists once the window is sourced; re-clamp on move because the
        // user may drag it to a monitor with a different size or DPI.
        SourceInitialized += (_, _) => ClampToMonitor();
        LocationChanged += (_, _) => ClampToMonitor();

        foreach (var label in OptionsViewModel.ThemeLabels) ThemeCombo.Items.Add(label);
        ThemeCombo.SelectedIndex = _vm.ThemeIndex;

        ScaleSlider.Value = _vm.UiScale;
        OpacitySlider.Value = _vm.Opacity;
        BgOpacitySlider.Value = _vm.BackgroundOpacity;
        TruncateCheck.IsChecked = _vm.TruncateLogs;
        PinChipsCheck.IsChecked = _vm.PinWatchChips;
        TutorialCheck.IsChecked = _vm.ShowTutorial;

        foreach (var choice in OptionsViewModel.WindowChoices) WindowCombo.Items.Add(choice);
        WindowCombo.SelectedIndex = _vm.RecentWindowIndex;

        foreach (var choice in OptionsViewModel.SoundChoices) SoundCombo.Items.Add(choice);
        SoundCombo.SelectedIndex = _vm.SoundIndex;
        UpdateSoundFileNote();

        BuildRulesEditor();
        BuildCardsEditor();
        HotkeyNote.Text = _vm.HotkeyNote;

        UpdateLabels();
        _ready = true;

        // CenterOwner + SizeToContent positions before the size is known and can land
        // off-screen next to an edge-docked widget — place ourselves once measured:
        // beside the widget (left if room, else right), clamped to the work area.
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            var left = _main.Left - ActualWidth - 12;
            if (left < wa.Left + 8) left = _main.Left + _main.ActualWidth + 12;
            Left = Math.Max(wa.Left + 8, Math.Min(left, wa.Right - ActualWidth - 8));
            Top = Math.Max(wa.Top + 8, Math.Min(_main.Top, wa.Bottom - ActualHeight - 8));
            Activate();
        };
    }

    private void UpdateLabels()
    {
        ScaleLabel.Text = _vm.ScaleLabel;
        OpacityLabel.Text = _vm.OpacityLabel;
        BgOpacityLabel.Text = _vm.BackgroundOpacityLabel;
    }

    private void OnScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _vm.UiScale = ScaleSlider.Value;
        _main.SetUiScale(_vm.UiScale);
        UpdateLabels();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _vm.Opacity = OpacitySlider.Value;
        _main.SetWindowOpacity(_vm.Opacity);
        UpdateLabels();
    }

    private void OnBgOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _vm.BackgroundOpacity = BgOpacitySlider.Value;
        _main.SetBackgroundOpacity(_vm.BackgroundOpacity);
        UpdateLabels();
    }

    private void OnTruncateChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) _vm.TruncateLogs = TruncateCheck.IsChecked == true;
    }

    private void OnTutorialToggled(object sender, RoutedEventArgs e)
    {
        if (_ready) _vm.ShowTutorial = TutorialCheck.IsChecked == true;
    }

    private void OnPinChipsChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) _vm.PinWatchChips = PinChipsCheck.IsChecked == true;
    }

    private void OnWindowChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_ready) _vm.RecentWindowIndex = WindowCombo.SelectedIndex;
    }

    private void OnThemeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _vm.ThemeIndex = ThemeCombo.SelectedIndex;
        ThemeManager.Apply(_vm.Settings.Theme);
        // The card rows pick Foreground (dim vs. normal) via FindResource at construction
        // time rather than a binding, so they need an explicit rebuild to pick up the new
        // palette — everything else in the window repaints on its own via DynamicResource.
        BuildCardsEditor();
        _main.RefreshTheme();
    }

    private void OnSoundChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (!_vm.IsCustomSoundIndex(SoundCombo.SelectedIndex))
        {
            _vm.SelectNamedSound(SoundCombo.SelectedIndex);
        }
        else
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose an alert sound",
                Filter = "Sound files (*.wav;*.mp3)|*.wav;*.mp3|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) == true)
                _vm.SetCustomSound(dlg.FileName);
            else if (!_vm.IsCustomSoundIndex(_vm.SoundIndex))
            {
                _ready = false; SoundCombo.SelectedIndex = _vm.SoundIndex; _ready = true;   // cancelled — revert
            }
        }
        UpdateSoundFileNote();
        _main.PlayAlertSound();   // instant feedback on the new choice
    }

    private void OnSoundTest(object sender, RoutedEventArgs e) => _main.PlayAlertSound();

    private void UpdateSoundFileNote()
    {
        SoundFileNote.Text = _vm.SoundFileNote;
        SoundFileNote.Visibility = _vm.SoundFileNote.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Resize state captured at drag start. Deriving each frame from the cursor's absolute
    // position rather than accumulating DragDelta avoids the feedback jitter you get when
    // the thumb moves with the window (which the left grip does).
    private double _dragCursorX, _dragLeft, _dragWidth;

    private void OnResizeStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _dragCursorX = CursorX();
        _dragLeft = Left;
        _dragWidth = Width;
    }

    private void OnResizeRightDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) =>
        Width = Math.Clamp(_dragWidth + (CursorX() - _dragCursorX), MinWidth, MaxWidth);

    /// <summary>Left edge: grow leftwards, keeping the right edge where it is.</summary>
    private void OnResizeLeftDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        var width = Math.Clamp(_dragWidth - (CursorX() - _dragCursorX), MinWidth, MaxWidth);
        Left = _dragLeft + (_dragWidth - width);
        Width = width;
    }

    private void OnResizeCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        _vm.OptionsWidth = Width;

    /// <summary>Cursor X in device-independent units (the space Left/Width live in).</summary>
    private double CursorX()
    {
        Native.GetCursorPos(out var p);
        return p.X * DipScale().X;
    }

    private (double X, double Y) DipScale()
    {
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        return m is { } t ? (t.M11, t.M22) : (1.0, 1.0);
    }

    /// <summary>
    /// Cap the window to the work area of whichever monitor it is on. At high Windows
    /// scaling (a tester runs 300%) the full options panel is taller than the screen, so
    /// without this the bottom is simply unreachable — the ScrollViewer only helps once
    /// the window itself is bounded. Recomputed on move because monitors differ in both
    /// size and DPI.
    /// </summary>
    private void ClampToMonitor()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var monitor = Native.MonitorFromWindow(hwnd, Native.MonitorDefaultToNearest);
        var info = new Native.MonitorInfo { cbSize = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return;

        var scale = DipScale();
        var workHeight = (info.rcWork.bottom - info.rcWork.top) * scale.Y;
        var workWidth = (info.rcWork.right - info.rcWork.left) * scale.X;
        // Leave a little breathing room so the rounded border isn't flush to the edge.
        MaxHeight = Math.Max(MinHeight + 1, workHeight - 24);
        MaxWidth = Math.Max(MinWidth + 1, Math.Min(900, workWidth - 24));
        if (Width > MaxWidth) Width = MaxWidth;
    }

    private static class Native
    {
        public const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X, Y; }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        _vm.AddRule();
        BuildRulesEditor();
    }

    /// <summary>
    /// Column layout for both the header and every rule row. Auto columns are matched by
    /// SharedSizeGroup (the panel is a shared-size scope) so the header labels stay lined
    /// up with the controls no matter how wide the combo boxes render.
    /// </summary>
    private static System.Windows.Controls.Grid RuleGrid()
    {
        var grid = new System.Windows.Controls.Grid();
        void Auto(string group) => grid.ColumnDefinitions.Add(
            new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = group });
        void Star(double w) => grid.ColumnDefinitions.Add(
            new System.Windows.Controls.ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });

        // Kind and name were fixed at 58/60 px, which clipped their content even before
        // the spell-class picker existed. Name and match text share the free width, so
        // widening the window grows the fields that actually hold free text.
        Auto("RuleKind");
        Star(1);
        Star(1.4);
        Auto("RuleBanner");
        Auto("RuleSound");
        Auto("RuleDelete");
        return grid;
    }

    private void BuildRulesEditor()
    {
        RulesPanel.Children.Clear();

        var header = RuleGrid();
        header.Margin = new Thickness(0, 2, 0, 2);
        var headings = new[] { ("Watch", 0), ("Name", 1), ("Match", 2) };
        foreach (var (text, column) in headings)
        {
            var label = new System.Windows.Controls.TextBlock
            {
                Text = text,
                FontSize = 10,
                Opacity = 0.7,
                Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 0),
            };
            System.Windows.Controls.Grid.SetColumn(label, column);
            header.Children.Add(label);
        }
        RulesPanel.Children.Add(header);

        foreach (var rule in _vm.Rules)
        {
            var row = RuleGrid();
            row.Margin = new Thickness(0, 3, 0, 0);

            var kind = new System.Windows.Controls.ComboBox { FontSize = 11, ToolTip = "What this rule watches" };
            foreach (var k in OptionsViewModel.KindNames) kind.Items.Add(k);
            kind.SelectedIndex = (int)rule.Kind;
            row.Children.Add(kind);

            var name = DarkBox(rule.Name, "name");
            name.Margin = new Thickness(4, 0, 0, 0);
            name.LostFocus += (_, _) => { rule.Name = name.Text.Trim(); _vm.Persist(); };
            System.Windows.Controls.Grid.SetColumn(name, 1);
            row.Children.Add(name);

            // Column 2 holds the match text, preceded (for Spell fade rules) by a class
            // picker: one named spell, or a whole class that keeps working as the
            // character levels into new spells and ranks.
            var matchArea = new System.Windows.Controls.Grid();
            matchArea.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            matchArea.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            System.Windows.Controls.Grid.SetColumn(matchArea, 2);
            row.Children.Add(matchArea);

            var spellFilter = new System.Windows.Controls.ComboBox
            {
                FontSize = 11,
                MinWidth = 122,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Watch one named spell, or a whole class of spells",
            };
            foreach (var f in OptionsViewModel.SpellFilterNames) spellFilter.Items.Add(f);
            spellFilter.SelectedIndex = (int)rule.SpellFilter;
            matchArea.Children.Add(spellFilter);

            var pattern = DarkBox(rule.Pattern, "match text (uses the name if left empty; optional for Death/Milestone)");
            pattern.Margin = new Thickness(4, 0, 0, 0);
            pattern.LostFocus += (_, _) => { rule.Pattern = pattern.Text.Trim(); _vm.Persist(); };
            System.Windows.Controls.Grid.SetColumn(pattern, 1);
            matchArea.Children.Add(pattern);

            // A class filter needs no match text, so the box goes away rather than sitting
            // there inviting input that would be ignored.
            void SyncMatchArea()
            {
                var isFade = rule.Kind == EQBuddy.Core.WatchKind.SpellFade;
                var byName = rule.SpellFilter == EQBuddy.Core.SpellFilter.ByName;
                spellFilter.Visibility = isFade ? Visibility.Visible : Visibility.Collapsed;
                pattern.Visibility = isFade && !byName ? Visibility.Collapsed : Visibility.Visible;
                // With no match box beside it the combo takes the whole cell, so its text
                // and drop arrow stay inside the row instead of running under the toggles.
                System.Windows.Controls.Grid.SetColumnSpan(spellFilter, isFade && !byName ? 2 : 1);
            }
            SyncMatchArea();

            kind.SelectionChanged += (_, _) =>
            {
                if (!_ready || kind.SelectedIndex < 0) return;
                rule.Kind = (EQBuddy.Core.WatchKind)kind.SelectedIndex;
                SyncMatchArea();
                _vm.Persist();
            };
            spellFilter.SelectionChanged += (_, _) =>
            {
                if (!_ready || spellFilter.SelectedIndex < 0) return;
                rule.SpellFilter = (EQBuddy.Core.SpellFilter)spellFilter.SelectedIndex;
                SyncMatchArea();
                _vm.Persist();
            };

            row.Children.Add(RuleToggle("🔔", "Banner alert on match", 3, rule.AlertBanner,
                v => rule.AlertBanner = v));

            // Per-rule sound, so you can tell what happened from the audio alone.
            // Replaces the old on/off toggle: "Off" mutes, "Default" follows the shared
            // choice below, anything else is this rule's own sound.
            var sound = new System.Windows.Controls.ComboBox
            {
                FontSize = 11,
                MinWidth = 84,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Sound for this rule — pick a different one per rule to tell them apart by ear",
            };
            foreach (var s in AlertSoundCatalog.RuleChoices) sound.Items.Add(s);
            sound.SelectedIndex = AlertSoundCatalog.RuleChoiceIndex(rule);
            if (AlertSoundCatalog.IsCustom(rule.AlertSoundName) && rule.AlertSoundName.Length > 0)
                sound.ToolTip = $"Custom: {rule.AlertSoundName}";
            sound.SelectionChanged += (_, _) =>
            {
                if (!_ready || sound.SelectedIndex < 0) return;
                if (AlertSoundCatalog.ApplyRuleChoice(rule, sound.SelectedIndex))
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = $"Choose a sound for \"{(rule.Name.Length > 0 ? rule.Name : rule.Pattern)}\"",
                        Filter = "Sound files (*.wav;*.mp3)|*.wav;*.mp3|All files (*.*)|*.*",
                    };
                    if (dlg.ShowDialog(this) == true)
                    {
                        rule.AlertSoundName = dlg.FileName;
                        sound.ToolTip = $"Custom: {dlg.FileName}";
                    }
                    else
                    {
                        // Cancelled — snap back to whatever the rule already had.
                        _ready = false;
                        sound.SelectedIndex = AlertSoundCatalog.RuleChoiceIndex(rule);
                        _ready = true;
                        return;
                    }
                }
                _vm.Persist();
                // Play it straight away so picking a sound is a decision you can hear.
                if (AlertSoundCatalog.Resolve(rule, _main.Settings.AlertSound) is { } preview)
                    _main.PlayAlertSound(preview);
            };
            System.Windows.Controls.Grid.SetColumn(sound, 4);
            row.Children.Add(sound);

            var del = new System.Windows.Controls.Button
            {
                Content = "✕", Style = (Style)FindResource("IconButton"), FontSize = 11,
            };
            del.Click += (_, _) =>
            {
                _vm.RemoveRule(rule);
                BuildRulesEditor();
            };
            System.Windows.Controls.Grid.SetColumn(del, 5);
            row.Children.Add(del);

            RulesPanel.Children.Add(row);
        }
    }

    private System.Windows.Controls.Primitives.ToggleButton RuleToggle(
        string glyph, string tip, int column, bool initial, Action<bool> apply)
    {
        var t = new System.Windows.Controls.Primitives.ToggleButton
        {
            Content = glyph, ToolTip = tip, IsChecked = initial, FontSize = 11,
            Style = (Style)FindResource("IconToggle"),
        };
        t.Checked += (_, _) => { apply(true); _vm.Persist(); };
        t.Unchecked += (_, _) => { apply(false); _vm.Persist(); };
        System.Windows.Controls.Grid.SetColumn(t, column);
        return t;
    }

    private System.Windows.Controls.TextBox DarkBox(string text, string tip)
    {
        var box = new System.Windows.Controls.TextBox
        {
            Text = text, ToolTip = tip, FontSize = 12,
            Padding = new Thickness(4, 2, 4, 2),
        };
        // SetResourceReference (not FindResource) so an in-place theme switch repaints
        // these rows too, not just the chrome built from XAML.
        box.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ComboBoxBrush");
        box.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderBrush");
        return box;
    }

    private void BuildCardsEditor()
    {
        CardsPanel.Children.Clear();
        foreach (var card in _vm.Cards)
        {
            var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 2, 0, 0) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 3; i++)
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = card.Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource(card.Hidden ? "DimBrush" : "TextBrush"),
            });

            row.Children.Add(CardButton("↑", "Move up", 1, () => { _vm.MoveCard(card.Key, -1); ApplyCards(); }));
            row.Children.Add(CardButton("↓", "Move down", 2, () => { _vm.MoveCard(card.Key, +1); ApplyCards(); }));
            row.Children.Add(CardButton(card.Hidden ? "🙈" : "👁",
                card.Hidden ? "Show card" : "Hide card (data still collected)", 3,
                () => { _vm.ToggleCard(card.Key); ApplyCards(); }));
            CardsPanel.Children.Add(row);
        }
    }

    private void ApplyCards()
    {
        _main.ApplySectionLayout();
        BuildCardsEditor();
    }

    private System.Windows.Controls.Button CardButton(string glyph, string tip, int column, Action action)
    {
        var b = new System.Windows.Controls.Button
        {
            Content = glyph, ToolTip = tip, FontSize = 11,
            Style = (Style)FindResource("IconButton"), Margin = new Thickness(6, 0, 0, 0),
        };
        b.Click += (_, _) => action();
        System.Windows.Controls.Grid.SetColumn(b, column);
        return b;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
