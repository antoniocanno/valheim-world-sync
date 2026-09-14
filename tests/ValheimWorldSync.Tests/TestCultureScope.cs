using System.Globalization;
using ValheimWorldSync.Core.Localization;

namespace ValheimWorldSync.Tests;

/// <summary>
/// Captures and restores the thread ambient cultures and the global
/// <see cref="Strings.Language"/> state, so tests that change either
/// cannot leak into other tests running in the same session.
/// </summary>
internal sealed class TestCultureScope : IDisposable
{
    private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo previousLanguage = Strings.Language;

    public TestCultureScope(string culture) : this(CultureInfo.GetCultureInfo(culture)) { }

    public TestCultureScope(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Strings.SetLanguage(culture);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = previousCulture;
        CultureInfo.CurrentUICulture = previousUiCulture;
        Strings.SetLanguage(previousLanguage);
    }
}
