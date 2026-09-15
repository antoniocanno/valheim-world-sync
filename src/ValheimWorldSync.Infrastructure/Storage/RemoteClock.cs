using System.Diagnostics;
using Amazon.Runtime;
using ValheimWorldSync.Core.Localization;
namespace ValheimWorldSync.Infrastructure.Storage;

// Server Date is sampled by the same HTTP transport used by the S3 SDK.
// Never use the machine's wall clock to decide that another player's lease expired.
internal sealed class RemoteClock : HttpClientFactory
{
    private readonly object gate = new();
    private DateTimeOffset? sample;
    private long timestamp;
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (gate)
            {
                if (sample is null) throw new IOException(Strings.Get("Clock_NoSample"));
                var elapsed = Stopwatch.GetElapsedTime(timestamp);
                if (elapsed > TimeSpan.FromMinutes(5)) throw new IOException(Strings.Get("Clock_StaleSample"));
                return sample.Value + elapsed;
            }
        }
    }
    public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(new DateHandler(this));
    private sealed class DateHandler(RemoteClock clock) : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var result = await base.SendAsync(request, token);
            if (result.Headers.Date is { } date)
                lock (clock.gate) { clock.sample = date; clock.timestamp = Stopwatch.GetTimestamp(); }
            return result;
        }
    }
}
