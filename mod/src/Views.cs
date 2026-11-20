// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The camera views (docs/design.md "Views"): over-the-shoulder and first person, as the RE_Kenshi plugin had them on Ctrl and P; here on
// Z and N (the user's choice, 2026-11-16; wheel steps at the zoom limits maybe later). The rig keeps turning, tilting and following; once a frame,
// right after Meitou's follow camera places the view (SandboxScenario.FollowCamera), the eye is moved to the shoulder or the head.
using System.Numerics;

namespace PZA.WASDControl.Meitou;

public enum View
{
    Normal,
    Shoulder,
    First,
}

/// <summary>What the input asks of the views this control tick.</summary>
public readonly record struct ViewInput(bool Z, bool N);

public static class Views
{
    /// <summary>The view now (Normal while WASD Control is off).</summary>
    public static View Current = View.Normal;

    /// <summary>Over-the-shoulder: the boom, the height of the look point above the feet and its shift to the right (the plugin's ini
    /// defaults OtsDistance 20, OtsHeight 15, OtsSide 4, in Kenshi's units, which Meitou keeps).</summary>
    public const float ShoulderDistance = 20, ShoulderHeight = 15, ShoulderSide = 4;
    /// <summary>First person: how far in front of the head bone the eye sits (out of the face), and the tiny boom that puts the eye there.
    /// 0.10.1 (user 2026-11-18: "EVER SO SLIGHTLY a little too far ahead ... when RUNNING in first person, the hands come up and seemingly
    /// clip through your cam"): 2 -> 1.5, five centimetres back, so the running arms stay in front of the near plane and more of the
    /// character's own chest shows when looking down.</summary>
    public const float FaceAhead = 1.5f, FirstBoom = 1;
    /// <summary>
    /// Carrying someone (play test clip 2026-11-17 19:55: the carried body on the shoulder filled half the over-the-shoulder view, and the first
    /// person eye sat inside it for a moment): the shoulder view looks from higher, further back and further out to the side, over the body;
    /// first person moves the eye further out of the face and a little up, past the body beside the head.
    /// </summary>
    public const float CarryShoulderDistance = 32, CarryShoulderHeight = 22, CarryShoulderSide = 7, CarryFaceAhead = 6, CarryEyeLift = 1.5f;
    /// <summary>The head's height above the feet when the head bone is not drawn (a typical human's, used for a frame or two).</summary>
    public const float HeadFallback = 16;
    /// <summary>
    /// The views' own tilt, radians (WorldCamera's pitch: positive looks down). The strategy rig stops the view direction at 0.2 up
    /// (KenshiCamera.MaxViewY, about 11 degrees), Kenshi's limit for its high camera; the views need to look up at a wall or the sky
    /// (user, 2026-11-17: "I want to be able to look up higher").
    /// </summary>
    public static float Pitch = StartPitch;
    /// <summary>The tilt range of the views: about 70 degrees up to about 83 degrees down (a sleeping bag at your feet).</summary>
    public const float MinPitch = -1.2f, MaxPitch = 1.45f;
    /// <summary>The tilt a view starts at: slightly down, as a person looks ahead while walking.</summary>
    public const float StartPitch = 0.12f;

    /// <summary>Tilts the views by <paramref name="radians"/> (positive looks down), within the range.</summary>
    public static void Tilt(float radians) => Pitch = Math.Clamp(Pitch + radians, MinPitch, MaxPitch);

    /// <summary>
    /// The tilt the camera shows this frame: <see cref="Pitch"/> eased in over <see cref="EaseSeconds"/>. Play test 2026-11-17 ("VERY jittery"
    /// looking up and down with the stick): the tilt changed once per control tick (30 Hz) while frames run at up to 150 Hz, so the view moved in
    /// steps; the yaw is Meitou's rig's, which eases on its own. A view that was not shown for a moment starts at the tilt itself.
    /// </summary>
    public static float ShownPitch { get; private set; } = StartPitch;
    public const float EaseSeconds = 0.035f;
    static long s_shownAt;

    /// <summary>Once a frame (a view on): moves <see cref="ShownPitch"/> toward <see cref="Pitch"/>; returns it.</summary>
    public static float EasePitch()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        float dt = s_shownAt == 0 ? 1 : (float)System.Diagnostics.Stopwatch.GetElapsedTime(s_shownAt, now).TotalSeconds;
        s_shownAt = now;
        ShownPitch = Ease(ShownPitch, Pitch, dt);
        return ShownPitch;
    }

    /// <summary>The eased tilt after <paramref name="dt"/> seconds; a gap over 0.2 s (a view just turned on) jumps straight there.</summary>
    public static float Ease(float shown, float target, float dt) =>
        dt <= 0 ? shown : dt > 0.2f ? target : target + (shown - target) * MathF.Exp(-dt / EaseSeconds);

    /// <summary>The next view: Z toggles over-the-shoulder, N first person (each from any view).</summary>
    public static View Next(View current, ViewInput i)
    {
        if (i.Z) return current == View.Shoulder ? View.Normal : View.Shoulder;
        if (i.N) return current == View.First ? View.Normal : View.First;
        return current;
    }

    /// <summary>Applies a change of view: counted, logged, shown on screen.</summary>
    public static void Set(View next, Action<string> notice)
    {
        if (next == Current) return;

        if (Current == View.Normal) Pitch = StartPitch;   // entering a view from the normal camera: look ahead
        if (next != View.Shoulder) ViewLens.Release("view " + next.ToString().ToLowerInvariant());
        Current = next;
        Counters.Add("view." + next.ToString().ToLowerInvariant());
        string text = Strings.Get("notice.view." + next.ToString().ToLowerInvariant());
        Log.Line(Strings.Format("log.view", text));
        notice(text);
    }

    /// <summary>
    /// The over-the-shoulder boom: full while looking level or down; looking up it shortens so the eye stays above the ground (the eye sits
    /// below the look point by boom * sin(-pitch)), as a game camera pulls in when it tilts up: at most ShoulderHeight - 4 below the look point.
    /// </summary>
    public static float ShoulderBoom(float pitch, bool carrying = false)
    {
        float distance = carrying ? CarryShoulderDistance : ShoulderDistance, height = carrying ? CarryShoulderHeight : ShoulderHeight;
        float down = -MathF.Sin(pitch);
        if (down <= 0) return distance;

        return MathF.Max(MathF.Min(distance, (height - 4) / down), 6);
    }

    /// <summary>
    /// The orbit camera's look point and boom for a view (WorldCamera: eye = target + back · distance, back = (cos p · sin y, sin p, cos p · cos y)).
    /// Over-the-shoulder: the look point above the feet, shifted to the camera's right. First person: the eye just in front of the head.
    /// <paramref name="carrying"/>: the controlled character carries someone (the Carry* values).
    /// </summary>
    public static (Vector3 Target, float Distance) Pose(View view, Vector3 feet, Vector3? head, float yaw, float pitch, float side = 1, bool carrying = false)
    {
        var back = new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
        var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
        if (view == View.Shoulder)
        {
            float height = carrying ? CarryShoulderHeight : ShoulderHeight, shift = carrying ? CarryShoulderSide : ShoulderSide;
            return (feet + new Vector3(0, height, 0) + right * (shift * side), ShoulderBoom(pitch, carrying));
        }

        var forward = new Vector3(-MathF.Sin(yaw), 0, -MathF.Cos(yaw));
        var eye = (head ?? feet + new Vector3(0, HeadFallback, 0)) + forward * (carrying ? CarryFaceAhead : FaceAhead)
            + new Vector3(0, carrying ? CarryEyeLift : 0, 0);
        return (eye - back * FirstBoom, FirstBoom);
    }
}
