namespace ValheimWorldSync.Core.Models;

public sealed record WorldVersion(string Id, string Key, string Sha256, string TreeHash, long Size, DateTimeOffset CreatedAt)
{
    public string? CreatedBy { get; init; }
}
public sealed record WorldLease(string Player, string InstallationId, string SessionId, DateTimeOffset ExpiresAt);
public sealed record WorldManifest
{
    public int SchemaVersion { get; init; } = 1;
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
        if (SchemaVersion is not (1 or 2) || SaveFormat != "valheim-1.0-directory" || WorldId != worldId)
            throw new InvalidDataException("Manifesto incompatível com o mundo configurado.");
        if (SchemaVersion == 2 && (string.IsNullOrWhiteSpace(WorldDisplayName) ||
            string.IsNullOrWhiteSpace(WorldFolderName) || WorldFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            RetentionCount is < 0 or > 1000 or null))
            throw new InvalidDataException("Metadados do manifesto v2 são inválidos.");
        if (History is null || PendingDeletes is null || string.IsNullOrWhiteSpace(Revision))
            throw new InvalidDataException("Manifesto inválido.");
        foreach (var version in History.Concat(Current is null ? [] : new[] { Current }))
        {
            if (!ValidKey(version.Key) || version.Size <= 0 || version.Sha256.Length != 64 || version.TreeHash.Length != 64)
                throw new InvalidDataException("Referência de versão inválida.");
        }
        if (PendingDeletes.Any(key => !ValidKey(key)) ||
            PendingDeletes.Any(key => Current?.Key == key || History.Any(v => v.Key == key)))
            throw new InvalidDataException("Fila de exclusão inválida.");
    }
    public static bool ValidKey(string key) => key.StartsWith("backups/", StringComparison.Ordinal) &&
        key.EndsWith(".zip", StringComparison.Ordinal) && !key.Contains("..") &&
        key.Count(c => c == '/') == 1 && !key.Contains('\\');
}
public sealed record ManifestSnapshot(WorldManifest Manifest, string ETag);
