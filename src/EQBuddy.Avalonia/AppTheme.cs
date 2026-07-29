using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

internal static class AppTheme
{
    // Every theme color is a single, never-replaced SolidColorBrush instance. Controls
    // hold a reference to these (not a copy), so Apply() mutating .Color repaints
    // everything already on screen — Avalonia brushes raise Invalidated on change, no
    // resource-dictionary lookup or rebuild required. Values must stay in sync with the
    // WPF app's Themes/*.xaml palettes (see Palette below) — same keys, same hex.
    public static readonly SolidColorBrush BgBrush = new();
    public static readonly SolidColorBrush PanelBrush = new();
    public static readonly SolidColorBrush PanelHoverBrush = new();
    public static readonly SolidColorBrush BorderBrush = new();
    public static readonly SolidColorBrush TextBrush = new();
    public static readonly SolidColorBrush DimBrush = new();
    public static readonly SolidColorBrush AccentBrush = new();
    public static readonly SolidColorBrush GoodBrush = new();
    public static readonly SolidColorBrush BadBrush = new();
    public static readonly SolidColorBrush WarnBrush = new();
    public static readonly SolidColorBrush PopupBrush = new();
    public static readonly SolidColorBrush ComboBoxBrush = new();
    public static readonly SolidColorBrush GoodWashBrush = new();
    public static readonly SolidColorBrush WarnWashBrush = new();

    private sealed record Palette(
        string Bg, string Panel, string PanelHover, string Border, string Text, string Dim,
        string Accent, string Good, string Bad, string Warn, string Popup, string ComboBox,
        string GoodWash, string WarnWash);

    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        ["ParchmentBrass"] = new("#F21C1917", "#26FFFFFF", "#33FFD98C", "#66C9A227", "#FFEDE4D3",
            "#FF9C927F", "#FFE3B341", "#FF7FBF5F", "#FFD9634F", "#FFE0A030", "#FF262119",
            "#FF2A251F", "#337FBF5F", "#33E0A030"),
        ["BlueGrey"] = new("#F2181C21", "#26FFFFFF", "#335C8AC2", "#665C7A99", "#FFE4E9EF",
            "#FF8B96A3", "#FF5FA8D3", "#FF6FBF7F", "#FFD9634F", "#FFE0A030", "#FF20242B",
            "#FF242830", "#336FBF7F", "#33E0A030"),
        ["Turquoise"] = new("#F2131C1C", "#26FFFFFF", "#3340C7B8", "#6629A99A", "#FFE0F2EF",
            "#FF87A6A0", "#FF3FCFBE", "#FF6FBF7F", "#FFD9634F", "#FFE0A030", "#FF16211F",
            "#FF1A2725", "#336FBF7F", "#33E0A030"),
        ["Redish"] = new("#F21F1615", "#26FFFFFF", "#33D96A55", "#66B34A3D", "#FFF2E2DE",
            "#FFA88A83", "#FFE0654A", "#FF7FBF5F", "#FFD9345F", "#FFE0A030", "#FF251815",
            "#FF291C18", "#337FBF5F", "#33E0A030"),
        ["Grey"] = new("#F21A1A1A", "#26FFFFFF", "#33BFBFBF", "#66808080", "#FFEAEAEA",
            "#FF9C9C9C", "#FFC0C0C0", "#FF7FBF5F", "#FFD9634F", "#FFE0A030", "#FF232323",
            "#FF272727", "#337FBF5F", "#33E0A030"),
        ["Solarized"] = new("#F2FDF6E3", "#14002B36", "#33268BD2", "#66586E75", "#FF657B83",
            "#FF93A1A1", "#FF268BD2", "#FF859900", "#FFDC322F", "#FFCB4B16", "#FFEEE8D5",
            "#FFEEE8D5", "#33859900", "#33CB4B16"),
        ["SolarizedDark"] = new("#F2002B36", "#26FFFFFF", "#33268BD2", "#66586E75", "#FF839496",
            "#FF586E75", "#FF268BD2", "#FF859900", "#FFDC322F", "#FFCB4B16", "#FF073642",
            "#FF0A3C48", "#33859900", "#33CB4B16"),
    };

    static AppTheme() => Apply("ParchmentBrass");

    /// <summary>Repaints every control holding one of the brushes above. An unrecognized
    /// key (e.g. from an older settings.json) falls back to the first theme rather than
    /// throwing — same behavior as the WPF app's ThemeManager.</summary>
    public static void Apply(string themeKey)
    {
        var key = ThemeCatalog.Themes[ThemeCatalog.IndexOf(themeKey)].Key;
        var p = Palettes[key];
        BgBrush.Color = Color.Parse(p.Bg);
        PanelBrush.Color = Color.Parse(p.Panel);
        PanelHoverBrush.Color = Color.Parse(p.PanelHover);
        BorderBrush.Color = Color.Parse(p.Border);
        TextBrush.Color = Color.Parse(p.Text);
        DimBrush.Color = Color.Parse(p.Dim);
        AccentBrush.Color = Color.Parse(p.Accent);
        GoodBrush.Color = Color.Parse(p.Good);
        BadBrush.Color = Color.Parse(p.Bad);
        WarnBrush.Color = Color.Parse(p.Warn);
        PopupBrush.Color = Color.Parse(p.Popup);
        ComboBoxBrush.Color = Color.Parse(p.ComboBox);
        GoodWashBrush.Color = Color.Parse(p.GoodWash);
        WarnWashBrush.Color = Color.Parse(p.WarnWash);
    }

    // Tint comes from the current theme's BgBrush rather than a fixed color, so this
    // still reads right after a theme switch — only the alpha is opacity's to control.
    // Returns a fresh brush each call (opacity is a slider, not a theme), so callers that
    // want it to track a live theme switch must re-invoke this after AppTheme.Apply.
    public static IBrush BgWithOpacity(double opacity)
    {
        var c = BgBrush.Color;
        return new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(opacity, 0.15, 1.0) * 255), c.R, c.G, c.B));
    }

    public static Button IconButton(AppIcon icon, string tip)
    {
        var button = IconButtonContent(CreateIcon(icon, DimBrush), tip);
        button.Padding = new Thickness(5);
        return button;
    }

    public static Button IconButton(string text, string tip)
    {
        return IconButtonContent(text, tip);
    }

    private static Button IconButtonContent(object content, string tip)
    {
        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            Foreground = DimBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            FontSize = 13,
            Cursor = new Cursor(StandardCursorType.Hand),
            MinWidth = 26,
            MinHeight = 24,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    public static ToggleButton IconToggle(string text, string tip)
    {
        var button = new ToggleButton
        {
            Content = text,
            Background = Brushes.Transparent,
            Foreground = AccentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            FontSize = 13,
            Cursor = new Cursor(StandardCursorType.Hand),
            MinWidth = 26,
            MinHeight = 24,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    public static Button StarButton(string key, string tip)
    {
        var button = IconButtonContent(CreateIcon(AppIcon.Star, DimBrush, 13), tip);
        button.Tag = key;
        button.Margin = new Thickness(8, 0, 0, 0);
        return button;
    }

    public static PathIcon Icon(AppIcon icon, IBrush? brush = null, double size = 14) =>
        CreateIcon(icon, brush ?? DimBrush, size);

    public static TextBlock DimText(string text, Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = DimBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? default,
    };

    public static TextBlock StatValue(string text = "") => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Foreground = AccentBrush,
    };

    public static SectionPanel Section(Control header, Control content) => new(header, content);

    public static TextBlock Heading(string text, IBrush? brush = null) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = brush ?? AccentBrush,
    };

    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    private static PathIcon CreateIcon(AppIcon icon, IBrush brush, double size = 14)
    {
        var data = icon switch
        {
            AppIcon.Settings => "M19.43 12.98c.04-.32.07-.65.07-.98s-.02-.66-.07-.98l2.11-1.65c.19-.15.24-.42.12-.64l-2-3.46c-.12-.22-.37-.31-.6-.22l-2.49 1a7.28 7.28 0 0 0-1.69-.98L14.5 2.42A.5.5 0 0 0 14 2h-4a.5.5 0 0 0-.5.42L9.12 5.07c-.61.23-1.18.56-1.69.98l-2.49-1a.5.5 0 0 0-.6.22l-2 3.46a.5.5 0 0 0 .12.64l2.11 1.65c-.05.32-.07.65-.07.98s.02.66.07.98l-2.11 1.65a.5.5 0 0 0-.12.64l2 3.46c.12.22.37.31.6.22l2.49-1c.51.4 1.08.74 1.69.98l.38 2.65a.5.5 0 0 0 .5.42h4a.5.5 0 0 0 .5-.42l.38-2.65c.61-.23 1.18-.56 1.69-.98l2.49 1c.23.08.48 0 .6-.22l2-3.46a.5.5 0 0 0-.12-.64l-2.11-1.65ZM12 15.5A3.5 3.5 0 1 1 12 8a3.5 3.5 0 0 1 0 7.5Z",
            AppIcon.Refresh => "M17.65 6.35A7.95 7.95 0 0 0 12 4a8 8 0 1 0 7.45 5.08h-2.16A6 6 0 1 1 12 6c1.66 0 3.14.69 4.22 1.78L13 11h8V3l-3.35 3.35Z",
            AppIcon.Minimize => "M5 12h14v2H5z",
            AppIcon.Expand => "M5 5h6v2H8.41l3.3 3.29-1.42 1.42L7 8.41V11H5V5Zm14 14h-6v-2h2.59l-3.3-3.29 1.42-1.42L17 15.59V13h2v6Z",
            AppIcon.Close => "M6.4 5 5 6.4 10.6 12 5 17.6 6.4 19 12 13.4 17.6 19 19 17.6 13.4 12 19 6.4 17.6 5 12 10.6 6.4 5Z",
            AppIcon.Star => "M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73-1.64 7.03L12 17.27 18.18 21l-1.63-7.03L22 9.24ZM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4Z",
            AppIcon.StarFilled => "M12 17.27 18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27Z",
            AppIcon.ChevronRight => "M8.59 16.59 13.17 12 8.59 7.41 10 6l6 6-6 6-1.41-1.41Z",
            AppIcon.ChevronDown => "M7.41 8.59 12 13.17l4.59-4.58L18 10l-6 6-6-6 1.41-1.41Z",
            _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null),
        };

        return new PathIcon
        {
            Data = StreamGeometry.Parse(data),
            Foreground = brush,
            Width = size,
            Height = size,
        };
    }
}

internal enum AppIcon
{
    Settings,
    Refresh,
    Minimize,
    Expand,
    Close,
    Star,
    StarFilled,
    ChevronRight,
    ChevronDown,
}

internal sealed class SectionPanel : Border
{
    private readonly Border _body;
    private readonly PathIcon _chevron;

    public bool IsExpanded
    {
        get => _body.IsVisible;
        set
        {
            _body.IsVisible = value;
            _chevron.Data = StreamGeometry.Parse(value
                ? "M7.41 8.59 12 13.17l4.59-4.58L18 10l-6 6-6-6 1.41-1.41Z"
                : "M8.59 16.59 13.17 12 8.59 7.41 10 6l6 6-6 6-1.41-1.41Z");
        }
    }

    public SectionPanel(Control header, Control content)
    {
        Background = AppTheme.PanelBrush;
        CornerRadius = new CornerRadius(6);
        Margin = new Thickness(0, 2, 0, 0);

        _chevron = AppTheme.Icon(AppIcon.ChevronRight, AppTheme.DimBrush, 15);
        _chevron.VerticalAlignment = VerticalAlignment.Center;
        _chevron.Margin = new Thickness(6, 0, 0, 0);

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        headerGrid.Children.Add(header);
        Grid.SetColumn(_chevron, 1);
        headerGrid.Children.Add(_chevron);

        var headerBorder = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = headerGrid,
        };
        headerBorder.PointerPressed += (_, args) =>
        {
            if (args.Source is Button or ToggleButton) return;
            IsExpanded = !IsExpanded;
            args.Handled = true;
        };

        _body = new Border
        {
            Padding = new Thickness(10, 0, 10, 8),
            Child = content,
            IsVisible = false,
        };

        Child = new StackPanel
        {
            Children =
            {
                headerBorder,
                _body,
            },
        };
    }
}
