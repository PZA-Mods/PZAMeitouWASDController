// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// Falling off ledges (0.11.0; Workshop comment by Pez, 2026-11-18: "wouldn't it be very Kenshi-esque if, upon reaching the
// edge, instead of just getting stuck, you fell off and broke your legs?"). The pure rules, with no game state, so the fuzzer drives them as they
// are: when a push at a lip starts a tumble, the outward throw, how a tumble ends (rest, time limit, a broken reading), the cooldown, and the
// leg damage by height with its caps. Meitou facts behind the numbers (world units are
// 10 to the metre, gravity -98.1 units/s², a whole ragdoll freezes 2 to 7 s after it starts).
using System.Numerics;

namespace PZA.WASDControl.Meitou;

public static class LedgeRules
{
    /// <summary>The least drop that tumbles (1.5 m, from the last walkable height to the first surface past the lip). Smaller drops (a stair's
    /// steep bit, a curb) stay as Stop: headless run 2026-11-19, the tower stairs at The Hub step down 0.66 m within one sample near their
    /// foot and a walk down them tumbled.</summary>
    public const float MinFall = 15f;

    /// <summary>A push outward starts a tumble when the character is within this of the lip's last walkable sample (60 cm), flat distance.</summary>
    public const float LipReach = 6f;
    /// <summary>The throw outward, world units/s (3 m/s): with Advanced ragdolls off the body starts at rest, so this alone must carry it over.</summary>
    public const float PushSpeed = 30f;
    /// <summary>The share of the sideways throw given when Meitou's Advanced ragdolls already carries the run's speed into the body.</summary>
    public const float ThrowWithRun = 0.5f;
    /// <summary>The throw's lift, world units/s (a small hop, so the feet clear the lip's edge instead of catching on it).</summary>
    public const float PushLift = 12f;

    /// <summary>Drops up to this height do nothing (3 m).</summary>
    public const float SafeDrop = 30f;
    /// <summary>
    /// Blunt damage to each leg for every world unit of fall past SafeDrop: 13.5 a metre (play test 2026-11-20: "a stupid amount of fall
    /// damage ... it wasn't that crazy of a drop"; at 25 a metre a 9 m drop broke both legs at the cap). Now 6 m is 40 a leg (hurt, still
    /// walking), 9 m 81 (badly hurt), about 10.4 m 100 (a 100-HP leg broken), the cap from about 14 m.
    /// </summary>
    public const float BluntPerUnit = 1.35f;
    /// <summary>Most blunt damage a fall gives one leg (reached at about 14 m).</summary>
    public const float BluntCap = 150f;
    /// <summary>Meitou's gravity, world units/s² (Meitou's physics).</summary>
    public const float Gravity = 98.1f;

    /// <summary>
    /// The height a fall hurts by: the height the body covered while really falling (TumbleTracker.AirDrop: the descent on ticks faster than
    /// FallingSpeed), never more than the drop measured to the ground; a broken reading falls back to the drop. A roll down a slope after the
    /// landing is slower than FallingSpeed and adds nothing.
    /// Play test 2026-11-20 (the Rebirth bridge, "didn't die"): the first rule, the free-fall height of the fastest downward speed (v² / 2g),
    /// read a 58.3 m drop as 20.4 m: Meitou's ragdolls are damped to about 20 m/s, where a free fall from 58 m reaches about 34 m/s.
    /// </summary>
    public static float DamageHeight(float drop, float airDrop)
    {
        if (!float.IsFinite(drop) || drop <= 0) return 0;
        if (!float.IsFinite(airDrop) || airDrop < 0) return drop;
        return MathF.Min(drop, airDrop);
    }

    /// <summary>World units/s of descent above which the body counts as falling, not rolling (4 m/s; Meitou's ragdolls fall at up to ~20).</summary>
    public const float FallingSpeed = 40f;

    /// <summary>
    /// The torso's (chest and stomach) blunt damage by height, from TorsoSafe, TorsoPerUnit a unit, with no cap: a vital part dies once its
    /// flesh minus stun falls under minus its max HP (Meitou MedicalState.CheckPart), i.e. 2 x MaxHp of blunt from full health. 8 a metre past
    /// 8 m: 16 at 10 m, 96 at 20 m, 200 at 33 m (a 100-HP chest dies), 400 at 58 m (the Rebirth bridge: anyone dies).
    /// </summary>
    public const float TorsoSafe = 80f, TorsoPerUnit = 0.8f;
    /// <summary>The head's: 6 a metre past 10 m (60 at 20 m, 138 at 33 m, 288 at 58 m).</summary>
    public const float HeadSafe = 100f, HeadPerUnit = 0.6f;

    /// <summary>The blunt damage a torso part takes from a fall of <paramref name="height"/> world units (0 for a broken reading).</summary>
    public static float TorsoBlunt(float height) =>
        !float.IsFinite(height) || height <= TorsoSafe ? 0 : (height - TorsoSafe) * TorsoPerUnit;

    /// <summary>The blunt damage the head takes from a fall of <paramref name="height"/> world units (0 for a broken reading).</summary>
    public static float HeadBlunt(float height) =>
        !float.IsFinite(height) || height <= HeadSafe ? 0 : (height - HeadSafe) * HeadPerUnit;
    /// <summary>A fall never takes a leg's flesh below this share of its max HP below zero (Meitou severs a part at -max), so it breaks legs, never
    /// cuts them off.</summary>
    public const float FleshFloorShare = 0.5f;

    /// <summary>The hard time limit of one tumble, game seconds (Meitou freezes a whole ragdoll by 7 s at the latest).</summary>
    public const float MaxSeconds = 6f;
    /// <summary>A tumble that never became a whole ragdoll by this time is given up on (the patch did not take: logged, no damage).</summary>
    public const float WholeWithin = 0.5f;
    /// <summary>The body counts as landed once it moves slower than this (world units/s) ...</summary>
    public const float RestSpeed = 3f;
    /// <summary>... for this long (game seconds; longer than the slow moment at the top of the throw's arc, about 0.12 s).</summary>
    public const float RestSeconds = 0.4f;
    /// <summary>The wait after a tumble ends before another can start, game seconds.</summary>
    public const float CooldownSeconds = 3f;
    /// <summary>
    /// The body counts as on the ground when its root is at most this far above the ground under it (world units; the root lies about half a
    /// metre up on flat ground). Play test 2026-11-20: off Dust King's tower the fall outlasted MaxSeconds and Meitou's own freeze
    /// (RagdollSystem.FreezeLatest, 7 s), and the body hung in the air; a tumble now ends only on the ground, or after HardMaxSeconds.
    /// </summary>
    public const float GroundedWithin = 15f;
    /// <summary>The time limit while the body is still in the air (game seconds).</summary>
    public const float HardMaxSeconds = 20f;
    /// <summary>A body still in the air gets a small throw (Meitou rebuilds a frozen ragdoll for it and its freeze clock starts over) at most
    /// this many times; then the tumble ends where it is (a roof or a beam the ground reading does not see).</summary>
    public const int MaxNudges = 3;
    /// <summary>The nudge: downward and a little outward along the first push (world units/s).</summary>
    public const float NudgeDown = 10f, NudgeOut = 5f;

    /// <summary>
    /// Whether a step at a drop starts a tumble: the setting on Fall, the step stopped at a drop (not a wall) at least MinFall deep
    /// (<paramref name="depth"/>), and the character within
    /// LipReach of the lip's last walkable sample (<paramref name="lip"/>; null when the very first sample was already over the drop, i.e. the
    /// character stands at the lip).
    /// </summary>
    public static bool AtLip(bool fall, bool dropped, Vector3 at, Vector3? lip, float depth)
    {
        if (!fall || !dropped) return false;
        if (!float.IsFinite(depth) || depth < MinFall) return false;
        if (!Finite(at)) return false;
        if (lip is not { } l) return true;
        if (!Finite(l)) return false;

        float dx = l.X - at.X, dz = l.Z - at.Z;
        return dx * dx + dz * dz <= LipReach * LipReach;
    }

    /// <summary>The throw for a push along <paramref name="dir"/> (the steering direction on the ground, X / Z); zero for a broken direction.</summary>
    public static Vector3 Push(Vector2 dir)
    {
        if (!float.IsFinite(dir.X) || !float.IsFinite(dir.Y)) return Vector3.Zero;
        float len = dir.Length();
        if (len < 1e-4f || !float.IsFinite(len)) return Vector3.Zero;

        var d = dir / len;
        return new Vector3(d.X * PushSpeed, PushLift, d.Y * PushSpeed);
    }

    /// <summary>Game seconds a push must be held at a lip before the fall (play test 2026-11-20: brushing an open stairwell's edge while
    /// walking down tumbled the walker).</summary>
    public const double LipHoldSeconds = 0.35;

    /// <summary>Whether a push held this long at a lip commits to the fall (pure).</summary>
    public static bool Committed(double secondsAtLip) => double.IsFinite(secondsAtLip) && secondsAtLip >= LipHoldSeconds;

    /// <summary>
    /// The leap (user 2026-11-20: "can we make it so the game detects if we're JUMPING off the ledge and the player rag dolls from that?"): a
    /// jump pushed toward a drop of MinFall or more within LeapReach (2.5 m) tumbles with this throw instead of playing a clip: 5 m/s out and
    /// 3.5 m/s up (a walk-off is 3 m/s and 1.2 m/s), so the body clears the lip in an arc.
    /// </summary>
    public const float LeapReach = 25f, LeapSpeed = 50f, LeapLift = 35f;

    /// <summary>
    /// The dive (user 2026-11-20: "in GTA you could run, jump, and then hit the R3 joystick to trigger a ragdoll"): R3 pressed while a jump
    /// plays throws the body forward along its facing, 4 m/s out and 2 m/s up, as a tumble (any height; legs hurt only past SafeDrop).
    /// </summary>
    public const float DiveSpeed = 40f, DiveLift = 20f;

    /// <summary>The dive's throw along <paramref name="dir"/> (zero for a broken or zero direction).</summary>
    public static Vector3 DivePush(Vector2 dir)
    {
        var p = Push(dir);
        return p == Vector3.Zero ? p : new Vector3(p.X / PushSpeed * DiveSpeed, DiveLift, p.Z / PushSpeed * DiveSpeed);
    }

    /// <summary>Whether a jump leaps off: the ground ahead stopped at a drop of MinFall or more.</summary>
    public static bool LeapOff(bool dropped, float depth) => dropped && float.IsFinite(depth) && depth >= MinFall;

    /// <summary>The leap's throw along <paramref name="dir"/> (zero for a broken or zero direction).</summary>
    public static Vector3 LeapPush(Vector2 dir)
    {
        var p = Push(dir);
        return p == Vector3.Zero ? p : new Vector3(p.X / PushSpeed * LeapSpeed, LeapLift, p.Z / PushSpeed * LeapSpeed);
    }

    /// <summary>The blunt damage one leg takes from a drop of <paramref name="drop"/> world units: 0 up to SafeDrop, then BluntPerUnit a unit,
    /// at most BluntCap; 0 for a broken reading.</summary>
    public static float LegBlunt(float drop)
    {
        if (!float.IsFinite(drop) || drop <= SafeDrop) return 0;
        return MathF.Min(BluntCap, (drop - SafeDrop) * BluntPerUnit);
    }

    /// <summary>
    /// <paramref name="blunt"/> cut down so the leg keeps flesh above -FleshFloorShare x max HP. Meitou takes blunt x perm from the flesh
    /// (<paramref name="perm"/>: the constant "blunt permanent organ damage"). Never more than asked, never below 0; 0 for broken readings.
    /// </summary>
    public static float CapForLeg(float blunt, float flesh, float maxHp, float perm)
    {
        if (!float.IsFinite(blunt) || blunt <= 0 || !float.IsFinite(flesh) || !float.IsFinite(maxHp) || !float.IsFinite(perm)) return 0;
        if (perm <= 0) return blunt;   // no flesh taken at all: nothing to cap

        // in double: huge readings must not overflow past the floor (fuzz find 2026-11-18: flesh and perm near float.MaxValue)
        double room = (double)flesh + FleshFloorShare * Math.Max(0, (double)maxHp);
        if (room <= 0) return 0;
        return (float)Math.Min(blunt, room / perm);
    }

    /// <summary>The heights above the feet (world units) of the rays that look for a parapet or railing over the lip: knee and chest.</summary>
    public static readonly float[] ClearHeights = [5f, 12f];
    /// <summary>How far out over the lip the rays look (1.5 m: past a wall walkway's parapet).</summary>
    public const float ClearReach = 15f;

    /// <summary>
    /// Whether the way over the lip is clear: rays from <paramref name="feet"/> along <paramref name="dir"/> (X / Z) at ClearHeights, ClearReach
    /// long, meet none of <paramref name="triangles"/> (three points each). Headless run 2026-11-18: The Hub's wall walkway has a waist-high
    /// parapet the navmesh shows only as a strip without surface; the body hit it and fell back onto the walkway. A broken input is not clear.
    /// </summary>
    public static bool Clear(Vector3 feet, Vector2 dir, IReadOnlyList<Vector3> triangles)
    {
        if (!Finite(feet) || !float.IsFinite(dir.X) || !float.IsFinite(dir.Y)) return false;
        float len = dir.Length();
        if (!float.IsFinite(len) || len < 1e-4f) return false;

        var d = new Vector3(dir.X / len, 0, dir.Y / len) * ClearReach;
        foreach (float h in ClearHeights)
        {
            var a = feet + new Vector3(0, h, 0);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
                if (SegmentHits(a, a + d, triangles[t], triangles[t + 1], triangles[t + 2])) return false;
        }
        return true;
    }

    /// <summary>Whether the segment <paramref name="a"/>-<paramref name="b"/> meets the triangle (either side, edges included; Moeller-Trumbore).</summary>
    public static bool SegmentHits(Vector3 a, Vector3 b, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        var dir = b - a;
        var e1 = p1 - p0;
        var e2 = p2 - p0;
        var pv = Vector3.Cross(dir, e2);
        float det = Vector3.Dot(e1, pv);
        if (!float.IsFinite(det) || MathF.Abs(det) < 1e-9f) return false;   // parallel or degenerate

        float inv = 1f / det;
        var tv = a - p0;
        float u = Vector3.Dot(tv, pv) * inv;
        if (u < -1e-6f || u > 1 + 1e-6f) return false;
        var qv = Vector3.Cross(tv, e1);
        float v = Vector3.Dot(dir, qv) * inv;
        if (v < -1e-6f || u + v > 1 + 1e-6f) return false;
        float t = Vector3.Dot(e2, qv) * inv;
        return t >= -1e-6f && t <= 1 + 1e-6f;
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

/// <summary>How a tumble ended.</summary>
public enum TumbleEnd { None, Rest, Timeout, NeverWhole, BadReading, Gone }

/// <summary>
/// One character's tumble, step by step: started at a height, fed the body's position every tick, ended by rest, the time limit, a body that
/// never went limp, a broken reading or the character going away. Then the drop (start height minus the height it ended at; 0 when it ended
/// higher) and the cooldown. One tumble at a time.
/// </summary>
public sealed class TumbleTracker
{
    /// <summary>A tumble is running.</summary>
    public bool Active { get; private set; }
    /// <summary>The height the tumble started at, world units.</summary>
    public float StartY { get; private set; }
    /// <summary>Game seconds since it started.</summary>
    public float Elapsed { get; private set; }
    /// <summary>Game seconds left before another tumble may start.</summary>
    public float CooldownLeft { get; private set; }

    float _still;
    bool _wasWhole;
    /// <summary>The fastest downward speed the whole body reached this tumble (world units/s), for the log.</summary>
    public float MaxDownSpeed { get; private set; }
    /// <summary>The height the whole body covered while falling faster than LedgeRules.FallingSpeed off the ground (world units), for
    /// LedgeRules.DamageHeight. Off the ground: more than GroundedWithin above it on this tick or the one before (the tick that lands from the
    /// air counts; the user, 2026-11-20: "Who's to say I won't also die tumbling down a low grade slope though?": a fast roll down a steep
    /// hillside dropped 4 m/s and more with the body on the ground, and it counted as a fall).</summary>
    public float AirDrop { get; private set; }
    bool _wasAloft;
    Vector3 _last;
    /// <summary>Nudges given this tumble (MaxNudges at most).</summary>
    public int Nudges { get; private set; }
    /// <summary>Set by Step: the body has stopped in the air; the caller gives it a nudge and calls <see cref="Nudged"/>.</summary>
    public bool WantsNudge { get; private set; }

    /// <summary>The caller gave the nudge WantsNudge asked for.</summary>
    public void Nudged()
    {
        WantsNudge = false;
        Nudges++;
        _still = 0;
    }

    /// <summary>Starts a tumble at <paramref name="at"/>; false with the reason when one is running, in its cooldown, or the reading is broken.</summary>
    public bool TryStart(Vector3 at, out string why)
    {
        if (Active) { why = "busy"; return false; }
        if (CooldownLeft > 0) { why = "cooldown"; return false; }
        if (!float.IsFinite(at.X) || !float.IsFinite(at.Y) || !float.IsFinite(at.Z)) { why = "bad_height"; return false; }

        (Active, StartY, Elapsed, _still, _wasWhole, _last, Nudges, WantsNudge, MaxDownSpeed) = (true, at.Y, 0, 0, false, at, 0, false, 0);
        AirDrop = 0;
        _wasAloft = false;
        why = "started";
        return true;
    }

    /// <summary>
    /// One tick of <paramref name="dt"/> game seconds with the body at <paramref name="at"/> (<paramref name="whole"/>: Meitou says the whole
    /// body is a ragdoll; <paramref name="aboveGround"/>: how far its root is above the ground under it, 0 when unknown). Idle, it only runs the
    /// cooldown down. Returns how the tumble ended this tick (None: still going) and the drop.
    /// </summary>
    public (TumbleEnd End, float Drop) Step(float dt, Vector3 at, bool whole, float aboveGround = 0)
    {
        if (!float.IsFinite(dt) || dt < 0) dt = 0;
        if (!Active)
        {
            CooldownLeft = MathF.Max(0, CooldownLeft - dt);
            return (TumbleEnd.None, 0);
        }

        Elapsed += dt;
        if (!float.IsFinite(at.X) || !float.IsFinite(at.Y) || !float.IsFinite(at.Z)) return End(TumbleEnd.BadReading, 0);

        _wasWhole |= whole;
        if (!_wasWhole && Elapsed >= LedgeRules.WholeWithin) return End(TumbleEnd.NeverWhole, 0);

        float speed = dt > 0 ? Vector3.Distance(at, _last) / dt : 0;
        bool aloft = float.IsFinite(aboveGround) && aboveGround > LedgeRules.GroundedWithin;
        if (_wasWhole && dt > 0)
        {
            float down = (_last.Y - at.Y) / dt;
            MaxDownSpeed = MathF.Max(MaxDownSpeed, down);
            if (down >= LedgeRules.FallingSpeed && (aloft || _wasAloft)) AirDrop += _last.Y - at.Y;   // falling through the air, not rolling down a slope
        }
        _wasAloft = aloft;
        _last = at;
        _still = _wasWhole && speed < LedgeRules.RestSpeed ? _still + dt : 0;

        float drop = MathF.Max(0, StartY - at.Y);
        WantsNudge = false;
        if (_still >= LedgeRules.RestSeconds)
        {
            if (!aloft || Nudges >= LedgeRules.MaxNudges) return End(TumbleEnd.Rest, drop);
            WantsNudge = true;   // stopped in the air: Meitou froze it (FreezeLatest) or it caught on something
        }
        if (Elapsed >= LedgeRules.HardMaxSeconds || Elapsed >= LedgeRules.MaxSeconds && !aloft) return End(TumbleEnd.Timeout, drop);
        return (TumbleEnd.None, 0);
    }

    /// <summary>The character went away (gone, carried, in a bed or cage): ends the tumble with no drop.</summary>
    public (TumbleEnd End, float Drop) Abort() => Active ? End(TumbleEnd.Gone, 0) : (TumbleEnd.None, 0);

    (TumbleEnd, float) End(TumbleEnd how, float drop)
    {
        Active = false;
        WantsNudge = false;
        CooldownLeft = LedgeRules.CooldownSeconds;
        return (how, drop);
    }
}
