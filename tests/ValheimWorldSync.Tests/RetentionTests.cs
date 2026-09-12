using ValheimWorldSync.Core.Synchronization;
using Xunit;
namespace ValheimWorldSync.Tests;

public sealed class RetentionTests
{
    [Fact]
    public async Task KeepsCurrentAndNPreviousVersions()
    {
        var repo = new MemoryRepository();
        var lease = new LeaseCoordinator(repo, "world", "A", "a");
        await lease.AcquireAsync("a");
        for (var n = 0; n < 5; n++)
        {
            var version = LeaseTests.Version(n.ToString());
            repo.Objects[version.Key] = [];
            await lease.PublishAsync("a", n == 0 ? null : (n - 1).ToString(), version);
        }
        await new BackupRetention(repo, lease).PruneAsync("a", 2);
        var manifest = (await repo.ReadAsync())!.Manifest;
        Assert.Equal("4", manifest.Current!.Id);
        Assert.Equal(new[] { "3", "2" }, manifest.History.Select(v => v.Id));
        Assert.Empty(manifest.PendingDeletes);
        Assert.Equal(3, repo.Objects.Count);
        Assert.Contains(manifest.Current.Key, repo.Objects.Keys);
    }
    [Fact]
    public async Task FailedDeletionRemainsQueuedAndCanBeRetriedByNextOwner()
    {
        var repo = new MemoryRepository();
        var lease = new LeaseCoordinator(repo, "world", "A", "a");
        await lease.AcquireAsync("a");
        await lease.PublishAsync("a", null, LeaseTests.Version("one"));
        await lease.PublishAsync("a", "one", LeaseTests.Version("two"));
        repo.FailDelete = true;
        await Assert.ThrowsAsync<IOException>(() => new BackupRetention(repo, lease).PruneAsync("a", 0));
        Assert.Equal("two", (await repo.ReadAsync())!.Manifest.Current!.Id);
        Assert.Single((await repo.ReadAsync())!.Manifest.PendingDeletes);
        await lease.ReleaseAsync("a");
        repo.FailDelete = false;
        var next = new LeaseCoordinator(repo, "world", "B", "b");
        await next.AcquireAsync("b");
        await new BackupRetention(repo, next).PruneAsync("b", 0);
        Assert.Empty((await repo.ReadAsync())!.Manifest.PendingDeletes);
    }
}
