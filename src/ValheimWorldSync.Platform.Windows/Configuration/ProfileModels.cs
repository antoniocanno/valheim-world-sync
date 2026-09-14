using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Platform.Windows.Credentials;

namespace ValheimWorldSync.Platform.Windows.Configuration;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 2;
    public string PlayerName { get; init; } = Environment.UserName;
    public string? SelectedProfileId { get; init; }
    public int OnboardingVersion { get; init; }
    public string Language { get; init; } = AppLanguage.Default;
}

public static class AppLanguage
{
    public const string English = "en-US";
    public const string Portuguese = "pt-BR";
    public const string Default = English;

    public static bool IsSupported(string? language) =>
        string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, Portuguese, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return Default;
        var trimmed = language.Trim();
        if (string.Equals(trimmed, English, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "en", StringComparison.OrdinalIgnoreCase))
            return English;
        if (string.Equals(trimmed, Portuguese, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "pt", StringComparison.OrdinalIgnoreCase))
            return Portuguese;
        return Default;
    }

    public static string NormalizeStrict(string? language)
    {
        if (string.Equals(language?.Trim(), English, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language?.Trim(), "en", StringComparison.OrdinalIgnoreCase))
            return English;
        if (string.Equals(language?.Trim(), Portuguese, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language?.Trim(), "pt", StringComparison.OrdinalIgnoreCase))
            return Portuguese;
        throw new InvalidDataException(Strings.Get("Error_InvalidLanguage"));
    }
}

public sealed record ProfileConnection
{
    public int SchemaVersion { get; init; } = 1;
    public required string Endpoint { get; init; }
    public required string Bucket { get; init; }
    public required string WorldId { get; init; }
    public required string RemotePrefix { get; init; }
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

    public AppConfiguration ToConfiguration(R2Credentials credentials, string player) => new()
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
