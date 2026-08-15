using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace EQBuddy.Avalonia;

public sealed class App : Application
{
    private static readonly string ErrorLog = Core.AppPaths.File("error.log");
    private Mutex? _instanceLock;
    private EventWaitHandle? _showRequest;

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
            if (!ClaimSingleInstance(desktop))
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
    /// (mirrors the WPF App). Named kernel objects are a Windows facility; elsewhere
    /// every launch simply runs (same degradation the WPF app never had to face).
    /// Keyed on the profile directory, not the machine, so an isolated EQBUDDY_APPDATA
    /// instance still runs alongside a normal one — that's how the app gets tested.
    /// </summary>
    private bool ClaimSingleInstance(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            var key = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(Core.AppPaths.Dir.ToLowerInvariant())))[..16];
            _instanceLock = new Mutex(initiallyOwned: true, $"Local\\EQBuddy_{key}", out var isFirst);
            _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\EQBuddyShow_{key}");

            if (!isFirst)
            {
                _showRequest.Set();   // ask the running copy to surface, then stand down
                return false;
            }

            // Background waiter: no UI thread cost while idle.
            var waiter = new Thread(() =>
            {
                while (_showRequest.WaitOne())
                    Dispatcher.UIThread.Post(() =>
                        (desktop.MainWindow as MainWindow)?.RestoreFromAnotherInstance());
            })
            { IsBackground = true, Name = "EQBuddy single-instance listener" };
            waiter.Start();
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
