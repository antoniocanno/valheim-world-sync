using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Core.Synchronization;
using Xunit;
namespace ValheimWorldSync.Tests;

public sealed class LeaseTests
{
    internal static WorldVersion Version(string id) => new(id, $"backups/{id}.zip", new('A',64), new('B',64), 100, DateTimeOffset.UtcNow);
    [Fact]
    public async Task OnlyOneCompetingPlayerAcquires()
    {
        var repo = new MemoryRepository();
        async Task<bool> Acquire(string id)
        {
            try { await new LeaseCoordinator(repo, "world", id, id).AcquireAsync(id); return true; }
            catch (WorldBusyException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Acquire("a")), Task.Run(() => Acquire("b")));
        Assert.Single(results.Where(x => x));
    }
    [Fact]
    public async Task ExpiredOwnerCannotPublishRenewOrReleaseNewOwner()
    {
        var repo = new MemoryRepository();
        var a = new LeaseCoordinator(repo, "world", "A", "a");
        var b = new LeaseCoordinator(repo, "world", "B", "b");
        await a.AcquireAsync("a");
        repo.UtcNow += TimeSpan.FromSeconds(183);
        await b.AcquireAsync("b");
        await Assert.ThrowsAsync<LeaseLostException>(() => a.RenewAsync("a"));
        await Assert.ThrowsAsync<LeaseLostException>(() => a.PublishAsync("a", null, Version("old")));
        await a.ReleaseAsync("a");
        Assert.Equal("b", (await repo.ReadAsync())!.Manifest.Lease!.SessionId);
        await b.PublishAsync("b", null, Version("new"));
        Assert.Equal("new", (await repo.ReadAsync())!.Manifest.Current!.Id);
    }
    [Fact]
    public async Task PublicationChecksBaseVersionAndPreservesHistory()
    {
        var repo = new MemoryRepository();
        var lease = new LeaseCoordinator(repo, "world", "A", "a");
        await lease.AcquireAsync("a");
        await lease.PublishAsync("a", null, Version("one"));
        await Assert.ThrowsAsync<WorldConflictException>(() => lease.PublishAsync("a", null, Version("two")));
        await lease.PublishAsync("a", "one", Version("two"));
        await lease.PublishAsync("a", "one", Version("two")); // response reconciliation
        await lease.ReleaseAsync("a");
        var m = (await repo.ReadAsync())!.Manifest;
        Assert.Equal("two", m.Current!.Id);
        Assert.Single(m.History);
        Assert.Null(m.Lease);
    }
    [Fact]
    public async Task StaleETagCannotReplaceManifest()
    {
        var repo = new MemoryRepository();
        var initial = await repo.TryWriteAsync(new WorldManifest { WorldId = "world" }, null);
        await repo.TryWriteAsync(initial!.Manifest with { Revision = "new" }, initial.ETag);
        Assert.Null(await repo.TryWriteAsync(initial.Manifest, initial.ETag));
    }
    [Fact]
    public void ManifestRejectsDeletingReferencedObjects()
    {
        var version = Version("one");
        var manifest = new WorldManifest { WorldId = "world", Current = version, PendingDeletes = [version.Key] };
        Assert.Throws<InvalidDataException>(() => manifest.Validate("world"));
    }
}
