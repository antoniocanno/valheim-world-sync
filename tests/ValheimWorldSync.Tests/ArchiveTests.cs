using System.IO.Compression;
using System.Security.Cryptography;
using ValheimWorldSync.Infrastructure.WorldFiles;
using ValheimWorldSync.Infrastructure.Recovery;
using Xunit;
namespace ValheimWorldSync.Tests;

public sealed class ArchiveTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vws-test-" + Guid.NewGuid().ToString("N"));
    public ArchiveTests() => Directory.CreateDirectory(root);
    [Fact]
    public async Task CompleteDirectoryRoundTripsAndOldWorldIsPreserved()
    {
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "chunks"));
        await File.WriteAllTextAsync(Path.Combine(source, "world.fwl"), "metadata");
        await File.WriteAllTextAsync(Path.Combine(source, "chunks", "0.chunk"), "world-data");
        var archive = new WorldArchive(Path.Combine(root, "app"));
        var snapshot = await archive.CreateAsync(source);
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "old"), "old-data");
        await archive.InstallAsync(snapshot.Version, snapshot.Path, target, () => false);
        Assert.Equal("world-data", await File.ReadAllTextAsync(Path.Combine(target, "chunks", "0.chunk")));
        Assert.False(File.Exists(Path.Combine(target, "old")));
        Assert.Single(Directory.GetDirectories(root, ".vws-backup-*"));
        var repeated = await archive.CreateAsync(target);
        Assert.Equal(snapshot.Version.TreeHash, repeated.Version.TreeHash);
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/escape")]
    [InlineData("chunks/../../escape")]
    [InlineData("chunks\\escape")]
    [InlineData("CON")]
    public async Task RejectsUnsafeZipWithoutTouchingTarget(string entryName)
    {
        var zip = Path.Combine(root, "bad.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open())) writer.Write("evil");
        var bytes = await File.ReadAllBytesAsync(zip);
        var version = LeaseTests.Version("bad") with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), Size = bytes.Length };
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "keep"), "safe");
        var service = new WorldArchive(Path.Combine(root, "app"));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(version, zip, target, () => false));
        Assert.Equal("safe", await File.ReadAllTextAsync(Path.Combine(target, "keep")));
    }
    [Fact]
    public async Task InterruptedRenameRestoresBackup()
    {
        var app = Path.Combine(root, "app");
        var target = Path.Combine(root, "world");
        var backup = Path.Combine(root, ".vws-backup-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, ".vws-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(backup, "saved"), "original");
        await DurableJson.WriteAsync(Path.Combine(app, "install.json"), new WorldArchive.InstallRecord(target, stage, backup));
        await new WorldArchive(app).RecoverInstallAsync(target, () => false);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(target, "saved")));
    }
    [Fact]
    public async Task DoesNotInstallWhileGameIsRunning()
    {
        var archive = new WorldArchive(Path.Combine(root, "app"));
        await Assert.ThrowsAsync<IOException>(() => archive.InstallAsync(LeaseTests.Version("one"), "absent", Path.Combine(root,"world"), () => true));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
