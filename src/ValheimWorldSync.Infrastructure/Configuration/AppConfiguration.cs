using System.Text.Json;
namespace ValheimWorldSync.Infrastructure.Configuration;

public sealed record AppConfiguration
{
    public string Endpoint { get; init; } = "";
    public string Bucket { get; init; } = "";
    public string AccessKeyId { get; init; } = "";
    public string SecretAccessKey { get; init; } = "";
    public string Player { get; init; } = Environment.UserName;
    public string WorldId { get; init; } = "";
    public string WorldFolderName { get; init; } = "";
    public string SavesRoot { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "IronGate", "Valheim", "worlds_local");
    public int BackupCount { get; init; } = 10;
    public string InstallationId { get; init; } = Guid.NewGuid().ToString("N");
    public string WorldPath => Path.Combine(SavesRoot, WorldFolderName);
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValheimWorldSync");
    public static string DefaultPath => Path.Combine(DataRoot, "config.json");
    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query))
            throw new InvalidDataException("Endpoint deve ser a URL HTTPS S3 do Cloudflare R2.");
        if (new[] { Bucket, AccessKeyId, SecretAccessKey, Player, WorldId, InstallationId }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Preencha bucket, credenciais, jogador e WorldId em config.json.");
        if (string.IsNullOrWhiteSpace(WorldFolderName) || WorldFolderName is "." or ".." ||
            WorldFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            WorldFolderName.Contains('/') || WorldFolderName.Contains('\\') || !Path.IsPathFullyQualified(SavesRoot))
            throw new InvalidDataException("Configure uma raiz absoluta e um nome simples de pasta de mundo.");
        if (BackupCount is < 0 or > 1000) throw new InvalidDataException("BackupCount deve estar entre 0 e 1000.");
    }
    public static async Task<AppConfiguration> LoadAsync(string path, bool createTemplate = false)
    {
        if (!File.Exists(path) && createTemplate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new AppConfiguration(), JsonOptions));
        }
        return JsonSerializer.Deserialize<AppConfiguration>(await File.ReadAllTextAsync(path), JsonOptions)
            ?? throw new InvalidDataException("Configuração vazia.");
    }
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
