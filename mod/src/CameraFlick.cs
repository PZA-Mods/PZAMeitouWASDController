// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// Quadrant flicks for PZA's Director's Kit (user 2026-11-20: "if you're using the Director's Cut mod, the right stick can be used to move the
// camera to one of the 45-degree quadrants it can be positioned at ... whichever direction you flick the camera while it's locked").
// - The Kit's settling cameras (Isometric, Diorama, Side view) settle on offset + n x step after the player stops turning. Its CameraApi (found
//   by type name; neither mod references the other) says the current step and offset, 0 when the camera does not settle.
// - While it settles and the normal view is on, the right stick's left / right no longer turns freely: a flick (the stick crossing Flick from
//   inside) eases the camera to the next step that way; up / down still tilt. The Kit's own settle then holds it there.
// - Without the Kit, or with a free camera, nothing changes.
using System.Reflection;

namespace PZA.WASDControl.Meitou;

public static class CameraFlick
{
    /// <summary>How far the stick must be pushed sideways for a flick (it must have been inside this first).</summary>
    public const float Flick = 0.6f;
    /// <summary>Radians a second the camera eases toward the flick's step.</summary>
    public const float TurnRate = 6f;

    /// <summary>The flick's direction in yaw (+1 / -1), 0 for none: the stick crossed Flick sideways since the last reading. Right turns the
    /// camera the way the free stick does (the rig rotates by minus the stick's X).</summary>
    public static int Flicked(float previousX, float x)
    {
        if (!float.IsFinite(previousX) || !float.IsFinite(x) || MathF.Abs(previousX) >= Flick || MathF.Abs(x) < Flick) return 0;
        return x > 0 ? -1 : 1;
    }

    /// <summary>The next settle yaw from <paramref name="yaw"/> in direction <paramref name="dir"/> (+1 / -1) on steps of
    /// <paramref name="stepDeg"/> from <paramref name="offsetDeg"/>; the yaw itself for no step or no direction.</summary>
    public static float Next(float yaw, double stepDeg, double offsetDeg, int dir)
    {
        if (!(stepDeg > 0) || dir == 0 || !float.IsFinite(yaw) || !double.IsFinite(offsetDeg)) return yaw;
        double step = stepDeg * Math.PI / 180, off = offsetDeg * Math.PI / 180;
        double n = Math.Round((yaw - off) / step) + Math.Sign(dir);
        return Wrap((float)(off + n * step));
    }

    /// <summary>The signed shortest turn from <paramref name="from"/> to <paramref name="to"/> (-pi..pi).</summary>
    public static float Turn(float from, float to) => MathF.IEEERemainder(to - from, 2 * MathF.PI);

    /// <summary>A yaw in -pi..pi.</summary>
    public static float Wrap(float yaw) => MathF.IEEERemainder(yaw, 2 * MathF.PI);

    static float s_lastX;
    static float? s_target;

    /// <summary>The pad's right stick this tick, while the normal view is on: true when the Kit's lock took the stick's left / right (the
    /// caller then turns nothing itself). Rotates the rig toward a flick's step.</summary>
    public static bool Step(global::Meitou.Game.Host.Cameras.StrategyCamera rig, float stickX, float dt)
    {
        double step = Api.Step();
        if (!(step > 0))
        {
            s_target = null;
            s_lastX = stickX;
            return false;
        }

        int dir = Flicked(s_lastX, stickX);
        s_lastX = stickX;
        if (dir != 0)
        {
            s_target = Next(s_target ?? rig.Yaw, step, Api.Offset(), dir);
            Counters.Add("flick.step");
        }

        if (s_target is { } target)
        {
            float turn = Turn(rig.Yaw, target);
            float most = TurnRate * Math.Max(0, dt);
            if (MathF.Abs(turn) <= Math.Max(most, 1e-3f))
            {
                rig.Rotate(turn);
                s_target = null;
            }
            else rig.Rotate(MathF.Sign(turn) * most);
        }
        return true;
    }

    /// <summary>PZA's Director's Kit's CameraApi by type name (contract: the Kit's CameraApi, Version 1).</summary>
    public static class Api
    {
        public const string TypeName = "PZA.Camera.Meitou.CameraApi";
        static Type? s_type;
        static long s_lookedAt = -1;

        /// <summary>For the offline check: a stand-in (null: look it up again).</summary>
        public static Type? Override;

        static Type? Find()
        {
            if (Override is not null) return Override;
            if (s_type is not null) return s_type;
            long now = Environment.TickCount64;
            if (s_lookedAt >= 0 && now - s_lookedAt < 10000) return null;
            s_lookedAt = now;
            s_type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(TypeName, false)).FirstOrDefault(x => x is not null);
            return s_type;
        }

        public static void Reset() { s_type = null; s_lookedAt = -1; }

        /// <summary>A Kit shot owns the camera now: CameraApi.Holding (Kit 0.6.1: the two-shot, a shot, between shots, an impact; false on the
        /// way back, which then lands on our view), or CameraApi.Busy on older Kits. WASD's view writes nothing meanwhile. False without the Kit.</summary>
        public static bool ShotBusy()
        {
            try
            {
                var api = Find();
                if (api is null) return false;
                object? holding = api.GetProperty("Holding", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                return KitRules.StandAside(holding as bool?, api.GetProperty("Busy", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is true);
            }
            catch (Exception e)
            {
                Log.Exception("Director's Kit Busy", e);
                return false;
            }
        }

        /// <summary>The settle step now (degrees), 0 without the Kit or a settling camera.</summary>
        public static double Step() => Read("SettleStepDegrees");

        /// <summary>The settle offset now (degrees).</summary>
        public static double Offset() => Read("SettleOffsetDegrees");

        static double Read(string name)
        {
            try
            {
                return Find()?.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is double d && double.IsFinite(d) ? d : 0;
            }
            catch (Exception e)
            {
                Log.Exception("Director's Kit " + name, e);
                return 0;
            }
        }
    }
}
