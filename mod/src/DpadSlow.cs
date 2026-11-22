// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// Slow motion on the D-pad (0.10.2, user 2026-11-19: "let you turn SlowMo on (0.30x default if you don't have it) with the D-Pad the same way it
// controls game speed right now"). D-pad left / right still step Meitou's own speeds (1, 2, 5); one step below 1x is slow motion:
// - left at 1x (not paused): slow motion on; left while slow: nothing (it is the slowest step);
// - right while slow: slow motion off (back to 1x); right otherwise: Meitou's faster, as before;
// - left above 1x: Meitou's slower, as before; while paused both keys are Meitou's own.
// Who slows the clock (by type name: neither mod references the other):
// - PZA's SlowMo for Meitou with SlowMoApi Version 2+: its own slow motion through SetOn (its pop-up, window and saved state stay in step);
// - Version 1: a hold ("pza.wasd.dpad") at SlowMo's own slow speed;
// - no SlowMo: WASD sets Meitou's clock to OwnSpeed (0.30x), as the dials do; a clock no longer at that speed (the player paused or picked
//   a speed) counts as off, and theirs stands.
using System.Reflection;
using Meitou.Game.Host.Input;
using Meitou.Simulation.Session;

namespace PZA.WASDControl.Meitou;

public enum DpadAction { None, SlowOn, SlowOff, GameSlower, GameFaster }

public static class DpadSlow
{
    /// <summary>The slow speed without PZA's SlowMo (its own default is 0.30x).</summary>
    public const double OwnSpeed = 0.30;
    public const string Holder = "pza.wasd.dpad";

    /// <summary>What a D-pad left (<paramref name="left"/>) or right press does, from the clock and whether slow motion is on (pure: the
    /// offline check and the fuzzer drive it).</summary>
    public static DpadAction Decide(bool left, double speed, bool paused, bool slowOn)
    {
        if (paused || !double.IsFinite(speed)) return left ? DpadAction.GameSlower : DpadAction.GameFaster;
        if (left)
        {
            if (slowOn) return DpadAction.None;
            return speed <= 1.0001 ? DpadAction.SlowOn : DpadAction.GameSlower;
        }

        return slowOn ? DpadAction.SlowOff : DpadAction.GameFaster;
    }

    enum Way { None, SlowMoOn, SlowMoHold, Own }

    static Way s_way;
    static SimulationClock.SpeedState s_saved = default!;

    /// <summary>Slow motion from the D-pad is on now (ours, or SlowMo's own when it has Version 2).</summary>
    public static bool On(SimulationClock clock) => s_way switch
    {
        Way.SlowMoOn => Api.IsOn(),
        Way.SlowMoHold => true,
        Way.Own => !clock.IsPaused && Math.Abs(clock.Speed - OwnSpeed) < 1e-6,
        _ => Api.Version >= 2 && Api.IsOn(),   // turned on in SlowMo itself (its key or button): the D-pad right turns it off
    };

    /// <summary>One D-pad press; returns the Meitou action to press, if any (the caller presses it as before).</summary>
    public static InputAction? Press(bool left, SimulationClock clock, Action<string> notice)
    {
        bool on = On(clock);
        if (s_way == Way.Own && !on) s_way = Way.None;   // the player changed the speed meanwhile: theirs stands
        var act = Decide(left, clock.Speed, clock.IsPaused, on);
        Counters.Add("dpad." + act);
        switch (act)
        {
            case DpadAction.GameSlower: return Act.TimeSlower;
            case DpadAction.GameFaster: return Act.TimeFaster;
            case DpadAction.SlowOn: TurnOn(clock, notice); break;
            case DpadAction.SlowOff: TurnOff(clock, notice); break;
        }

        return null;
    }

    static void TurnOn(SimulationClock clock, Action<string> notice)
    {
        if (Api.Version >= 2 && Api.SetOn(true))
        {
            s_way = Way.SlowMoOn;   // SlowMo shows its own pop-up
            return;
        }

        if (Api.Version >= 1 && Api.Hold(Holder, Api.SlowSpeed()))
        {
            s_way = Way.SlowMoHold;
            notice(Strings.Format("notice.slow_on", Api.SlowSpeed().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
            return;
        }

        s_saved = clock.SaveSpeeds();
        clock.SetSpeed(OwnSpeed);
        s_way = Way.Own;
        notice(Strings.Format("notice.slow_on", OwnSpeed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>A new world: SlowMo's hold given back; the old world's clock (and its saved speeds) is not touched, it is gone.</summary>
    public static void Forget()
    {
        if (s_way == Way.SlowMoHold) Api.Release(Holder);
        if (s_way != Way.None) Counters.Add("dpad_slow.forgot_new_world");
        s_way = Way.None;
    }

    static void TurnOff(SimulationClock clock, Action<string> notice)
    {
        switch (s_way)
        {
            case Way.SlowMoHold:
                Api.Release(Holder);
                notice(Strings.Get("notice.slow_off"));
                break;
            case Way.Own:
                clock.RestoreSpeeds(s_saved);
                notice(Strings.Get("notice.slow_off"));
                break;
            default:   // SlowMo's own (ours or turned on in SlowMo)
                Api.SetOn(false);
                break;
        }

        s_way = Way.None;
    }

    /// <summary>PZA's SlowMo for Meitou's SlowMoApi by type name (its Version 1 / 2 contract).</summary>
    public static class Api
    {
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
            s_type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(DialTime.SlowMo.TypeName, false)).FirstOrDefault(x => x is not null);
            return s_type;
        }

        public static void Reset() { s_type = null; s_lookedAt = -1; }

        public static int Version => Find()?.GetField("Version", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int v ? v : 0;

        public static bool IsOn() => Call("get_On", []) is true;

        public static double SlowSpeed() => Call("get_SlowSpeed", []) is double d && double.IsFinite(d) && d > 0 ? d : OwnSpeed;

        public static bool SetOn(bool on) => Call("SetOn", [on]) is true;

        public static bool Hold(string holder, double speed) => Call("Hold", [holder, speed]) is true;

        public static void Release(string holder) => Call("Release", [holder]);

        static object? Call(string name, object[] args)
        {
            var m = Find()?.GetMethod(name, BindingFlags.Public | BindingFlags.Static, [.. args.Select(a => a.GetType())]);
            if (m is null) return null;
            try { return m.Invoke(null, args); }
            catch (Exception e) { Log.Exception("SlowMo " + name, e); return null; }
        }
    }
}
