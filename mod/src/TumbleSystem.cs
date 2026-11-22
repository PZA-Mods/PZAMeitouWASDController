// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// Falling off ledges, in the world (rules: LedgeRules).
// - The host (Driver) sends a TumbleCommand for the character it steers when a push outward reaches a lip with the setting on Fall.
// - This tick system, right after Meitou's animation and ragdolls, takes it: only a player character, conscious, not carried, not in a bed or
//   cage, with a ragdoll for its look, one at a time, after the cooldown. It marks the character (s_tumbler) and gives the body a throw outward
//   through CharacterCold.Launch, which Meitou adds to a new whole-body ragdoll whether or not Advanced ragdolls is on (Blow is not: it is
//   dropped with that option off).
// - LedgePatch's postfix on RagdollSystem.Wanted (private static) answers Whole for that one character while it is marked, so Meitou ragdolls it
//   (gravity, the run's momentum, the throw) and the character's position follows the body.
// - Each tick the tracker reads the body's position; at rest (or the time limit) it measures the drop, puts blunt damage on the legs through
//   MedicalState.ApplyHit (capped so a leg breaks, never comes off), and clears the mark: Meitou's own get-up follows.
// - Nothing is saved: a save loaded mid-fall has no mark, and Meitou restores a saved ragdoll only when it is still wanted, so the character
//   simply gets up. The mark, the tracker and the counts go into the state hash.
using System.Numerics;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Population;
using Meitou.Simulation.Ragdolls;

namespace PZA.WASDControl.Meitou;

/// <summary>Start a tumble for <see cref="Id"/> with the throw <see cref="Push"/> (world units/s).</summary>
public sealed record TumbleCommand(CharacterId Id, Vector3 Push) : SimCommand;

public sealed class TumbleSystem : ITickSystem, IStateHashed
{
    /// <summary>The one character that tumbles now (read by LedgePatch on any thread; written only in the serial phases).</summary>
    static volatile CharacterCold? s_tumbler;
    /// <summary>The character tumbling now, for the host (the driver stops steering it); null when none.</summary>
    public static CharacterId? Tumbling => s_tumblingId;
    static CharacterId? s_tumblingId;

    /// <summary>The character the driver steers now, packed (slot, generation; -1: none); written by the host each control tick, read here.</summary>
    static long s_steered = -1;

    /// <summary>The host's steered character (Driver.OnTick); null when it steers nobody.</summary>
    public static void SetSteered(CharacterId? id) => Volatile.Write(ref s_steered, id is { } c ? ((long)c.Slot << 32) | (uint)c.Generation : -1);

    /// <summary>Whether <paramref name="id"/> is the character the host steers now (JumpSystem asks too).</summary>
    internal static bool SteeredIs(CharacterId id) => IsSteered(id);

    static bool IsSteered(CharacterId id) => Volatile.Read(ref s_steered) == (((long)id.Slot << 32) | (uint)id.Generation);

    /// <summary>Whether LedgePatch should answer Whole for <paramref name="cold"/>.</summary>
    internal static bool Marked(CharacterCold cold) => ReferenceEquals(cold, s_tumbler);

    readonly PopulationData _data;
    readonly IRagdollState? _ragdolls;
    /// <summary>Meitou's combat system: its Buildings is the building collision (IRagdollEnvironment) the clear-way test reads.</summary>
    readonly global::Meitou.Simulation.Combat.CombatSystem? _combat;
    readonly List<Vector3> _triangles = [];
    string? _lastRefusal;
    long _lastRefusalTick;
    readonly TumbleTracker _tracker = new();
    CharacterId _id;
    Vector3 _push;
    long _started, _ended;

    public TumbleSystem(PopulationData data, IRagdollState? ragdolls, global::Meitou.Simulation.Combat.CombatSystem? combat)
    {
        (_data, _ragdolls, _combat) = (data, ragdolls, combat);
        // A new world (Meitou builds the systems again for every new game and load): no mark is carried over. (IWorldReady would say it too,
        // but it is newer than Meitou 0.20.1, the oldest this mod loads on.)
        if (s_tumbler is not null) Log.Line(Strings.Get("log.ledge_ready_cleared"));
        Clear();
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var c in commands)
            if (c is TumbleCommand t) Start(world, t);
    }

    public void SlowWorld(World world)
    {
        if (!_tracker.Active)
        {
            _tracker.Step(world.TickSeconds, default, false);   // the cooldown
            return;
        }

        var table = world.Characters;
        if (!table.TryResolveNext(_id, out int slot) || table.Cold(slot) is not { } cold || !Free(cold))
        {
            Finish(world, null, -1, _tracker.Abort());
            return;
        }

        bool whole = _ragdolls?.WasWhole(_id) ?? false;
        var at = table.Next[slot].Position;
        var result = _tracker.Step(world.TickSeconds, at, whole, AboveGround(world, at));
        if (_tracker.WantsNudge)
        {
            // still in the air: a small throw; Meitou rebuilds a frozen ragdoll for it and starts its freeze clock over (RagdollSystem.Launch)
            var out2 = _push.X * _push.X + _push.Z * _push.Z > 1e-6f ? Vector2.Normalize(new Vector2(_push.X, _push.Z)) : Vector2.Zero;
            cold.Launch = new Vector3(out2.X * LedgeRules.NudgeOut, -LedgeRules.NudgeDown, out2.Y * LedgeRules.NudgeOut);
            _tracker.Nudged();
            Counters.Add("ledge.nudge");
            Log.Line(Strings.Format("log.ledge_nudge", _tracker.Nudges, AboveGround(world, at) / 10f));
        }
        if (result.End is TumbleEnd.Rest or TumbleEnd.Timeout) result.Drop = GroundDrop(at, result.Drop);
        if (result.End != TumbleEnd.None) Finish(world, cold, slot, result);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_tracker.Active);
        hasher.Add(_id);
        hasher.Add(_tracker.Elapsed);
        hasher.Add(_tracker.CooldownLeft);
        hasher.Add(_started);
        hasher.Add(_ended);
    }

    #region Start

    void Start(World world, TumbleCommand t)
    {
        var table = world.Characters;
        string why;
        if (!LedgePatch.Installed) why = "no_patch";
        else if (!table.TryResolveNext(t.Id, out int slot) || table.Cold(slot) is not { } cold) why = "gone";
        else if (!cold.IsPlayer) why = "not_player";   // never an NPC
        else if (!IsSteered(t.Id)) why = "not_steered";   // never another squad member
        else if (!Free(cold)) why = "not_free";
        else if (cold.Medical is not { Dead: false, Incapacitated: false }) why = "down";
        else if (_ragdolls is null || !_ragdolls.HasRagdoll(cold)) why = "no_ragdoll";
        else if (Way(table.Next[slot].Position, t.Push) is { } blocked) why = blocked;
        else if (!_tracker.TryStart(table.Next[slot].Position, out why)) { }
        else
        {
            _id = t.Id;
            _push = t.Push;
            s_tumblingId = t.Id;
            s_tumbler = cold;
            // With Advanced ragdolls the body already keeps the run's speed (RagdollSystem.SetUp): half the sideways throw then (headless run
            // 2026-11-18: run + full throw carried a body 10.5 m out from a 4 m drop).
            bool advanced = _data.BodyOptions.AdvancedRagdolls;
            cold.Launch = advanced ? new Vector3(t.Push.X * LedgeRules.ThrowWithRun, t.Push.Y, t.Push.Z * LedgeRules.ThrowWithRun) : t.Push;
            Counters.Add(advanced ? "ledge.throw_with_run" : "ledge.throw_full");
            _started++;
            Counters.Add("ledge.tumble_started");
            Log.Line(Strings.Format("log.ledge_start", t.Id.Slot, _tracker.StartY / 10f, t.Push.Length() / 10f));
            return;
        }

        Counters.Add("ledge.refused." + why);
        // once per reason, or every 5 s (the driver asks every half second while the push goes on); the counter has every one
        if (why != _lastRefusal || world.Tick - _lastRefusalTick >= 150)
        {
            Log.Line(Strings.Format("log.ledge_refused", t.Id.Slot, why));
            (_lastRefusal, _lastRefusalTick) = (why, world.Tick);
        }
    }

    /// <summary>
    /// The way over the lip (LedgeRules.Clear against the building collision near it): null when clear, else why not ("blocked": a parapet or a
    /// railing; "unread": the collision is not read yet; "no_buildings": no collision source, which counts as clear only off buildings, so it is
    /// refused too). Terrain is not tested: the navmesh already put lower ground past the lip.
    /// </summary>
    string? Way(Vector3 feet, Vector3 push)
    {
        if (_combat?.Buildings is not { } env) return "no_buildings";
        var dir = new Vector2(push.X, push.Z);
        float r = LedgeRules.ClearReach + 2;
        float top = LedgeRules.ClearHeights.Max() + 2;
        _triangles.Clear();
        if (!env.Buildings(new Vector3(feet.X - r, feet.Y - 2, feet.Z - r), new Vector3(feet.X + r, feet.Y + top, feet.Z + r), _triangles)) return "unread";
        return LedgeRules.Clear(feet, dir, _triangles) ? null : "blocked";
    }

    /// <summary>
    /// The drop from the start (the feet) to the ground under the body at rest: Meitou's ragdoll ground (terrain or a floor near the body,
    /// IRagdollEnvironment.GroundHeight), not the body's root, which lies about half a metre up (headless run 2026-11-18: 3.9 m read from the
    /// root, 4.4 m feet to feet). The tracker's own drop when there is no environment or the reading is broken or above the root.
    /// </summary>
    float GroundDrop(Vector3 rest, float rootDrop)
    {
        if (_combat?.Buildings is not { } env) return rootDrop;
        float ground = env.GroundHeight(rest.X, rest.Z, rest.Y);
        if (!float.IsFinite(ground) || ground > rest.Y + 1) return rootDrop;
        Counters.Add("ledge.drop_from_ground");
        return MathF.Max(0, _tracker.StartY - ground);
    }

    /// <summary>How far the body's root is above Meitou's ragdoll ground under it (0 when there is no reading).</summary>
    /// <summary>How far the body's root is above the highest ground under it: Meitou's ragdoll ground or the walkable floor near the body
    /// (IWalkability.GroundHeight with the body's height: building floors too; play test 2026-11-20, a watchtower: the body lay on a floor
    /// "22.9 m above the ground" and was nudged three times). 0 when there is no reading.</summary>
    float AboveGround(World world, Vector3 at)
    {
        float ground = float.NegativeInfinity;
        if (_combat?.Buildings is { } env && env.GroundHeight(at.X, at.Z, at.Y) is var g && float.IsFinite(g) && g <= at.Y + 1) ground = g;
        float walk = world.Walkability.GroundHeight(at.X, at.Z, at.Y);
        if (float.IsFinite(walk) && walk <= at.Y + 1) ground = MathF.Max(ground, walk);
        return float.IsFinite(ground) ? MathF.Max(0, at.Y - ground) : 0;
    }

    /// <summary>Not carried and not in a bed or a cage (Meitou's Wanted answers None in furniture, and we never override that).</summary>
    static bool Free(CharacterCold cold) => cold.CarriedBy.IsNone && cold.InSomething == default;

    #endregion

    #region The landing

    void Finish(World world, CharacterCold? cold, int slot, (TumbleEnd End, float Drop) result)
    {
        Clear();
        _ended++;
        Counters.Add("ledge.end." + result.End.ToString().ToLowerInvariant());
        // the height that hurts: the free-fall height of the fastest downward speed, at most the drop (LedgeRules.DamageHeight)
        float height = LedgeRules.DamageHeight(result.Drop, _tracker.AirDrop);
        string hurt = cold is null || result.End is not (TumbleEnd.Rest or TumbleEnd.Timeout) ? "-" : Hurt(world, cold, slot, height);
        Log.Line(Strings.Format("log.ledge_end", result.End, result.Drop / 10f, hurt, _tracker.MaxDownSpeed / 10f, height / 10f));
    }

    /// <summary>The fall's blunt damage on each leg (LedgeRules.LegBlunt, capped per leg by CapForLeg); a summary for the log.</summary>
    string Hurt(World world, CharacterCold cold, int slot, float drop)
    {
        float blunt = LedgeRules.LegBlunt(drop), torso = LedgeRules.TorsoBlunt(drop), head = LedgeRules.HeadBlunt(drop);
        if (blunt <= 0)
        {
            Counters.Add("ledge.safe_drop");
            return "none";
        }

        if (cold.Medical is not { Dead: false } medical || cold.Race is not { } race) return "no_body";
        var ctx = new MedicalContext(_data.Bodies.Constants, _data.Bodies.Options, race)
        {
            Toughness = cold.Stats?.ScaledToughness ?? 0,
            Strength = cold.Stats?.Strength ?? 50,
            HasStats = cold.Stats is not null,
            Seed = world.Seed,
            CharacterKey = Rng.Key(world.Characters.IdOf(slot)),
        };

        float perm = _data.Bodies.Constants.BluntPermanentOrganDamage;
        var events = new List<MedicalEvent>();
        var parts = new List<string>();
        for (int i = 0; i < medical.Parts.Count && !medical.Dead; i++)
        {
            var p = medical.Parts[i];
            float b;
            if (p.IsLeg)
            {
                b = LedgeRules.CapForLeg(blunt, p.Flesh, p.MaxHp, perm);   // a leg breaks, never comes off
                if (b < blunt) Counters.Add("ledge.leg_capped");
            }
            // the torso and the head (2026-11-20): no cap, a high enough fall kills as any blow to a vital part does
            else if (p.Template.Type == global::Meitou.Data.Gameplay.Bodies.BodyPartType.Torso) b = torso;
            else if (p.Template.Type == global::Meitou.Data.Gameplay.Bodies.BodyPartType.Head) b = head;
            else continue;
            if (b <= 0) continue;

            medical.ApplyHit(i, new HitDamage(0, b, 0), ctx, events);
            parts.Add($"{p.Template.StringId} {b:0}");
        }
        if (medical.Dead) Counters.Add("ledge.fall_death");

        Counters.Add("ledge.legs_hurt");
        foreach (var e in events) Counters.Add("ledge.event." + e.Kind.ToString().ToLowerInvariant());
        string ev = events.Count == 0 ? "" : " (" + string.Join(", ", events.Select(e => e.Kind)) + ")";
        return string.Join("; ", parts) + ev;
    }

    void Clear()
    {
        s_tumbler = null;
        s_tumblingId = null;
        if (_tracker.Active) _tracker.Abort();
    }

    #endregion
}

/// <summary>
/// The postfix on Meitou's private static RagdollSystem.Wanted(CharacterCold): Whole for the one tumbling character, nothing else changed. Found
/// by name and checked (static, one CharacterCold parameter, returns RagdollPart); missing or different -> no patch, a line in the log, and the
/// setting falls back to Stop. The daily Meitou watch is the backstop for a release that changes it.
/// </summary>
public static class LedgePatch
{
    /// <summary>The patch is on.</summary>
    public static bool Installed { get; private set; }
    static long s_fired;

    public static string Apply(Harmony h)
    {
        var m = AccessTools.Method(typeof(RagdollSystem), "Wanted", [typeof(CharacterCold)]);
        if (m is null || !m.IsStatic || m.ReturnType != typeof(RagdollPart))
        {
            Counters.Add("ledge.patch_missing");
            return Strings.Get("log.ledge_patch_missing");
        }

        try
        {
            h.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(LedgePatch), nameof(WantedPostfix))));
            Installed = true;
            return Strings.Get("log.ledge_patch_on");
        }
        catch (Exception e)
        {
            Counters.Add("ledge.patch_failed");
            return Strings.Format("log.ledge_patch_failed", e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>Positional (__0) so a renamed parameter does not break it. Leaves carried / furniture answers alone (Meitou's own).</summary>
    static void WantedPostfix(CharacterCold __0, ref RagdollPart __result)
    {
        try
        {
            if (__0 is null || !TumbleSystem.Marked(__0)) return;
            if ((__result & (RagdollPart.Whole | RagdollPart.CarryMode)) != 0) return;
            if (!__0.CarriedBy.IsNone || __0.InSomething != default) return;

            __result = RagdollPart.Whole;
            Interlocked.Increment(ref s_fired);
        }
        catch
        {
            // never reaches the game: Meitou's own answer stands
        }
    }

    /// <summary>How many times the postfix answered Whole (the falsifier for an inlined Wanted: counts rise but WasWhole stays false).</summary>
    public static long Fired => Interlocked.Read(ref s_fired);
}
