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
    private readonly AmazonS3Client client;
    private readonly RemoteClock clock = new();
    private readonly string bucket;
    private readonly string worldId;
    private readonly string prefix;
    public DateTimeOffset UtcNow => clock.UtcNow;
    public R2WorldRepository(AppConfiguration config, string prefix = "")
    {
        config.Validate();
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
    public async Task UploadAsync(WorldVersion version, string archivePath, CancellationToken cancellationToken = default)
    {
        CheckKey(version.Key);
        if (version.Size > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Limite da v1: ZIP de 4 GiB.");
        await Retry(async () =>
        {
            try
            {
                var request = Put(prefix + version.Key);
                request.FilePath = archivePath;
                request.ContentType = "application/zip";
                request.IfNoneMatch = "*";
                request.Metadata["sha256"] = version.Sha256;
                await client.PutObjectAsync(request, cancellationToken);
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                var existing = await client.GetObjectMetadataAsync(bucket, prefix + version.Key, cancellationToken);
                if (existing.ContentLength != version.Size || existing.Metadata["x-amz-meta-sha256"] != version.Sha256)
                    throw new InvalidDataException("Colisão de versão: objeto existente não corresponde ao snapshot.");
            }
            return true;
        }, cancellationToken);
    }
    public async Task DownloadAsync(WorldVersion version, string destination, CancellationToken cancellationToken = default)
    {
        CheckKey(version.Key);
        await Retry(async () =>
        {
            using var response = await client.GetObjectAsync(bucket, prefix + version.Key, cancellationToken);
            await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await response.ResponseStream.CopyToAsync(output, cancellationToken);
            await using var verify = File.OpenRead(destination);
            if (verify.Length != version.Size || Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken)) != version.Sha256)
                throw new InvalidDataException("ZIP remoto não corresponde ao hash/tamanho publicado.");
            return true;
        }, cancellationToken);
    }
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        CheckKey(key);
        await Retry(async () => { await client.DeleteObjectAsync(bucket, prefix + key, cancellationToken); return true; }, cancellationToken);
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
    private static async Task<T> Retry<T>(Func<Task<T>> operation, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await operation(); }
            catch (Exception e) when (Transient(e, token) && attempt < 4) { await Backoff(attempt, token); }
        }
    }
    public void Dispose() => client.Dispose();
}
