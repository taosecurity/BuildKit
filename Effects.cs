// Every vanilla effect entry carries EFIF and a MUID unique within its list. A new Effect object has neither, and
// in game such effects never apply (seen on spells and enchantments built with Mutagen). PluginWriter.Write calls
// FixAll for you.
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;

namespace BuildKit;

public static class Effects
{
    /// <summary>A new effect entry. EFIF is set here; the MUID is given by Fix / FixAll (it depends on the list).</summary>
    public static Effect New(IFormLinkGetter<IMagicEffectGetter> baseEffect, float magnitude, int area = 0, int duration = 0) => new()
    {
        BaseEffect = new FormLinkNullable<IMagicEffectGetter>(baseEffect.FormKey),
        Data = new EffectData { Magnitude = magnitude, Area = area, Duration = duration },
        EFIF = 0,
    };

    /// <summary>Fills a missing EFIF with 0 and a missing MUID with the entry's position + 1 (or the next unused
    /// number, if another entry already has that one). Returns how many entries changed.</summary>
    public static int Fix(IList<Effect> effects)
    {
        var changed = 0;
        var used = effects.Where(e => e.MUID != null).Select(e => e.MUID!.Value).ToHashSet();
        for (var i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            var before = (e.EFIF, e.MUID);
            e.EFIF ??= 0;
            if (e.MUID == null)
            {
                var id = i + 1;
                while (used.Contains(id)) id = used.Max() + 1;
                e.MUID = id;
                used.Add(id);
            }
            if (before != (e.EFIF, e.MUID)) changed++;
        }
        return changed;
    }

    /// <summary>Fix on every spell, object effect (enchantment) and ingestible in the plugin.</summary>
    public static int FixAll(StarfieldMod mod) =>
        mod.Spells.Sum(s => Fix(s.Effects))
        + mod.ObjectEffects.Sum(o => Fix(o.Effects))
        + mod.Ingestibles.Sum(i => Fix(i.Effects));
}
