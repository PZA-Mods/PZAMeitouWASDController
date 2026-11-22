// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The planned leap (0.11.0, 2026-11-20; finding F171):
// a pushed jump at a gap, a lip or a low obstacle looks for walkable ground past it and carries the standing character there along an arc, so
// it can leave the walkable surface for the flight (Meitou's root motion never does, F164). Pure rules, no Meitou types: the offline check and
// the fuzzer drive them with made-up ground.
// - Plan: landing points MinReach..MaxReach ahead (past the obstacle), the first walkable one no more than MaxRise above or MaxDrop below the
//   take-off; the feet's arc (linear height from take-off to landing plus FeetClear * 4 s (1 - s), the clip's own foot clearance) must stay
//   above the ground under it at every Sample; the body's segments (feet, waist, head) must cross no building triangle.
// - Flight: the clip (pza_jump_forward, its ground travel overridden) leaves the ground at its take-off and lands at its touch-down; between
//   them the character moves linearly in X / Z, its height by HeightShare (the clip draws the rise itself); before take-off it stands at From, after touch-down at To.
using System.Numerics;

namespace PZA.WASDControl.Meitou;

/// <summary>A planned leap: where it leaves the ground and where it lands (world units).</summary>
public readonly record struct LeapPlan(Vector3 From, Vector3 To);

public static class LeapRules
{
    /// <summary>World units (decimetres): the nearest and farthest landing (1.5 to 4 m), the probe step, how far past the obstacle a landing
    /// must be, the most the landing may rise above or drop below the take-off (1 m up, 3 m down: deeper is the ledge fall's tumble).</summary>
    public const float MinReach = 15f, MaxReach = 40f, Step = 2.5f, Beyond = 3f, MaxRise = 10f, MaxDrop = 30f;
    /// <summary>The feet's highest point above the straight line take-off to landing (the clip's measured foot clearance, 0.72 m).</summary>
    public const float FeetClear = 7.2f;
    /// <summary>Room a landing needs above it (2 m): no walkable layer between 0.2 m and this over the landing (headless run 2026-11-21: a landing
    /// on the ground 1.3 m under a platform was taken by Meitou for a hole in the mesh, lifted onto the platform and walked off).</summary>
    public const float Headroom = 20f, CeilingFrom = 2f;
    /// <summary>The arc is tested every Sample world units; the body at these heights above the feet (feet, waist, head).</summary>
    public const float Sample = 2.5f;
    public static readonly float[] BodyHeights = [1f, 9f, 17f];

    /// <summary>
    /// The landing for a leap from <paramref name="from"/> (the feet) along <paramref name="dir"/> (X / Z), past an obstacle
    /// <paramref name="past"/> world units ahead (where the walk ahead stopped); null with the reason in <paramref name="why"/>.
    /// <paramref name="ground"/>(x, z, nearY): the height of the ground nearest nearY (NaN: none); <paramref name="walkable"/>(x, z, y): one
    /// can stand there.
    /// </summary>
    public static LeapPlan? Plan(Vector3 from, Vector2 dir, float past, Func<float, float, float, float> ground, Func<float, float, float, bool> walkable,
        out string why)
    {
        why = "broken";
        if (!Finite(from) || !float.IsFinite(dir.X) || !float.IsFinite(dir.Y) || dir.Length() < 1e-3f || !float.IsFinite(past)) return null;
        var d = Vector2.Normalize(dir);
        float start = MathF.Max(MinReach, MathF.Max(0, past) + Beyond);
        why = "too_far";
        if (start > MaxReach) return null;
        why = "no_landing";
        for (float r = start; r <= MaxReach + 1e-3f; r += Step)
        {
            float x = from.X + d.X * r, z = from.Z + d.Y * r;
            float y = ground(x, z, from.Y);
            if (!float.IsFinite(y) || y > from.Y + MaxRise || y < from.Y - MaxDrop || !walkable(x, z, y)) continue;
            if (LowCeiling(x, z, y, ground)) { why = "low_ceiling"; continue; }
            var to = new Vector3(x, y, z);
            if (!ArcClear(from, to, ground))
            {
                why = "arc_blocked";   // a farther landing crosses the same ground: no leap
                return null;
            }
            why = "";
            return new LeapPlan(from, to);
        }
        return null;
    }

    /// <summary>Whether a walkable layer lies between CeilingFrom and Headroom above (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).</summary>
    public static bool LowCeiling(float x, float z, float y, Func<float, float, float, float> ground)
    {
        float up = ground(x, z, y + Headroom);
        return float.IsFinite(up) && up > y + CeilingFrom && up <= y + Headroom;
    }

    /// <summary>The feet's height over the line at <paramref name="s"/> (0..1) of the flight.</summary>
    public static float Lift(float s) => FeetClear * 4 * s * (1 - s);

    /// <summary>
    /// The share of the height change made by <paramref name="s"/> (0..1) of the flight: down, s squared (slow off the edge, fast at the end, as
    /// gravity; fuzz find 2026-11-20: a straight line sank the feet under the take-off edge at once on a 2.9 m leap down); up, 1 - (1 - s) squared
    /// (the rise early, over the far edge).
    /// </summary>
    public static float HeightShare(float s, bool down) => down ? s * s : 1 - (1 - s) * (1 - s);

    /// <summary>The character's place (the feet, without the clip's own lift) at <paramref name="s"/> (0..1) of the flight.</summary>
    public static Vector3 Base(LeapPlan p, float s)
    {
        s = Math.Clamp(s, 0, 1);
        var at = Vector3.Lerp(p.From, p.To, s);
        return at with { Y = p.From.Y + (p.To.Y - p.From.Y) * HeightShare(s, p.To.Y < p.From.Y) };
    }

    /// <summary>The feet at <paramref name="s"/> (0..1) of the flight (the place plus the clip's foot clearance).</summary>
    public static Vector3 Feet(LeapPlan p, float s)
    {
        var at = Base(p, s);
        return at with { Y = at.Y + Lift(Math.Clamp(s, 0, 1)) };
    }

    /// <summary>Whether the feet's arc stays above the ground at every Sample (the first and last Sample are the take-off and landing ground).</summary>
    public static bool ArcClear(Vector3 from, Vector3 to, Func<float, float, float, float> ground)
    {
        float len = new Vector2(to.X - from.X, to.Z - from.Z).Length();
        if (!float.IsFinite(len)) return false;
        int n = Samples(len);
        var p = new LeapPlan(from, to);
        for (int i = 1; i < n; i++)
        {
            var f = Feet(p, i / (float)n);
            float g = ground(f.X, f.Z, f.Y);
            if (float.IsFinite(g) && g > f.Y) return false;
        }
        return true;
    }

    /// <summary>Whether the body (feet, waist, head segments along the arc) crosses none of <paramref name="triangles"/> (a building's walls).</summary>
    public static bool BodyClear(LeapPlan p, IReadOnlyList<Vector3> triangles)
    {
        if (!Finite(p.From) || !Finite(p.To)) return false;
        float len = new Vector2(p.To.X - p.From.X, p.To.Z - p.From.Z).Length();
        int n = Samples(len);
        for (int i = 0; i < n; i++)
        {
            var a = Feet(p, i / (float)n);
            var b = Feet(p, (i + 1) / (float)n);
            foreach (float h in BodyHeights)
            {
                var up = new Vector3(0, h, 0);
                for (int t = 0; t + 2 < triangles.Count; t += 3)
                    if (LedgeRules.SegmentHits(a + up, b + up, triangles[t], triangles[t + 1], triangles[t + 2])) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Where the leaper is at clip time <paramref name="clipTime"/> (seconds) of a clip that leaves the ground at <paramref name="takeoff"/> and
    /// lands at <paramref name="landing"/>: From before, To after, linear between (the clip draws the rise). Null for a broken reading.
    /// </summary>
    public static Vector3? At(LeapPlan p, float clipTime, float takeoff, float landing)
    {
        if (!float.IsFinite(clipTime) || !float.IsFinite(takeoff) || !float.IsFinite(landing) || landing <= takeoff) return null;
        float s = Math.Clamp((clipTime - takeoff) / (landing - takeoff), 0, 1);
        return Base(p, s);
    }

    /// <summary>The box (min, max) the body's arc sweeps, for reading the building triangles near it.</summary>
    public static (Vector3 Min, Vector3 Max) Box(LeapPlan p)
    {
        float top = BodyHeights.Max() + FeetClear + 1;
        return (Vector3.Min(p.From, p.To) - new Vector3(2, 2, 2), Vector3.Max(p.From, p.To) + new Vector3(2, top, 2));
    }

    /// <summary>How many pieces a flight of <paramref name="len"/> world units is tested in: one per Sample, at least 2; a length a hair over a
    /// whole number of samples (float noise: 15.000004 for 15) counts as that number (fuzz find 2026-11-20: the extra sample point made the same
    /// leap pass or fail by rounding).</summary>
    public static int Samples(float len) => !float.IsFinite(len) ? 2 : Math.Max(2, (int)MathF.Ceiling(len / Sample - 1e-3f));

    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
