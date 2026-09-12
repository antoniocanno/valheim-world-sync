using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Core.Synchronization;
using Xunit;
namespace ValheimWorldSync.Tests;

public sealed class GameTests
{
    [Fact]
    public async Task DoesNotLaunchOverAnExistingProcess()
    {
        var platform = new Platform { Processes = [new(1, DateTime.UtcNow)] };
        await Assert.ThrowsAsync<IOException>(() => new PollingGameSession(platform).LaunchAsync(TestContext.Current.CancellationToken));
        Assert.False(platform.Opened);
    }
    [Fact]
    public async Task TracksGameProcessInsteadOfSteamLauncher()
    {
        var platform = new Platform();
        var session = new PollingGameSession(platform);
        var process = await session.LaunchAsync(TestContext.Current.CancellationToken);
        Assert.True(platform.Opened);
        Assert.Equal(42, process.ProcessId);
        platform.Processes = [];
        await session.WaitForExitAsync(process, TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task ReusedPidDoesNotBecomeOriginalSession()
    {
        var identity = new GameIdentity(42, DateTime.UtcNow);
        var platform = new Platform { Processes = [identity with { StartTimeUtc = identity.StartTimeUtc.AddSeconds(1) }] };
        await new PollingGameSession(platform).WaitForExitAsync(identity, TestContext.Current.CancellationToken);
    }
    private sealed class Platform : IGamePlatform
    {
        public IReadOnlyList<GameIdentity> Processes { get; set; } = [];
        public bool Opened { get; private set; }
        public IReadOnlyList<GameIdentity> FindProcesses() => Processes;
        public void OpenSteam() { Opened = true; Processes = [new(42, DateTime.UtcNow)]; }
    }
}
