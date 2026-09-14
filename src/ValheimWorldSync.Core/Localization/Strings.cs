using System.Globalization;
using System.Resources;

namespace ValheimWorldSync.Core.Localization;

/// <summary>
/// Shared user-facing strings. The neutral resources are en-US; pt-BR ships as a satellite.
/// The active language is explicit static state (set once at startup and on save), so lookups
/// never depend on thread-ambient <see cref="CultureInfo.CurrentUICulture"/>, which resets to
/// the OS default outside async continuations.
/// </summary>
public static class Strings
{
    public const string BaseName = "ValheimWorldSync.Core.Localization.Strings";

    private static readonly ResourceManager Manager = new(BaseName, typeof(Strings).Assembly);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static CultureInfo language = English;

    /// <summary>Culture used for all string resolution; immune to thread ambient culture.</summary>
    public static CultureInfo Language => language;

    public static void SetLanguage(CultureInfo culture)
    {
        language = CultureInfo.GetCultureInfo(culture.Name);
    }

    public static string Get(string key)
    {
        var value = Manager.GetString(key, language) ?? Manager.GetString(key, English);
        return value ?? key;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(language, Get(key), args);

    public static IReadOnlySet<string> NeutralKeys()
    {
        var set = Manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (set is null) return keys;
        foreach (System.Collections.DictionaryEntry entry in set)
            if (entry.Key is string key) keys.Add(key);
        return keys;
    }

    public static IReadOnlySet<string> KeysFor(CultureInfo culture)
    {
        var set = Manager.GetResourceSet(culture, true, false);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (set is null) return keys;
        foreach (System.Collections.DictionaryEntry entry in set)
            if (entry.Key is string key) keys.Add(key);
        return keys;
    }
}
