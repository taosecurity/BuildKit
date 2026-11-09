// Settings > Gameplay Options records: the mod's own group (GPOG) holding its options (GMOP), with the debug switch
// "Show Debug Messages", off by default. The record shape was checked in game (2026-11-02); no vanilla record is
// edited. The console can't reach a light plugin's records by name, so this is the debug switch.
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Starfield;

namespace BuildKit;

public static class GameplayOptions
{
    public const string DebugName = "  Show Debug Messages";

    /// <summary>The line the game's own options end with: "ON/OFF: Default value of this option."</summary>
    public static string DefaultNote(bool on) =>
        $"\n\n<font color=\"#EDEDED\">{(on ? "ON" : "OFF")}</font>: Default value of this option.";

    /// <summary>An on/off option. The description gets the default-value line appended. key = a fixed FormID
    /// (otherwise the next free one); text = Localized.Text for localized plugins; formVersion null = leave as is.</summary>
    public static GameplayOption Bool(StarfieldMod mod, string editorId, string name, string description, bool defaultOn,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion)
    {
        var o = key is { } k ? new GameplayOption(k, StarfieldRelease.Starfield) : mod.GameplayOptions.AddNew(editorId);
        o.EditorID = editorId;
        if (formVersion is { } fv) o.FormVersion = fv;
        o.Name = Text(name, text);
        o.Description = Text(description + DefaultNote(defaultOn), text);
        o.Data = new BoolGameplayOptionData { Value1 = defaultOn, Value2 = defaultOn };
        if (key != null) mod.GameplayOptions.Add(o);
        return o;
    }

    /// <summary>The debug switch, off by default. shows = what the player sees, e.g. "My Mod's log lines on screen:
    /// the scan count and each change it makes"; the standard sentences about reporting problems follow it.</summary>
    public static GameplayOption Debug(StarfieldMod mod, string editorId, string shows,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion) =>
        Bool(mod, editorId, DebugName,
            $"Shows {shows}. Turn it on when reporting a problem. The lines always go to the Papyrus log when Papyrus logging is on.",
            false, key, text, formVersion);

    /// <summary>The mod's own options group, listing the options in this order. Create it after its options
    /// (the order every builder so far uses, which keeps their FormIDs).</summary>
    public static GameplayOptionsGroup Group(StarfieldMod mod, string editorId, string name, IEnumerable<IGameplayOptionGetter> options,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion)
    {
        var g = key is { } k ? new GameplayOptionsGroup(k, StarfieldRelease.Starfield) : mod.GameplayOptionsGroups.AddNew(editorId);
        g.EditorID = editorId;
        if (formVersion is { } fv) g.FormVersion = fv;
        g.Name = Text(name, text);
        var leaf = new GameplayOptionsGroupLeaf();
        foreach (var o in options) leaf.GameplayOptions.Add(o.ToLink<IGameplayOptionGetter>());
        g.Options = leaf;
        if (key != null) mod.GameplayOptionsGroups.Add(g);
        return g;
    }

    static TranslatedString Text(string s, Func<string, TranslatedString>? text) => text != null ? text(s) : s;
}
