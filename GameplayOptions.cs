// Settings > Gameplay Options records: the mod's own group (GPOG) holding its options (GMOP), with the debug switch
// "Show Debug Messages (PC only)", off by default. The record shape was checked in game (2026-11-02); no vanilla
// record is edited. The console can't reach a light plugin's records by name, so this is the debug switch.
// Since 0.5.0 an on/off option also carries the two value rows (VOVS, VORV, RESN, VORN) the Creation Kit writes.
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Starfield;

namespace BuildKit;

public static class GameplayOptions
{
    // "(PC only)" since 2026-11-18: the whole Debug script is DebugOnly, so debug lines never show on Xbox or
    // PlayStation. The two leading spaces indent the option under its group in the menu.
    public const string DebugName = "  Show Debug Messages (PC only)";

    /// <summary>The line the option description ends with: "DISABLE: Default value of this option." (or ENABLE). It names the value row the game shows, the "DISABLE" and "ENABLE" rows BoolRows writes (before 0.6.2 it said OFF or ON, which the game never shows for these options).</summary>
    public static string DefaultNote(bool on) =>
        $"\n\n<font color=\"#EDEDED\">{(on ? "ENABLE" : "DISABLE")}</font>: Default value of this option.";

    /// <summary>Adds the fixed default-value line to the end of the text in every language it has. The builder's own
    /// text function only ever sees the description itself, never the line, so a translator keyed on the
    /// description works (since 0.6.2; before that the key was the description plus an OFF/ON line).
    /// It makes a new TranslatedString and leaves the one it is given untouched, so a translator that caches its
    /// results or hands one object to several records never gets the line added twice. Every language the text holds
    /// gets the line, not only the nine in Localized.Languages. The line itself is always English, like the
    /// DISABLE/ENABLE value rows.</summary>
    static TranslatedString WithNote(TranslatedString? t, string note)
    {
        if (t == null)
            throw new ArgumentException("The text: function returned null for an option description; it must return a TranslatedString.");
        var copy = new TranslatedString(t.TargetLanguage);
        foreach (var (l, v) in t)
            if (v != null) copy.Set(l, v + note);
        return copy;
    }

    /// <summary>An on/off option. The description gets the default-value line appended after the text function, always
    /// in English (the function never sees it). key = a fixed FormID
    /// (otherwise the next free one); text = how to make the text (default: Localized.Text on a localized plugin,
    /// plain text otherwise); formVersion null = leave as is.</summary>
    public static GameplayOption Bool(StarfieldMod mod, string editorId, string name, string description, bool defaultOn,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion)
    {
        var o = key is { } k ? new GameplayOption(k, StarfieldRelease.Starfield) : mod.GameplayOptions.AddNew(editorId);
        o.EditorID = editorId;
        if (formVersion is { } fv) o.FormVersion = fv;
        o.Name = Localized.For(mod, name, text);
        o.Description = WithNote(Localized.For(mod, description, text), DefaultNote(defaultOn));
        o.Data = new BoolGameplayOptionData { Value1 = defaultOn, Value2 = defaultOn };
        o.Rewards.Clear();
        o.Rewards.AddRange(BoolRows(mod));
        if (key != null) mod.GameplayOptions.Add(o);
        return o;
    }

    /// <summary>The two value rows the Creation Kit writes on every on/off option (checked 2026-11-23 against the
    /// CK-made on/off options in six other authors' plugins): VOVS empty, VORV 0 then 1, RESN "DISABLE" then
    /// "ENABLE", VORN 0. No GPOD: the CK writes GPOD (min, max, step) only on slider options, and every official
    /// GPOF is a slider. These are fixed values, not player text, so a builder's text: function is never called for
    /// them (a translator that only knows the mod's own strings would fail on "DISABLE"); on a localized plugin they
    /// get Localized.Text.</summary>
    public static GameplayOptionReward[] BoolRows(StarfieldMod mod) =>
    [
        new() { Description = Localized.For(mod, ""), SettingValue = 0f, Name = Localized.For(mod, "DISABLE"), RewardValue = 0f },
        new() { Description = Localized.For(mod, ""), SettingValue = 1f, Name = Localized.For(mod, "ENABLE"), RewardValue = 0f },
    ];

    /// <summary>The debug switch, off by default. shows = what the player sees, e.g. "My Mod's log lines on screen:
    /// the scan count and each change it makes"; the standard sentences about reporting problems follow it.</summary>
    public static GameplayOption Debug(StarfieldMod mod, string editorId, string shows,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion) =>
        Bool(mod, editorId, DebugName,
            $"Shows {shows}. Set it to ENABLE when reporting a problem. The lines always go to the Papyrus log when Papyrus logging is on. " +
            "PC only: Xbox and PlayStation can't show debug lines.",
            false, key, text, formVersion);

    /// <summary>The mod's own options group, listing the options in this order. Create it after its options
    /// (the order every builder so far uses, which keeps their FormIDs).</summary>
    public static GameplayOptionsGroup Group(StarfieldMod mod, string editorId, string name, IEnumerable<IGameplayOptionGetter> options,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion)
    {
        var g = key is { } k ? new GameplayOptionsGroup(k, StarfieldRelease.Starfield) : mod.GameplayOptionsGroups.AddNew(editorId);
        g.EditorID = editorId;
        if (formVersion is { } fv) g.FormVersion = fv;
        g.Name = Localized.For(mod, name, text);
        var leaf = new GameplayOptionsGroupLeaf();
        foreach (var o in options) leaf.GameplayOptions.Add(o.ToLink<IGameplayOptionGetter>());
        g.Options = leaf;
        if (key != null) mod.GameplayOptionsGroups.Add(g);
        return g;
    }
}
