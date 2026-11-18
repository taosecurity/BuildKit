// On-screen text as Message records (MESG), shown in Papyrus with Message.Show(). The whole Debug script is
// DebugOnly (Debug.psc line 1; its header comment says the functions do nothing in release console builds), so
// Debug.Notification and Debug.MessageBox show nothing on Xbox and PlayStation. Text the player must see goes in
// a Message instead. Field shapes copy vanilla Starfield.esm records (checked with SFSearch, 2026-11-18):
//   HUD line  Trait_KidStuff_Msg_SentCredits 236F3C, UC02_DistanceToMorph 3E5D7D: DESC, INAM 0, DNAM 0, TNAM, BNAM FFFFFFFF
//   Box       CREW_MessageGenericCrew_Warning (no buttons; the game adds OK): DESC, INAM 0, DNAM 1, BNAM FFFFFFFF
//             MS05_Stage300Temp_Message (one button): the same plus one ITXT per button
// Text may hold %.0f-style slots for the numbers passed to Show() (up to nine). A Message can't fill in strings,
// so text that changes needs one Message per case.
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Starfield;

namespace BuildKit;

public static class Messages
{
    /// <summary>A HUD line in the top-left corner, like Debug.Notification, shown for <paramref name="seconds"/>.
    /// Show() returns at once. key = a fixed FormID (otherwise the next free one); text = how to make the text
    /// (default: Localized.Text on a localized plugin, plain text otherwise); formVersion null = leave as is.
    /// A "%" in the text starts a number slot, so don't use a literal percent sign (write "percent").</summary>
    public static Message Hud(StarfieldMod mod, string editorId, string message, uint seconds = 2,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion)
    {
        var m = New(mod, editorId, key, formVersion);
        m.Description = Localized.For(mod, message, text);
        m.INAM = 0;
        m.Flags = 0;
        m.DisplayTime = seconds;
        m.BNAM = 0xFFFFFFFF;
        if (key != null) mod.Messages.Add(m);
        return m;
    }

    /// <summary>A message box, like Debug.MessageBox. With no buttons the game shows its own OK button; with
    /// buttons, Show() returns the index of the one picked (0 = first). title = optional heading (FULL);
    /// ownerQuest = optional quest the box belongs to (QNAM). Other arguments as in <see cref="Hud"/>.</summary>
    public static Message Box(StarfieldMod mod, string editorId, string message, string? title = null,
        IEnumerable<string>? buttons = null, IQuestGetter? ownerQuest = null,
        FormKey? key = null, Func<string, TranslatedString>? text = null, ushort? formVersion = Plugin.CkFormVersion)
    {
        var m = New(mod, editorId, key, formVersion);
        m.Description = Localized.For(mod, message, text);
        if (title != null) m.Name = Localized.For(mod, title, text);
        m.INAM = 0;
        m.Flags = Message.Flag.MessageBox;
        m.BNAM = 0xFFFFFFFF;
        if (ownerQuest != null) m.OwnerQuest.SetTo(ownerQuest);
        foreach (var b in buttons ?? []) m.MenuButtons.Add(new MessageButton { Text = Localized.For(mod, b, text) });
        if (key != null) mod.Messages.Add(m);
        return m;
    }

    static Message New(StarfieldMod mod, string editorId, FormKey? key, ushort? formVersion)
    {
        var m = key is { } k ? new Message(k, StarfieldRelease.Starfield) : mod.Messages.AddNew(editorId);
        m.EditorID = editorId;
        if (formVersion is { } fv) m.FormVersion = fv;
        return m;
    }
}
