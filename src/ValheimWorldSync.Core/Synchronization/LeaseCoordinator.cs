using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Models;

namespace ValheimWorldSync.Core.Synchronization;

public sealed class LeaseLostException() : IOException("A posse do mundo foi perdida.");
public sealed class WorldBusyException(string player) : Exception($"Mundo em uso por {player}.")
{ public string Player { get; } = player; }
public sealed class WorldConflictException() : Exception("Outra versão foi publicada. O progresso local foi preservado.");

public sealed class LeaseCoordinator(IWorldRepository repository, string worldId, string player, string installationId,
    string worldDisplayName, string worldFolderName, int retentionCount)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public static TimeSpan Ttl { get; } = TimeSpan.FromSeconds(180);
    public static TimeSpan HeartbeatInterval { get; } = TimeSpan.FromSeconds(60);
    public async Task<WorldManifest> AcquireAsync(string sessionId, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var snapshot = await repository.ReadAsync(token);
                var manifest = snapshot?.Manifest ?? new WorldManifest
                {
                    WorldId = worldId,
                    WorldDisplayName = worldDisplayName,
                    WorldFolderName = worldFolderName,
                    RetentionCount = retentionCount
                };
                manifest.Validate(worldId);
                if (manifest.Lease is { } owner && owner.SessionId != sessionId &&
                    owner.ExpiresAt + TimeSpan.FromSeconds(2) > repository.UtcNow)
                    throw new WorldBusyException(owner.Player);
                var updated = manifest with
                {
                    Revision = Guid.NewGuid().ToString("N"),
                    Lease = new(player, installationId, sessionId, repository.UtcNow + Ttl)
                };
                if (await repository.TryWriteAsync(updated, snapshot?.ETag, token) is { } written)
                    return written.Manifest;
            }
            throw new IOException("Muitas alterações concorrentes. Tente novamente.");
        }
        finally { gate.Release(); }
    }
    public async Task<WorldManifest> RenewAsync(string sessionId, CancellationToken token = default) =>
        await MutateOwned(sessionId, m => m with { Lease = m.Lease! with { ExpiresAt = repository.UtcNow + Ttl } }, token);

    public async Task PublishAsync(string sessionId, string? baseVersionId, WorldVersion version, CancellationToken token = default) =>
        await MutateOwned(sessionId, m =>
        {
            if (m.Current?.Id == version.Id) return m; // Reconciliation after a lost response.
            if (m.Current?.Id != baseVersionId) throw new WorldConflictException();
            if (m.Current?.Key == version.Key || m.History.Any(v => v.Key == version.Key) || m.PendingDeletes.Contains(version.Key))
                throw new InvalidDataException("Uma publicação deve usar uma chave nova.");
            return m with { Current = version, History = m.Current is null ? m.History : [m.Current, .. m.History] };
        }, token);

    public async Task ReleaseAsync(string sessionId, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var snapshot = await repository.ReadAsync(token);
            if (snapshot?.Manifest.Lease?.SessionId != sessionId) return;
            await repository.TryWriteAsync(snapshot.Manifest with { Revision = Guid.NewGuid().ToString("N"), Lease = null }, snapshot.ETag, token);
        }
        finally { gate.Release(); }
    }
    public async Task<WorldManifest> MutateOwned(string sessionId, Func<WorldManifest, WorldManifest> change, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var snapshot = await repository.ReadAsync(token) ?? throw new LeaseLostException();
            if (snapshot.Manifest.Lease is not { } lease || lease.SessionId != sessionId || lease.ExpiresAt <= repository.UtcNow)
                throw new LeaseLostException();
            var updated = change(snapshot.Manifest) with { Revision = Guid.NewGuid().ToString("N") };
            return (await repository.TryWriteAsync(updated, snapshot.ETag, token))?.Manifest ?? throw new LeaseLostException();
        }
        finally { gate.Release(); }
    }
}
