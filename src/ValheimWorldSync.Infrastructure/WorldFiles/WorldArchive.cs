using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Infrastructure.Recovery;

namespace ValheimWorldSync.Infrastructure.WorldFiles;

public sealed class WorldArchive(string dataRoot) : IWorldArchive
{
    public const long MaxZipBytes = 4L * 1024 * 1024 * 1024;
    public const long MaxExpandedBytes = 32L * 1024 * 1024 * 1024;
    private readonly string root = Path.GetFullPath(dataRoot);
    private string InstallJournal => Path.Combine(root, "install.json");
    public sealed record InstallRecord(string Target, string Staging, string Backup);
    private sealed record FileDigest(string Name, long Size, string Hash);
    private static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    public async Task<LocalSnapshot> CreateAsync(string worldPath, CancellationToken token = default)
    {
        worldPath = Path.GetFullPath(worldPath);
        if (!Directory.Exists(worldPath)) throw new DirectoryNotFoundException("Pasta do mundo não encontrada. Importe uma pasta de mundo 1.0.");
        RejectReparseAncestors(worldPath);
        var snapshots = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(snapshots);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(snapshots, id + ".zip");
        var temporary = path + ".tmp";
        var before = await InventoryAsync(worldPath, token);
        if (before.Count == 0) throw new InvalidDataException("A pasta do mundo está vazia.");
        long total = 0;
        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var directory in EnumerateSafe(worldPath).Where(Directory.Exists))
                    zip.CreateEntry(Path.GetRelativePath(worldPath, directory).Replace('\\', '/') + "/");
                foreach (var file in before)
                {
                    token.ThrowIfCancellationRequested();
                    total = checked(total + file.Size);
                    if (total > MaxExpandedBytes) throw new InvalidDataException("Mundo excede o limite expandido de 32 GiB.");
                    var entry = zip.CreateEntry(file.Name, CompressionLevel.Fastest);
                    // Stable archive metadata; world data stays opaque.
                    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    await using var source = new FileStream(Path.Combine(worldPath, file.Name), FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var destination = entry.Open();
                    await source.CopyToAsync(destination, token);
                }
            }
            await output.FlushAsync(token);
            output.Flush(true);
        }
        var after = await InventoryAsync(worldPath, token);
        if (TreeHash(before) != TreeHash(after)) throw new IOException("O save mudou durante o backup; tentativa adiada.");
        if (new FileInfo(temporary).Length > MaxZipBytes) throw new InvalidDataException("ZIP excede o limite de 4 GiB da v1.");
        // Validate the bytes actually captured, not just the source before/after.
        using (var captured = ZipFile.OpenRead(temporary))
        {
            var contents = new List<FileDigest>();
            foreach (var entry in captured.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                await using var stream = entry.Open();
                contents.Add(new(entry.FullName, entry.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, token))));
            }
            if (TreeHash(contents) != TreeHash(before)) throw new IOException("Snapshot inconsistente; arquivos locais preservados.");
        }
        File.Move(temporary, path);
        var now = DateTimeOffset.UtcNow;
        await using var verify = File.OpenRead(path);
        var version = new WorldVersion(id, $"backups/{now:yyyyMMddTHHmmssfffZ}-{id}.zip",
            Convert.ToHexString(await SHA256.HashDataAsync(verify, token)), TreeHash(before), verify.Length, now);
        return new(path, version);
    }

    public async Task VerifyAsync(LocalSnapshot snapshot, CancellationToken token = default)
    {
        var path = Path.GetFullPath(snapshot.Path);
        if (!path.StartsWith(Path.Combine(root, "snapshots") + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidDataException("Snapshot fora da pasta de recuperação.");
        await using var file = File.OpenRead(path);
        if (file.Length != snapshot.Version.Size ||
            Convert.ToHexString(await SHA256.HashDataAsync(file, token)) != snapshot.Version.Sha256)
            throw new InvalidDataException("Snapshot local alterado ou incompleto; exporte para recuperação manual.");
    }

    public async Task InstallAsync(WorldVersion version, string zipPath, string worldPath, Func<bool> gameIsRunning, CancellationToken token = default)
    {
        await RecoverInstallAsync(worldPath, gameIsRunning, token);
        if (gameIsRunning()) throw new IOException("Feche o Valheim antes de instalar o mundo.");
        worldPath = Path.GetFullPath(worldPath);
        RejectReparseAncestors(worldPath);
        var parent = Path.GetDirectoryName(worldPath)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".vws-staging-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(parent, ".vws-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        await using (var input = File.OpenRead(zipPath))
        {
            if (input.Length != version.Size || input.Length > MaxZipBytes ||
                Convert.ToHexString(await SHA256.HashDataAsync(input, token)) != version.Sha256)
                throw new InvalidDataException("ZIP não corresponde à versão esperada.");
        }
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            if (zip.Entries.Count > 500_000) throw new InvalidDataException("ZIP com entradas demais.");
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                var name = entry.FullName;
                var isDirectory = name.EndsWith('/');
                var relative = isDirectory ? name.TrimEnd('/') : name;
                ValidateRelativePath(relative);
                if (!names.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("ZIP contém caminhos duplicados ou links.");
                var destination = Path.GetFullPath(Path.Combine(staging, relative));
                if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, PathComparison))
                    throw new InvalidDataException("ZIP contém caminho fora do mundo.");
                expanded = checked(expanded + entry.Length);
                if (expanded > MaxExpandedBytes) throw new InvalidDataException("ZIP excede o limite expandido.");
                if (isDirectory) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open();
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await source.CopyToAsync(output, token);
                if (output.Length != entry.Length) throw new InvalidDataException("Entrada ZIP incompleta.");
                await output.FlushAsync(token);
                output.Flush(true);
            }
        }
        var inventory = await InventoryAsync(staging, token);
        if (inventory.Count == 0 || TreeHash(inventory) != version.TreeHash)
            throw new InvalidDataException("Conteúdo extraído não corresponde ao inventário publicado.");
        if (gameIsRunning()) throw new IOException("O Valheim abriu durante a preparação. Instalação cancelada.");
        await DurableJson.WriteAsync(InstallJournal, new InstallRecord(worldPath, staging, backup), token);
        // From here, finish the two renames without cancellation. Recovery handles a process/OS crash.
        if (Directory.Exists(worldPath)) Directory.Move(worldPath, backup);
        try
        {
            if (gameIsRunning()) throw new IOException("O Valheim abriu durante a instalação.");
            Directory.Move(staging, worldPath);
            File.Delete(InstallJournal);
        }
        catch
        {
            if (!Directory.Exists(worldPath) && Directory.Exists(backup)) Directory.Move(backup, worldPath);
            throw;
        }
        // The old directory stays as a local backup. Never automatically delete recovery data.
    }

    public async Task RecoverInstallAsync(string worldPath, Func<bool> gameIsRunning, CancellationToken token = default)
    {
        var record = await DurableJson.ReadAsync<InstallRecord>(InstallJournal, token);
        if (record is null) return;
        if (gameIsRunning()) throw new IOException("Feche o Valheim para recuperar uma instalação interrompida.");
        worldPath = Path.GetFullPath(worldPath);
        var parent = Path.GetDirectoryName(worldPath)!;
        if (!string.Equals(record.Target, worldPath, PathComparison) ||
            !OwnedSibling(record.Staging, parent, ".vws-staging-") || !OwnedSibling(record.Backup, parent, ".vws-backup-"))
            throw new InvalidDataException("Diário de instalação pertence a outro mundo.");
        RejectReparseAncestors(record.Target);
        RejectReparseAncestors(record.Staging);
        RejectReparseAncestors(record.Backup);
        if (!Directory.Exists(record.Target))
        {
            if (Directory.Exists(record.Backup)) Directory.Move(record.Backup, record.Target);
            else if (Directory.Exists(record.Staging)) Directory.Move(record.Staging, record.Target);
            else throw new IOException("Arquivos de recuperação não encontrados.");
        }
        File.Delete(InstallJournal);
    }
    private static bool OwnedSibling(string path, string parent, string prefix) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), parent, PathComparison) &&
        Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(Path.GetFileName(path)[prefix.Length..], "N", out _);

    private static void ValidateRelativePath(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.StartsWith('/') || Path.IsPathRooted(name))
            throw new InvalidDataException("Caminho ZIP inválido.");
        foreach (var part in name.Split('/'))
        {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Contains(':'))
                throw new InvalidDataException("Nome de arquivo inválido.");
            var device = part.Split('.')[0].ToUpperInvariant();
            if (device is "CON" or "PRN" or "AUX" or "NUL" ||
                device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && char.IsAsciiDigit(device[3]))
                throw new InvalidDataException("Nome de dispositivo não permitido.");
        }
    }
    private static void RejectReparseAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links/junctions não são suportados para arquivos de mundo.");
    }
    private static IEnumerable<string> EnumerateSafe(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Link no mundo não suportado.");
            yield return path;
            if (Directory.Exists(path)) foreach (var child in EnumerateSafe(path)) yield return child;
        }
    }
    private static async Task<List<FileDigest>> InventoryAsync(string directory, CancellationToken token)
    {
        var result = new List<FileDigest>();
        foreach (var file in EnumerateSafe(directory).Where(File.Exists))
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetRelativePath(directory, file).Replace('\\', '/');
            ValidateRelativePath(name);
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            result.Add(new(name, stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, token))));
        }
        return result;
    }
    private static string TreeHash(IEnumerable<FileDigest> files) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", files.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => $"{f.Name}\0{f.Size}\0{f.Hash}")))));
}
