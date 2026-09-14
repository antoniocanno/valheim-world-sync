using System.Globalization;
using ValheimWorldSync.Core.Localization;
using Xunit;

namespace ValheimWorldSync.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void EveryNeutralKeyResolvesInEnglish()
    {
        using var _ = UseCulture("en-US");
        foreach (var key in Strings.NeutralKeys())
            Assert.False(string.IsNullOrWhiteSpace(Strings.Get(key)), key);
    }

    [Fact]
    public void EveryNeutralKeyResolvesInPortuguese()
    {
        using var _ = UseCulture("pt-BR");
        foreach (var key in Strings.NeutralKeys())
            Assert.False(string.IsNullOrWhiteSpace(Strings.Get(key)), key);
    }

    [Fact]
    public void PortugueseCatalogHasNoOrphansOrGaps()
    {
        var neutral = Strings.NeutralKeys().ToHashSet(StringComparer.Ordinal);
        var portuguese = Strings.KeysFor(CultureInfo.GetCultureInfo("pt-BR")).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(neutral, portuguese);
        Assert.Subset(portuguese, neutral);
    }

    [Fact]
    public void UnsupportedCultureFallsBackToEnglish()
    {
        using var _ = UseCulture("fr-FR");
        Assert.Equal("Play", Strings.Get("Main_Play"));
        Assert.Equal("Settings", Strings.Get("Main_Configure"));
    }

    [Fact]
    public void DefaultCultureIsEnglish()
    {
        using var _ = UseCulture("en-US");
        Assert.Equal("Play", Strings.Get("Main_Play"));
        Assert.Equal("One world. Your next adventure.", Strings.Get("Main_HeroTitle"));
    }

    [Fact]
    public void PortugueseStringsResolve()
    {
        using var _ = UseCulture("pt-BR");
        Assert.Equal("Jogar", Strings.Get("Main_Play"));
        Assert.Equal("Um mundo. Sua próxima aventura.", Strings.Get("Main_HeroTitle"));
    }

    [Fact]
    public void FormatUsesCurrentCulture()
    {
        using var _ = UseCulture("pt-BR");
        Assert.Equal("Mundo em uso por Freyja.", Strings.Format("Lease_Busy", "Freyja"));
    }

    [Fact]
    public void SetLanguageIsIndependentOfThreadAmbientCulture()
    {
        var previous = Strings.Language;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            Strings.SetLanguage(CultureInfo.GetCultureInfo("pt-BR"));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("pt-BR", Strings.Language.Name);
            Assert.Equal("fr-FR", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("Jogar", Strings.Get("Main_Play"));
            Assert.Equal("Mundo em uso por Freyja.", Strings.Format("Lease_Busy", "Freyja"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
            Strings.SetLanguage(previous);
        }
    }

    private static CultureScope UseCulture(string name)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousLanguage = Strings.Language;
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Strings.SetLanguage(culture);
        return new CultureScope(previousCulture, previousUiCulture, previousLanguage);
    }

    private sealed class CultureScope(CultureInfo culture, CultureInfo uiCulture, CultureInfo language) : IDisposable
    {
        public void Dispose()
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            Strings.SetLanguage(language);
        }
    }
}
