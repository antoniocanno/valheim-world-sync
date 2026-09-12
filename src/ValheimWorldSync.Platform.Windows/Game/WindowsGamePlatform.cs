using System.Diagnostics;
using ValheimWorldSync.Core.Abstractions;
using ValheimWorldSync.Core.Models;

namespace ValheimWorldSync.Platform.Windows.Game;

public sealed class WindowsGamePlatform : IGamePlatform
{
    public IReadOnlyList<GameIdentity> FindProcesses()
    {
        var result = new List<GameIdentity>();
        foreach (var process in Process.GetProcessesByName("valheim"))
        {
            using (process)
            {
                try { result.Add(new(process.Id, process.StartTime.ToUniversalTime())); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { result.Add(new(process.Id, DateTime.MinValue)); }
            }
        }
        return result;
    }

    public void OpenSteam()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var launcher = Process.Start(new ProcessStartInfo("steam://rungameid/892970") { UseShellExecute = true });
    }
}
