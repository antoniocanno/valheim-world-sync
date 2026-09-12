using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Infrastructure.Storage;
using Xunit;
namespace ValheimWorldSync.IntegrationTests;

public sealed class R2Tests
{
    [Fact]
    public async Task RealR2SupportsConditionalManifestWrites()
    {
        var path = Environment.GetEnvironmentVariable("VWS_R2_TEST_CONFIG");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "Defina VWS_R2_TEST_CONFIG para um bucket exclusivo de testes.");
        var config = await AppConfiguration.LoadAsync(path!);
        var prefix = $"vws-tests/{Guid.NewGuid():N}/";
        using var a = new R2WorldRepository(config, prefix);
        using var b = new R2WorldRepository(config, prefix);
        var one = new WorldManifest { WorldId = config.WorldId };
        var two = one with { Revision = Guid.NewGuid().ToString("N") };
        var writes = await Task.WhenAll(a.TryWriteAsync(one, null), b.TryWriteAsync(two, null));
        var winner = Assert.Single(writes.Where(w => w is not null))!;
        var updated = await a.TryWriteAsync(winner.Manifest with { Revision = Guid.NewGuid().ToString("N") }, winner.ETag);
        Assert.NotNull(updated);
        Assert.Null(await b.TryWriteAsync(two, winner.ETag));
        Assert.InRange(a.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
        // Isolated test manifests are deliberately retained for inspection; never delete production lock.json.
    }
}
