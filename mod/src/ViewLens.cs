// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The over-the-shoulder view's field of view (0.10.1, Discord 2026-11-18: "increase the fov of third person view"). Meitou's lens is
// WorldCamera.FieldOfView (50 degrees by default). Others that set it, and how this stays out of their way:
//   Photo mode       takes the rig over (the free camera), so the views skip their frames (Driver.OnFrame); it saves the lens it found (ours)
//                    and puts it back when it closes, and the view carries on. Nothing is handed back while the free camera has the view.
//   PZA's Camera     its 0.5x lens (Wide lens) stands down while a WASD view is on (its Access.WasdViewOn) and hands its lens back in its own
//                    PlaceSceneCameras postfix, which runs AFTER the views' FollowCamera postfix. So the first frame of the view only waits
//                    (SettleFrames), and the game's lens is read the frame after, once PZA's Camera has put its own back. Leaving the view hands
//                    the lens back at once (Views.Set, Driver.Toggle), between frames, so PZA's Camera reads the game's lens again, not ours.
//                    Leaving the view while photo mode is open (fuzz view_lens, seeds 1295403446 and 1273010001: view on, photo on, view off,
//                    photo off left the lens at the view's for good): the hand-back waits while the free camera has the view, because photo
//                    mode puts our lens back when it closes; the next frame after that gives the game's back.
//   Director's Kit   its shots own the camera and lens while CameraApi.Busy (agreed 2026-11-21, after a play test where the view and the
//   shots            shots wrote the lens every frame): the view Yields for the shot, and the first frame after it writes ours again WITHOUT
//                    taking the shot's lens as the game's (the Kit leaves the lens alone at hand-back while a WASD view is on).
//   Anything else    a lens that is not ours when we come to write is the game's new one (kept and given back later); a lens changed while
//                    ours is not given back over.
//   Blind spot       a lens set by someone else to exactly ours looks like ours (fuzz seed 361952058: the view at 50, leaving the dev free
//                    camera resets the lens to 50, then the view hands back the 40 it had read). Harmless in play: in the views nothing but
//                    Meitou's default 50 is the game's lens (PZA's Camera stands down).
using Meitou.Game.Rendering.Frame;

namespace PZA.WASDControl.Meitou;

public static class ViewLens
{
    public const float DefaultDeg = 50, MinDeg = 30, MaxDeg = 110;   // 110 (fp-fov): first person goes wider (Action RPG uses 90 vertical)
    public const int SettleFrames = 1;

    static WorldCamera? s_cam;
    static float s_ours = float.NaN, s_game = float.NaN;
    static int s_settle;

    /// <summary>The free camera (photo mode, the dev free camera) has the view this frame (Driver.OnFrame sets it).</summary>
    public static bool FreeCamera;

    /// <summary>The lens to draw, radians, from the setting in degrees (inside the slider's range; 50 when it is not a number).</summary>
    public static float Lens(double degrees) =>
        (float)((double.IsFinite(degrees) ? Math.Clamp(degrees, MinDeg, MaxDeg) : DefaultDeg) * Math.PI / 180);

    /// <summary>Ours is on the camera now.</summary>
    public static bool Holding => !float.IsNaN(s_ours);

    /// <summary>The lens last written (NaN when not holding).</summary>
    public static float Ours => s_ours;

    /// <summary>Once a frame while a view places the camera: the lens from that view's setting (degrees; FieldOfView is the vertical angle).</summary>
    public static void Frame(WorldCamera cam, double degrees)
    {
        s_cam = cam;
        if (!Holding && s_settle++ < SettleFrames)
        {
            Counters.Add("lens.settle");
            return;
        }

        bool first = !Holding;
        if (s_yielded) s_yielded = false;   // back from a Kit shot: what is on the camera is the shot's lens, not the game's
        else if (first || cam.FieldOfView != s_ours) s_game = cam.FieldOfView;
        float lens = Lens(degrees);
        cam.FieldOfView = lens;
        s_ours = lens;
        Counters.Add("lens.frames");
        if (first) Log.Line(Strings.Format("log.lens_on", lens * 180 / MathF.PI, s_game * 180 / MathF.PI));
    }

    static bool s_yielded;

    /// <summary>A Director's Kit shot has the camera this frame: write nothing, and keep the game's lens we know for after it.</summary>
    public static void Yield()
    {
        if (!Holding) return;
        if (!s_yielded) Counters.Add("lens.yield_shot");
        s_yielded = true;
    }

    /// <summary>Gives the game's lens back if ours is still on the camera (leaving the view, WASD Control off, no character). Safe to repeat.</summary>
    public static void Release(string why)
    {
        s_settle = 0;
        s_yielded = false;
        if (!Holding) return;

        // photo mode starts from our lens, may change it, and puts ours back when it closes: never hand back under it (a hand-back while its
        // lens still equals ours was undone when it closed; fuzz view_lens seed 1273010001), but after it, the next call
        if (FreeCamera)
        {
            Counters.Add("lens.back_waits_free_camera");
            return;
        }

        if (s_cam is { } cam && cam.FieldOfView == s_ours)
        {
            cam.FieldOfView = s_game;
            Counters.Add("lens.back");
            Log.Line(Strings.Format("log.lens_back", why, s_game * 180 / MathF.PI));
        }
        else
        {
            Counters.Add("lens.changed_elsewhere");
            Log.Line(Strings.Format("log.lens_elsewhere", why, (s_cam?.FieldOfView ?? float.NaN) * 180 / MathF.PI));
        }
        s_ours = float.NaN;
    }
}
