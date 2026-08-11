using System.Globalization;
using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The Custom theme derives most of its palette keys from three user colors; these
/// tests hold it to the same guarantees ThemePaletteTests gives the built-ins — full
/// key coverage, parseable values, and a text/background contrast floor the user
/// cannot break, however hostile their color picks.</summary>
public class CustomThemeTests
{
    [Fact]
    public void DerivesEveryKeyInPaletteOrder() =>
        Assert.Equal(ThemePalettes.Keys,
            CustomTheme.Derive("#1A1A1A", "#EAEAEA", "#E3B341").Select(e => e.Key));

    [Theory]
    [InlineData("#1A1A1A", "#EAEAEA", "#E3B341")]
    [InlineData("#FDF6E3", "#000000", "#268BD2")]
    [InlineData("#FF00FF", "#00FF00", "#0000FF")]
    public void EveryDerivedValueIsParseableArgbHex(string bg, string text, string accent)
    {
        foreach (var (key, hex) in CustomTheme.Derive(bg, text, accent))
        {
            Assert.True(hex.Length == 9 && hex[0] == '#', $"{key} = '{hex}'");
            Assert.True(uint.TryParse(hex[1..], NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out _), $"{key} = '{hex}' is not hex");
        }
    }

    [Theory]
    [InlineData("#1A1A1A", "#1A1A1A")]   // text identical to background
    [InlineData("#808080", "#8A8A8A")]   // grey-on-grey — the field complaint
    [InlineData("#FFFFFF", "#FFFFFF")]   // white on white
    [InlineData("#000000", "#000000")]   // black on black
    public void HostileTextChoicesAreCorrectedToTheContrastFloor(string bg, string text)
    {
        var palette = CustomTheme.Derive(bg, text, "#E3B341").ToDictionary(e => e.Key, e => e.Hex);
        Assert.True(Contrast(palette["TextBrush"], "#FF" + bg[1..]) >= 4.5,
            $"text {palette["TextBrush"]} on {bg} is below 4.5:1");
    }

    [Fact]
    public void PanelTintFollowsBackgroundLuminance()
    {
        Assert.Equal("#26FFFFFF",
            CustomTheme.Derive("#101010", "#EEEEEE", "#E3B341").First(e => e.Key == "PanelBrush").Hex);
        Assert.Equal("#14000000",
            CustomTheme.Derive("#F5F0E0", "#101010", "#E3B341").First(e => e.Key == "PanelBrush").Hex);
    }

    [Fact]
    public void SeedRowMatchesDerivedDefaults() =>
        Assert.Equal(
            CustomTheme.Derive(CustomTheme.DefaultBg, CustomTheme.DefaultText, CustomTheme.DefaultAccent)
                .Select(e => e.Hex),
            CustomTheme.SeedRow);

    [Fact]
    public void ValidAcceptsRgbAndArgbRejectsJunk()
    {
        Assert.Equal("#1A2B3C", CustomTheme.Valid("#1a2b3c"));
        Assert.Equal("#1A2B3C", CustomTheme.Valid("#FF1A2B3C"));   // pasted from settings.json
        Assert.Equal("#1A2B3C", CustomTheme.Valid(" #1A2B3C "));
        Assert.Null(CustomTheme.Valid("1A2B3C"));
        Assert.Null(CustomTheme.Valid("#1A2B"));
        Assert.Null(CustomTheme.Valid("#GGGGGG"));
        Assert.Null(CustomTheme.Valid(null));
        Assert.Null(CustomTheme.Valid("red"));
    }

    [Fact]
    public void PaletteForUsesCatalogThemesUnlessCustomIsSelected()
    {
        var settings = new AppSettings { Theme = "Grey" };
        Assert.Equal(ThemePalettes.For("Grey"), CustomTheme.PaletteFor(settings));

        settings.Theme = CustomTheme.Key;
        settings.CustomThemeBg = "#002B36";
        settings.CustomThemeText = "not-a-color";   // falls back to the seed text alone
        var palette = CustomTheme.PaletteFor(settings).ToDictionary(e => e.Key, e => e.Hex);
        Assert.Equal("#F2002B36", palette["BgBrush"]);
        Assert.Equal(ThemePalettes.Keys.Length, palette.Count);
    }

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(string hex)
    {
        var v = uint.Parse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double Channel(int shift)
        {
            var c = ((v >> shift) & 0xFF) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(16) + 0.7152 * Channel(8) + 0.0722 * Channel(0);
    }
}
