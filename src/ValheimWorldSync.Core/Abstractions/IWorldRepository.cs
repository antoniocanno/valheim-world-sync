using ValheimWorldSync.Core.Models;
namespace ValheimWorldSync.Core.Abstractions;

public interface IWorldRepository
{
    DateTimeOffset UtcNow { get; }
    Task<ManifestSnapshot?> ReadAsync(CancellationToken cancellationToken = default);
    // null means precondition failed; callers must never retry with a newer ETag blindly.
    Task<ManifestSnapshot?> TryWriteAsync(WorldManifest manifest, string? expectedETag, CancellationToken cancellationToken = default);
    Task UploadAsync(WorldVersion version, string archivePath, CancellationToken cancellationToken = default, IProgress<TransferProgress>? progress = null);
    Task DownloadAsync(WorldVersion version, string destination, CancellationToken cancellationToken = default, IProgress<TransferProgress>? progress = null);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
