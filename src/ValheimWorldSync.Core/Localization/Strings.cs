using System.Globalization;
using System.Resources;

namespace ValheimWorldSync.Core.Localization;

/// <summary>
/// Shared user-facing strings. The neutral resources are en-US; pt-BR ships as a satellite.
/// Lookup follows <see cref="CultureInfo.CurrentUICulture"/>, which the app sets from
/// settings at startup (restart required), so no live-reload plumbing is needed.
/// </summary>
public static class Strings
{
    public const string BaseName = "ValheimWorldSync.Core.Localization.Strings";

    private static readonly ResourceManager Manager = new(BaseName, typeof(Strings).Assembly);

    public static string Get(string key)
    {
        var value = Manager.GetString(key, CultureInfo.CurrentUICulture)
            ?? Manager.GetString(key, CultureInfo.GetCultureInfo("en-US"));
        return value ?? key;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

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
