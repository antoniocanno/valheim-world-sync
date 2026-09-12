using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Infrastructure.Recovery;
using ValheimWorldSync.Platform.Windows.Credentials;

namespace ValheimWorldSync.Platform.Windows.Configuration;

public sealed class ProfileStore(string dataRoot, ICredentialVault vault)
{
    public string DataRoot { get; } = Path.GetFullPath(dataRoot);
    public string SettingsPath => Path.Combine(DataRoot, "settings.json");
    public string ProfilesRoot => Path.Combine(DataRoot, "profiles");

    public async Task<ProfileCatalog> LoadOrMigrateAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(DataRoot);
        var settings = await DurableJson.ReadAsync<AppSettings>(SettingsPath, token);
        if (settings is null)
        {
            var legacyPath = Path.Combine(DataRoot, "config.json");
            settings = File.Exists(legacyPath)
                ? await MigrateLegacyAsync(legacyPath, token)
                : new AppSettings();
            if (!File.Exists(SettingsPath)) await DurableJson.WriteAsync(SettingsPath, settings, token);
        }
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
        if (Directory.Exists(root)) throw new IOException("O perfil local já existe.");
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
        var catalog = await LoadOrMigrateAsync(token);
        if (catalog.Profiles.All(candidate => candidate.Id != profileId)) throw new InvalidDataException("Perfil não encontrado.");
        await SaveSettingsAsync(catalog.Settings with { SelectedProfileId = profileId }, token);
    }

    public async Task DeleteAsync(WorldProfile profile, CancellationToken token = default)
    {
        if (File.Exists(Path.Combine(profile.Root, "session.json"))) throw new InvalidDataException("Resolva a sessão deste perfil antes de excluí-lo.");
        await vault.DeleteAsync(profile.Local.CredentialTarget, token);
        if (Directory.Exists(profile.Root)) Directory.Delete(profile.Root, true);
        var settings = (await LoadOrMigrateAsync(token)).Settings;
        if (settings.SelectedProfileId == profile.Id) await SaveSettingsAsync(settings with { SelectedProfileId = null }, token);
    }

    private async Task<AppSettings> MigrateLegacyAsync(string legacyPath, CancellationToken token)
    {
        var legacy = await AppConfiguration.LoadAsync(legacyPath);
        legacy.Validate();
        var id = Guid.NewGuid().ToString("N");
        var target = WindowsCredentialVault.TargetFor(id);
        var credentials = new R2Credentials(legacy.AccessKeyId, legacy.SecretAccessKey);
        var root = Path.Combine(ProfilesRoot, id);
        var movedSession = false;
        var movedLastSession = false;
        var movedSnapshots = false;
        await vault.WriteAsync(target, credentials, token);
        try
        {
            if (await vault.ReadAsync(target, token) != credentials)
                throw new IOException("A credencial protegida não pôde ser confirmada.");
            Directory.CreateDirectory(root);
            var connection = new ProfileConnection
            {
                Endpoint = legacy.Endpoint, Bucket = legacy.Bucket, WorldId = legacy.WorldId,
                WorldDisplayName = legacy.WorldFolderName, WorldFolderName = legacy.WorldFolderName,
                RetentionCount = legacy.BackupCount, RemotePrefix = ""
            };
            var local = new ProfileLocalSettings
            {
                CredentialTarget = target,
                SavesRootOverride = SamePath(legacy.SavesRoot, ValheimLocations.DefaultWorldsLocal) ? null : legacy.SavesRoot,
                Alias = legacy.WorldFolderName
            };
            await DurableJson.WriteAsync(Path.Combine(root, "connection.json"), connection, token);
            await DurableJson.WriteAsync(Path.Combine(root, "local.json"), local, token);
            movedSession = MoveIfPresent(Path.Combine(DataRoot, "session.json"), Path.Combine(root, "session.json"));
            movedLastSession = MoveIfPresent(Path.Combine(DataRoot, "last-session.json"), Path.Combine(root, "last-session.json"));
            var oldSnapshots = Path.Combine(DataRoot, "snapshots");
            if (Directory.Exists(oldSnapshots))
            {
                Directory.Move(oldSnapshots, Path.Combine(root, "snapshots"));
                movedSnapshots = true;
            }
            var settings = new AppSettings { PlayerName = legacy.Player, SelectedProfileId = id, OnboardingVersion = 1 };
            await DurableJson.WriteAsync(SettingsPath, settings, token);
            File.Delete(legacyPath);
            return settings;
        }
        catch
        {
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
            if (movedSession) File.Move(Path.Combine(root, "session.json"), Path.Combine(DataRoot, "session.json"));
            if (movedLastSession) File.Move(Path.Combine(root, "last-session.json"), Path.Combine(DataRoot, "last-session.json"));
            if (movedSnapshots) Directory.Move(Path.Combine(root, "snapshots"), Path.Combine(DataRoot, "snapshots"));
            if (Directory.Exists(root)) Directory.Delete(root, true);
            await vault.DeleteAsync(target, token);
            throw;
        }
    }

    private static bool MoveIfPresent(string source, string destination)
    {
        if (!File.Exists(source)) return false;
        File.Move(source, destination, true);
        return true;
    }
    private static bool SamePath(string one, string two) => string.Equals(Path.GetFullPath(one).TrimEnd('\\'), Path.GetFullPath(two).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    private static void ValidateSettings(AppSettings settings)
    {
        if (settings.SchemaVersion != 1 || string.IsNullOrWhiteSpace(settings.PlayerName) || settings.PlayerName.Length > 80)
            throw new InvalidDataException("Configurações locais inválidas.");
    }
    private static void Validate(string id, ProfileConnection connection, ProfileLocalSettings local)
    {
        if (!Guid.TryParseExact(id, "N", out _) || connection.SchemaVersion != 1 || local.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(connection.WorldDisplayName) || string.IsNullOrWhiteSpace(connection.WorldFolderName) ||
            connection.WorldFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || connection.RetentionCount is < 0 or > 1000 ||
            (!string.IsNullOrWhiteSpace(local.SavesRootOverride) && !Path.IsPathFullyQualified(local.SavesRootOverride)))
            throw new InvalidDataException("Perfil local inválido.");
        new AppConfiguration { Endpoint = connection.Endpoint, Bucket = connection.Bucket, AccessKeyId = "stored",
            SecretAccessKey = "stored", Player = "local", WorldId = connection.WorldId, WorldFolderName = connection.WorldFolderName,
            SavesRoot = local.SavesRootOverride ?? ValheimLocations.DefaultWorldsLocal, BackupCount = connection.RetentionCount }.ValidateRemote();
        if (!string.IsNullOrEmpty(connection.RemotePrefix) &&
            connection.RemotePrefix != $"worlds/{connection.WorldId}/") throw new InvalidDataException("Prefixo remoto inválido.");
    }
}
