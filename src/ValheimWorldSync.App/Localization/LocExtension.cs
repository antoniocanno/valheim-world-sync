using System.Windows.Markup;
using ValheimWorldSync.Core.Localization;

namespace ValheimWorldSync.Desktop.Localization;

/// <summary>
/// XAML lookup for shared strings: {loc:Loc Main_Play}.
/// Resolved once when the view loads, matching the restart-to-apply language model.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public string? Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Key is null ? string.Empty : Strings.Get(Key);
}
