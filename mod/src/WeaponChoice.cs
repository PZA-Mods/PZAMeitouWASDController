// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// Manual weapon switching (user 2026-11-20: "Manual weapon switching." on the Orders dial). Meitou draws, every tick (Fighter.Weapon, as the
// original's weapon choice): with both arms the first BACK weapon, else the HIP one; with the right arm only the hip one; without the right arm
// none (fists). So the back weapon always wins when there is one, and Kenshi has no switch.
// - A per-character choice: Auto (Meitou's own pick), Hip (the hip weapon when Meitou would draw the back one) or Fists (no weapon). Back is Auto:
//   Meitou already takes it whenever the arms can hold it.
// - The choice is a SimCommand taken in this system's serial Inputs step (deterministic), kept per character and in the state hash; not saved
//   (a loaded game is back on Auto). A postfix on Fighter.Weapon applies it (Fighter objects are rebuilt on equipment changes: each serial step
//   ties the chosen characters' current Fighter to their choice; the postfix only reads that table, so it is safe in the parallel steps).
// - The arms still rule: no right arm, no weapon whatever the choice (Pick never gives a weapon Meitou would not let the hand hold).
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Simulation;
using Fighter = Meitou.Simulation.Combat.Fighter;
using WeaponInstance = Meitou.Simulation.Combat.WeaponInstance;

namespace PZA.WASDControl.Meitou;

public enum WeaponPick : byte { Auto, Hip, Fists }

/// <summary>Set <see cref="Id"/>'s weapon choice.</summary>
public sealed record WeaponPickCommand(CharacterId Id, WeaponPick Pick) : SimCommand;

public static class WeaponRules
{
    /// <summary>
    /// The weapon in hand for a choice (pure: the check and the fuzzer drive it with stand-in objects). <paramref name="meitou"/>: Meitou's own
    /// pick this tick; <paramref name="back"/> / <paramref name="hip"/>: the character's back and hip weapons. Hip swaps a drawn back weapon for
    /// the hip one (the right arm holds it, as Meitou's own one-armed rule does); Fists gives none; neither ever gives a weapon when Meitou gives
    /// none (no right arm), and an animal keeps its natural weapon.
    /// </summary>
    public static T? Pick<T>(WeaponPick pick, T? meitou, T? back, T? hip, bool animal) where T : class
    {
        if (animal || meitou is null) return meitou;
        return pick switch
        {
            WeaponPick.Fists => null,
            WeaponPick.Hip when hip is not null && ReferenceEquals(meitou, back) => hip,
            _ => meitou,
        };
    }
}

public sealed class WeaponChoiceSystem : ITickSystem, IStateHashed
{
    readonly Dictionary<CharacterId, WeaponPick> _picks = [];

    /// <summary>Each chosen character's current Fighter and its choice (read by the postfix on any thread; written in the serial steps).</summary>
    static ConditionalWeakTable<Fighter, StrongBox<WeaponPick>> s_byFighter = new();
    static volatile WeaponChoiceSystem? s_current;

    public WeaponChoiceSystem()
    {
        s_byFighter = new();   // a new world: no choice carried over
        s_current = this;
    }

    /// <summary>The choice for <paramref name="id"/> (Auto when none), for the dial's lit slice.</summary>
    public static WeaponPick Of(CharacterId id) => s_current is { } s && s._picks.TryGetValue(id, out var p) ? p : WeaponPick.Auto;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var c in commands)
        {
            if (c is not WeaponPickCommand w) continue;
            if (w.Pick == WeaponPick.Auto) _picks.Remove(w.Id);
            else _picks[w.Id] = w.Pick;
            Counters.Add("weapon.pick." + w.Pick.ToString().ToLowerInvariant());
        }
        Bind(world);
    }

    public void SlowWorld(World world) => Bind(world);

    /// <summary>Ties each chosen character's current Fighter to its choice (equipment changes build a new Fighter).</summary>
    void Bind(World world)
    {
        var table = world.Characters;
        var fresh = new ConditionalWeakTable<Fighter, StrongBox<WeaponPick>>();
        foreach (var (id, pick) in _picks)
        {
            if (!table.TryResolveNext(id, out int slot) || table.Cold(slot)?.Fighter is not { } f) continue;
            fresh.AddOrUpdate(f, new StrongBox<WeaponPick>(pick));
        }
        s_byFighter = fresh;
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_picks.Count);
        foreach (var (id, p) in _picks.OrderBy(kv => kv.Key.Slot))
        {
            hasher.Add(id);
            hasher.Add((int)p);
        }
    }

    // ---------------------------------------------------------------- the patch on Fighter.Weapon
    public static bool Installed { get; private set; }

    public static string Apply(Harmony h)
    {
        var m = AccessTools.PropertyGetter(typeof(Fighter), "Weapon");
        if (m is null || m.ReturnType != typeof(WeaponInstance)) return Strings.Get("log.weapon_patch_missing");
        try
        {
            h.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(WeaponChoiceSystem), nameof(Postfix))));
            Installed = true;
            return Strings.Get("log.weapon_patch_on");
        }
        catch (Exception e)
        {
            return Strings.Format("log.weapon_patch_failed", e.Message);
        }
    }

    static void Postfix(Fighter __instance, ref WeaponInstance? __result)
    {
        if (!s_byFighter.TryGetValue(__instance, out var box)) return;
        __result = WeaponRules.Pick(box.Value, __result, __instance.BackWeapon, __instance.HipWeapon, __instance.IsAnimal);
    }
}
