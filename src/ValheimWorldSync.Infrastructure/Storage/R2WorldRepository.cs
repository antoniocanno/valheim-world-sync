using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Infrastructure.Configuration;

namespace ValheimWorldSync.Infrastructure.Storage;

public sealed class R2WorldRepository : IWorldRepository, IDisposable
{
    private readonly IAmazonS3 client;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly RemoteClock clock = new();
    private readonly string bucket;
    private readonly string worldId;
    private readonly string prefix;
    public DateTimeOffset UtcNow => utcNow();
    public R2WorldRepository(AppConfiguration config, string prefix = "")
    {
        config.Validate();
        utcNow = () => clock.UtcNow;
        this.prefix = prefix;
        bucket = config.Bucket;
        worldId = config.WorldId;
        client = new AmazonS3Client(new BasicAWSCredentials(config.AccessKeyId, config.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = config.Endpoint, AuthenticationRegion = "auto", ForcePathStyle = true,
            MaxErrorRetry = 0, HttpClientFactory = clock,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromMinutes(15)
        });
    }
    internal R2WorldRepository(IAmazonS3 client, string bucket, string worldId, Func<DateTimeOffset> utcNow)
    {
        this.client = client; this.bucket = bucket; this.worldId = worldId; this.utcNow = utcNow; prefix = "";
    }
    public async Task<ManifestSnapshot?> ReadAsync(CancellationToken cancellationToken = default) =>
        await Retry(async () =>
        {
            try
            {
                using var response = await client.GetObjectAsync(bucket, prefix + "lock.json", cancellationToken);
                if (response.ContentLength > 4 * 1024 * 1024) throw new InvalidDataException("Manifesto grande demais.");
                var manifest = await JsonSerializer.DeserializeAsync<WorldManifest>(response.ResponseStream,
                    AppConfiguration.JsonOptions, cancellationToken) ?? throw new InvalidDataException("Manifesto vazio.");
                manifest.Validate(worldId);
                return new ManifestSnapshot(manifest, response.ETag);
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound && e.ErrorCode == "NoSuchKey")
            { return null; }
        }, cancellationToken);

    public async Task<ManifestSnapshot?> TryWriteAsync(WorldManifest manifest, string? expectedETag, CancellationToken cancellationToken = default)
    {
        manifest.Validate(worldId);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var request = Put(prefix + "lock.json");
                request.ContentBody = JsonSerializer.Serialize(manifest, AppConfiguration.JsonOptions);
                request.ContentType = "application/json";
                request.IfMatch = expectedETag;
                request.IfNoneMatch = expectedETag is null ? "*" : null;
                var response = await client.PutObjectAsync(request, cancellationToken);
                return new(manifest, response.ETag);
            }
            catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                var actual = await ReadAsync(cancellationToken);
                return actual?.Manifest.Revision == manifest.Revision ? actual : null;
            }
            catch (Exception e) when (Transient(e, cancellationToken) && attempt < 4)
            {
                // An accepted PUT may lose its response. Reconcile before re-sending.
                var actual = await ReadAsync(cancellationToken);
                if (actual?.Manifest.Revision == manifest.Revision) return actual;
                if (actual?.ETag != expectedETag) return null;
                await Backoff(attempt, cancellationToken);
            }
        }
    }
    public async Task UploadAsync(WorldVersion version, string archivePath, CancellationToken cancellationToken = default, IProgress<TransferProgress>? progress = null)
    {
        CheckKey(version.Key);
        if (version.Size > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Limite da v1: ZIP de 4 GiB.");
        var completedAttempt = 1;
        await Retry(async attempt =>
        {
            completedAttempt = attempt;
            progress?.Report(new(TransferDirection.Upload, TransferPhase.Starting, 0, version.Size, attempt, 5));
            try
            {
                var request = Put(prefix + version.Key);
                request.FilePath = archivePath;
                request.ContentType = "application/zip";
                request.IfNoneMatch = "*";
                request.Metadata["sha256"] = version.Sha256;
                request.StreamTransferProgress += (_, e) => progress?.Report(new(TransferDirection.Upload,
                    TransferPhase.Transferring, e.TransferredBytes, version.Size, attempt, 5));
                await client.PutObjectAsync(request, cancellationToken);
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                var existing = await client.GetObjectMetadataAsync(bucket, prefix + version.Key, cancellationToken);
                if (existing.ContentLength != version.Size || existing.Metadata["x-amz-meta-sha256"] != version.Sha256)
                    throw new InvalidDataException("Colisão de versão: objeto existente não corresponde ao snapshot.");
            }
            return true;
        }, TransferDirection.Upload, version.Size, progress, cancellationToken);
        progress?.Report(new(TransferDirection.Upload, TransferPhase.Completed, version.Size, version.Size, completedAttempt, 5));
    }
    public async Task DownloadAsync(WorldVersion version, string destination, CancellationToken cancellationToken = default, IProgress<TransferProgress>? progress = null)
    {
        CheckKey(version.Key);
        var completedAttempt = 1;
        await Retry(async attempt =>
        {
            completedAttempt = attempt;
            progress?.Report(new(TransferDirection.Download, TransferPhase.Starting, 0, version.Size, attempt, 5));
            using var response = await client.GetObjectAsync(bucket, prefix + version.Key, cancellationToken);
            await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long copied = 0; int read;
                while ((read = await response.ResponseStream.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken); copied += read;
                    progress?.Report(new(TransferDirection.Download, TransferPhase.Transferring, copied, version.Size, attempt, 5));
                }
            }
            progress?.Report(new(TransferDirection.Download, TransferPhase.Verifying, version.Size, version.Size, attempt, 5));
            await using var verify = File.OpenRead(destination);
            if (verify.Length != version.Size || Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken)) != version.Sha256)
                throw new InvalidDataException("ZIP remoto não corresponde ao hash/tamanho publicado.");
            return true;
        }, TransferDirection.Download, version.Size, progress, cancellationToken);
        progress?.Report(new(TransferDirection.Download, TransferPhase.Completed, version.Size, version.Size, completedAttempt, 5));
    }
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        CheckKey(key);
        await Retry(async () => { await client.DeleteObjectAsync(bucket, prefix + key, cancellationToken); return true; }, cancellationToken);
    }
    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var key = prefix + "diagnostics/" + Guid.NewGuid().ToString("N") + ".json";
        var payload = Guid.NewGuid().ToString("N");
        var uploaded = false;
        try
        {
            var request = Put(key);
            request.ContentBody = payload; request.ContentType = "application/json"; request.IfNoneMatch = "*";
            await client.PutObjectAsync(request, cancellationToken); uploaded = true;
            using var response = await client.GetObjectAsync(bucket, key, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            if (await reader.ReadToEndAsync(cancellationToken) != payload) throw new InvalidDataException("O R2 devolveu conteúdo diferente no teste.");
        }
        finally
        {
            if (uploaded) try { await client.DeleteObjectAsync(bucket, key, cancellationToken); }
            catch when (!cancellationToken.IsCancellationRequested) { throw new IOException("A credencial R2 não possui permissão de exclusão."); }
        }
    }
    private static void CheckKey(string key)
    { if (!WorldManifest.ValidKey(key)) throw new InvalidDataException("Chave de snapshot inválida."); }
    private PutObjectRequest Put(string key) => new()
    {
        BucketName = bucket, Key = key, DisablePayloadSigning = true,
        DisableDefaultChecksumValidation = true, UseChunkEncoding = false
    };
    private static bool Transient(Exception e, CancellationToken ct) => !ct.IsCancellationRequested &&
        (e is HttpRequestException or IOException or TaskCanceledException ||
         e is AmazonS3Exception s && ((int)s.StatusCode >= 500 || (int)s.StatusCode == 429 || s.StatusCode == HttpStatusCode.RequestTimeout));
    private static Task Backoff(int attempt, CancellationToken token) =>
        Task.Delay(TimeSpan.FromMilliseconds((1 << attempt) * 1000 + Random.Shared.Next(250)), token);
    private static async Task<T> Retry<T>(Func<int, Task<T>> operation, TransferDirection direction, long total,
        IProgress<TransferProgress>? progress, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await operation(attempt + 1); }
            catch (Exception e) when (Transient(e, token) && attempt < 4)
            {
                var delay = TimeSpan.FromMilliseconds((1 << attempt) * 1000 + Random.Shared.Next(250));
                progress?.Report(new(direction, TransferPhase.RetryWait, 0, total, attempt + 1, 5, delay));
                await Task.Delay(delay, token);
            }
        }
    }
    private static Task<T> Retry<T>(Func<Task<T>> operation, CancellationToken token) =>
        Retry(_ => operation(), TransferDirection.Download, 0, null, token);
    public void Dispose() => client.Dispose();
}
