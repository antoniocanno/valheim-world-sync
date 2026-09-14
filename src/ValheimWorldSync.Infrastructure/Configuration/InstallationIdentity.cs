using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Infrastructure.Recovery;

namespace ValheimWorldSync.Infrastructure.Configuration;

public sealed record InstallationIdentity(string Id)
{
    public static async Task<InstallationIdentity> LoadOrCreateAsync(string dataRoot, CancellationToken token = default)
    {
        var path = Path.Combine(dataRoot, "installation.json");
        var existing = await DurableJson.ReadAsync<InstallationIdentity>(path, token);
        if (existing is not null)
        {
            if (!Guid.TryParseExact(existing.Id, "N", out _))
                throw new InvalidDataException(Strings.Get("Install_BadIdentity"));
            return existing;
        }

        var created = new InstallationIdentity(Guid.NewGuid().ToString("N"));
        await DurableJson.WriteAsync(path, created, token);
        return created;
    }
}
