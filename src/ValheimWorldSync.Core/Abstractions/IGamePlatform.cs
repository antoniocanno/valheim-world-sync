using ValheimWorldSync.Core.Models;
namespace ValheimWorldSync.Core.Abstractions;

public interface IGamePlatform
{
    IReadOnlyList<GameIdentity> FindProcesses();
    void OpenSteam();
}
