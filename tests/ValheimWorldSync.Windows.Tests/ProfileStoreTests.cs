using System.Text.Json;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Platform.Windows.Configuration;
using ValheimWorldSync.Platform.Windows.Credentials;
using Xunit;

namespace ValheimWorldSync.Windows.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vws-profiles-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreatesEmptySettingsWithoutLegacyConfiguration()
    {
        var catalog = await new ProfileStore(root, new MemoryVault()).LoadOrMigrateAsync();
        Assert.Empty(catalog.Profiles);
        Assert.Null(catalog.Settings.SelectedProfileId);
        Assert.True(File.Exists(Path.Combine(root, "settings.json")));
    }

    [Fact]
    public async Task MigratesLegacyConfigurationAndProtectsCredentials()
    {
        Directory.CreateDirectory(root);
        var legacy = new AppConfiguration
        {
            Endpoint = "https://account.r2.cloudflarestorage.com", Bucket = "bucket",
            AccessKeyId = "access", SecretAccessKey = "secret", Player = "Viking",
            WorldId = "world", WorldFolderName = "Midgard", SavesRoot = ValheimLocations.DefaultWorldsLocal
        };
        await File.WriteAllTextAsync(Path.Combine(root, "config.json"), JsonSerializer.Serialize(legacy, AppConfiguration.JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(root, "session.json"), "{}");
        var vault = new MemoryVault();

        var catalog = await new ProfileStore(root, vault).LoadOrMigrateAsync();

        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal("Viking", catalog.Settings.PlayerName);
        Assert.Equal("Midgard", profile.Connection.WorldFolderName);
        Assert.Empty(profile.Connection.RemotePrefix);
        Assert.Null(profile.Local.SavesRootOverride);
        Assert.Equal(new R2Credentials("access", "secret"), await vault.ReadAsync(profile.Local.CredentialTarget));
        Assert.False(File.Exists(Path.Combine(root, "config.json")));
        Assert.True(File.Exists(Path.Combine(profile.Root, "session.json")));
        Assert.DoesNotContain("secret", await File.ReadAllTextAsync(Path.Combine(profile.Root, "connection.json")));
        Assert.DoesNotContain("secret", await File.ReadAllTextAsync(Path.Combine(profile.Root, "local.json")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private sealed class MemoryVault : ICredentialVault
    {
        private readonly Dictionary<string, R2Credentials> values = [];
        public Task<R2Credentials?> ReadAsync(string target, CancellationToken token = default) => Task.FromResult(values.GetValueOrDefault(target));
        public Task WriteAsync(string target, R2Credentials credentials, CancellationToken token = default) { values[target] = credentials; return Task.CompletedTask; }
        public Task DeleteAsync(string target, CancellationToken token = default) { values.Remove(target); return Task.CompletedTask; }
    }
}
