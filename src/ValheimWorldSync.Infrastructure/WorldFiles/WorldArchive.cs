using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Infrastructure.Recovery;

namespace ValheimWorldSync.Infrastructure.WorldFiles;

public sealed class WorldArchive(string dataRoot, string? recoveryDirectory = null, string profileId = "legacy") : IWorldArchive
{
    public const long MaxZipBytes = 4L * 1024 * 1024 * 1024;
    public const long MaxExpandedBytes = 32L * 1024 * 1024 * 1024;
    public const int MaxEntries = 500_000;
    private readonly string root = Path.GetFullPath(dataRoot);
    private readonly string recoveryRoot = Path.GetFullPath(recoveryDirectory ?? Path.Combine(dataRoot, "recovery"));
    private string InstallJournal => Path.Combine(root, "install.json");
    public sealed record InstallRecord(string Target, string Staging, string Backup);
    private sealed record FileDigest(string Name, long Size, string Hash);
    private static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    public async Task<LocalSnapshot> CreateAsync(string worldPath, CancellationToken token = default)
    {
        worldPath = Path.GetFullPath(worldPath);
        if (!Directory.Exists(worldPath)) throw new DirectoryNotFoundException("Pasta do mundo não encontrada. Importe uma pasta de mundo 1.0.");
        EnsureDataRootOutsideWorld(worldPath);
        RejectReparseAncestors(worldPath);
        var snapshots = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(snapshots);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(snapshots, id + ".zip");
        var temporary = path + ".tmp";
        var sourceEntries = EnumerateSafe(worldPath).Take(MaxEntries + 1).ToArray();
        if (sourceEntries.Length > MaxEntries) throw new InvalidDataException($"Mundo excede o limite de {MaxEntries:N0} entradas.");
        var before = await InventoryAsync(worldPath, sourceEntries, token);
        if (before.Count == 0) throw new InvalidDataException("A pasta do mundo está vazia.");
        long total = 0;
        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var directory in sourceEntries.Where(Directory.Exists))
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

    public async Task<string> GetTreeHashAsync(string worldPath, CancellationToken token = default)
    {
        worldPath = Path.GetFullPath(worldPath);
        if (!Directory.Exists(worldPath)) throw new DirectoryNotFoundException("Pasta do mundo não encontrada.");
        EnsureDataRootOutsideWorld(worldPath);
        RejectReparseAncestors(worldPath);
        return TreeHash(await InventoryAsync(worldPath, token));
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
        var workspaceParent = Path.GetDirectoryName(parent)!;
        var workspace = Path.Combine(workspaceParent, ".vws-work-" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(workspace, "staging");
        var backup = Path.Combine(workspace, "previous");
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
            if (zip.Entries.Count > MaxEntries) throw new InvalidDataException($"ZIP excede o limite de {MaxEntries:N0} entradas.");
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
        }
        catch
        {
            if (!Directory.Exists(worldPath) && Directory.Exists(backup)) Directory.Move(backup, worldPath);
            throw;
        }
        await PreserveBackupAsync(backup, token);
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        File.Delete(InstallJournal);
    }

    public async Task RecoverInstallAsync(string worldPath, Func<bool> gameIsRunning, CancellationToken token = default)
    {
        var record = await DurableJson.ReadAsync<InstallRecord>(InstallJournal, token);
        if (record is null) return;
        if (gameIsRunning()) throw new IOException("Feche o Valheim para recuperar uma instalação interrompida.");
        worldPath = Path.GetFullPath(worldPath);
        var parent = Path.GetDirectoryName(worldPath)!;
        var workspaceParent = Path.GetDirectoryName(parent)!;
        if (!string.Equals(record.Target, worldPath, PathComparison) ||
            !OwnedWorkspace(record.Staging, record.Backup, workspaceParent, parent))
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
        else if (Directory.Exists(record.Backup)) await PreserveBackupAsync(record.Backup, token);
        var workspace = Path.GetDirectoryName(record.Staging)!;
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        File.Delete(InstallJournal);
    }
    public async Task<RecoveryEntry> PreserveAsync(LocalSnapshot snapshot, string player, string origin, CancellationToken token = default)
    {
        var source = Path.GetFullPath(snapshot.Path);
        if (!source.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidDataException("Arquivo fora da área de recuperação do perfil.");
        await using (var file = File.OpenRead(source))
            if (file.Length != snapshot.Version.Size || Convert.ToHexString(await SHA256.HashDataAsync(file, token)) != snapshot.Version.Sha256)
                throw new InvalidDataException("Cópia local alterada ou incompleta.");
        Directory.CreateDirectory(recoveryRoot);
        var destination = Path.Combine(recoveryRoot, $"{snapshot.Version.CreatedAt:yyyyMMddTHHmmssfffZ}-{snapshot.Version.Id}.zip");
        if (!string.Equals(Path.GetFullPath(snapshot.Path), Path.GetFullPath(destination), PathComparison))
            File.Copy(snapshot.Path, destination, true);
        var entry = new RecoveryEntry(snapshot.Version.Id, profileId, snapshot.Version.CreatedAt, player,
            snapshot.Version.Size, origin, snapshot.Version, destination);
        await DurableJson.WriteAsync(Path.ChangeExtension(destination, ".json"), entry, token);
        return entry;
    }
    private static bool OwnedWorkspace(string staging, string backup, string parent, string legacyParent)
    {
        var workspace = Path.GetDirectoryName(Path.GetFullPath(staging));
        if (workspace is null || !(string.Equals(Path.GetDirectoryName(workspace), parent, PathComparison) ||
                string.Equals(Path.GetDirectoryName(workspace), legacyParent, PathComparison)) ||
            !Path.GetFileName(workspace).StartsWith(".vws-work-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(workspace)[10..], "N", out _)) return false;
        return string.Equals(staging, Path.Combine(workspace, "staging"), PathComparison) &&
            string.Equals(backup, Path.Combine(workspace, "previous"), PathComparison);
    }

    private async Task PreserveBackupAsync(string backup, CancellationToken token)
    {
        if (!Directory.Exists(backup)) return;
        var snapshot = await CreateAsync(backup, token);
        await VerifyAsync(snapshot, token);
        await PreserveAsync(snapshot, snapshot.Version.CreatedBy ?? "desconhecido", "antes-de-instalar", token);
        File.Delete(snapshot.Path);
        Directory.Delete(backup, true);
    }

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
        var entries = EnumerateSafe(directory).Take(MaxEntries + 1).ToArray();
        if (entries.Length > MaxEntries) throw new InvalidDataException($"Mundo excede o limite de {MaxEntries:N0} entradas.");
        return await InventoryAsync(directory, entries, token);
    }
    private static async Task<List<FileDigest>> InventoryAsync(string directory, IEnumerable<string> entries, CancellationToken token)
    {
        var result = new List<FileDigest>();
        foreach (var file in entries.Where(File.Exists))
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetRelativePath(directory, file).Replace('\\', '/');
            ValidateRelativePath(name);
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            result.Add(new(name, stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, token))));
        }
        return result;
    }
    private void EnsureDataRootOutsideWorld(string worldPath)
    {
        var worldPrefix = worldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (string.Equals(root, worldPath, PathComparison) || root.StartsWith(worldPrefix, PathComparison))
            throw new InvalidDataException("A pasta de dados do aplicativo não pode ficar dentro da pasta do mundo.");
    }
    private static string TreeHash(IEnumerable<FileDigest> files) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", files.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => $"{f.Name}\0{f.Size}\0{f.Hash}")))));
}
