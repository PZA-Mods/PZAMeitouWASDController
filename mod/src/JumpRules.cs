// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The jump's pure rules (0.11.0, 2026-11-19). No Meitou types, so the
// offline check and the fuzzer drive them directly. JumpSystem plays the chosen clip as a combat move; the host (Driver) picks it.
// - Which clip: standing -> jump_up; the stick or W A S D pushed with 2 m of level walkable ground ahead -> jump_forward (it relocates 1.8 m
//   along the walker's facing); pushed with no such ground -> jump_up in place, never into a wall or over a lip (F164: the root step only
//   slides along walkable ground, so this is the honest jump).
// - Refused: on ground steeper than MaxSlopeDegrees, while one plays (and its cooldown), down, carried, in a bed or cage, tumbling.
// - The pad (user 2026-11-19: "What if it is ... (A) ... when not looking at something, will jump?"): A on a person, door or container gives
//   the safe action as in 0.10; A with nothing under the crosshair jumps. No double tap, no delay.
// All seven clips ship, the stride clips (jump_walk / jump_run) included (the user, 2026-11-20: "Keep all 7"; it replaces the 2026-11-19
// "Only the five safe").
namespace PZA.WASDControl.Meitou;

public enum JumpClip { None, Up, Forward, Down, LandSoft, LandHard, Walk, Run }

public enum TapResult { None, Safe, Jump }

public enum PressResult { Now, Buffer, Drop }

public static class JumpRules
{
    /// <summary>The clip names in the animation files and the techniques' <c>anim name</c> (pza.jump.contract version 1).</summary>
    public static string Name(JumpClip c) => c switch
    {
        JumpClip.Up => "pza_jump_up",
        JumpClip.Forward => "pza_jump_forward",
        JumpClip.Down => "pza_jump_down",
        JumpClip.LandSoft => "pza_land_soft",
        JumpClip.LandHard => "pza_land_hard",
        JumpClip.Walk => "pza_jump_walk",
        JumpClip.Run => "pza_jump_run",
        _ => "",
    };

    /// <summary>Each clip's length in seconds, measured from the written file (contract length_s).</summary>
    public static float Length(JumpClip c) => c switch
    {
        JumpClip.Up => 2.37f,
        JumpClip.Forward => 2.37f,
        JumpClip.Down => 2.44f,
        JumpClip.LandSoft => 1.25f,
        JumpClip.LandHard => 2.72f,
        JumpClip.Walk => 1.61f,
        JumpClip.Run => 0.7837f,
        _ => 0,
    };

    /// <summary>Take-off and touch-down in each clip, seconds (pza_jumps.contract.json takeoff_s / landing_s, measured from the file).</summary>
    public static float Takeoff(JumpClip c) => c switch
    {
        JumpClip.Up or JumpClip.Forward => 0.668f,
        JumpClip.Down => 0.904f,
        JumpClip.Walk => 0.101f,
        JumpClip.Run => 0.1f,
        _ => 0,
    };
    public static float Landing(JumpClip c) => c switch
    {
        JumpClip.Up => 1.218f,
        JumpClip.Forward => 1.235f,
        JumpClip.Down => 1.256f,
        JumpClip.LandSoft => 0.283f,
        JumpClip.LandHard => 0.584f,
        JumpClip.Walk => 0.403f,
        JumpClip.Run => 0.484f,
        _ => 0,
    };

    /// <summary>
    /// The stride jumps (user 2026-11-20: "Bring in the 'unsafe' jumps"; LOCAL TEST DATA, their frames are built with the Kenshi gait curves):
    /// jumped mid-walk or mid-run, in place in the clip (no ground travel of their own), so the walk or run's own move goes on under them.
    /// A pushed jump while the character moves at least MovingSpeed takes jump_walk, at least RunSpeed jump_run (world units/s, measured
    /// by the host from the position: Meitou's walk is about 1.5 m/s, its run 5 m/s and more).
    /// </summary>
    public static bool Stride(JumpClip c) => c is JumpClip.Walk or JumpClip.Run;

    /// <summary>
    /// The fastest a character can really cross the ground (world units/s; 20 m/s, well above Kenshi's fastest run). A reading above it is a
    /// teleport, a load or a switch of character, not a pace (the jump session, 2026-11-21: "right after a teleport or load, Driver.MeasureSpeed
    /// reads a huge speed, so the next jump picks pza_jump_run").
    /// </summary>
    public const float MaxGroundSpeed = 200f;

    /// <summary>The ground speed from <paramref name="distance"/> covered in <paramref name="seconds"/>: 0 for a broken reading or one above
    /// MaxGroundSpeed.</summary>
    public static float SpeedReading(float distance, float seconds)
    {
        if (!float.IsFinite(distance) || !float.IsFinite(seconds) || seconds <= 0 || distance < 0) return 0;
        float v = distance / seconds;
        return v > MaxGroundSpeed ? 0 : v;
    }
    public const float MovingSpeed = 8f, RunSpeed = 30f;

    /// <summary>
    /// Play test 2026-11-20 ("the recovery feels slow"): each jump opens with a 0.67 s crouch and stands back up for 1.1 s after touch-down.
    /// A jump while the player pushes a direction starts QuickLead before take-off (most of the crouch skipped), and every jump hands control
    /// back ControlAfterLanding after touch-down: Meitou's own walk or idle takes over from there.
    /// </summary>
    public const float QuickLead = 0.2f, ControlAfterLanding = 0.25f;

    /// <summary>Where in the clip the jump starts (seconds): QuickLead before take-off for a quick (pushed) jump, else 0.</summary>
    public static float From(JumpClip c, bool quick) => quick && Takeoff(c) > QuickLead ? Takeoff(c) - QuickLead : 0;

    /// <summary>Where in the clip control comes back (seconds): ControlAfterLanding after touch-down for a jump; the whole landing clips.</summary>
    public static float Until(JumpClip c) => c is JumpClip.Up or JumpClip.Forward or JumpClip.Down or JumpClip.Walk or JumpClip.Run
        ? MathF.Min(Length(c), Landing(c) + ControlAfterLanding) : Length(c);

    /// <summary>The seconds of the clip that play.</summary>
    public static float Span(JumpClip c, bool quick) => MathF.Max(0, Until(c) - From(c, quick));

    /// <summary>The clip's own progress 0..1 for the played window's progress <paramref name="window"/> (0..1).</summary>
    public static float ClipProgress(JumpClip c, bool quick, float window) =>
        Length(c) <= 0 ? 0 : (From(c, quick) + Math.Clamp(window, 0, 1) * Span(c, quick)) / Length(c);

    /// <summary>Seconds after a clip ends before the next jump (design 5: "the clip's length + 0.3 s").</summary>
    public const float Cooldown = 0.15f;

    /// <summary>
    /// The press buffer (play test 2026-11-20: "the jump stopped working all of a sudden"; log: 37 of 96 presses refused "busy", pressed within
    /// the second a running jump and its cooldown take): a press at most BufferSeconds before the character is free again is kept and jumps
    /// the moment it is; an earlier press is dropped as before.
    /// </summary>
    public const float BufferSeconds = 0.35f;

    /// <summary>What a press does: Now (free), Buffer (free within BufferSeconds), Drop. <paramref name="readyAt"/>: the tick the character
    /// is free (long.MinValue: free).</summary>
    public static PressResult Press(long tick, long readyAt, float tickSeconds)
    {
        if (tick >= readyAt) return PressResult.Now;
        if (!float.IsFinite(tickSeconds) || tickSeconds <= 0) return PressResult.Drop;
        return (readyAt - tick) * (double)tickSeconds <= BufferSeconds ? PressResult.Buffer : PressResult.Drop;
    }
    /// <summary>No standing jump on ground steeper than this (the clip has no foot IK; design 3c).</summary>
    public const float MaxSlopeDegrees = 25f;
    /// <summary>World units of walkable ground ahead a forward jump needs (2 m; it travels 1.8 m).</summary>
    public const float ClearAhead = 20f;
    /// <summary>The ground ahead may rise or fall this much over ClearAhead (world units): a slope up to MaxSlopeDegrees, about 0.73 m over
    /// the 2 m. Play test 2026-11-20: at 30 cm (the first test build) almost no outdoor ground passed, and every running jump was the standing
    /// one (log: jump.choose.in_place 4 of 4).</summary>
    public static readonly float LevelWithin = ClearAhead * MathF.Tan(MaxSlopeDegrees * MathF.PI / 180f);
    /// <summary>What A does: the safe action when the crosshair is on something (a person, a door, a container), else the jump.</summary>
    public static TapResult APress(bool targeted) => targeted ? TapResult.Safe : TapResult.Jump;

    /// <summary>The direction a character with yaw <paramref name="yaw"/> faces on X / Z (Meitou: (sin, cos); measured in the headless run of
    /// 2026-11-19, where yaw 3.14 and jump_forward moved the character 1.8 m toward -Z).</summary>
    public static System.Numerics.Vector2 Facing(float yaw) => new(MathF.Sin(yaw), MathF.Cos(yaw));

    /// <summary>The yaw that faces the X / Z direction (<paramref name="x"/>, <paramref name="z"/>); the inverse of <see cref="Facing"/>.</summary>
    public static float YawOf(float x, float z) => MathF.Atan2(x, z);

    /// <summary>The play time (Span) in ticks of <paramref name="tickSeconds"/> (at least 1), 0 for no clip or a broken tick.</summary>
    public static int Ticks(JumpClip c, float tickSeconds, bool quick = false) =>
        c == JumpClip.None || !float.IsFinite(tickSeconds) || tickSeconds <= 0 ? 0 : Math.Max(1, (int)MathF.Round(Span(c, quick) / tickSeconds));

    /// <summary>The clip's progress 0..1 at <paramref name="tick"/> for a jump that started at <paramref name="start"/> and lasts
    /// <paramref name="ticks"/>; null before its start or once it has ended.</summary>
    public static float? Progress(long start, int ticks, long tick)
    {
        if (ticks <= 0 || tick < start || tick >= start + ticks) return null;
        return (tick - start) / (float)ticks;
    }

    /// <summary>The slope in degrees between two ground heights <paramref name="run"/> world units apart; 90 for a broken reading.</summary>
    public static float SlopeDegrees(float behind, float ahead, float run)
    {
        if (!float.IsFinite(behind) || !float.IsFinite(ahead) || !float.IsFinite(run) || run <= 0) return 90;
        return MathF.Atan(MathF.Abs(ahead - behind) / run) * 180f / MathF.PI;
    }

    /// <summary>The clip for a jump, or None with the reason in <paramref name="why"/>. <paramref name="pushed"/>: the player pushes a direction;
    /// <paramref name="clear"/>: the planner walked ClearAhead of level ground that way; <paramref name="slope"/>: degrees under the feet.</summary>
    public static JumpClip Choose(bool pushed, bool clear, float slope, out string why) => Choose(pushed, clear, slope, 0, out why);

    /// <summary>Choose, with the character's measured ground speed <paramref name="speed"/> (world units/s): moving and pushed on clear
    /// ground, the stride jump for its pace.</summary>
    public static JumpClip Choose(bool pushed, bool clear, float slope, float speed, out string why)
    {
        why = "";
        if (!float.IsFinite(slope) || slope > MaxSlopeDegrees)
        {
            why = "slope";
            return JumpClip.None;
        }

        if (!pushed) return JumpClip.Up;
        if (clear)
        {
            if (float.IsFinite(speed) && speed >= RunSpeed) return JumpClip.Run;
            if (float.IsFinite(speed) && speed >= MovingSpeed) return JumpClip.Walk;
            return JumpClip.Forward;
        }

        why = "in_place";   // pushed at a wall, a lip or a rise: the jump stays where it is
        return JumpClip.Up;
    }

    /// <summary>Whether the planner's walk ahead is the clear, walkable ground a forward jump needs: the whole ClearAhead walked ("flat", or
    /// "climb" when it rises or falls more than half a metre; never stopped at a wall or a lip) and no steeper than MaxSlopeDegrees overall.</summary>
    public static bool Clear(string how, float walked, float startY, float endY) =>
        how is "flat" or "climb" && float.IsFinite(walked) && walked >= ClearAhead - 0.01f && float.IsFinite(startY) && float.IsFinite(endY)
        && MathF.Abs(endY - startY) <= LevelWithin;

    /// <summary>Why a character may not jump now (null: it may). <paramref name="playingUntil"/>: the tick the last jump's cooldown ends.</summary>
    public static string? Refuse(bool down, bool carried, bool inSomething, bool tumbling, long tick, long playingUntil)
    {
        if (down) return "down";
        if (carried) return "carried";
        if (inSomething) return "in_something";
        if (tumbling) return "tumbling";
        if (tick < playingUntil) return "busy";
        return null;
    }
}
