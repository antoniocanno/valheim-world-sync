using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Core.Models;
namespace ValheimWorldSync.Core.Synchronization;

public sealed class PollingGameSession(IGamePlatform platform, TimeProvider? timeProvider = null) : IGameSession
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    public bool IsRunning => platform.FindProcesses().Count != 0;
    public async Task<GameIdentity> LaunchAsync(CancellationToken token = default)
    {
        if (IsRunning) throw new IOException(Strings.Get("Game_AlreadyOpen"));
        token.ThrowIfCancellationRequested();
        var start = time.GetTimestamp();
        platform.OpenSteam();
        while (time.GetElapsedTime(start) < TimeSpan.FromMinutes(3))
        {
            token.ThrowIfCancellationRequested();
            var processes = platform.FindProcesses();
            if (processes.Count > 1) throw new IOException(Strings.Get("Game_MultipleInstances"));
            if (processes.Count == 1)
            {
                if (processes[0].StartTimeUtc == DateTime.MinValue)
                    throw new IOException(Strings.Get("Game_NoSession"));
                return processes[0];
            }
            await Task.Delay(TimeSpan.FromSeconds(2), time, token);
        }
        throw new TimeoutException(Strings.Get("Game_StartTimeout"));
    }
    public async Task WaitForExitAsync(GameIdentity game, CancellationToken token = default)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var processes = platform.FindProcesses();
            if (processes.Any(p => p.ProcessId == game.ProcessId && p.StartTimeUtc == DateTime.MinValue))
                throw new IOException(Strings.Get("Game_VerifySession"));
            if (!processes.Contains(game)) return; // PID reuse must not adopt a different session.
            await Task.Delay(TimeSpan.FromSeconds(2), time, token);
        }
    }
}
