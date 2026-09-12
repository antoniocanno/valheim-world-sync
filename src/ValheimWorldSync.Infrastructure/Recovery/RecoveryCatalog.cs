using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Infrastructure.Configuration;

namespace ValheimWorldSync.Infrastructure.Recovery;

public sealed class RecoveryCatalog(string recoveryRoot)
{
    public async Task<IReadOnlyList<RecoveryEntry>> ListAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(recoveryRoot)) return [];
        var entries = new List<RecoveryEntry>();
        foreach (var path in Directory.EnumerateFiles(recoveryRoot, "*.json"))
        {
            var entry = await DurableJson.ReadAsync<RecoveryEntry>(path, token);
            if (entry is not null && File.Exists(entry.ArchivePath) &&
                Path.GetFullPath(entry.ArchivePath).StartsWith(Path.GetFullPath(recoveryRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                entries.Add(entry);
        }
        return entries.OrderByDescending(e => e.CreatedAt).ToArray();
    }
    public Task ExportAsync(RecoveryEntry entry, string destination, CancellationToken token = default)
    { token.ThrowIfCancellationRequested(); File.Copy(entry.ArchivePath, destination, true); return Task.CompletedTask; }
    public Task DeleteAsync(RecoveryEntry entry, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); File.Delete(entry.ArchivePath); File.Delete(Path.ChangeExtension(entry.ArchivePath, ".json")); return Task.CompletedTask;
    }
}
