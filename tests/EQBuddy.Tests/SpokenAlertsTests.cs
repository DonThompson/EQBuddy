using System.Text.Json;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

/// <summary>
/// The voice-control logic that runs without SAPI: phrase resolution, the picker's
/// index mapping (fed fake voice lists — enumerating real ones instantiates SAPI),
/// clamping, and the settings round trip. Actual synthesis stays untested on purpose:
/// SpokenAlerts guards every hardware call behind the platform voice's lazy creation,
/// which none of this triggers.
/// </summary>
public sealed class SpokenAlertsTests
{
    private static readonly string[] Voices =
        ["Microsoft David Desktop", "Microsoft Zira Desktop"];

    private static (OptionsViewModel Vm, AppSettings Settings, Counter Persists) Create(AppSettings? settings = null)
    {
        var s = settings ?? new AppSettings();
        var counter = new Counter();
        return (new OptionsViewModel(s, () => counter.Value++), s, counter);
    }

    private sealed class Counter { public int Value; }

    // ---- phrase resolution ----

    [Fact]
    public void EmptyPhraseSpeaksTheAutoLabel()
    {
        Assert.Equal("Rusty Sword ×3", SpokenAlerts.ResolvePhrase("", "Rusty Sword ×3"));
        Assert.Equal("Rusty Sword ×3", SpokenAlerts.ResolvePhrase("   ", "Rusty Sword ×3"));
    }

    [Fact]
    public void CustomPhraseWinsAndIsTrimmed()
    {
        Assert.Equal("Recast charm now",
            SpokenAlerts.ResolvePhrase("  Recast charm now  ", "Befriend Animal faded off a bear"));
    }

    // ---- voice picker mapping ----

    [Fact]
    public void VoiceChoicesPutTheDefaultFirst()
    {
        var choices = OptionsViewModel.VoiceChoices(Voices);
        Assert.Equal(OptionsViewModel.DefaultVoiceChoice, choices[0]);
        Assert.Equal(Voices.Length + 1, choices.Length);
    }

    [Fact]
    public void SelectVoiceRoundTripsThroughTheSetting()
    {
        var (vm, s, persists) = Create();
        Assert.Equal(0, vm.VoiceIndex(Voices));   // fresh install = system default

        vm.SelectVoice(Voices, 2);
        Assert.Equal("Microsoft Zira Desktop", s.SpeechVoice);
        Assert.Equal(2, vm.VoiceIndex(Voices));

        vm.SelectVoice(Voices, 0);
        Assert.Equal("", s.SpeechVoice);
        Assert.Equal(2, persists.Value);
    }

    /// <summary>A voice from another machine (or an uninstalled language pack) shows as
    /// the default — the same fallback SpokenAlerts applies at speak time, so the picker
    /// never claims a voice the alerts won't actually use.</summary>
    [Fact]
    public void UninstalledVoiceFallsBackToDefault()
    {
        var (vm, _, _) = Create(new AppSettings { SpeechVoice = "Microsoft Hazel Desktop" });
        Assert.Equal(0, vm.VoiceIndex(Voices));
    }

    // ---- rate & volume ----

    [Fact]
    public void RateClampsHandEditedSettingsOnReadAndWrite()
    {
        var (vm, s, _) = Create(new AppSettings { SpeechRate = 40 });
        Assert.Equal(SpokenAlerts.MaxRate, vm.SpeechRate);   // read: shown as it will speak

        vm.SpeechRate = -40;
        Assert.Equal(SpokenAlerts.MinRate, s.SpeechRate);    // write: stored clamped
        Assert.Equal($"{SpokenAlerts.MinRate}", vm.SpeechRateLabel);

        vm.SpeechRate = 0;
        Assert.Equal("normal", vm.SpeechRateLabel);
        vm.SpeechRate = 3;
        Assert.Equal("+3", vm.SpeechRateLabel);
    }

    [Fact]
    public void VolumeClampsToSapiRange()
    {
        var (vm, s, persists) = Create();
        vm.SpeechVolume = 250;
        Assert.Equal(100, s.SpeechVolume);
        vm.SpeechVolume = -5;
        Assert.Equal(0, s.SpeechVolume);
        Assert.Equal("0%", vm.SpeechVolumeLabel);
        Assert.Equal(2, persists.Value);
    }

    // ---- settings round trip ----

    // AppSettings' own serializer options: NaN window positions are legitimate values.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    [Fact]
    public void SpeechSettingsSurviveSerialization()
    {
        var settings = new AppSettings
        {
            SpeechVoice = "Microsoft Zira Desktop",
            SpeechRate = -2,
            SpeechVolume = 60,
        };
        settings.TrackedRules.Add(new TrackedRule
        {
            Name = "Charm broke",
            AlertSpeech = true,
            SpokenPhrase = "Recast charm now",
        });

        var reloaded = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, JsonOpts), JsonOpts)!;
        Assert.Equal("Microsoft Zira Desktop", reloaded.SpeechVoice);
        Assert.Equal(-2, reloaded.SpeechRate);
        Assert.Equal(60, reloaded.SpeechVolume);
        Assert.Equal("Recast charm now", reloaded.TrackedRules.Single().SpokenPhrase);
    }

    /// <summary>settings.json from before the feature: defaults reproduce the old
    /// behavior exactly — system voice, normal pace, full volume, label spoken as-is.</summary>
    [Fact]
    public void PreVoiceControlSettingsGetTheOldBehavior()
    {
        var reloaded = JsonSerializer.Deserialize<AppSettings>(
            """{"AlertVolume":0.8,"TrackedRules":[{"Name":"CC broke","AlertSpeech":true}]}""",
            JsonOpts)!;
        Assert.Equal("", reloaded.SpeechVoice);
        Assert.Equal(0, reloaded.SpeechRate);
        Assert.Equal(100, reloaded.SpeechVolume);
        Assert.Equal("", reloaded.TrackedRules.Single().SpokenPhrase);
    }
}
