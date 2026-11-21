// Text for localized plugins: our own text goes into every language the game ships, so no language shows a
// missing string.
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Starfield;

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

    /// <summary>Text for a record in <paramref name="mod"/>: the builder's own <paramref name="text"/> function if
    /// given, otherwise all nine languages on a localized plugin (so no language shows a missing string) and plain
    /// text on any other plugin. Use it for the names and descriptions of your own records, so they follow the same
    /// rule as <see cref="GameplayOptions"/> and <see cref="Messages"/>.</summary>
    public static TranslatedString For(StarfieldMod mod, string s, Func<string, TranslatedString>? text = null) =>
        text != null ? text(s) : mod.UsingLocalization ? Text(s) : s;
}
