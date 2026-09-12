using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Models;
namespace ValheimWorldSync.Tests;

internal sealed class MemoryRepository : IWorldRepository
{
    private readonly object gate = new();
    private ManifestSnapshot? current;
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    public Dictionary<string, byte[]> Objects { get; } = [];
    public Func<Task>? BeforeUpload { get; set; }
    public Action<WorldManifest>? Written { get; set; }
    public bool FailUpload { get; set; }
    public bool FailDelete { get; set; }
    public Action? BeforeWrite { get; set; }
    public Task<ManifestSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    { lock (gate) return Task.FromResult(current); }
    public Task<ManifestSnapshot?> TryWriteAsync(WorldManifest manifest, string? expectedETag, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            BeforeWrite?.Invoke();
            if (current?.ETag != expectedETag) return Task.FromResult<ManifestSnapshot?>(null);
            current = new(manifest, Guid.NewGuid().ToString());
            Written?.Invoke(manifest);
            return Task.FromResult<ManifestSnapshot?>(current);
        }
    }
    public async Task UploadAsync(WorldVersion version, string archivePath, CancellationToken cancellationToken = default, IProgress<TransferProgress>? progress = null)
    {
        if (BeforeUpload is not null) await BeforeUpload();
        if (FailUpload) throw new IOException("offline");
        Objects[version.Key] = await File.ReadAllBytesAsync(archivePath, cancellationToken);
    }
    public Task DownloadAsync(WorldVersion version, string destination, CancellationToken cancellationToken = default, IProgress<TransferProgress>? progress = null) =>
        File.WriteAllBytesAsync(destination, Objects[version.Key], cancellationToken);
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (FailDelete) throw new IOException("offline");
        Objects.Remove(key);
        return Task.CompletedTask;
    }
}
