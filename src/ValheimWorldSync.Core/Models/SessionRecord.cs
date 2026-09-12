namespace ValheimWorldSync.Core.Models;

public enum SessionStage { Preparing, Launching, Playing, SnapshotPending, Ready, Conflict }
public sealed record LocalSnapshot(string Path, WorldVersion Version);
public sealed record GameIdentity(int ProcessId, DateTime StartTimeUtc);
public sealed record SessionRecord
{
    public required string SessionId { get; init; }
    public required string WorldId { get; init; }
    public required string WorldPath { get; init; }
    public required string RepositoryIdentity { get; init; }
    public WorldVersion? BaseVersion { get; init; }
    public SessionStage Stage { get; init; }
    public GameIdentity? Game { get; init; }
    public LocalSnapshot? Snapshot { get; init; }
    public bool IsImport { get; init; }
}
public enum SyncState { Idle, Checking, Acquiring, Downloading, Preparing, Launching, Playing, LocalBackup, Uploading, Publishing, Releasing, InUse, Offline, Pending, Conflict, Error }
public sealed record SyncStatus(SyncState State, string Message);
