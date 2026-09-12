using ValheimWorldSync.Core.Models;
namespace ValheimWorldSync.Infrastructure.Recovery;

// Deliberately record only state names: no credentials, endpoint, player names or save contents.
public sealed class StatusLog(string dataRoot)
{
    private readonly object gate = new();
    private SyncState? previous;
    public void Write(SyncState state)
    {
        lock (gate)
        {
            if (previous == state) return;
            previous = state;
            try
            {
                var directory = Path.Combine(dataRoot, "logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    File.Move(path, Path.Combine(directory, "app.previous.log"), true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {state}{Environment.NewLine}");
            }
            catch (IOException) { } // Logging must not interrupt synchronization.
            catch (UnauthorizedAccessException) { }
        }
    }
}
