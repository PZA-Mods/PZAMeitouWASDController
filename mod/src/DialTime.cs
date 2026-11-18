// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// Time while a dial is open (0.10, user 2026-11-18: "THIS WORKS WITH SLOWMO MOD IF WE CHOOSE THAT"). Setting "Time under a dial":
// - Slow (default): time runs at a quarter through PZA's SlowMo for Meitou's hold door (SlowMoApi, found by type name; contract
//   version 1, the hold contract agreed with SlowMo 0.3.0), so SlowMo's own slow motion, its window and the game's speed buttons stay in step.
//   Without SlowMo it pauses instead.
// - Pause: the game pauses while the dial is open (Meitou's own SaveSpeeds / SetSpeed(0) / RestoreSpeeds).
// - Normal: time is left alone.
// Rules as SlowMo's: a game already paused stays paused; if the player changes the speed while a dial is open, theirs stands.
// The dial engine closes a dial before it acts on the pick, and this runs once a frame, so the pick's check runs at the restored speed.
using System.Reflection;
using Meitou.Simulation.Session;

namespace PZA.WASDControl.Meitou;

public static class DialTime
{
    public const string Holder = "pza.wasd.dial";
    public const double SlowSpeed = 0.25;
    public static readonly string[] Options = ["Slow", "Pause", "Normal"];

    public enum Mode { None, SlowMo, Paused, Left }

    static Mode s_mode;
    static bool s_saved;
    static SimulationClock.SpeedState s_state = default!;
    static SimulationClock? s_clock;

    public static Mode Current => s_mode;

    /// <summary>Any dial has the pad.</summary>
    public static bool AnyDialOpen => SquadWheel.IsOpen || ScreenWheel.IsOpen || OrdersDial.IsOpen;

    /// <summary>Once a frame.</summary>
    public static void EachFrame()
    {
        try
        {
            bool open = AnyDialOpen && Driver.On && Driver.Player is not null;
            if (open && s_mode == Mode.None) Begin(Access.Session(Driver.Player!).Simulation, Driver.Settings.DialTime);
            else if (!open && s_mode != Mode.None) End();
        }
        catch (Exception e)
        {
            Log.Exception("dial time", e);
            s_mode = Mode.None;
            s_saved = false;
        }
    }

    /// <summary>A dial opened (public for the offline check, which drives Meitou's real SimulationClock).</summary>
    public static void Begin(SimulationClock clock, string setting)
    {
        s_clock = clock;
        s_saved = false;
        if (setting == "Normal") { s_mode = Mode.Left; Counters.Add("dial_time.normal"); return; }
        if (setting == "Slow" && SlowMo.Hold(Holder, SlowSpeed)) { s_mode = Mode.SlowMo; Counters.Add("dial_time.slowmo"); return; }
        if (setting == "Slow") Counters.Add("dial_time.no_slowmo");   // SlowMo missing or refused: pause instead
        s_mode = Mode.Paused;
        if (clock.IsPaused || clock.Speed <= 0) { Counters.Add("dial_time.already_paused"); return; }
        s_state = clock.SaveSpeeds();
        s_saved = true;
        clock.SetSpeed(0);
        Counters.Add("dial_time.paused");
    }

    /// <summary>Every dial closed.</summary>
    public static void End()
    {
        switch (s_mode)
        {
            case Mode.SlowMo:
                SlowMo.Release(Holder);
                break;
            case Mode.Paused when s_saved && s_clock is { } clock:
                if (clock.Speed <= 0) { clock.RestoreSpeeds(s_state); Counters.Add("dial_time.restored"); }
                else Counters.Add("dial_time.changed_outside");   // the player set a speed meanwhile: theirs stands
                break;
        }
        s_mode = Mode.None;
        s_saved = false;
        s_clock = null;
    }

    /// <summary>PZA's SlowMo for Meitou's hold door, by type name (neither mod references the other).</summary>
    public static class SlowMo
    {
        public const string TypeName = "PZA.SlowMotion.Meitou.SlowMoApi";
        const int NeedVersion = 1;
        static MethodInfo? s_hold, s_release;
        static long s_lookedAt = -1;

        /// <summary>For the offline check: a stand-in door (null: look it up again).</summary>
        public static Type? Override;

        public static bool Found => Find();

        static bool Find()
        {
            if (s_hold is not null && s_release is not null) return true;
            long now = Environment.TickCount64;
            if (Override is null && s_lookedAt >= 0 && now - s_lookedAt < 10000) return false;   // look again at most every 10 s
            s_lookedAt = now;
            var t = Override ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(TypeName, false)).FirstOrDefault(x => x is not null);
            if (t is null) return false;
            if (t.GetField("Version", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is not int v || v < NeedVersion)
            {
                Counters.Add("dial_time.slowmo_old");
                return false;
            }
            s_hold = t.GetMethod("Hold", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(double)]);
            s_release = t.GetMethod("Release", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
            if (s_hold is null || s_release is null) { s_hold = s_release = null; return false; }
            Log.Line("WASD Control: PZA's SlowMo found; dials slow time through it.");
            return true;
        }

        /// <summary>Forget the door (offline check).</summary>
        public static void Reset() { s_hold = s_release = null; s_lookedAt = -1; }

        public static bool Hold(string holder, double speed)
        {
            if (!Find()) return false;
            try { return s_hold!.Invoke(null, [holder, speed]) is true; }
            catch (Exception e) { Log.Exception("SlowMo hold", e); return false; }
        }

        public static void Release(string holder)
        {
            if (!Find()) return;
            try { s_release!.Invoke(null, [holder]); }
            catch (Exception e) { Log.Exception("SlowMo release", e); }
        }
    }
}
