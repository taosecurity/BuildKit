// Text for localized plugins: our own text goes into every language the game ships, so no language shows a
// missing string.
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;

namespace BuildKit;

public static class Localized
{
    /// <summary>The nine languages Starfield ships string tables for.</summary>
    public static readonly Language[] Languages =
    [
        Language.English, Language.German, Language.Spanish, Language.French, Language.Italian, Language.Japanese,
        Language.Polish, Language.Portuguese_Brazil, Language.ChineseSimplified,
    ];

    /// <summary>The same text in all nine languages.</summary>
    public static TranslatedString Text(string s)
    {
        var t = new TranslatedString(Language.English, s);
        foreach (var l in Languages) t.Set(l, s);
        return t;
    }
}
