using System.Net;
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
        using var repository = new R2WorldRepository(transport, "bucket", "world", () => DateTimeOffset.UtcNow);
        var manifest = new WorldManifest { WorldId = "world" };
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
        using var repository = new R2WorldRepository(transport, "bucket", "world", () => DateTimeOffset.UtcNow);
        var first = await repository.TryWriteAsync(new WorldManifest { WorldId = "world" }, null, TestContext.Current.CancellationToken);
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
        using var repository = new R2WorldRepository(transport, "bucket", "world", () => DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<AmazonS3Exception>(() => repository.ReadAsync(TestContext.Current.CancellationToken));
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
        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            Writes++;
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
        public override Task<GetObjectResponse> GetObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        {
            if (body is null) throw new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound, ErrorCode = MissingCode };
            var bytes = Encoding.UTF8.GetBytes(body);
            return Task.FromResult(new GetObjectResponse { ETag = etag, ResponseStream = new MemoryStream(bytes), ContentLength = bytes.Length });
        }
    }
}
