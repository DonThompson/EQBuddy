using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EQBuddy.Core;

namespace EQBuddy.Avalonia;

/// <summary>
/// The notification-area icon (#114 follow-up, Frankthetankk): when the widget hides —
/// unfocused game, game not running — its taskbar entry vanishes with it, and a
/// running program with no visible presence anywhere reads as "closed". The tray icon
/// is the always-there answer: visible the whole time EQBuddy runs, hidden or not.
/// Left-click surfaces the widget; the menu adds Options and Exit.
///
/// WPF leans on WinForms NotifyIcon; here Avalonia's cross-platform TrayIcon carries
/// the same job (Windows tray, macOS status bar, Linux StatusNotifier where the shell
/// offers one). The icon itself is drawn at runtime — the project ships no bitmap
/// asset, and a generated accent disc beats adding one for a 16-pixel glyph; swap in a
/// real asset by loading a WindowIcon here if the csproj ever gains one.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly global::Avalonia.Controls.TrayIcon _icon;

    public TrayIcon(MainWindow main, Action? showOptions = null)
    {
        _icon = new global::Avalonia.Controls.TrayIcon
        {
            ToolTipText = $"EQBuddy v{UpdateChecker.CurrentVersion} — click to show",
            IsVisible = true,
        };
        try { _icon.Icon = DrawIcon(); }
        catch (Exception ex) { App.LogError(ex); /* icon-less tray entry still beats no tray entry */ }

        var menu = new NativeMenu();
        menu.Add(MenuItem("Show EQBuddy", () => Restore(main)));
        if (showOptions is not null)
            menu.Add(MenuItem("Options…", () => { Restore(main); showOptions(); }));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(MenuItem("Exit EQBuddy", main.Close));
        _icon.Menu = menu;
        _icon.Clicked += (_, _) => Restore(main);

        if (Application.Current is { } app)
        {
            var icons = global::Avalonia.Controls.TrayIcon.GetIcons(app) ?? [];
            icons.Add(_icon);
            global::Avalonia.Controls.TrayIcon.SetIcons(app, icons);
        }
    }

    private static NativeMenuItem MenuItem(string header, Action click)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>An explicit show is the user's choice, whatever hide logic was in
    /// effect — same path a second launch takes (MainWindow clears its focus-hide
    /// state there, so the widget can't come back visible-but-frozen).</summary>
    private static void Restore(MainWindow main) => main.RestoreFromAnotherInstance();

    /// <summary>The shipped EQBuddy.ico when it loads (the csproj carries it as an
    /// AvaloniaResource now), else a 32px accent-colored disc with "EQ" — recognizable
    /// next to the widget it summons, following whatever theme was live at startup.</summary>
    private static WindowIcon DrawIcon()
    {
        try
        {
            return new WindowIcon(global::Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://EQBuddy.Avalonia/Assets/EQBuddy.ico")));
        }
        catch (Exception ex) { App.LogError(ex); /* fall back to the drawn disc */ }
        var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var accent = AppTheme.AccentBrush.Color;
            ctx.DrawEllipse(new SolidColorBrush(accent),
                new Pen(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), 1.5),
                new Point(16, 16), 14.5, 14.5);
            var text = new FormattedText("EQ", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, weight: FontWeight.Bold), 13,
                new SolidColorBrush(Color.FromArgb(230, 0, 0, 0)));
            ctx.DrawText(text, new Point(16 - text.Width / 2, 16 - text.Height / 2));
        }
        return new WindowIcon(bitmap);
    }

    public void Dispose()
    {
        _icon.IsVisible = false;
        if (Application.Current is { } app &&
            global::Avalonia.Controls.TrayIcon.GetIcons(app) is { } icons)
            icons.Remove(_icon);
        _icon.Dispose();
    }
}
