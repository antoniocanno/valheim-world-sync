using ValheimWorldSync.Core.Abstractions;
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
    public SyncStatus Status { get; private set; } = new(SyncState.Idle, "Pronto para verificar o mundo.");
    public event Action<SyncStatus>? StatusChanged;
    public event Action<TransferProgress>? TransferProgressChanged;
    public SyncEngine(IWorldRepository repository, IWorldArchive archive, ISessionJournal journal, IGameSession game, SyncOptions options, TimeProvider? time = null)
    {
        this.repository = repository; this.archive = archive; this.journal = journal; this.game = game; this.options = options;
        this.time = time ?? TimeProvider.System;
        lease = new(repository, options.WorldId, options.Player, options.InstallationId);
    }

    public Task PlayAsync(CancellationToken token = default) => Guard(async () =>
    {
        if (await journal.ReadAsync(token) is not null) throw new InvalidDataException("Resolva a sessão pendente antes de jogar.");
        EnsureGameClosed();
        await archive.RecoverInstallAsync(options.WorldPath, () => game.IsRunning, token);
        Set(SyncState.Checking, "Verificando disponibilidade do mundo…");
        var sessionId = Guid.NewGuid().ToString("N");
        Set(SyncState.Acquiring, "Reservando o mundo…");
        var manifest = await UpgradeManifestAsync(sessionId, await lease.AcquireAsync(sessionId, token), token);
        if (manifest.Current is null)
        {
            await lease.ReleaseAsync(sessionId, token);
            throw new InvalidDataException("Mundo ainda não publicado. Use Importar mundo local.");
        }
        var session = NewSession(sessionId, manifest.Current, false);
        await journal.WriteAsync(session, token);
        await WithHeartbeat(sessionId, async () =>
        {
            Set(SyncState.Downloading, "Baixando a versão mais recente…");
            var downloadRoot = Path.Combine(options.DataRoot, "downloads");
            Directory.CreateDirectory(downloadRoot);
            var download = Path.Combine(downloadRoot, sessionId + ".zip");
            await repository.DownloadAsync(manifest.Current, download, token, new CallbackProgress<TransferProgress>(p => TransferProgressChanged?.Invoke(p)));
            Set(SyncState.Preparing, "Validando e instalando o save; cópia anterior será preservada…");
            await archive.InstallAsync(manifest.Current, download, options.WorldPath, () => game.IsRunning, token);
            await lease.RenewAsync(sessionId, token); // Do not launch after losing ownership during download.
            EnsureGameClosed();
            session = session with { Stage = SessionStage.Launching };
            await journal.WriteAsync(session, token);
            Set(SyncState.Launching, "Abrindo Valheim pela Steam…");
            var identity = await game.LaunchAsync(token);
            session = session with { Stage = SessionStage.Playing, Game = identity };
            await journal.WriteAsync(session, token);
            Set(SyncState.Playing, "Valheim aberto. Selecione o mundo configurado e inicie o servidor no jogo.");
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
            throw new InvalidDataException("Selecione a pasta de um único mundo; origem e destino não podem conter uma à outra.");
        if (await journal.ReadAsync(token) is not null) throw new InvalidDataException("Resolva a sessão pendente antes de importar.");
        await archive.RecoverInstallAsync(options.WorldPath, () => game.IsRunning, token);
        var sessionId = Guid.NewGuid().ToString("N");
        Set(SyncState.Acquiring, "Reservando o mundo para a primeira importação…");
        var manifest = await UpgradeManifestAsync(sessionId, await lease.AcquireAsync(sessionId, token), token);
        if (manifest.Current is not null)
        {
            await lease.ReleaseAsync(sessionId, token);
            throw new InvalidDataException("O bucket já contém um mundo. A importação inicial não o substitui.");
        }
        var session = NewSession(sessionId, null, true);
        await journal.WriteAsync(session, token);
        await WithHeartbeat(sessionId, async () =>
        {
            Set(SyncState.LocalBackup, "Copiando a origem para a pasta local usada pelo Valheim…");
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
        if (session.Stage == SessionStage.Conflict) { Set(SyncState.Conflict, "Há progresso local divergente. Exporte antes de voltar à versão da nuvem."); return; }
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
            Set(SyncState.Idle, "Preparação interrompida recuperada. Você pode jogar.");
            return;
        }
        await WithHeartbeat(session.SessionId, async () =>
        {
            if (session.Stage == SessionStage.Playing && session.Game is { } identity)
            {
                Set(SyncState.Playing, "Retomando acompanhamento da sessão anterior…");
                await game.WaitForExitAsync(identity, token);
            }
            EnsureGameClosed();
            if (session.Stage == SessionStage.Ready && session.Snapshot is not null)
                await PublishPending(session, token);
            else await CaptureAndPublish(session, token);
        }, token);
    }, token);

    public Task RefreshAsync(CancellationToken token = default) => Guard(() => RefreshCore(token), token);

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
        await journal.WriteAsync(session with { Snapshot = snapshot, Stage = SessionStage.Conflict }, token);
        await lease.ReleaseAsync(session.SessionId, token);
        await journal.ClearAsync(token);
        Set(SyncState.Idle, "Cópia local preservada em Recuperação. Próximo Jogar usará a nuvem.");
    }, token);

    private async Task CaptureAndPublish(SessionRecord session, CancellationToken token)
    {
        EnsureSafeToCapture();
        session = session with { Stage = SessionStage.SnapshotPending };
        await journal.WriteAsync(session, token);
        Set(SyncState.LocalBackup, "Guardando uma cópia completa do progresso local…");
        var snapshot = await archive.CreateAsync(options.WorldPath, token);
        EnsureSafeToCapture();
        session = session with { Stage = SessionStage.Ready, Snapshot = snapshot };
        await journal.WriteAsync(session, token);
        await PublishPending(session, token);
    }
    private async Task PublishPending(SessionRecord session, CancellationToken token)
    {
        EnsureGameClosed();
        var snapshot = session.Snapshot ?? throw new InvalidDataException("Snapshot pendente ausente.");
        if (string.IsNullOrWhiteSpace(snapshot.Version.CreatedBy))
        {
            snapshot = snapshot with { Version = snapshot.Version with { CreatedBy = options.Player } };
            session = session with { Snapshot = snapshot };
            await journal.WriteAsync(session, token);
        }
        await archive.VerifyAsync(snapshot, token);
        await EnsureLocalMatchesSnapshot(snapshot, token);
        Set(SyncState.Acquiring, "Confirmando posse e versão-base para publicar…");
        var manifest = await UpgradeManifestAsync(session.SessionId, await lease.AcquireAsync(session.SessionId, token), token);
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
        Set(SyncState.Uploading, "Enviando o snapshot. O progresso já está salvo localmente…");
        await repository.UploadAsync(snapshot.Version, snapshot.Path, token, new CallbackProgress<TransferProgress>(p => TransferProgressChanged?.Invoke(p)));
        await EnsureLocalMatchesSnapshot(snapshot, token);
        await lease.RenewAsync(session.SessionId, token);
        Set(SyncState.Publishing, "Publicando a nova versão por CAS…");
        await lease.PublishAsync(session.SessionId, session.BaseVersion?.Id, snapshot.Version, token);
        await Finish(session, token);
    }
    private async Task Finish(SessionRecord session, CancellationToken token)
    {
        var cleanupPending = false;
        try { await new BackupRetention(repository, lease).PruneAsync(session.SessionId, options.BackupCount, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { cleanupPending = true; }
        Set(SyncState.Releasing, "Concluindo a sincronização…");
        await lease.ReleaseAsync(session.SessionId, token);
        await journal.ClearAsync(token);
        Set(SyncState.Idle, cleanupPending ? "Mundo sincronizado. A limpeza de backups será retomada na próxima sincronização." : "Sincronizado. Mundo disponível para o próximo anfitrião.");
    }
    private async Task RefreshCore(CancellationToken token)
    {
        if (await journal.ReadAsync(token) is { } pending)
        {
            ValidateSession(pending);
            Set(pending.Stage == SessionStage.Conflict ? SyncState.Conflict : SyncState.Pending, "Há uma sessão local pendente de recuperação.");
            return;
        }
        var snapshot = await repository.ReadAsync(token);
        if (snapshot?.Manifest.Lease is { } owner && owner.ExpiresAt + TimeSpan.FromSeconds(2) > repository.UtcNow)
            Set(SyncState.InUse, $"Em uso por {owner.Player}. Entre pela lista de amigos/convite da Steam.");
        else Set(SyncState.Idle, snapshot?.Manifest.Current is null ? "Nenhum mundo publicado. Importe a pasta local do mundo." : "Mundo livre. Pronto para jogar.");
    }
    private SessionRecord NewSession(string id, WorldVersion? baseVersion, bool import) => new()
    {
        SessionId = id, WorldId = options.WorldId, WorldPath = Path.GetFullPath(options.WorldPath),
        RepositoryIdentity = options.RepositoryIdentity, BaseVersion = baseVersion, Stage = SessionStage.Preparing, IsImport = import
    };
    private void ValidateSession(SessionRecord session)
    {
        if (session.WorldId != options.WorldId || session.RepositoryIdentity != options.RepositoryIdentity ||
            !string.Equals(session.WorldPath, Path.GetFullPath(options.WorldPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Configuração mudou enquanto havia uma sessão pendente. Restaure a configuração anterior.");
    }
    private void EnsureGameClosed()
    { if (game.IsRunning) throw new IOException("Feche o Valheim antes de sincronizar arquivos locais."); }
    private void EnsureSafeToCapture()
    { if (game.IsRunning) throw new WorldConflictException(); }
    private async Task EnsureLocalMatchesSnapshot(LocalSnapshot snapshot, CancellationToken token)
    {
        EnsureSafeToCapture();
        if (await archive.GetTreeHashAsync(options.WorldPath, token) != snapshot.Version.TreeHash)
            throw new WorldConflictException();
        EnsureSafeToCapture();
    }
    private async Task<WorldManifest> UpgradeManifestAsync(string sessionId, WorldManifest manifest, CancellationToken token)
    {
        if (manifest.SchemaVersion == 2) return manifest;
        var folder = options.WorldFolderName ?? Path.GetFileName(Path.GetFullPath(options.WorldPath));
        var display = options.WorldDisplayName ?? folder;
        return await lease.MutateOwned(sessionId, current => current with
        {
            SchemaVersion = 2,
            WorldDisplayName = display,
            WorldFolderName = folder,
            RetentionCount = options.BackupCount
        }, token);
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
                Set(Status.State, "Não foi possível renovar a posse. Progresso local será preservado; publicação depende de nova verificação.");
            }
        }
    }
    private async Task Guard(Func<Task> action, CancellationToken token)
    {
        if (!await operationGate.WaitAsync(0, token)) return;
        IsBusy = true;
        try { await action(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { Set(SyncState.Pending, "Operação interrompida. Recuperação será verificada na próxima abertura."); }
        catch (WorldConflictException)
        {
            var session = await journal.ReadAsync(CancellationToken.None);
            if (session is not null) await journal.WriteAsync(session with { Stage = SessionStage.Conflict }, CancellationToken.None);
            Set(SyncState.Conflict, "Outra versão pode ter avançado. Progresso local preservado; exporte para recuperação manual.");
        }
        catch (WorldBusyException e)
        {
            var pending = await journal.ReadAsync(CancellationToken.None);
            Set(pending is null ? SyncState.InUse : SyncState.Pending,
                pending is null ? $"Em uso por {e.Player}. Entre pela Steam." : $"Progresso local salvo. Aguardando {e.Player} liberar o mundo.");
        }
        catch (InvalidDataException e) { Set(SyncState.Error, e.Message); }
        catch (Exception e)
        {
            var pending = await journal.ReadAsync(CancellationToken.None);
            Set(pending is null ? SyncState.Offline : SyncState.Pending,
                $"Não foi possível concluir ({e.GetType().Name}). Verifique rede e configuração. Dados locais preservados.");
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
