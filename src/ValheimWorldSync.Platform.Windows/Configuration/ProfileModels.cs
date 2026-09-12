using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Platform.Windows.Credentials;

namespace ValheimWorldSync.Platform.Windows.Configuration;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string PlayerName { get; init; } = Environment.UserName;
    public string? SelectedProfileId { get; init; }
    public int OnboardingVersion { get; init; }
}

public sealed record ProfileConnection
{
    public int SchemaVersion { get; init; } = 1;
    public required string Endpoint { get; init; }
    public required string Bucket { get; init; }
    public required string WorldId { get; init; }
    public string RemotePrefix { get; init; } = "";
    public required string WorldDisplayName { get; init; }
    public required string WorldFolderName { get; init; }
    public int RetentionCount { get; init; } = 10;
}

public sealed record ProfileLocalSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string? SavesRootOverride { get; init; }
    public required string CredentialTarget { get; init; }
    public string Alias { get; init; } = "";
}

public sealed record WorldProfile(string Id, string Root, ProfileConnection Connection, ProfileLocalSettings Local)
{
    public string SavesRoot => string.IsNullOrWhiteSpace(Local.SavesRootOverride)
        ? ValheimLocations.DefaultWorldsLocal : Path.GetFullPath(Local.SavesRootOverride);
    public string WorldPath => Path.Combine(SavesRoot, Connection.WorldFolderName);

    public AppConfiguration ToLegacyConfiguration(R2Credentials credentials, string player) => new()
    {
        Endpoint = Connection.Endpoint,
        Bucket = Connection.Bucket,
        AccessKeyId = credentials.AccessKeyId,
        SecretAccessKey = credentials.SecretAccessKey,
        Player = player,
        WorldId = Connection.WorldId,
        WorldFolderName = Connection.WorldFolderName,
        SavesRoot = SavesRoot,
        BackupCount = Connection.RetentionCount
    };
}

public sealed record ProfileCatalog(AppSettings Settings, IReadOnlyList<WorldProfile> Profiles)
{
    public WorldProfile? Selected => Profiles.FirstOrDefault(p => p.Id == Settings.SelectedProfileId);
}

public static class ValheimLocations
{
    public static string Base => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "IronGate", "Valheim");
    public static string DefaultWorldsLocal => Path.Combine(Base, "worlds_local");
}
