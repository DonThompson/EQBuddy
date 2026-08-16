using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

public sealed class App : Application
{
    private static readonly string ErrorLog = Core.AppPaths.File("error.log");

    // Held for the process's lifetime and deliberately never read again: this is the
    // profile claim, and letting it go out of scope would let the finalizer close the
    // handle and release the lock while EQBuddy is still running.
    private IDisposable? _instanceLock;

    public static void LogError(object? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLog)!);
            File.AppendAllText(ErrorLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        EQBuddy.Core.CoreLog.Sink = LogError;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            args.SetObserved();
        };

        // Applied before MainWindow is constructed so the saved theme is already live
        // for the very first frame (mirrors the WPF app's App.xaml.cs).
        try { AppTheme.Apply(Core.AppSettings.Load()); }
        catch (Exception ex) { LogError(ex); }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!ClaimSingleInstance())
            {
                desktop.Shutdown();
                return;
            }
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Second launches surface the running copy instead of starting a twin — the usual
    /// reason to relaunch is that the widget is hidden behind a fullscreen game
    /// (mirrors the WPF App). This used to be a named mutex, which is a Windows
    /// facility, so on Linux and macOS EVERY launch started a full second copy: two
    /// tailers on one log, two competitors for the hotkeys, and two whole-file writers
    /// racing on settings.json where the loser's changes vanish without a word.
    /// <see cref="SingleInstance"/> now carries it on every platform, keyed on the
    /// profile directory so an isolated EQBUDDY_APPDATA instance still runs alongside a
    /// normal one — that's how the app gets tested. The running copy picks the request
    /// up on its own tick (see MainWindow), so there is no waiter thread.
    /// </summary>
    private bool ClaimSingleInstance()
    {
        try
        {
            _instanceLock = SingleInstance.TryClaim(Core.AppPaths.Dir);
            if (_instanceLock is not null) return true;

            // Held — but only stand down if a live copy actually answers. A stale lock
            // file must never be the reason EQBuddy won't launch.
            if (SingleInstance.AskRunningCopyToShow(Core.AppPaths.Dir, TimeSpan.FromSeconds(4)))
                return false;

            LogError("Another EQBuddy holds this profile's lock but did not answer a " +
                "show request; starting anyway.");
            return true;
        }
        catch (Exception ex)
        {
            // Never let instance coordination stop the app from starting.
            LogError(ex);
            return true;
        }
    }
}
