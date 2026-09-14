namespace ValheimWorldSync.Platform.Windows.Game;

using ValheimWorldSync.Core.Localization;

public sealed record ValheimSaveStatus(string WorldsLocal, bool PossibleSteamCloud, string? Guidance);
public static class ValheimSaveDiscovery
{
    public static ValheimSaveStatus Detect(string? valheimRoot = null)
    {
        var root = valheimRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "IronGate", "Valheim");
        var local = Path.Combine(root, "worlds_local");
        var hasLocalWorld = Directory.Exists(local) && Directory.EnumerateDirectories(local).Any();
        var legacy = Path.Combine(root, "worlds");
        var cloudIndicator = File.Exists(Path.Combine(local, "steam_autocloud.vdf")) ||
            Directory.Exists(legacy) && Directory.EnumerateFileSystemEntries(legacy).Any();
        var possible = !hasLocalWorld && cloudIndicator;
        return new(local, possible, possible
            ? Strings.Get("Guidance_SteamCloud")
            : null);
    }
}
