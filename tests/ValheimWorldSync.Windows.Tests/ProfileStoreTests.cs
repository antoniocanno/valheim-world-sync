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
        Assert.True(File.Exists(Path.Combine(root, "settings.json")));
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
