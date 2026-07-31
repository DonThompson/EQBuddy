using Avalonia;
using Avalonia.Headless;
using EQBuddy.Avalonia.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace EQBuddy.Avalonia.Tests;

/// <summary>
/// Boots the real <see cref="App"/> on Avalonia's headless platform, so these tests exercise
/// the same application the Linux build ships rather than a stand-in.
///
/// <c>UseHeadlessDrawing = false</c> is the important part: with the default no-op drawing
/// backend a window "renders" without producing pixels, which would make these tests pass on
/// a UI that draws nothing. Skia does the real work and frames can be captured.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
