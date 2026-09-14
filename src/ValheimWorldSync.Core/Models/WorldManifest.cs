using ValheimWorldSync.Core.Localization;

namespace ValheimWorldSync.Core.Models;

public sealed record WorldVersion(string Id, string Key, string Sha256, string TreeHash, long Size, DateTimeOffset CreatedAt)
{
    public string? CreatedBy { get; init; }
}
public sealed record WorldLease(string Player, string InstallationId, string SessionId, DateTimeOffset ExpiresAt);
public sealed record WorldManifest
{
    public int SchemaVersion { get; init; } = 2;
    public string SaveFormat { get; init; } = "valheim-1.0-directory";
    public required string WorldId { get; init; }
    public string Revision { get; init; } = Guid.NewGuid().ToString("N");
    public WorldVersion? Current { get; init; }
    public WorldLease? Lease { get; init; }
    public WorldVersion[] History { get; init; } = [];
    public string[] PendingDeletes { get; init; } = [];
    public string? WorldDisplayName { get; init; }
    public string? WorldFolderName { get; init; }
    public int? RetentionCount { get; init; }

    public void Validate(string worldId)
    {
        if (SchemaVersion != 2 || SaveFormat != "valheim-1.0-directory" || WorldId != worldId)
            throw new InvalidDataException(Strings.Get("Manifest_Incompatible"));
        if (string.IsNullOrWhiteSpace(WorldDisplayName) ||
            string.IsNullOrWhiteSpace(WorldFolderName) || WorldFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            RetentionCount is < 0 or > 1000 or null)
            throw new InvalidDataException(Strings.Get("Manifest_BadMetadata"));
        if (History is null || PendingDeletes is null || string.IsNullOrWhiteSpace(Revision))
            throw new InvalidDataException(Strings.Get("Manifest_Invalid"));
        foreach (var version in History.Concat(Current is null ? [] : new[] { Current }))
        {
            if (!ValidKey(version.Key) || version.Size <= 0 || version.Sha256.Length != 64 || version.TreeHash.Length != 64)
                throw new InvalidDataException(Strings.Get("Manifest_BadVersionRef"));
        }
        if (PendingDeletes.Any(key => !ValidKey(key)) ||
            PendingDeletes.Any(key => Current?.Key == key || History.Any(v => v.Key == key)))
            throw new InvalidDataException(Strings.Get("Manifest_BadDeleteQueue"));
    }
    public static bool ValidKey(string key) => key.StartsWith("backups/", StringComparison.Ordinal) &&
        key.EndsWith(".zip", StringComparison.Ordinal) && !key.Contains("..") &&
        key.Count(c => c == '/') == 1 && !key.Contains('\\');
}
public sealed record ManifestSnapshot(WorldManifest Manifest, string ETag);
