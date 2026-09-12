using System.Security.Cryptography;
using System.Diagnostics;
using Amazon.S3;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Infrastructure.Storage;
using ValheimWorldSync.Infrastructure.WorldFiles;
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
        var winner = Assert.Single(writes, w => w is not null)!;
        var updated = await a.TryWriteAsync(winner.Manifest with { Revision = Guid.NewGuid().ToString("N") }, winner.ETag);
        Assert.NotNull(updated);
        Assert.Null(await b.TryWriteAsync(two, winner.ETag));
        Assert.InRange(a.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));

        var temporary = Path.Combine(Path.GetTempPath(), $"vws-r2-{Guid.NewGuid():N}.zip");
        var downloaded = temporary + ".downloaded";
        var payload = RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 17);
        await File.WriteAllBytesAsync(temporary, payload, TestContext.Current.CancellationToken);
        var versionId = Guid.NewGuid().ToString("N");
        var version = new WorldVersion(
            versionId,
            $"backups/{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{versionId}.zip",
            Convert.ToHexString(SHA256.HashData(payload)),
            new string('A', 64),
            payload.Length,
            DateTimeOffset.UtcNow);
        try
        {
            await a.UploadAsync(version, temporary, TestContext.Current.CancellationToken);
            await a.UploadAsync(version, temporary, TestContext.Current.CancellationToken); // idempotent retry
            await a.DownloadAsync(version, downloaded, TestContext.Current.CancellationToken);
            Assert.Equal(payload, await File.ReadAllBytesAsync(downloaded, TestContext.Current.CancellationToken));
        }
        finally
        {
            await a.DeleteAsync(version.Key, TestContext.Current.CancellationToken);
            File.Delete(temporary);
            File.Delete(downloaded);
        }
        // Isolated test manifests are deliberately retained for inspection; never delete production lock.json.
    }

    [Fact]
    public async Task ConfiguredValheimWorldRoundTripsWithoutChangingSource()
    {
        var path = Environment.GetEnvironmentVariable("VWS_R2_TEST_CONFIG");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "Defina VWS_R2_TEST_CONFIG para validar o save local configurado.");
        Assert.SkipWhen(Process.GetProcessesByName("valheim").Length != 0, "Feche o Valheim antes de validar o save local.");
        var config = await AppConfiguration.LoadAsync(path!);
        config.Validate();
        Assert.True(Directory.Exists(config.WorldPath), "A pasta do mundo configurado não existe.");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "vws-world-integration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archive = new WorldArchive(Path.Combine(temporaryRoot, "app"));
            var sourceBefore = await archive.GetTreeHashAsync(config.WorldPath, TestContext.Current.CancellationToken);
            var snapshot = await archive.CreateAsync(config.WorldPath, TestContext.Current.CancellationToken);
            var restored = Path.Combine(temporaryRoot, "restored-world");
            await archive.InstallAsync(snapshot.Version, snapshot.Path, restored, () => false, TestContext.Current.CancellationToken);

            Assert.Equal(sourceBefore, snapshot.Version.TreeHash);
            Assert.Equal(sourceBefore, await archive.GetTreeHashAsync(restored, TestContext.Current.CancellationToken));
            Assert.Equal(sourceBefore, await archive.GetTreeHashAsync(config.WorldPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    [Fact]
    public async Task InvalidCredentialsAreRejectedWithoutTouchingData()
    {
        var path = Environment.GetEnvironmentVariable("VWS_R2_TEST_CONFIG");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "Defina VWS_R2_TEST_CONFIG para validar autenticação.");
        var config = await AppConfiguration.LoadAsync(path!);
        config.Validate();
        using var repository = new R2WorldRepository(config with { SecretAccessKey = config.SecretAccessKey + "-invalid" },
            $"vws-tests/{Guid.NewGuid():N}/");

        var error = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            repository.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Contains((int)error.StatusCode, new[] { 401, 403 });
    }
}
