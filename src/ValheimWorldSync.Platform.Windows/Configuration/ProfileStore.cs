using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Infrastructure.Recovery;
using ValheimWorldSync.Platform.Windows.Credentials;

namespace ValheimWorldSync.Platform.Windows.Configuration;

public sealed class ProfileStore(string dataRoot, ICredentialVault vault)
{
    public string DataRoot { get; } = Path.GetFullPath(dataRoot);
    public string SettingsPath => Path.Combine(DataRoot, "settings.json");
    public string ProfilesRoot => Path.Combine(DataRoot, "profiles");

    public async Task<ProfileCatalog> LoadAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(DataRoot);
        var settings = await DurableJson.ReadAsync<AppSettings>(SettingsPath, token) ?? new AppSettings();
        var migrated = MigrateSettings(settings);
        if (!File.Exists(SettingsPath) || migrated != settings) await DurableJson.WriteAsync(SettingsPath, migrated, token);
        settings = migrated;
        ValidateSettings(settings);
        var profiles = new List<WorldProfile>();
        if (Directory.Exists(ProfilesRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(ProfilesRoot).Order())
            {
                var connection = await DurableJson.ReadAsync<ProfileConnection>(Path.Combine(directory, "connection.json"), token);
                var local = await DurableJson.ReadAsync<ProfileLocalSettings>(Path.Combine(directory, "local.json"), token);
                if (connection is null || local is null) continue;
                var id = Path.GetFileName(directory);
                Validate(id, connection, local);
                profiles.Add(new(id, directory, connection, local));
            }
        }
        return new(settings, profiles);
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default)
    {
        settings = settings with { Language = AppLanguage.NormalizeStrict(settings.Language) };
        ValidateSettings(settings);
        await DurableJson.WriteAsync(SettingsPath, settings, token);
    }

    public async Task<WorldProfile> CreateAsync(ProfileConnection connection, ProfileLocalSettings local,
        R2Credentials credentials, CancellationToken token = default)
    {
        var id = Guid.NewGuid().ToString("N");
        local = local with { CredentialTarget = WindowsCredentialVault.TargetFor(id) };
        Validate(id, connection, local);
        var root = Path.Combine(ProfilesRoot, id);
        if (Directory.Exists(root)) throw new IOException(Strings.Get("Profile_Exists"));
        await vault.WriteAsync(local.CredentialTarget, credentials, token);
        try
        {
            Directory.CreateDirectory(root);
            await DurableJson.WriteAsync(Path.Combine(root, "connection.json"), connection, token);
            await DurableJson.WriteAsync(Path.Combine(root, "local.json"), local, token);
            return new(id, root, connection, local);
        }
        catch
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            await vault.DeleteAsync(local.CredentialTarget, token);
            throw;
        }
    }

    public Task<R2Credentials?> ReadCredentialsAsync(WorldProfile profile, CancellationToken token = default) =>
        vault.ReadAsync(profile.Local.CredentialTarget, token);

    public async Task UpdateAsync(WorldProfile profile, ProfileConnection connection, ProfileLocalSettings local,
        R2Credentials? credentials, CancellationToken token = default)
    {
        local = local with { CredentialTarget = profile.Local.CredentialTarget };
        Validate(profile.Id, connection, local);
        if (credentials is not null) await vault.WriteAsync(local.CredentialTarget, credentials, token);
        await DurableJson.WriteAsync(Path.Combine(profile.Root, "connection.json"), connection, token);
        await DurableJson.WriteAsync(Path.Combine(profile.Root, "local.json"), local, token);
    }

    public async Task SelectAsync(string profileId, CancellationToken token = default)
    {
        var catalog = await LoadAsync(token);
        if (catalog.Profiles.All(candidate => candidate.Id != profileId)) throw new InvalidDataException(Strings.Get("Profile_NotFound"));
        await SaveSettingsAsync(catalog.Settings with { SelectedProfileId = profileId }, token);
    }

    public async Task DeleteAsync(WorldProfile profile, CancellationToken token = default)
    {
        if (File.Exists(Path.Combine(profile.Root, "session.json"))) throw new InvalidDataException(Strings.Get("Profile_PendingSession"));
        await vault.DeleteAsync(profile.Local.CredentialTarget, token);
        if (Directory.Exists(profile.Root)) Directory.Delete(profile.Root, true);
        var settings = (await LoadAsync(token)).Settings;
        if (settings.SelectedProfileId == profile.Id) await SaveSettingsAsync(settings with { SelectedProfileId = null }, token);
    }

    private static AppSettings MigrateSettings(AppSettings settings)
    {
        if (settings.SchemaVersion == 1)
            return settings with { SchemaVersion = 2, Language = AppLanguage.Normalize(settings.Language) };
        if (settings.SchemaVersion == 2 && !AppLanguage.IsSupported(settings.Language))
            return settings with { Language = AppLanguage.Default };
        return settings;
    }

    private static void ValidateSettings(AppSettings settings)
    {
        if ((settings.SchemaVersion != 1 && settings.SchemaVersion != 2) ||
            string.IsNullOrWhiteSpace(settings.PlayerName) || settings.PlayerName.Length > 80 ||
            (settings.SchemaVersion == 2 && !AppLanguage.IsSupported(settings.Language)))
            throw new InvalidDataException(Strings.Get("Profile_InvalidSettings"));
    }
    private static void Validate(string id, ProfileConnection connection, ProfileLocalSettings local)
    {
        if (!Guid.TryParseExact(id, "N", out _) || connection.SchemaVersion != 1 || local.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(connection.WorldDisplayName) || string.IsNullOrWhiteSpace(connection.WorldFolderName) ||
            connection.WorldFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || connection.RetentionCount is < 0 or > 1000 ||
            (!string.IsNullOrWhiteSpace(local.SavesRootOverride) && !Path.IsPathFullyQualified(local.SavesRootOverride)))
            throw new InvalidDataException(Strings.Get("Profile_Invalid"));
        new AppConfiguration
        {
            Endpoint = connection.Endpoint,
            Bucket = connection.Bucket,
            AccessKeyId = "stored",
            SecretAccessKey = "stored",
            Player = "local",
            WorldId = connection.WorldId,
            WorldFolderName = connection.WorldFolderName,
            SavesRoot = local.SavesRootOverride ?? ValheimLocations.DefaultWorldsLocal,
            BackupCount = connection.RetentionCount
        }.ValidateRemote();
        if (connection.RemotePrefix != $"worlds/{connection.WorldId}/") throw new InvalidDataException(Strings.Get("Profile_BadPrefix"));
    }
}
