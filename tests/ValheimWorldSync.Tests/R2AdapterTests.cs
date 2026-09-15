using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Infrastructure.Storage;
using Xunit;
namespace ValheimWorldSync.Tests;

public sealed class R2AdapterTests
{
    [Fact]
    public async Task LostPutResponseIsReconciledWithoutAnotherWrite()
    {
        using var transport = new FakeS3 { LoseResponse = true };
        using var repository = new R2WorldRepository(transport, "bucket", "world", () => DateTimeOffset.UtcNow, "worlds/world/");
        var manifest = new WorldManifest
        {
            WorldId = "world",
            WorldDisplayName = "Midgard",
            WorldFolderName = "Midgard",
            RetentionCount = 10
        };
        var result = await repository.TryWriteAsync(manifest, null, TestContext.Current.CancellationToken);
        Assert.Equal(manifest.Revision, result!.Manifest.Revision);
        Assert.Equal(1, transport.Writes);
        Assert.Equal("*", transport.LastIfNoneMatch);
        Assert.True(transport.CompatibleSigning);
    }
    [Fact]
    public async Task StaleConditionIsNotRetriedWithNewETag()
    {
        using var transport = new FakeS3();
        using var repository = new R2WorldRepository(transport, "bucket", "world", () => DateTimeOffset.UtcNow, "worlds/world/");
        var first = await repository.TryWriteAsync(new WorldManifest
        {
            WorldId = "world",
            WorldDisplayName = "Midgard",
            WorldFolderName = "Midgard",
            RetentionCount = 10
        }, null, TestContext.Current.CancellationToken);
        var second = first!.Manifest with { Revision = Guid.NewGuid().ToString("N") };
        await repository.TryWriteAsync(second, first.ETag, TestContext.Current.CancellationToken);
        Assert.Null(await repository.TryWriteAsync(first.Manifest, first.ETag, TestContext.Current.CancellationToken));
        Assert.Equal(3, transport.Writes);
        Assert.Equal(first.ETag, transport.LastIfMatch);
        Assert.Equal(second.Revision, (await repository.ReadAsync(TestContext.Current.CancellationToken))!.Manifest.Revision);
    }
    [Fact]
    public async Task MissingBucketIsNotTreatedAsUninitializedWorld()
    {
        using var transport = new FakeS3 { MissingCode = "NoSuchBucket" };
        using var repository = new R2WorldRepository(transport, "bucket", "world", () => DateTimeOffset.UtcNow, "worlds/world/");
        await Assert.ThrowsAsync<AmazonS3Exception>(() => repository.ReadAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task ConnectionTestWritesReadsAndDeletesOnlyDiagnosticObject()
    {
        using var transport = new FakeS3();
        using var repository = new R2WorldRepository(transport, "bucket", "world", () => DateTimeOffset.UtcNow, "worlds/world/");
        await repository.TestConnectionAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("worlds/world/diagnostics/", transport.LastKey);
        Assert.True(transport.Deleted);
    }
    [Fact]
    public async Task DownloadReportsBytesAndVerification()
    {
        using var transport = new FakeS3(); transport.Seed("world-data");
        using var repository = new R2WorldRepository(transport, "bucket", "world", () => DateTimeOffset.UtcNow, "worlds/world/");
        var bytes = Encoding.UTF8.GetBytes("world-data");
        var version = LeaseTests.Version("progress") with { Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        var destination = Path.GetTempFileName(); var events = new List<TransferProgress>();
        try { await repository.DownloadAsync(version, destination, progress: new CaptureProgress(events)); }
        finally { File.Delete(destination); }
        Assert.Contains(events, p => p.Phase == TransferPhase.Transferring && p.BytesTransferred == bytes.Length);
        Assert.Contains(events, p => p.Phase == TransferPhase.Verifying);
    }
    private sealed class FakeS3() : AmazonS3Client(new BasicAWSCredentials("test", "test"),
        new AmazonS3Config { ServiceURL = "https://s3.us-east-1.amazonaws.com" })
    {
        private string? body;
        private string? etag;
        public bool LoseResponse { get; init; }
        public string MissingCode { get; init; } = "NoSuchKey";
        public int Writes { get; private set; }
        public string? LastIfMatch { get; private set; }
        public string? LastIfNoneMatch { get; private set; }
        public bool CompatibleSigning { get; private set; }
        public string LastKey { get; private set; } = "";
        public bool Deleted { get; private set; }
        public void Seed(string value) { body = value; etag = "seed"; }
        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            Writes++;
            LastKey = request.Key;
            LastIfMatch = request.IfMatch;
            LastIfNoneMatch = request.IfNoneMatch;
            CompatibleSigning = request.DisablePayloadSigning == true && request.DisableDefaultChecksumValidation == true && !request.UseChunkEncoding;
            if ((request.IfNoneMatch == "*" && body is not null) || (request.IfMatch is not null && request.IfMatch != etag))
                throw new AmazonS3Exception("precondition") { StatusCode = HttpStatusCode.PreconditionFailed };
            body = request.ContentBody;
            etag = Guid.NewGuid().ToString("N");
            if (LoseResponse) throw new HttpRequestException("response lost after server committed");
            return Task.FromResult(new PutObjectResponse { ETag = etag });
        }
        public override Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        {
            LastKey = key; Deleted = true; body = null; return Task.FromResult(new DeleteObjectResponse());
        }
        public override Task<GetObjectResponse> GetObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        {
            if (body is null) throw new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound, ErrorCode = MissingCode };
            var bytes = Encoding.UTF8.GetBytes(body);
            return Task.FromResult(new GetObjectResponse { ETag = etag, ResponseStream = new MemoryStream(bytes), ContentLength = bytes.Length });
        }
    }
    private sealed class CaptureProgress(List<TransferProgress> events) : IProgress<TransferProgress>
    { public void Report(TransferProgress value) => events.Add(value); }
}
