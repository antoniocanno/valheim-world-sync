using System.Text.Json;
using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Infrastructure.Configuration;
namespace ValheimWorldSync.Infrastructure.Recovery;

public static class DurableJson
{
    public static async Task WriteAsync<T>(string path, T value, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, AppConfiguration.JsonOptions, token);
            await stream.FlushAsync(token);
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }
    public static async Task<T?> ReadAsync<T>(string path, CancellationToken token = default)
    {
        if (!File.Exists(path)) return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, AppConfiguration.JsonOptions, token)
            ?? throw new InvalidDataException(Strings.Get("Journal_Invalid"));
    }
}
