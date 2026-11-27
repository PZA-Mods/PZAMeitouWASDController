// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// WasdApi: the public door for other mods. Found by type name "PZA.WASDControl.Meitou.WasdApi" (static class); neither mod needs the other.
// Members are only ever added, never renamed or removed; read Version first. Every member is safe to call from any thread and never throws.
//
// Version 1 (2026-11-21, PZA's Director's Kit): On, ViewActive, ViewName, SetNormalView.
// Version 2 (2026-11-22, for combat mods that fight the steered character by hand): Steered, Intent, WorldDirection, HoldSteering,
//   ReleaseSteering, SteeringHeld, Keys. A combat mod reads who WASD steers and where the player pushes (keys or stick, the same numbers),
//   and holds WASD's move / stop orders while that character fights (a move order or a stop ends a Meitou fight), instead of patching the
//   command queue or reading WASD's internals. Contract and examples: docs/api.md.
//
// Camera ownership agreed with the Kit (2026-11-21): a Kit shot owns the camera while its CameraApi.Holding (Busy on older Kits); outside
// shots the latest player choice wins: picking a Kit camera calls SetNormalView, and the Kit stands its cameras down while ViewActive.
using System.Numerics;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public static class WasdApi
{
    /// <summary>The contract version (2).</summary>
    public const int Version = 2;

    /// <summary>WASD Control is on (W A S D / the pad steer the first selected character).</summary>
    public static bool On => Driver.On;

    /// <summary>One of WASD's own views (over the shoulder, first person) places the camera and owns the lens now.</summary>
    public static bool ViewActive => Driver.On && Views.Current != View.Normal;

    /// <summary>The view now: "Normal", "Shoulder" or "First".</summary>
    public static string ViewName => Views.Current.ToString();

    /// <summary>
    /// Back to the normal camera (another mod's camera was just chosen by the player). Done on WASD's next frame.
    /// True when a view was on and is being left; false when none was.
    /// </summary>
    public static bool SetNormalView(string caller)
    {
        if (!ViewActive) return false;
        s_normalFor = Clean(caller);
        return true;
    }

    // ------------------------------------------------------------------------------------------------ version 2

    /// <summary>v2: the character WASD steers now (the first selected player character that can walk), or null (WASD off, nobody selected,
    /// down, carried, in a bed or cage). Updated every control tick.</summary>
    public static CharacterId? Steered => On ? s_steered : null;

    /// <summary>v2: the player's push now, relative to the camera: X right, Y forward, length 0 to 1 (W A S D or the left stick, the same
    /// numbers). Zero when nothing is pushed or WASD is off. Read it for footwork or a swing's direction; it is reported while the steering
    /// is held too.</summary>
    public static Vector2 Intent => On ? s_intent : Vector2.Zero;

    /// <summary>v2: <see cref="Intent"/> turned into the world's ground plane (X, Z of a world position; length 0 to 1), as WASD would walk.</summary>
    public static Vector2 WorldDirection => On ? s_world : Vector2.Zero;

    /// <summary>
    /// v2: WASD sends no move or stop order for the steered character for the next <paramref name="ticks"/> world ticks (1 to 600; 30 is about
    /// a second at speed 1). Call it again every tick or so to keep the hold: a hold that is not renewed ends by itself, so a mod that stops
    /// calling (turned off, crashed) never leaves the player stuck. Several callers may hold at once; the steering comes back when every hold has
    /// ended or been released. The walk under way when the hold starts is left to the caller (no stop is sent: a stop ends a fight). Ended
    /// on a new world and when WASD is turned off. True when the hold is taken.
    /// </summary>
    public static bool HoldSteering(string caller, int ticks = 30)
    {
        if (!On) return false;
        string who = Clean(caller);
        lock (s_holds)
        {
            bool fresh = !s_holds.ContainsKey(who);
            s_holds[who] = Math.Clamp(ticks, 1, 600);
            if (fresh) s_heldLog = who;
        }
        return true;
    }

    /// <summary>v2: ends this caller's hold now. True when it had one.</summary>
    public static bool ReleaseSteering(string caller)
    {
        lock (s_holds) return s_holds.Remove(Clean(caller));
    }

    /// <summary>v2: some mod holds WASD's steering now.</summary>
    public static bool SteeringHeld
    {
        get { lock (s_holds) return s_holds.Count > 0; }
    }

    /// <summary>v2: the keys WASD uses now, as "name=key" ("toggle=V", "jump=Shift+U", ...), so another mod can stay off them or say where
    /// they clash. The pad's layout is fixed and listed in docs/api.md.</summary>
    public static string[] Keys
    {
        get
        {
            var list = new List<string>();
            foreach (var a in Enum.GetValues<KeyAction>()) list.Add(a.ToString().ToLowerInvariant() + "=" + global::PZA.WASDControl.Meitou.Keys.Of(a));
            list.Add("jump=Shift+U");
            list.Add("move=W,A,S,D");
            return [.. list];
        }
    }

    // ------------------------------------------------------------------------------------------------ WASD's side

    static volatile string? s_normalFor;
    static CharacterId? s_steered;
    static Vector2 s_intent, s_world;
    static readonly Dictionary<string, int> s_holds = new(StringComparer.Ordinal);
    static volatile string? s_heldLog;

    static string Clean(string? caller) => string.IsNullOrWhiteSpace(caller) ? "?" : caller.Length > 64 ? caller[..64] : caller;

    /// <summary>Driver.OnFrame: a SetNormalView asked for since the last frame is done here, on the host thread.</summary>
    internal static void Apply()
    {
        if (s_normalFor is not { } who) return;
        s_normalFor = null;
        if (Views.Current == View.Normal) return;
        Views.Set(View.Normal, Driver.ShowNotice);
        Counters.Add("api.normal_view");
        Log.Line(Strings.Format("log.api_normal_view", who));
    }

    /// <summary>Driver.OnTick, every control tick: who is steered and the push, published; one tick off every hold. Returns whether the
    /// steering is held this tick.</summary>
    internal static bool Tick(CharacterId? steered, Vector2 intent, Vector2 world)
    {
        (s_steered, s_intent, s_world) = (steered, intent, world);
        if (s_heldLog is { } who)
        {
            s_heldLog = null;
            Counters.Add("api.steering_held");
            Log.Line(Strings.Format("log.api_steering_held", who));
        }
        lock (s_holds)
        {
            if (s_holds.Count == 0) return false;
            foreach (var key in s_holds.Keys.ToList())
                if (--s_holds[key] <= 0) s_holds.Remove(key);
            return true;   // this tick was held (the last tick of a hold included)
        }
    }

    /// <summary>A new world, or WASD turned off: every hold ends, nothing is reported.</summary>
    internal static void Clear()
    {
        lock (s_holds) s_holds.Clear();
        (s_steered, s_intent, s_world) = (null, Vector2.Zero, Vector2.Zero);
    }
}
