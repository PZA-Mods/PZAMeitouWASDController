// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The dial engine (0.10, the dials plan of 2026-11-18): every text dial is a list of slices on one shared gesture.
// - Hold the dial's button past HoldMs: the dial opens; a shorter press is a tap (the caller decides what a tap does). A dial can sit on a
//   chord of buttons (the Orders dial: LB + RB together, user 2026-11-18): held = all down, let go = any one let go.
// - The right stick aims by angle once past Aim; the lit slice stays lit when the stick springs back, and keeps a Margin of angle before it
//   hands over to a neighbour (no flicker on the border).
// - Letting go of the button acts on the lit slice; B cancels; letting go with nothing lit does nothing.
// - Slices never move: one that does not apply now is drawn dimmed in its place (rule 2 of the plan), and its pick only says why.
// - Several pages (LB / RB while the dial is open, when the dial's own button is neither) for dials over 8 slices.
// - Every pick that can be checked is checked: the slice says what must change (Expect); a moment later the Verifier looks and logs
//   dial.<slice>.ok or .failed, with a short notice on failure, so a slice that silently does nothing (0.9.21's INVENTORY) cannot hide.
using System.Numerics;
using Meitou.Game.Rendering.Overlays;

namespace PZA.WASDControl.Meitou;

/// <summary>A dial's slice: its caption key (Strings), what it does, when it applies, its on/off state, and the change it must cause.</summary>
/// <param name="Expect">Called just before the act; returns the check to run a moment later (true = it took), or null when it can't be checked.</param>
/// <param name="World">The check needs the world to tick (an order, a command); false: the interface changes at once (a screen).</param>
public sealed record Slice(string Key, Action<object> Act, Func<object, bool>? Available = null, Func<object, bool?>? State = null,
    Func<object, Func<object, bool>?>? Expect = null, bool World = true);

/// <summary>Angle picking with a margin (rule 6: no flicker between neighbours).</summary>
public static class DialAim
{
    /// <summary>The stick points at a slice past this tilt (0..1).</summary>
    public const float Aim = 0.5f;
    /// <summary>Extra angle (radians, about 6 degrees) the lit slice keeps past its own border.</summary>
    public const float Margin = 0.105f;

    /// <summary>The slice the stick points at (0 at the top, clockwise), keeping <paramref name="current"/> within its border plus the margin; -1 inside the aim radius.</summary>
    public static int Slot(Vector2 stick, int slots, int current = -1)
    {
        // A broken reading (NaN / infinity) aims at nothing: found by the fuzzer, NaN used to land on the top slice.
        if (!float.IsFinite(stick.X) || !float.IsFinite(stick.Y) || stick.Length() < Aim) return -1;
        float a = MathF.Atan2(stick.X, stick.Y);   // 0 up, +pi/2 right
        if (a < 0) a += MathF.Tau;
        float step = MathF.Tau / slots;
        if (current >= 0 && current < slots)
        {
            float d = MathF.Abs(Wrap(a - current * step));
            if (d <= step / 2 + Margin) return current;
        }
        return (int)MathF.Round(a / step) % slots;
    }

    static float Wrap(float a) => a - MathF.Tau * MathF.Floor((a + MathF.PI) / MathF.Tau);
}

/// <summary>One text dial on the shared gesture.</summary>
public sealed class Dial(string id, PadButton[] buttons, string titleKey, Slice[][] pages, string[]? pageKeys = null)
{
    public const long HoldMs = 220;

    /// <summary>The clock the gesture and the checks read (ms); the fuzzer drives its own.</summary>
    internal static Func<long> Clock = () => Environment.TickCount64;
    /// <summary>Where the dial's short notes go (the game's message line); the fuzzer records them.</summary>
    internal static Action<object, string> Notify = (p, text) => Access.Notice(p, text);
    public readonly string Id = id;
    /// <summary>The button, or the chord of buttons, that holds the dial.</summary>
    public readonly PadButton[] Buttons = buttons;
    public readonly Slice[][] Pages = pages;

    long _since = -1;
    bool _open;
    int _page, _lit = -1;

    public bool IsOpen => _open;
    public int Page => _page;
    public int Lit => _lit;

    /// <summary>The pad this control tick. Tap: a short press; Open: the dial has the pad (it may have acted on release).</summary>
    public WheelResult Pad(object player, PadState pad, bool mayOpen = true)
    {
        long now = Clock();
        bool held = Held(pad);
        if (held && Buttons.Any(global::PZA.WASDControl.Meitou.Pad.WasPressed)) _since = mayOpen ? now : -1;   // the press that completes the hold
        bool released = Buttons.Any(global::PZA.WASDControl.Meitou.Pad.WasReleased);

        if (!_open)
        {
            if (_since < 0) return WheelResult.None;
            if (released) { _since = -1; Counters.Add(Id + ".tap"); return WheelResult.Tap; }
            if (!held) { _since = -1; return WheelResult.None; }
            if (now - _since < HoldMs) return WheelResult.None;
            _open = true;
            _lit = -1;
            _page = 0;
            Counters.Add(Id + ".open");
        }

        if (Pages.Length > 1)
        {
            int flip = (Pressed(PadButton.RightBumper, PadButton.DPadRight) ? 1 : 0) - (Pressed(PadButton.LeftBumper, PadButton.DPadLeft) ? 1 : 0);
            if (flip != 0)
            {
                _page = ((_page + flip) % Pages.Length + Pages.Length) % Pages.Length;
                _lit = -1;
                Counters.Add(Id + ".page");
            }
        }

        var slices = Pages[_page];
        int slot = DialAim.Slot(pad.Right, slices.Length, _lit);
        if (slot >= 0 && slot != _lit) { _lit = slot; Counters.Add(Id + ".aim"); }

        if (global::PZA.WASDControl.Meitou.Pad.WasPressed(PadButton.East)) { Close(Id + ".cancel"); return WheelResult.Open; }
        if (!released && held) return WheelResult.Open;

        int lit = _lit;
        Close(null);
        if (lit >= 0) Pick(player, slices[lit]);
        else Counters.Add(Id + ".released_empty");
        return WheelResult.Open;
    }

    /// <summary>The dial's own button (or every button of its chord) is down.</summary>
    bool Held(PadState pad) => Buttons.All(pad.Down);

    /// <summary>A page button, unless it is one of the dial's own (LB / RB on the Orders dial: only the D-pad turns its pages).</summary>
    bool Pressed(PadButton a, PadButton b) =>
        (!Buttons.Contains(a) && global::PZA.WASDControl.Meitou.Pad.WasPressed(a)) || (!Buttons.Contains(b) && global::PZA.WASDControl.Meitou.Pad.WasPressed(b));

    /// <summary>Forgets a press that has not opened the dial (another dial took the buttons): no tap follows.</summary>
    public void Abort()
    {
        if (!_open) _since = -1;
    }

    /// <summary>The buttons' names for the hub ("LB", "LB + RB").</summary>
    public string ButtonNames => string.Join(" + ", Buttons.Select(b => Strings.Get("pad.name." + b)));

    void Pick(object player, Slice s)
    {
        string key = Id + "." + s.Key;
        if (s.Available is { } available && !available(player))
        {
            Counters.Add(key + ".unavailable");
            Notify(player, Strings.Format("dial.not_now", Strings.Get(s.Key)));
            return;
        }
        try
        {
            var check = s.Expect?.Invoke(player);
            s.Act(player);
            Counters.Add(key);
            if (check is not null) Verifier.Expect(player, key, Strings.Get(s.Key), check, s.World);
            else Counters.Add(key + ".unchecked");
        }
        catch (Exception e)
        {
            Log.Exception("dial " + key, e);
        }
    }

    void Close(string? why)
    {
        _open = false;
        _lit = -1;
        _since = -1;
        if (why is not null) Counters.Add(why);
    }

    /// <summary>Once a frame: shut when WASD Control is off.</summary>
    public void EachFrame()
    {
        if (_open && !Driver.On) Close(Id + ".closed_off");
    }

    // ---------------------------------------------------------------- drawing (SquadWheel's helpers and colours)
    internal void Draw(DebugOverlay o, int w, int h, object player)
    {
        var slices = Pages[_page];
        int slots = slices.Length;
        var c = new Vector2(w / 2f, h / 2f);
        float s = h / 1080f, radius = 230 * s, inner = radius - 70 * s, outer = radius + 70 * s, step = MathF.Tau / slots;

        o.Flush(w, h);
        SquadWheel.Ring(o, c, inner, outer, 0, MathF.Tau, SquadWheel.Back);
        SquadWheel.Disc(o, c, inner, SquadWheel.Hub);
        if (_lit >= 0) SquadWheel.Ring(o, c, inner, outer, _lit * step - step / 2, _lit * step + step / 2, SquadWheel.GoldWash);
        for (int i = 0; i < slots; i++)
        {
            float a = i * step + step / 2;
            var d = new Vector2(MathF.Sin(a), -MathF.Cos(a));
            o.Line(c.X + d.X * inner, c.Y + d.Y * inner, c.X + d.X * outer, c.Y + d.Y * outer, Math.Max(1, s), SquadWheel.Seam);
        }
        SquadWheel.Circle(o, c, inner, Math.Max(1, 1.5f * s), SquadWheel.Rim);
        SquadWheel.Circle(o, c, outer, Math.Max(1, 2f * s), SquadWheel.Rim);
        if (_lit >= 0) SquadWheel.Arc(o, c, outer, _lit * step - step / 2, _lit * step + step / 2, 4 * s, SquadWheel.Gold);
        o.Flush(w, h);

        for (int i = 0; i < slots; i++)
        {
            var sl = slices[i];
            bool available = sl.Available?.Invoke(player) ?? true;
            var p = SquadWheel.SlotCentre(c, i, slots, radius);
            var colour = i == _lit ? SquadWheel.Gold : available ? SquadWheel.Ink : SquadWheel.Seam;
            SquadWheel.Centred(o, Strings.Get(sl.Key), p.X, p.Y - 9 * s, colour, 1.05f * s);
            if (sl.State?.Invoke(player) is { } on)
                SquadWheel.Centred(o, Strings.Get(on ? "dial.on" : "dial.off"), p.X, p.Y + 10 * s, on ? SquadWheel.Gold : SquadWheel.Faint, 0.8f * s);
        }

        string title = _lit >= 0 ? Strings.Get(slices[_lit].Key) : Strings.Get(pageKeys is { } keys ? keys[_page] : titleKey);
        SquadWheel.Centred(o, title, c.X, c.Y - 34 * s, _lit >= 0 ? SquadWheel.Gold : SquadWheel.Faint, 1.3f * s);
        if (_lit >= 0)
        {
            var sl = slices[_lit];
            string what = !(sl.Available?.Invoke(player) ?? true) ? Strings.Get("dial.sub_not_now")
                : sl.State?.Invoke(player) is { } on ? Strings.Get(on ? "dial.sub_turn_off" : "dial.sub_turn_on") : "";
            SquadWheel.Centred(o, what, c.X, c.Y - 8 * s, SquadWheel.Faint, 0.9f * s);
        }
        else if (Pages.Length > 1) SquadWheel.Centred(o, Strings.Format("wheel.page", _page + 1, Pages.Length), c.X, c.Y - 8 * s, SquadWheel.Faint, 0.9f * s);
        SquadWheel.Centred(o, Strings.Format("dial.hint", ButtonNames), c.X, c.Y + 14 * s, SquadWheel.Faint, 0.85f * s);
        string cancel = Pages.Length == 1 ? "dial.cancel" : Buttons.Contains(PadButton.LeftBumper) || Buttons.Contains(PadButton.RightBumper) ? "dial.cancel_pages_dpad" : "dial.cancel_pages";
        SquadWheel.Centred(o, Strings.Get(cancel), c.X, c.Y + 36 * s, SquadWheel.Faint, 0.85f * s);
        o.Flush(w, h);
    }
}

/// <summary>
/// LB and RB pressed together (0.10, user 2026-11-18: "lets go with LB+RB together"): the Orders dial, so the right thumb stays on the stick.
/// The second bumper must go down within ChordMs of the first (before either bumper's own dial can open at HoldMs); from then on the
/// chord owns both bumpers until both are up again, so neither bumper's tap or dial follows it.
/// </summary>
public sealed class BumperChord
{
    public const long ChordMs = 150;
    long _lb = -1, _rb = -1;

    public bool Active { get; private set; }

    /// <summary>One pad tick: the bumpers held and pressed this tick; <paramref name="mayStart"/>: no window, menu or dial is in the way.</summary>
    public bool Step(long now, bool lbDown, bool rbDown, bool lbPressed, bool rbPressed, bool mayStart)
    {
        if (lbPressed) _lb = now;
        if (!lbDown) _lb = -1;
        if (rbPressed) _rb = now;
        if (!rbDown) _rb = -1;
        if (!Active)
        {
            if (mayStart && (lbPressed || rbPressed) && _lb >= 0 && _rb >= 0 && Math.Abs(_lb - _rb) <= ChordMs)
            {
                Active = true;
                Counters.Add("chord.on");
            }
        }
        else if (!lbDown && !rbDown) Active = false;
        return Active;
    }

    public void Reset() { _lb = _rb = -1; Active = false; }
}

/// <summary>The check after a pick: a moment later, did the game change as the slice said it would?</summary>
public static class Verifier
{
    /// <summary>A world change is looked at once the world has run this many ticks (or given up after GiveUpMs, e.g. paused); an interface change after UiMs.</summary>
    public const int WorldTicks = 12;
    public const long UiMs = 250, GiveUpMs = 20000;

    sealed record Pending(string Key, string Label, Func<object, bool> Check, bool World, long Tick, long At);

    /// <summary>The world's tick for a player (the fuzzer drives its own).</summary>
    internal static Func<object, long> WorldTick = p => Access.Session(p).World.Tick;
    static readonly List<Pending> s_pending = [];

    public static void Expect(object player, string key, string label, Func<object, bool> check, bool world) =>
        s_pending.Add(new(key, label, check, world, WorldTick(player), Dial.Clock()));

    public static int PendingCount => s_pending.Count;

    /// <summary>Drops every pending check (a new world; the fuzzer between cases).</summary>
    internal static void Clear() => s_pending.Clear();

    /// <summary>Each control tick: looks at the checks that are due.</summary>
    public static void Tick(object player)
    {
        if (s_pending.Count == 0) return;
        long tick = WorldTick(player), now = Dial.Clock();
        for (int i = s_pending.Count - 1; i >= 0; i--)
        {
            var p = s_pending[i];
            bool due = p.World ? tick - p.Tick >= WorldTicks : now - p.At >= UiMs;
            if (!due && now - p.At < GiveUpMs) continue;
            s_pending.RemoveAt(i);
            if (!due) { Counters.Add(p.Key + ".unverified"); continue; }   // the world never ran (paused): no verdict
            bool ok;
            try { ok = p.Check(player); }
            catch (Exception e) { Log.Exception("dial check " + p.Key, e); ok = false; }
            Counters.Add(p.Key + (ok ? ".ok" : ".failed"));
            if (!ok)
            {
                Log.Line(Strings.Format("log.dial_failed", p.Key));
                Dial.Notify(player, Strings.Format("dial.failed", p.Label));
            }
        }
    }
}
