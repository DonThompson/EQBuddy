using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia.Tests;

/// <summary>
/// The Options window on Linux, rendered headlessly.
///
/// This is where the Avalonia port drifted furthest from WPF without anyone noticing: rules
/// had a boolean "sound on/off" toggle where Windows had a per-rule sound picker, so the
/// recommended way to use delayed alerts — two rules on one match, a quiet "heard it" and a
/// loud "cast now" — was silently useless on Linux. Nothing failed; the option simply wasn't
/// there. These tests assert the controls exist rather than trusting that they do.
/// </summary>
[Collection("avalonia")]
public class OptionsRenderTests : IDisposable
{
    private readonly string _profile =
        Directory.CreateTempSubdirectory("eqbuddy-options-").FullName;

    public OptionsRenderTests()
    {
        Environment.SetEnvironmentVariable("EQBUDDY_APPDATA", _profile);
        Directory.CreateDirectory(Path.Combine(_profile, "logs"));
        // A rule of each interesting shape, so the editor has something to draw.
        File.WriteAllText(Path.Combine(_profile, "settings.json"),
            $$"""
              {
                "LogFolder": {{System.Text.Json.JsonSerializer.Serialize(Path.Combine(_profile, "logs"))}},
                "TruncateLogs": false, "ShowTutorial": false, "Theme": "ParchmentBrass",
                "_comment": "DefaultRulesVersion is set so loading doesn't inject the built-in CC broke rule and change the rule count out from under these tests",
                "DefaultRulesVersion": 1,
                "TrackedRules": [
                  { "Name": "heard it", "Pattern": "CH -->", "Kind": 6, "Enabled": true,
                    "AlertBanner": true, "AlertSound": true, "AlertSoundName": "Ding" },
                  { "Name": "CAST NOW", "Pattern": "CH -->", "Kind": 6, "Enabled": true,
                    "AlertBanner": true, "AlertSound": true, "AlertSoundName": "Alarm",
                    "AlertDelaySeconds": 2.5 }
                ]
              }
              """);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("EQBUDDY_APPDATA", null);
        try { Directory.Delete(_profile, recursive: true); } catch { /* best effort */ }
    }

    private static (MainWindow Main, OptionsWindow Options) Open()
    {
        var main = new MainWindow();
        main.Show();
        var options = new OptionsWindow(main);
        options.Show();
        return (main, options);
    }

    [AvaloniaFact]
    public void OptionsRendersAFrame()
    {
        var (main, options) = Open();

        var frame = options.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.True(frame!.Size.Width > 100, $"Options rendered only {frame.Size.Width}px wide");
        options.Close();
        main.Close();
    }

    /// <summary>Each rule offers a real sound choice, not just on/off — and the two rules in
    /// the fixture keep their different sounds.</summary>
    [AvaloniaFact]
    public void EachRuleHasItsOwnSoundPicker()
    {
        var (main, options) = Open();

        var soundPickers = options.GetVisualDescendants().OfType<ComboBox>()
            .Where(c => c.Items.Contains(AlertSoundCatalog.CustomChoice))
            .ToList();

        Assert.Equal(2, soundPickers.Count);   // one per rule
        Assert.NotEqual(soundPickers[0].SelectedIndex, soundPickers[1].SelectedIndex);
        options.Close();
        main.Close();
    }

    /// <summary>The delay box is present and shows what was saved — the entry point for the
    /// cue feature.</summary>
    [AvaloniaFact]
    public void TheDelayBoxShowsTheSavedValue()
    {
        var (main, options) = Open();

        var texts = options.GetVisualDescendants().OfType<TextBox>()
            .Select(t => t.Text ?? "").ToList();

        Assert.Contains("2.5", texts);
        options.Close();
        main.Close();
    }

    /// <summary>Every watch-rule kind is offered here too — a kind that exists in Core but
    /// never reaches the Linux dropdown is unreachable for those users.</summary>
    [AvaloniaFact]
    public void EveryWatchKindIsOffered()
    {
        var (main, options) = Open();

        var kindPicker = options.GetVisualDescendants().OfType<ComboBox>()
            .First(c => c.Items.Contains(OptionsViewModel.KindNames[0]));

        Assert.Equal(Enum.GetValues<WatchKind>().Length, kindPicker.Items.Count);
        options.Close();
        main.Close();
    }
}
