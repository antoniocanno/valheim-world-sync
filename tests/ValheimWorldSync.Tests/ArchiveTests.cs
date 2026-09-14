using System.IO.Compression;
using System.Security.Cryptography;
using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Infrastructure.Recovery;
using ValheimWorldSync.Infrastructure.WorldFiles;
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
        var archive = new WorldArchive(Path.Combine(root, "app"), "test");
        var snapshot = await archive.CreateAsync(source);
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "old"), "old-data");
        await archive.InstallAsync(snapshot.Version, snapshot.Path, target, () => false);
        Assert.Equal("world-data", await File.ReadAllTextAsync(Path.Combine(target, "chunks", "0.chunk")));
        Assert.False(File.Exists(Path.Combine(target, "old")));
        Assert.Empty(Directory.GetDirectories(root, ".vws-backup-*"));
        Assert.Empty(Directory.GetDirectories(root, ".vws-staging-*"));
        Assert.Empty(Directory.GetDirectories(root, ".vws-work-*"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "app", "recovery"), "*.zip"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "app", "recovery"), "*.json"));
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
        var service = new WorldArchive(Path.Combine(root, "app"), "test");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(version, zip, target, () => false));
        Assert.Equal("safe", await File.ReadAllTextAsync(Path.Combine(target, "keep")));
    }
    [Fact]
    public async Task InterruptedRenameRestoresBackup()
    {
        var app = Path.Combine(root, "app");
        var target = Path.Combine(root, "saves", "world");
        Directory.CreateDirectory(Path.Combine(root, "saves"));
        var workspace = Path.Combine(root, ".vws-work-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(workspace, "previous");
        var stage = Path.Combine(workspace, "staging");
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(backup, "saved"), "original");
        await DurableJson.WriteAsync(Path.Combine(app, "install.json"), new WorldArchive.InstallRecord(target, stage, backup));
        await new WorldArchive(app, "test").RecoverInstallAsync(target, () => false);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(target, "saved")));
    }
    [Fact]
    public async Task DoesNotInstallWhileGameIsRunning()
    {
        var archive = new WorldArchive(Path.Combine(root, "app"), "test");
        await Assert.ThrowsAsync<IOException>(() => archive.InstallAsync(LeaseTests.Version("one"), "absent", Path.Combine(root, "world"), () => true));
    }
    [Fact]
    public async Task RejectsDataDirectoryNestedInsideWorld()
    {
        foreach (var (culture, fragment) in new[] { ("en-US", "inside the world folder"), ("pt-BR", "dentro da pasta do mundo") })
        using (var _ = new TestCultureScope(culture))
        {
            var world = Path.Combine(root, "world-" + culture);
            Directory.CreateDirectory(world);
            await File.WriteAllTextAsync(Path.Combine(world, "chunk"), "data");
            var archive = new WorldArchive(Path.Combine(world, ".app-data"), "test");
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => archive.CreateAsync(world));
            Assert.Contains(fragment, error.Message);
        }
    }
    [Fact]
    public async Task RecoveryPromotesStagingWhenNoPreviousWorldExists()
    {
        var app = Path.Combine(root, "app");
        var target = Path.Combine(root, "saves", "world");
        var workspace = Path.Combine(root, ".vws-work-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(workspace, "previous");
        var stage = Path.Combine(workspace, "staging");
        Directory.CreateDirectory(Path.Combine(root, "saves"));
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(stage, "saved"), "staged");
        await DurableJson.WriteAsync(Path.Combine(app, "install.json"), new WorldArchive.InstallRecord(target, stage, backup));
        await new WorldArchive(app, "test").RecoverInstallAsync(target, () => false);
        Assert.Equal("staged", await File.ReadAllTextAsync(Path.Combine(target, "saved")));
    }
    [Fact]
    public async Task RecoveryFailsClosedWhenAllInstallDirectoriesAreMissing()
    {
        var app = Path.Combine(root, "app");
        var target = Path.Combine(root, "saves", "world");
        var workspace = Path.Combine(root, ".vws-work-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(workspace, "previous");
        var stage = Path.Combine(workspace, "staging");
        await DurableJson.WriteAsync(Path.Combine(app, "install.json"), new WorldArchive.InstallRecord(target, stage, backup));
        await Assert.ThrowsAsync<IOException>(() => new WorldArchive(app, "test").RecoverInstallAsync(target, () => false));
        Assert.True(File.Exists(Path.Combine(app, "install.json")));
    }
    public void Dispose() => DeleteEventually(root);
    internal static void DeleteEventually(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); return; }
            catch (IOException) when (attempt < 9) { Thread.Sleep(50); }
        }
    }
}
