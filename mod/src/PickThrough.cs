// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// In a view the crosshair looks past your own controlled character (play test 2026-11-17: "in third person, the pointer accidentally
// overlaps with the player being controlled often, which blocks your targeting of stuff in the distance"). A prefix on Meitou's character
// pick (WorldPicking.PickWhereAt, which the right click, the hover, the action pointer and the outline all use) adds "not the controlled
// character" to what it accepts while WASD Control is on and a view is on. Optional: without it the old picking stays.
using HarmonyLib;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public static class PickThrough
{
    public static string Apply(Harmony h)
    {
        var picking = AccessTools.TypeByName("Meitou.Game.Ui.WorldPicking");
        var m = picking is null ? null : AccessTools.Method(picking, "PickWhereAt");
        if (m is null) return "pick-through: WorldPicking.PickWhereAt missing, your character can block the crosshair";

        h.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(PickThrough), nameof(PickWhereAtPrefix))));
        return "patched WorldPicking.PickWhereAt prefix (the crosshair looks past your character)";
    }

    /// <summary>The accept rule with the controlled character left out, or the rule as it was.</summary>
    public static Func<CharacterSnapshot, bool> Without(Func<CharacterSnapshot, bool> accept, CharacterId? skip) =>
        skip is { } id ? c => c.Id != id && accept(c) : accept;

    static void PickWhereAtPrefix(ref Func<CharacterSnapshot, bool> accept)
    {
        try
        {
            if (!Driver.On || Views.Current == View.Normal) return;
            accept = Without(accept, Driver.ControlledId);
        }
        catch (Exception e)
        {
            Log.Exception("pick-through", e);
        }
    }
}
