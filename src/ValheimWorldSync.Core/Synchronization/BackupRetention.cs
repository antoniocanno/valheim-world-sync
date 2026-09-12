using ValheimWorldSync.Core.Abstractions;
namespace ValheimWorldSync.Core.Synchronization;

public sealed class BackupRetention(IWorldRepository repository, LeaseCoordinator lease)
{
    public async Task PruneAsync(string sessionId, int previousVersionsToKeep, CancellationToken token = default)
    {
        if (previousVersionsToKeep is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(previousVersionsToKeep));
        // Commit the removal from the authoritative history before deleting any bytes.
        var manifest = await lease.MutateOwned(sessionId, m => m with
        {
            History = m.History.Take(previousVersionsToKeep).ToArray(),
            PendingDeletes = m.PendingDeletes.Concat(m.History.Skip(previousVersionsToKeep).Select(v => v.Key)).Distinct().ToArray()
        }, token);
        foreach (var key in manifest.PendingDeletes)
        {
            token.ThrowIfCancellationRequested();
            await repository.DeleteAsync(key, token);
            await lease.MutateOwned(sessionId, m => m with { PendingDeletes = m.PendingDeletes.Where(k => k != key).ToArray() }, token);
        }
    }
}
