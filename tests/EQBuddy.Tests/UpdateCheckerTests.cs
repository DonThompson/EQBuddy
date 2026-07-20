using System.Security.Cryptography;
using EQBuddy.Core;

namespace EQBuddy.Tests;

public class UpdateCheckerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eqbuddy-upd-").FullName;
    private string SetupPath => Path.Combine(_dir, "EQBuddySetup.exe");

    public UpdateCheckerTests() => File.WriteAllBytes(SetupPath, [1, 2, 3, 4, 5]);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private UpdateInfo Info => new(new Version(9, 9, 9), SetupPath);

    [Fact]
    public void StagesWithoutHashFile()
    {
        var staged = UpdateChecker.StageForInstall(Info);
        Assert.True(File.Exists(staged));
    }

    [Fact]
    public void StagesWhenHashMatches()
    {
        using var s = File.OpenRead(SetupPath);
        File.WriteAllText(SetupPath + ".sha256", Convert.ToHexString(SHA256.HashData(s)));
        var staged = UpdateChecker.StageForInstall(Info);
        Assert.True(File.Exists(staged));
    }

    [Fact]
    public void RefusesWhenHashMismatches()
    {
        File.WriteAllText(SetupPath + ".sha256", new string('A', 64));
        Assert.Throws<InvalidOperationException>(() => UpdateChecker.StageForInstall(Info));
    }

    [Fact]
    public void WebUpdateCannotBeStaged() =>
        Assert.Throws<InvalidOperationException>(() =>
            UpdateChecker.StageForInstall(new UpdateInfo(new Version(9, 9, 9), null)));
}
