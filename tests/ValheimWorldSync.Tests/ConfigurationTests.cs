using ValheimWorldSync.Infrastructure.Configuration;
using Xunit;

namespace ValheimWorldSync.Tests;

public sealed class ConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vws-config-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallationIdentityIsStableAndSeparateFromSharedConfiguration()
    {
        var first = await InstallationIdentity.LoadOrCreateAsync(root);
        var second = await InstallationIdentity.LoadOrCreateAsync(root);

        Assert.Equal(first, second);
        Assert.True(Guid.TryParseExact(first.Id, "N", out _));
        Assert.True(File.Exists(Path.Combine(root, "installation.json")));
    }

    [Fact]
    public async Task DifferentProfilesReceiveDifferentInstallationIdentities()
    {
        var first = await InstallationIdentity.LoadOrCreateAsync(Path.Combine(root, "one"));
        var second = await InstallationIdentity.LoadOrCreateAsync(Path.Combine(root, "two"));

        Assert.NotEqual(first.Id, second.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
