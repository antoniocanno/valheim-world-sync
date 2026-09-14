using ValheimWorldSync.Platform.Windows.Configuration;
using ValheimWorldSync.Platform.Windows.Credentials;
using Xunit;

namespace ValheimWorldSync.Windows.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vws-profiles-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreatesEmptySettings()
    {
        var catalog = await new ProfileStore(root, new MemoryVault()).LoadAsync();
        Assert.Empty(catalog.Profiles);
        Assert.Null(catalog.Settings.SelectedProfileId);
        Assert.Equal(AppLanguage.Default, catalog.Settings.Language);
        Assert.Equal("en-US", catalog.Settings.Language);
        Assert.Equal(2, catalog.Settings.SchemaVersion);
        Assert.True(File.Exists(Path.Combine(root, "settings.json")));
    }

    [Fact]
    public async Task DefaultsToEnglish()
    {
        var store = new ProfileStore(root, new MemoryVault());
        await store.SaveSettingsAsync(new AppSettings { PlayerName = "Player" });
        var reloaded = await store.LoadAsync();
        Assert.Equal("en-US", reloaded.Settings.Language);
    }

    [Fact]
    public async Task RoundtripsPortuguese()
    {
        var store = new ProfileStore(root, new MemoryVault());
        await store.SaveSettingsAsync(new AppSettings { PlayerName = "Jogador", Language = "pt-BR" });
        var reloaded = await store.LoadAsync();
        Assert.Equal("pt-BR", reloaded.Settings.Language);
    }

    [Fact]
    public async Task NormalizesLanguageAliasesOnSave()
    {
        var store = new ProfileStore(root, new MemoryVault());
        await store.SaveSettingsAsync(new AppSettings { PlayerName = "Player", Language = "en" });
        Assert.Equal("en-US", (await store.LoadAsync()).Settings.Language);
        await store.SaveSettingsAsync(new AppSettings { PlayerName = "Jogador", Language = "pt" });
        Assert.Equal("pt-BR", (await store.LoadAsync()).Settings.Language);
    }

    [Fact]
    public async Task RejectsUnsupportedLanguageOnSave()
    {
        var store = new ProfileStore(root, new MemoryVault());
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveSettingsAsync(new AppSettings { PlayerName = "Player", Language = "fr-FR" }));
    }

    [Fact]
    public async Task MigratesLegacySettingsWithoutLanguageToEnglish()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"),
            """{"schemaVersion":1,"playerName":"Jogador","selectedProfileId":null,"onboardingVersion":0}""");
        var catalog = await new ProfileStore(root, new MemoryVault()).LoadAsync();
        Assert.Equal("en-US", catalog.Settings.Language);
        Assert.Equal(2, catalog.Settings.SchemaVersion);
        // Migration is durable: raw file now carries the new schema.
        var raw = await File.ReadAllTextAsync(Path.Combine(root, "settings.json"));
        Assert.Contains("en-US", raw);
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
