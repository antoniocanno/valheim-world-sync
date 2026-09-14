using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Core.Models;
namespace ValheimWorldSync.Core.Synchronization;

public sealed record SyncOptions(string WorldId, string WorldPath, string RepositoryIdentity, string DataRoot, string Player,
    string InstallationId, int BackupCount = 10, string? WorldDisplayName = null, string? WorldFolderName = null);

public sealed class SyncEngine
{
    private readonly IWorldRepository repository;
    private readonly IWorldArchive archive;
    private readonly ISessionJournal journal;
    private readonly IGameSession game;
    private readonly SyncOptions options;
    private readonly TimeProvider time;
    private readonly LeaseCoordinator lease;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    public bool IsBusy { get; private set; }
    public SyncStatus Status { get; private set; } = new(SyncState.Idle, Strings.Get("Sync_Initial"));
    public event Action<SyncStatus>? StatusChanged;
    public event Action<TransferProgress>? TransferProgressChanged;
    public SyncEngine(IWorldRepository repository, IWorldArchive archive, ISessionJournal journal, IGameSession game, SyncOptions options, TimeProvider? time = null)
    {
        this.repository = repository; this.archive = archive; this.journal = journal; this.game = game; this.options = options;
        this.time = time ?? TimeProvider.System;
        var folder = options.WorldFolderName ?? Path.GetFileName(Path.GetFullPath(options.WorldPath));
        var display = options.WorldDisplayName ?? folder;
        lease = new(repository, options.WorldId, options.Player, options.InstallationId, display, folder, options.BackupCount);
    }

    public Task PlayAsync(CancellationToken token = default) => Guard(async () =>
    {
        if (await journal.ReadAsync(token) is not null) throw new InvalidDataException(Strings.Get("Sync_ResolveBeforePlay"));
        EnsureGameClosed();
        await archive.RecoverInstallAsync(options.WorldPath, () => game.IsRunning, token);
        Set(SyncState.Checking, Strings.Get("Sync_Checking"));
        var sessionId = Guid.NewGuid().ToString("N");
        Set(SyncState.Acquiring, Strings.Get("Sync_Acquiring"));
        var manifest = await lease.AcquireAsync(sessionId, token);
        if (manifest.Current is null)
        {
            await lease.ReleaseAsync(sessionId, token);
            throw new InvalidDataException(Strings.Get("Sync_NotPublished"));
        }
        var session = NewSession(sessionId, manifest.Current, false);
        await journal.WriteAsync(session, token);
        await WithHeartbeat(sessionId, async () =>
        {
            Set(SyncState.Downloading, Strings.Get("Sync_Downloading"));
            var downloadRoot = Path.Combine(options.DataRoot, "downloads");
            Directory.CreateDirectory(downloadRoot);
            var download = Path.Combine(downloadRoot, sessionId + ".zip");
            await repository.DownloadAsync(manifest.Current, download, token, new CallbackProgress<TransferProgress>(p => TransferProgressChanged?.Invoke(p)));
            Set(SyncState.Preparing, Strings.Get("Sync_Preparing"));
            await archive.InstallAsync(manifest.Current, download, options.WorldPath, () => game.IsRunning, token);
            await lease.RenewAsync(sessionId, token); // Do not launch after losing ownership during download.
            EnsureGameClosed();
            session = session with { Stage = SessionStage.Launching };
            await journal.WriteAsync(session, token);
            Set(SyncState.Launching, Strings.Get("Sync_Launching"));
            var identity = await game.LaunchAsync(token);
            session = session with { Stage = SessionStage.Playing, Game = identity };
            await journal.WriteAsync(session, token);
            Set(SyncState.Playing, Strings.Get("Sync_Playing"));
            await game.WaitForExitAsync(identity, token);
            await CaptureAndPublish(session, token);
        }, token);
    }, token);

    public Task ImportAsync(string sourceWorldPath, CancellationToken token = default) => Guard(async () =>
    {
        EnsureGameClosed();
        sourceWorldPath = Path.GetFullPath(sourceWorldPath);
        var destinationWorldPath = Path.GetFullPath(options.WorldPath);
        if (!string.Equals(sourceWorldPath.TrimEnd(Path.DirectorySeparatorChar),
                destinationWorldPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
            (IsAncestorOf(sourceWorldPath, destinationWorldPath) || IsAncestorOf(destinationWorldPath, sourceWorldPath)))
            throw new InvalidDataException(Strings.Get("Sync_SingleFolder"));
        if (await journal.ReadAsync(token) is not null) throw new InvalidDataException(Strings.Get("Sync_ResolveBeforeImport"));
        await archive.RecoverInstallAsync(options.WorldPath, () => game.IsRunning, token);
        var sessionId = Guid.NewGuid().ToString("N");
        Set(SyncState.Acquiring, Strings.Get("Sync_AcquiringFirstImport"));
        var manifest = await lease.AcquireAsync(sessionId, token);
        if (manifest.Current is not null)
        {
            await lease.ReleaseAsync(sessionId, token);
            throw new InvalidDataException(Strings.Get("Sync_BucketHasWorld"));
        }
        var session = NewSession(sessionId, null, true);
        await journal.WriteAsync(session, token);
        await WithHeartbeat(sessionId, async () =>
        {
            Set(SyncState.LocalBackup, Strings.Get("Sync_CopyingSource"));
            var snapshot = await archive.CreateAsync(sourceWorldPath, token);
            if (!string.Equals(sourceWorldPath.TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(options.WorldPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                await archive.InstallAsync(snapshot.Version, snapshot.Path, options.WorldPath, () => game.IsRunning, token);
            }
            session = session with { Stage = SessionStage.Ready, Snapshot = snapshot };
            await journal.WriteAsync(session, token);
            await PublishPending(session, token);
        }, token);
    }, token);

    public Task RecoverAsync(CancellationToken token = default) => Guard(async () =>
    {
        var session = await journal.ReadAsync(token);
        if (session is null)
        {
            await archive.RecoverInstallAsync(options.WorldPath, () => game.IsRunning, token);
            await RefreshCore(token);
            return;
        }
        ValidateSession(session);
        if (session.Stage == SessionStage.Conflict) { Set(SyncState.Conflict, Strings.Get("Sync_Diverged")); return; }
        if (session.Stage == SessionStage.Launching)
            throw new WorldConflictException(); // A crash may have occurred between launch and saving the PID.
        if (session.Stage == SessionStage.SnapshotPending)
            throw new WorldConflictException(); // We cannot prove another game did not write after capture was interrupted.
        if (session.Stage == SessionStage.Preparing)
        {
            EnsureGameClosed();
            await archive.RecoverInstallAsync(options.WorldPath, () => game.IsRunning, token);
            await lease.ReleaseAsync(session.SessionId, token);
            await journal.ClearAsync(token);
            Set(SyncState.Idle, Strings.Get("Sync_PreparingRecovered"));
            return;
        }
        await WithHeartbeat(session.SessionId, async () =>
        {
            if (session.Stage == SessionStage.Playing && session.Game is { } identity)
            {
                Set(SyncState.Playing, Strings.Get("Sync_Resuming"));
                await game.WaitForExitAsync(identity, token);
            }
            EnsureGameClosed();
            if (session.Stage == SessionStage.Ready && session.Snapshot is not null)
                await PublishPending(session, token);
            else await CaptureAndPublish(session, token);
        }, token);
    }, token);

    public Task RefreshAsync(CancellationToken token = default) => Guard(() => RefreshCore(token), token);

    public Task ResetRemoteAsync(string sourceWorldPath, CancellationToken token = default) => Guard(async () =>
    {
        EnsureGameClosed();
        if (await journal.ReadAsync(token) is not null) throw new InvalidDataException(Strings.Get("Sync_ResolveBeforeReset"));
        var sessionId = Guid.NewGuid().ToString("N");
        var manifest = await lease.AcquireAsync(sessionId, token);
        try
        {
            await WithHeartbeat(sessionId, async () =>
            {
                if (manifest.Current is { } current)
                {
                    var downloads = Path.Combine(options.DataRoot, "downloads"); Directory.CreateDirectory(downloads);
                    var oldPath = Path.Combine(downloads, "reset-" + current.Id + ".zip");
                    await repository.DownloadAsync(current, oldPath, token, new CallbackProgress<TransferProgress>(p => TransferProgressChanged?.Invoke(p)));
                    await archive.PreserveAsync(new(oldPath, current), current.CreatedBy ?? "desconhecido", "remoto-antes-de-reiniciar", token);
                }
                var snapshot = await archive.CreateAsync(Path.GetFullPath(sourceWorldPath), token);
                snapshot = snapshot with { Version = snapshot.Version with { CreatedBy = options.Player } };
                await archive.PreserveAsync(snapshot, options.Player, "fonte-da-reinicializacao", token);
                await repository.UploadAsync(snapshot.Version, snapshot.Path, token, new CallbackProgress<TransferProgress>(p => TransferProgressChanged?.Invoke(p)));
                await lease.RenewAsync(sessionId, token);
                await lease.PublishAsync(sessionId, manifest.Current?.Id, snapshot.Version, token);
                await new BackupRetention(repository, lease).PruneAsync(sessionId, options.BackupCount, token);
            }, token);
            Set(SyncState.Idle, Strings.Get("Sync_RemoteReset"));
        }
        finally { try { await lease.ReleaseAsync(sessionId, CancellationToken.None); } catch { } }
    }, token);

    // Called only after the UI explicitly confirms keeping a recovery snapshot and abandoning automatic publication.
    public Task KeepLocalAndUseCloudAsync(CancellationToken token = default) => Guard(async () =>
    {
        EnsureGameClosed();
        var session = await journal.ReadAsync(token);
        if (session is null) return;
        ValidateSession(session);
        await archive.RecoverInstallAsync(options.WorldPath, () => game.IsRunning, token);
        var snapshot = session.Snapshot ?? await archive.CreateAsync(options.WorldPath, token);
        await archive.VerifyAsync(snapshot, token);
        await archive.PreserveAsync(snapshot, options.Player, "conflito-local", token);
        await journal.WriteAsync(session with { Snapshot = snapshot, Stage = SessionStage.Conflict }, token);
        await lease.ReleaseAsync(session.SessionId, token);
        await journal.ClearAsync(token);
        Set(SyncState.Idle, Strings.Get("Sync_KeptLocal"));
    }, token);

    private async Task CaptureAndPublish(SessionRecord session, CancellationToken token)
    {
        EnsureSafeToCapture();
        session = session with { Stage = SessionStage.SnapshotPending };
        await journal.WriteAsync(session, token);
        Set(SyncState.LocalBackup, Strings.Get("Sync_Snapshotting"));
        var snapshot = await archive.CreateAsync(options.WorldPath, token);
        EnsureSafeToCapture();
        session = session with { Stage = SessionStage.Ready, Snapshot = snapshot };
        await journal.WriteAsync(session, token);
        await PublishPending(session, token);
    }
    private async Task PublishPending(SessionRecord session, CancellationToken token)
    {
        EnsureGameClosed();
        var snapshot = session.Snapshot ?? throw new InvalidDataException(Strings.Get("Sync_SnapshotMissing"));
        if (string.IsNullOrWhiteSpace(snapshot.Version.CreatedBy))
        {
            snapshot = snapshot with { Version = snapshot.Version with { CreatedBy = options.Player } };
            session = session with { Snapshot = snapshot };
            await journal.WriteAsync(session, token);
        }
        await archive.VerifyAsync(snapshot, token);
        await EnsureLocalMatchesSnapshot(snapshot, token);
        Set(SyncState.Acquiring, Strings.Get("Sync_ConfirmingOwnership"));
        var manifest = await lease.AcquireAsync(session.SessionId, token);
        if (manifest.Current?.Id == snapshot.Version.Id)
        {
            await Finish(session, token);
            return;
        }
        // No local changes: there is no branch to publish, even if another owner advanced the remote world.
        if (!session.IsImport && snapshot.Version.TreeHash == session.BaseVersion?.TreeHash)
        {
            await Finish(session, token);
            return;
        }
        if (manifest.Current?.Id != session.BaseVersion?.Id)
        {
            await lease.ReleaseAsync(session.SessionId, token);
            throw new WorldConflictException();
        }
        Set(SyncState.Uploading, Strings.Get("Sync_Uploading"));
        await repository.UploadAsync(snapshot.Version, snapshot.Path, token, new CallbackProgress<TransferProgress>(p => TransferProgressChanged?.Invoke(p)));
        await EnsureLocalMatchesSnapshot(snapshot, token);
        await lease.RenewAsync(session.SessionId, token);
        Set(SyncState.Publishing, Strings.Get("Sync_Publishing"));
        await lease.PublishAsync(session.SessionId, session.BaseVersion?.Id, snapshot.Version, token);
        await Finish(session, token);
    }
    private async Task Finish(SessionRecord session, CancellationToken token)
    {
        var cleanupPending = false;
        try { await new BackupRetention(repository, lease).PruneAsync(session.SessionId, options.BackupCount, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { cleanupPending = true; }
        Set(SyncState.Releasing, Strings.Get("Sync_Releasing"));
        await lease.ReleaseAsync(session.SessionId, token);
        await journal.ClearAsync(token);
        Set(SyncState.Idle, cleanupPending ? Strings.Get("Sync_SyncedCleanupPending") : Strings.Get("Sync_Synced"));
    }
    private async Task RefreshCore(CancellationToken token)
    {
        if (await journal.ReadAsync(token) is { } pending)
        {
            ValidateSession(pending);
            Set(pending.Stage == SessionStage.Conflict ? SyncState.Conflict : SyncState.Pending, Strings.Get("Sync_PendingRecovery"));
            return;
        }
        var snapshot = await repository.ReadAsync(token);
        if (snapshot?.Manifest.Lease is { } owner && owner.ExpiresAt + TimeSpan.FromSeconds(2) > repository.UtcNow)
            Set(SyncState.InUse, Strings.Format("Sync_InUseBy", owner.Player));
        else Set(SyncState.Idle, snapshot?.Manifest.Current is null ? Strings.Get("Sync_NoWorldPublished") : Strings.Get("Sync_WorldFree"));
    }
    private SessionRecord NewSession(string id, WorldVersion? baseVersion, bool import) => new()
    {
        SessionId = id,
        WorldId = options.WorldId,
        WorldPath = Path.GetFullPath(options.WorldPath),
        RepositoryIdentity = options.RepositoryIdentity,
        BaseVersion = baseVersion,
        Stage = SessionStage.Preparing,
        IsImport = import
    };
    private void ValidateSession(SessionRecord session)
    {
        if (session.WorldId != options.WorldId || session.RepositoryIdentity != options.RepositoryIdentity ||
            !string.Equals(session.WorldPath, Path.GetFullPath(options.WorldPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Strings.Get("Sync_ConfigChanged"));
    }
    private void EnsureGameClosed()
    { if (game.IsRunning) throw new IOException(Strings.Get("Sync_CloseGame")); }
    private void EnsureSafeToCapture()
    { if (game.IsRunning) throw new WorldConflictException(); }
    private async Task EnsureLocalMatchesSnapshot(LocalSnapshot snapshot, CancellationToken token)
    {
        EnsureSafeToCapture();
        if (await archive.GetTreeHashAsync(options.WorldPath, token) != snapshot.Version.TreeHash)
            throw new WorldConflictException();
        EnsureSafeToCapture();
    }
    private static bool IsAncestorOf(string possibleAncestor, string path)
    {
        var prefix = possibleAncestor.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
    private async Task WithHeartbeat(string sessionId, Func<Task> action, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var heartbeat = Heartbeat(sessionId, stop.Token);
        try { await action(); }
        finally
        {
            await stop.CancelAsync();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }
    private async Task Heartbeat(string sessionId, CancellationToken token)
    {
        while (true)
        {
            await Task.Delay(LeaseCoordinator.HeartbeatInterval, time, token);
            try { await lease.RenewAsync(sessionId, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                Set(Status.State, Strings.Get("Sync_RenewFailed"));
            }
        }
    }
    private async Task Guard(Func<Task> action, CancellationToken token)
    {
        if (!await operationGate.WaitAsync(0, token)) return;
        IsBusy = true;
        try { await action(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { Set(SyncState.Pending, Strings.Get("Sync_Interrupted")); }
        catch (WorldConflictException)
        {
            var session = await journal.ReadAsync(CancellationToken.None);
            if (session is not null) await journal.WriteAsync(session with { Stage = SessionStage.Conflict }, CancellationToken.None);
            Set(SyncState.Conflict, Strings.Get("Sync_MaybeAdvanced"));
        }
        catch (WorldBusyException e)
        {
            var pending = await journal.ReadAsync(CancellationToken.None);
            Set(pending is null ? SyncState.InUse : SyncState.Pending,
                pending is null ? Strings.Format("Sync_BusyNoPending", e.Player) : Strings.Format("Sync_BusyPending", e.Player));
        }
        catch (InvalidDataException e) { Set(SyncState.Error, e.Message); }
        catch (Exception e)
        {
            var pending = await journal.ReadAsync(CancellationToken.None);
            Set(pending is null ? SyncState.Offline : SyncState.Pending,
                Strings.Format("Sync_Failed", e.GetType().Name));
        }
        finally { IsBusy = false; operationGate.Release(); StatusChanged?.Invoke(Status); }
    }
    private void Set(SyncState state, string message)
    {
        Status = new(state, message);
        StatusChanged?.Invoke(Status);
    }
    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    { public void Report(T value) => callback(value); }
}
