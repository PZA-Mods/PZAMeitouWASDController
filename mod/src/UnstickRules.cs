// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The way out when the step planner leaves a pushed walker standing (0.11.0, play test 2026-11-20: after a ledge fall the
// character got up on a steep slope and "basically couldn't move"; log: every step "stopped", the slope read as a drop too small to tumble
// one way and a rise too steep the other). Pure: the offline check and the fuzzer drive it; Driver.Steer applies it.
// - Stuck: the stick or W A S D pushed, and the character has not moved Progress for After seconds of game time (pausing does not count).
// - Way out: one plain Meitou move order Reach ahead along the push, at the world's ground there, so Meitou's own pathfinder takes the
//   character off the slope; the planner's orders wait Hold seconds so they do not cancel it.
// - Never at a ledge: not when the planner stopped at a drop of LedgeRules.MinFall or more, and not when the ground Reach ahead lies further
//   than the limit from the character's height (Ledges on Fall: MinFall, what the mod counts as no ledge; Ledges on Stop: 0.75 m, the
//   stair rule's most).
namespace PZA.WASDControl.Meitou;

public static class UnstickRules
{
    /// <summary>World units the character must move to count as moving (20 cm).</summary>
    public const float Progress = 2f;
    /// <summary>Game seconds of pushing without Progress before the way out.</summary>
    public const double After = 0.6;
    /// <summary>How far ahead the way out aims (1 m).</summary>
    public const float Reach = 10f;
    /// <summary>Game seconds the planner leaves the way out's order alone.</summary>
    public const double Hold = 1.0;
    /// <summary>With Ledges on Stop, the most the ground Reach ahead may lie from the character's height (0.75 m).</summary>
    public const float StopLimit = 7.5f;

    /// <summary>
    /// The way back (play test 2026-11-20: "after the standing jump the character gets stuck on odd terrain"; log: after jumps pushed at
    /// rocks every step read "edge" and seven ways out ahead freed nothing). When the way ahead is not allowed, or MaxAheadTries of it
    /// moved nothing, one Meitou move order back to the last spot the planner could walk from (on the navmesh), when that is at most
    /// BackReach away and further than Progress.
    /// </summary>
    public const int MaxAheadTries = 2;
    public const float BackReach = 50f;

    public enum Way { None, Ahead, Back }

    /// <summary>Which way out, if any: <paramref name="aheadOk"/> is Escape's answer; <paramref name="aheadTries"/> the ways ahead sent
    /// since the walker last moved; <paramref name="back"/> the flat distance to the last walkable spot (NaN: none known).</summary>
    public static Way Choose(bool stuck, bool aheadOk, int aheadTries, float back, bool atDrop = false)
    {
        if (!stuck) return Way.None;
        if (aheadOk && aheadTries < MaxAheadTries) return Way.Ahead;
        // never at a drop: a walker pushing out at a lip (Ledges on Stop) stands there as before
        return !atDrop && float.IsFinite(back) && back > Progress && back <= BackReach ? Way.Back : Way.None;
    }

    /// <summary>The height limit for the ground ahead: Ledges on Fall MinFall, else StopLimit.</summary>
    public static float Limit(bool fall) => fall ? LedgeRules.MinFall : StopLimit;

    /// <summary>Whether to send the way out now. <paramref name="stuckSeconds"/>: game seconds pushed without Progress;
    /// <paramref name="bigDrop"/>: the planner stopped at a drop of MinFall or more; <paramref name="y"/> / <paramref name="groundAhead"/>:
    /// the character's height and the world's ground Reach ahead.</summary>
    public static bool Escape(double stuckSeconds, bool bigDrop, float y, float groundAhead, float limit) =>
        !bigDrop && double.IsFinite(stuckSeconds) && stuckSeconds >= After && float.IsFinite(y) && float.IsFinite(groundAhead)
        && float.IsFinite(limit) && MathF.Abs(groundAhead - y) < limit;
}
