using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Core.Synchronization;
using ValheimWorldSync.Infrastructure.Recovery;
using ValheimWorldSync.Infrastructure.WorldFiles;
using Xunit;
namespace ValheimWorldSync.Tests;

public sealed class EngineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vws-engine-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTime time = new();
    private readonly MemoryRepository repo = new();
    private readonly TestGame game = new();
    private readonly FileSessionJournal journal;
    private readonly WorldArchive archive;
    private readonly SyncEngine engine;
    private readonly string world;
    public EngineTests()
    {
        world = Path.Combine(root, "world");
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "chunk"), "initial");
        journal = new(Path.Combine(root, "app"));
        archive = new(Path.Combine(root, "app"), "test");
        engine = new(repo, archive, journal, game, new("world", world, "test", Path.Combine(root, "app"), "A", "a"), time);
    }
    [Fact]
    public async Task ImportPlayAndClosePublishesAutomatically()
    {
        await engine.ImportAsync(world);
        var first = (await repo.ReadAsync())!.Manifest.Current!;
        game.OnExit = () => File.WriteAllTextAsync(Path.Combine(world, "chunk"), "progress");
        await engine.PlayAsync();
        var last = (await repo.ReadAsync())!.Manifest;
        Assert.Equal(SyncState.Idle, engine.Status.State);
        Assert.NotEqual(first.Id, last.Current!.Id);
        Assert.Null(last.Lease);
        Assert.Null(await journal.ReadAsync());
        Assert.Single(last.History);
    }
    [Fact]
    public async Task FailedUploadPersistsSnapshotAndResumePublishesSameVersion()
    {
        repo.FailUpload = true;
        await engine.ImportAsync(world);
        Assert.Equal(SyncState.Pending, engine.Status.State);
        var pending = await journal.ReadAsync();
        Assert.NotNull(pending!.Snapshot);
        var versionId = pending.Snapshot.Version.Id;
        repo.FailUpload = false;
        await engine.RecoverAsync();
        Assert.Equal(versionId, (await repo.ReadAsync())!.Manifest.Current!.Id);
        Assert.Null(await journal.ReadAsync());
    }
    [Fact]
    public async Task RemoteAdvanceDuringGamePreservesConflictWithoutOverwriting()
    {
        await engine.ImportAsync(world);
        game.OnExit = async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(world, "chunk"), "local progress");
            repo.UtcNow += TimeSpan.FromSeconds(183);
            var other = new LeaseCoordinator(repo, "world", "B", "b", "Midgard", "Midgard", 10);
            var manifest = await other.AcquireAsync("b");
            await other.PublishAsync("b", manifest.Current!.Id, LeaseTests.Version("other"));
            await other.ReleaseAsync("b");
        };
        await engine.PlayAsync();
        Assert.Equal(SyncState.Conflict, engine.Status.State);
        Assert.Equal("other", (await repo.ReadAsync())!.Manifest.Current!.Id);
        Assert.True(File.Exists((await journal.ReadAsync())!.Snapshot!.Path));
    }
    [Fact]
    public async Task UnchangedSessionDoesNotPublishAnotherVersion()
    {
        await engine.ImportAsync(world);
        var first = (await repo.ReadAsync())!.Manifest.Current!.Id;
        await engine.PlayAsync();
        Assert.Equal(first, (await repo.ReadAsync())!.Manifest.Current!.Id);
        Assert.Empty((await repo.ReadAsync())!.Manifest.History);
    }
    [Fact]
    public async Task ExternalGameBlocksImportAndNeverPublishes()
    {
        game.IsRunning = true;
        await engine.ImportAsync(world);
        Assert.Null(await repo.ReadAsync());
        Assert.Empty(repo.Objects);
    }
    [Fact]
    public async Task UncertainLaunchIsNotAutomaticallyAdopted()
    {
        await journal.WriteAsync(new()
        {
            SessionId = "session",
            WorldId = "world",
            WorldPath = world,
            RepositoryIdentity = "test",
            Stage = SessionStage.Launching
        });
        await engine.RecoverAsync();
        Assert.Equal(SyncState.Conflict, engine.Status.State);
        Assert.Empty(repo.Objects);
    }
    [Fact]
    public async Task HeartbeatContinuesDuringUploadAfterGameExit()
    {
        await engine.ImportAsync(world);
        var uploading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowUpload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        game.OnExit = () => File.WriteAllTextAsync(Path.Combine(world, "chunk"), "new progress");
        repo.BeforeUpload = async () => { uploading.TrySetResult(); await allowUpload.Task; };
        var playing = engine.PlayAsync(TestContext.Current.CancellationToken);
        await uploading.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(game.IsRunning);
        var originalExpiry = (await repo.ReadAsync())!.Manifest.Lease!.ExpiresAt;
        repo.Written = m => { if (m.Lease?.ExpiresAt > originalExpiry) renewed.TrySetResult(); };
        repo.UtcNow += TimeSpan.FromSeconds(60);
        time.Advance(TimeSpan.FromSeconds(60));
        try { await renewed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
        finally { allowUpload.TrySetResult(); }
        await playing;
        Assert.Equal(SyncState.Idle, engine.Status.State);
    }
    [Fact]
    public async Task ExpiredLeaseWithSameBaseCanRecoverProgress()
    {
        await engine.ImportAsync(world);
        game.OnExit = async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(world, "chunk"), "offline progress");
            repo.UtcNow += TimeSpan.FromSeconds(200);
        };
        await engine.PlayAsync();
        Assert.Equal(SyncState.Idle, engine.Status.State);
        Assert.Null(await journal.ReadAsync());
        Assert.Single((await repo.ReadAsync())!.Manifest.History);
    }
    [Fact]
    public async Task CancelledSessionRetainsJournalForRecovery()
    {
        await engine.ImportAsync(world);
        using var cancel = new CancellationTokenSource();
        game.OnExit = () => { cancel.Cancel(); return Task.CompletedTask; };
        await engine.PlayAsync(cancel.Token);
        Assert.NotNull(await journal.ReadAsync());
        game.OnExit = null;
        await engine.RecoverAsync();
        Assert.Null(await journal.ReadAsync());
    }
    [Fact]
    public async Task ImportCopiesExternalSourceToConfiguredGameFolder()
    {
        Directory.Delete(world, true);
        var downloads = Path.Combine(root, "downloads", "ImportedWorld");
        Directory.CreateDirectory(downloads);
        await File.WriteAllTextAsync(Path.Combine(downloads, "chunk"), "downloaded save");

        await engine.ImportAsync(downloads);

        Assert.Equal("downloaded save", await File.ReadAllTextAsync(Path.Combine(world, "chunk")));
        Assert.Equal("downloaded save", await File.ReadAllTextAsync(Path.Combine(downloads, "chunk")));
        Assert.NotNull((await repo.ReadAsync())!.Manifest.Current);
    }
    [Fact]
    public async Task ImportRejectsSelectingSavesRootInsteadOfOneWorld()
    {
        var savesRoot = Path.GetDirectoryName(world)!;

        await engine.ImportAsync(savesRoot);

        Assert.Equal(SyncState.Error, engine.Status.State);
        Assert.Null(await repo.ReadAsync());
    }
    [Fact]
    public async Task InterruptedCaptureRequiresManualResolution()
    {
        await engine.ImportAsync(world);
        await journal.WriteAsync(new()
        {
            SessionId = "session",
            WorldId = "world",
            WorldPath = world,
            RepositoryIdentity = "test",
            Stage = SessionStage.SnapshotPending,
            BaseVersion = (await repo.ReadAsync())!.Manifest.Current
        });

        await engine.RecoverAsync();

        Assert.Equal(SyncState.Conflict, engine.Status.State);
        Assert.Equal(SessionStage.Conflict, (await journal.ReadAsync())!.Stage);
    }
    [Fact]
    public async Task ChangesAfterSnapshotPreventPublication()
    {
        await engine.ImportAsync(world);
        var original = (await repo.ReadAsync())!.Manifest.Current!.Id;
        game.OnExit = () => File.WriteAllTextAsync(Path.Combine(world, "chunk"), "tracked progress");
        repo.BeforeUpload = () => File.WriteAllTextAsync(Path.Combine(world, "chunk"), "external progress");

        await engine.PlayAsync();

        Assert.Equal(SyncState.Conflict, engine.Status.State);
        Assert.Equal(original, (await repo.ReadAsync())!.Manifest.Current!.Id);
        Assert.Equal("external progress", await File.ReadAllTextAsync(Path.Combine(world, "chunk")));
        Assert.True(File.Exists((await journal.ReadAsync())!.Snapshot!.Path));
    }
    [Fact]
    public async Task RemoteResetPreservesPreviousVersionAndPublishesNewSource()
    {
        await engine.ImportAsync(world);
        var previous = (await repo.ReadAsync())!.Manifest.Current!;
        var replacement = Path.Combine(root, "replacement"); Directory.CreateDirectory(replacement);
        await File.WriteAllTextAsync(Path.Combine(replacement, "chunk"), "replacement");

        await engine.ResetRemoteAsync(replacement);

        var manifest = (await repo.ReadAsync())!.Manifest;
        Assert.NotEqual(previous.Id, manifest.Current!.Id);
        Assert.Contains(manifest.History, version => version.Id == previous.Id);
        Assert.Null(manifest.Lease);
        Assert.True(Directory.GetFiles(Path.Combine(root, "app", "recovery"), "*.json").Length >= 2);
    }

    public void Dispose() => ArchiveTests.DeleteEventually(root);
    private sealed class TestGame : IGameSession
    {
        public bool IsRunning { get; set; }
        public Func<Task>? OnExit { get; set; }
        public Task<GameIdentity> LaunchAsync(CancellationToken token = default)
        { IsRunning = true; return Task.FromResult(new GameIdentity(1, DateTime.UtcNow)); }
        public async Task WaitForExitAsync(GameIdentity game, CancellationToken token = default)
        { if (OnExit is not null) await OnExit(); IsRunning = false; }
    }
}
