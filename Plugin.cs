// A new plugin with the header the Creation Kit writes, and the FormID range its style allows.
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;

namespace BuildKit;

public enum PluginStyle { Full, Medium, Light }

public static class Plugin
{
    /// <summary>The form version the Creation Kit writes for Starfield 1.16 (header and records).</summary>
    public const ushort CkFormVersion = 582;

    /// <summary>A new master plugin (.esm): Master flag plus Light or Medium, CK form version, author, description.</summary>
    public static StarfieldMod New(string fileName, PluginStyle style, string author, string description, bool localized = false)
    {
        var mod = new StarfieldMod(ModKey.FromFileName(fileName), StarfieldRelease.Starfield);
        mod.ModHeader.Flags |= StarfieldModHeader.HeaderFlag.Master | style switch
        {
            PluginStyle.Light => StarfieldModHeader.HeaderFlag.Light,
            PluginStyle.Medium => StarfieldModHeader.HeaderFlag.Medium,
            _ => 0,
        };
        mod.UsingLocalization = localized;
        mod.ModHeader.FormVersion = CkFormVersion;
        mod.ModHeader.Author = author;
        mod.ModHeader.Description = description;
        return mod;
    }

    public static PluginStyle StyleOf(IStarfieldModGetter mod) =>
        mod.ModHeader.Flags.HasFlag(StarfieldModHeader.HeaderFlag.Light) ? PluginStyle.Light
        : mod.ModHeader.Flags.HasFlag(StarfieldModHeader.HeaderFlag.Medium) ? PluginStyle.Medium
        : PluginStyle.Full;

    /// <summary>The FormIDs a plugin's own new records may use (0x800 up; light 0xFFF, medium 0xFFFF, full 0xFFFFFF).</summary>
    public static (uint First, uint Last) IdRange(PluginStyle style) => style switch
    {
        PluginStyle.Light => (0x800, 0xFFF),
        PluginStyle.Medium => (0x800, 0xFFFF),
        _ => (0x800, 0xFFFFFF),
    };
}
