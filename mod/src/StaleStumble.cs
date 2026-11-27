// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// A Meitou slip worked round (play test 2026-11-20: "I just got out of combat and now when I go to try and jump on same character, it won't
// do it ... when I switch to a diff character they'll do"). Headless run the same day: 66 s after a fight the character stood idle and the jump
// was refused "combat_stumble" (clip 56). CombatSystem.ModMove asks the mods' moves only when StumbleClip < 0, but Meitou never clears
// StumbleClip when a stumble ends on its own (only a break-out does); everywhere else Meitou counts a stumble as StumbleClip >= 0 AND
// tick < StunUntil (AggroSystem, AiSystem.Assault, TaskFinished, CombatSystem.Apply). So a character stumbled once refused every mod move.
// - A postfix on ModMove (Meitou 0.21.0+; found by reflection, CombatMove built through a compiled constructor, as JumpSystem's source): when
//   ModMove answered nothing, the jump is playing for that character and the ONLY reason is a stumble whose stun is over (Stale), the jump's
//   move is answered. Nothing else changes; no state is written. A fix for Meitou itself is the real one (the same condition in ModMove).
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using Meitou.Simulation;
using CombatSystem = Meitou.Simulation.Combat.CombatSystem;
using Technique = Meitou.Data.Gameplay.Combat.CombatTechnique;

namespace PZA.WASDControl.Meitou;

public static class StaleStumblePatch
{
    public static bool Installed { get; private set; }

    /// <summary>Times the postfix answered the jump through a stale stumble (read by the counters on the main thread).</summary>
    public static long Fired;

    /// <summary>Whether a character's combat state holds it only through a stumble whose stun is over (pure: the check and the fuzzer).</summary>
    public static bool Stale(bool down, int attackTech, int reactTech, int stumbleClip, long tick, long stunUntil) =>
        !down && attackTech < 0 && reactTech < 0 && stumbleClip >= 0 && tick >= stunUntil;

    public static string Apply(Harmony h)
    {
        var m = AccessTools.Method(typeof(CombatSystem), "ModMove");
        var move = m is null ? null : Nullable.GetUnderlyingType(m.ReturnType);
        var ctor = move?.GetConstructor([typeof(Technique), typeof(float)]);
        if (m is null || move is null || ctor is null || m.GetParameters().Length != 3) return Strings.Get("log.stumble_patch_missing");

        try
        {
            var holder = typeof(StaleStumble<>).MakeGenericType(move);
            holder.GetMethod(nameof(StaleStumble<int>.Init))!.Invoke(null, [ctor]);
            h.Patch(m, postfix: new HarmonyMethod(holder.GetMethod(nameof(StaleStumble<int>.Postfix))));
            Installed = true;
            return Strings.Get("log.stumble_patch_on");
        }
        catch (Exception e)
        {
            return Strings.Format("log.stumble_patch_failed", e.Message);
        }
    }
}

/// <summary>The postfix for Meitou's CombatMove type <typeparamref name="T"/> (made at run time: the mod compiles against 0.20.1).</summary>
public static class StaleStumble<T> where T : struct
{
    static Func<Technique, float, T> s_make = null!;

    public static void Init(ConstructorInfo ctor)
    {
        var t = Expression.Parameter(typeof(Technique));
        var p = Expression.Parameter(typeof(float));
        s_make = Expression.Lambda<Func<Technique, float, T>>(Expression.New(ctor, t, p), t, p).Compile();
    }

    /// <summary>CombatSystem.ModMove(World world, CharacterId id, long tick) postfix; runs in the animation's parallel step, so it only reads.</summary>
    public static void Postfix(CombatSystem __instance, World world, CharacterId id, long tick, ref T? __result)
    {
        if (__result is not null) return;
        if (JumpSystem.Answer(world, id, tick) is not { } jump) return;

        var s = __instance.StateOf(id);
        if (!StaleStumblePatch.Stale(s.Down, s.AttackTech, s.ReactTech, s.StumbleClip, tick, s.StunUntil)) return;

        __result = s_make(jump.Technique, jump.Progress);
        Interlocked.Increment(ref StaleStumblePatch.Fired);
    }
}

/// <summary>
/// The jump over Meitou's leftover states, where the clip is chosen (play test 2026-11-20, again after a fight with the ModMove fix on: the jump
/// was "played" here and not shown). AnimationSystem.TechniqueOf takes, in order, the combat system's own move, a crossbow pose
/// (RangedPoseOf: the ranged controller's aiming pose lasts while its Ranged flag does, which Meitou clears only once the character is out of
/// combat), nothing while carried or getting up, then a mod's move (ModMove). A prefix answers the jump first when it is playing and the
/// character holds no REAL move of its own: no attack or reaction, no live stumble, not down, carried or getting up, and no crossbow pose
/// while still in combat (a pose left over after the fight does not count). Reads only (the animation's parallel step).
/// </summary>
public static class JumpOverPatch
{
    public static bool Installed { get; private set; }

    /// <summary>Ticks the prefix answered the jump.</summary>
    public static long Fired;

    /// <summary>Whether the jump plays over what Meitou would show (pure: the check and the fuzzer). Stumbles do not block a player-triggered jump.</summary>
    public static bool JumpWins(bool ownMove, bool rangedPose, bool inCombat, bool carried, bool gettingUp, bool down, bool liveStumble) =>
        !ownMove && !carried && !gettingUp && !down && !(rangedPose && inCombat);

    public static string Apply(Harmony h)
    {
        var m = AccessTools.Method(typeof(global::Meitou.Simulation.Movement.AnimationSystem), "TechniqueOf");
        if (m is null || m.ReturnType != typeof((int, float)) || m.GetParameters().Length != 4) return Strings.Get("log.jumpover_missing");

        try
        {
            h.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(JumpOverPatch), nameof(Prefix))));
            Installed = true;
            return Strings.Get("log.jumpover_on");
        }
        catch (Exception e)
        {
            return Strings.Format("log.jumpover_failed", e.Message);
        }
    }

    static bool Prefix(global::Meitou.Simulation.Movement.AnimationSystem __instance, World world, CharacterId id,
        CharacterCold cold, float tickSeconds, ref (int, float) __result)
    {
        long tick = world.Tick;
        if (JumpSystem.Answer(world, id, tick) is not { } jump || JumpSystem.CombatOf(world) is not { } combat) return true;

        var s = combat.StateOf(id);
        bool own = combat.Playing(id, tick) is not null;
        bool ranged = combat.RangedPoseOf(id, tick, tickSeconds) is not null;
        bool live = s.StumbleClip >= 0 && tick < s.StunUntil;
        bool up = global::Meitou.Simulation.Bodies.CharacterFacts.GettingUp(cold, tick);
        if (!JumpWins(own, ranged, cold.InCombat, !cold.CarriedBy.IsNone, up, s.Down, live)) return true;

        int clip = __instance.Library.IndexOfTechnique(jump.Technique.StringId);
        if (clip < 0) return true;
        __result = (clip, jump.Progress);
        Interlocked.Increment(ref Fired);
        return false;
    }
}
