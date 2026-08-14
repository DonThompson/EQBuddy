using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EQBuddy.Companion;

namespace EQBuddy;

/// <summary>
/// The EQBuddy Mobile pairing window (Options → Behavior → "EQBuddy Mobile…"): the
/// enable switch, the QR code a phone scans to become a live companion display, the
/// literal URL for when scanning won't take, the desktop gate (which screens the PC
/// is willing to send), and the honest firewall talk. All lifecycle logic lives in
/// CompanionHost (EQBuddy.Companion, UI-free); this window is furniture.
/// </summary>
public sealed class CompanionWindow : Window
{
    private readonly CompanionHost _host;
    private readonly CheckBox _enable;
    private readonly Image _qrImage = new() { Width = 220, Height = 220, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBox _urlBox = new()
    {
        FontSize = 12, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
        BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        Margin = new Thickness(0, 2, 0, 2),
    };
    private readonly TextBlock _statusLine = new() { FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _errorLine = new()
    {
        FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0),
    };
    private readonly StackPanel _pairPanel = new();

    public CompanionWindow(CompanionHost host)
    {
        _host = host;
        Title = "EQBuddy Mobile (Beta)";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "BgBrush");

        var root = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };

        var intro = Dim(
            "Turn a phone or tablet on the same Wi-Fi into a live EQBuddy display: scan " +
            "the code, the browser opens, and your timers, map and checklists follow you " +
            "around the house. Everything stays on your own network — nothing is hosted, " +
            "nothing is uploaded, and it's off unless you turn it on. Beta: it works, it " +
            "just hasn't been through as many camps as the rest of EQBuddy.");
        intro.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(intro);

        _enable = new CheckBox
        {
            Content = new TextBlock { Text = "Enable EQBuddy Mobile", FontSize = 12 },
            IsChecked = _host.Running,
        };
        ((TextBlock)_enable.Content).SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _enable.Checked += (_, _) => { _host.SetEnabled(true); Refresh(); };
        _enable.Unchecked += (_, _) => { _host.SetEnabled(false); Refresh(); };
        root.Children.Add(_enable);
        root.Children.Add(_errorLine);
        _errorLine.SetResourceReference(TextBlock.ForegroundProperty, "BadBrush");

        // ---- pairing (QR + URL + connected count), visible while running ----
        _qrImage.HorizontalAlignment = HorizontalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(_qrImage, BitmapScalingMode.NearestNeighbor);
        _pairPanel.Children.Add(_qrImage);
        var urlHint = Dim("Scanning not cooperating? Type this address in the device's browser " +
            "instead — the part after # is the pairing code, keep it:");
        urlHint.Margin = new Thickness(0, 4, 0, 0);
        _pairPanel.Children.Add(urlHint);
        _urlBox.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        _pairPanel.Children.Add(_urlBox);
        _statusLine.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _pairPanel.Children.Add(_statusLine);

        var chrome = Dim("Propping a tablet beside the monitor? Once the page is open, use the " +
            "browser's \"Add to Home Screen\" — it launches EQBuddy Mobile in its own window " +
            "with no address bar, and remembers the pairing code. The ⛶ button at the top of " +
            "the page does the same for one visit.");
        chrome.Margin = new Thickness(0, 6, 0, 0);
        _pairPanel.Children.Add(chrome);

        var regen = Theming.Button("New code (disconnects every paired device)");
        regen.Margin = new Thickness(0, 6, 0, 0);
        regen.HorizontalAlignment = HorizontalAlignment.Left;
        regen.Click += (_, _) => { _host.RegenerateToken(); Refresh(); };
        _pairPanel.Children.Add(regen);

        // ---- desktop gate: which screens this PC is willing to send ----
        var gateHead = new TextBlock
        {
            Text = "Screens offered to devices", FontSize = 12,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2),
        };
        gateHead.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _pairPanel.Children.Add(gateHead);
        _pairPanel.Children.Add(Dim("Untick anything you'd rather never leave this PC. " +
            "Each device then picks its own screens (the ⚙ on the page) from what's offered."));
        // The list is long enough now to need its own scroll rather than a window
        // taller than a laptop screen.
        var gateList = new StackPanel();
        foreach (var surface in CompanionSurfaces.All)
        {
            var cb = new CheckBox
            {
                Content = new TextBlock { Text = CompanionSurfaces.Label(surface), FontSize = 12 },
                IsChecked = _host.OfferedSurfaces.Contains(surface),
                Margin = new Thickness(0, 4, 0, 0),
                ToolTip = CompanionSurfaces.Describe(surface),
            };
            ((TextBlock)cb.Content).SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            cb.Checked += (_, _) => _host.SetSurfaceOffered(surface, true);
            cb.Unchecked += (_, _) => _host.SetSurfaceOffered(surface, false);
            gateList.Children.Add(cb);
        }
        _pairPanel.Children.Add(new ScrollViewer
        {
            Content = gateList,
            MaxHeight = 210,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 2, 0, 0),
        });

        // ---- the honest firewall talk (see CompanionServer's header comment) ----
        var fw = Dim(
            "First time on, Windows Firewall usually asks whether to allow EQBuddy — say yes " +
            "for private networks. If the page never loads on your device: that prompt was " +
            "missed (Windows Security → Firewall → Allow an app), or your Wi-Fi keeps devices " +
            "apart (guest networks often do — use the main network). Best check: open the " +
            "address above in that device's browser right now.");
        fw.Margin = new Thickness(0, 12, 0, 0);
        _pairPanel.Children.Add(fw);

        root.Children.Add(_pairPanel);
        Content = root;

        _host.ClientsChanged += OnClientsChanged;
        Closed += (_, _) => _host.ClientsChanged -= OnClientsChanged;
        Refresh();
    }

    private void OnClientsChanged() => Dispatcher.BeginInvoke(UpdateStatus);

    private void Refresh()
    {
        _pairPanel.Visibility = _host.Running ? Visibility.Visible : Visibility.Collapsed;
        _errorLine.Text = _host.LastError ?? "";
        _errorLine.Visibility = _host.LastError is null ? Visibility.Collapsed : Visibility.Visible;
        _enable.IsChecked = _host.Running;

        if (_host.PairingUrl is { } url)
        {
            _urlBox.Text = url;
            _qrImage.Source = QrBitmap.Render(QrEncoder.Encode(url));
        }
        UpdateStatus();
    }

    private void UpdateStatus() =>
        _statusLine.Text = _host.ClientCount switch
        {
            0 => "No device connected yet.",
            1 => "1 device connected.",
            var n => $"{n} devices connected.",
        };

    private static TextBlock Dim(string text)
    {
        var tb = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        return tb;
    }
}

/// <summary>QR module matrix → a crisp WPF bitmap: black on white (scanners want
/// contrast, not theming) with the spec's 4-module quiet zone baked in.</summary>
internal static class QrBitmap
{
    public static BitmapSource Render(bool[,] modules, int quietZone = 4)
    {
        var n = modules.GetLength(0);
        var size = n + quietZone * 2;
        var stride = (size + 7) / 8;
        var pixels = new byte[stride * size];
        // BlackWhite format: 1 = white. Start all white, punch the dark modules.
        Array.Fill(pixels, (byte)0xFF);
        for (var r = 0; r < n; r++)
            for (var c = 0; c < n; c++)
            {
                if (!modules[r, c]) continue;
                var x = c + quietZone;
                var y = r + quietZone;
                pixels[y * stride + x / 8] &= (byte)~(0x80 >> (x % 8));
            }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.BlackWhite, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }
}
