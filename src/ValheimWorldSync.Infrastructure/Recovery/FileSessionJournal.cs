using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Models;
namespace ValheimWorldSync.Infrastructure.Recovery;

public sealed class FileSessionJournal(string dataRoot) : ISessionJournal
{
    private readonly string path = Path.Combine(dataRoot, "session.json");
    public Task<SessionRecord?> ReadAsync(CancellationToken token = default) => DurableJson.ReadAsync<SessionRecord>(path, token);
    public Task WriteAsync(SessionRecord session, CancellationToken token = default) => DurableJson.WriteAsync(path, session, token);
    public Task ClearAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            // Keep the last completed journal for troubleshooting, atomically removing the active record.
            File.Move(path, Path.Combine(dataRoot, "last-session.json"), true);
        }
        return Task.CompletedTask;
    }
}
