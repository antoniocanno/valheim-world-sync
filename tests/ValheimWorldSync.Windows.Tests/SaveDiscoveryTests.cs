using ValheimWorldSync.Platform.Windows.Game;
using Xunit;
namespace ValheimWorldSync.Windows.Tests;
public sealed class SaveDiscoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vws-saves-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void WarnsWhenOnlyCloudIndicatorsExist()
    {
        Directory.CreateDirectory(Path.Combine(root, "worlds"));
        File.WriteAllText(Path.Combine(root, "worlds", "steam_autocloud.vdf"), "");
        Assert.True(ValheimSaveDiscovery.Detect(root).PossibleSteamCloud);

        Directory.CreateDirectory(Path.Combine(root, "worlds_local", "Midgard"));
        Assert.False(ValheimSaveDiscovery.Detect(root).PossibleSteamCloud);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
