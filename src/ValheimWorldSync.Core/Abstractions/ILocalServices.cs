using ValheimWorldSync.Core.Models;
namespace ValheimWorldSync.Core.Abstractions;

public interface IWorldArchive
{
    Task<LocalSnapshot> CreateAsync(string worldPath, CancellationToken token = default);
    Task<string> GetTreeHashAsync(string worldPath, CancellationToken token = default);
    Task InstallAsync(WorldVersion version, string zipPath, string worldPath, Func<bool> gameIsRunning, CancellationToken token = default);
    Task RecoverInstallAsync(string worldPath, Func<bool> gameIsRunning, CancellationToken token = default);
    Task VerifyAsync(LocalSnapshot snapshot, CancellationToken token = default);
    Task<RecoveryEntry> PreserveAsync(LocalSnapshot snapshot, string player, string origin, CancellationToken token = default);
}
public interface ISessionJournal
{
    Task<SessionRecord?> ReadAsync(CancellationToken token = default);
    Task WriteAsync(SessionRecord session, CancellationToken token = default);
    Task ClearAsync(CancellationToken token = default);
}
public interface IGameSession
{
    bool IsRunning { get; }
    Task<GameIdentity> LaunchAsync(CancellationToken token = default);
    Task WaitForExitAsync(GameIdentity game, CancellationToken token = default);
}
