// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The game controller (docs/controller-design.md). Read straight from GLFW (Meitou's own glfw3.dll through its Silk.NET.GLFW), once a control
// tick on the game's main thread, where Meitou made its window: Meitou pumps window events only (GameLoop: DoEvents), so Silk's gamepad objects
// are never updated; glfwGetGamepadState reads the pad now. GLFW maps every pad it knows (Xbox, PlayStation, Switch Pro...) by position onto one
// Xbox-style layout, so the buttons below are positions: South = bottom face button (Xbox A, Switch B).
using System.Diagnostics;
using System.Numerics;
using Silk.NET.GLFW;

namespace PZA.WASDControl.Meitou;

/// <summary>GLFW's gamepad buttons, in its order (GLFW_GAMEPAD_BUTTON_*), named by position.</summary>
public enum PadButton
{
    South, East, West, North, LeftBumper, RightBumper, View, Menu, Guide, LeftStick, RightStick, DPadUp, DPadRight, DPadDown, DPadLeft,
    LeftTrigger, RightTrigger,   // not GLFW buttons: the triggers past half way (axes 4 and 5)
}

/// <summary>One reading of a pad: sticks with the dead zone applied (y up), triggers 0..1, buttons as bits of PadButton.</summary>
public readonly record struct PadState(bool Connected, Vector2 Left, Vector2 Right, float LeftTrigger, float RightTrigger, uint Buttons)
{
    public bool Down(PadButton b) => (Buttons & (1u << (int)b)) != 0;

    public static PadState None => new(false, Vector2.Zero, Vector2.Zero, 0, 0, 0);
}

public static class Pad
{
    /// <summary>A stick inside this radius reads as centred; outside it the reading is rescaled to start at 0 (no jump at the edge).</summary>
    public const float DeadZone = 0.2f;
    /// <summary>A trigger counts as pressed past this (0..1).</summary>
    public const float TriggerPress = 0.5f;
    /// <summary>Seconds between searches for a pad while none is connected (a search asks GLFW about all 16 slots).</summary>
    public const double SearchGapSeconds = 1;

    /// <summary>Seconds a newly found pad gets to read idle once before the next candidate is tried.</summary>
    public const double ArmTimeoutSeconds = 3;

    static Glfw? s_glfw;
    static int s_slot = -1;
    static long s_lastSearch;
    static long s_slotSince;
    static uint s_previous;
    static string s_name = "";
    static PadGate s_gate = new();
    /// <summary>Slots dropped this session (noise, never idle, or on Steam's ignore list), until the pad is unplugged.</summary>
    static readonly HashSet<int> s_dropped = [];

    /// <summary>The buttons that went down this reading (edges), as bits of PadButton.</summary>
    public static uint Pressed { get; private set; }

    /// <summary>The buttons that went up this reading (edges), as bits of PadButton.</summary>
    public static uint Released { get; private set; }

    /// <summary>The pad now, once it has proved itself (PadGate), or None; also updates <see cref="Pressed"/>.</summary>
    public static PadState Read()
    {
        var state = ReadRaw();
        Pressed = state.Buttons & ~s_previous;
        Released = s_previous & ~state.Buttons;
        s_previous = state.Buttons;
        return state;
    }

    public static bool WasPressed(PadButton b) => (Pressed & (1u << (int)b)) != 0;

    public static bool WasReleased(PadButton b) => (Released & (1u << (int)b)) != 0;

    static PadState ReadRaw()
    {
        var glfw = s_glfw ??= Glfw.GetApi();
        ForgetUnplugged(glfw);
        if (s_slot < 0 && !Search(glfw)) return PadState.None;

        if (!TryState(glfw, s_slot, out var raw))
        {
            Log.Line(Strings.Format("log.pad_lost", s_name));
            Counters.Add("pad.lost");
            s_slot = -1;
            return PadState.None;
        }

        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var used = s_gate.Feed(raw, now);
        if (s_gate.Noisy) Drop("log.pad_noise", "pad.dropped_noise");
        else if (!s_gate.Armed && Stopwatch.GetElapsedTime(s_slotSince).TotalSeconds > ArmTimeoutSeconds) Drop("log.pad_never_idle", "pad.dropped_never_idle");
        else if (s_gate.JustArmed)
        {
            Log.Line(Strings.Format("log.pad_ready", s_name));
            Counters.Add("pad.armed");
        }
        return used;
    }

    /// <summary>Drops the pad in use (logged once, with why) and searches the others from the next second.</summary>
    static void Drop(string line, string counter)
    {
        Log.Line(Strings.Format(line, s_name, s_slot));
        Counters.Add(counter);
        s_dropped.Add(s_slot);
        s_slot = -1;
    }

    /// <summary>A dropped slot whose pad was unplugged may hold another pad later: it gets a fresh chance.</summary>
    static void ForgetUnplugged(Glfw glfw)
    {
        if (s_dropped.Count == 0) return;

        s_dropped.RemoveWhere(jid => !glfw.JoystickPresent(jid));
    }

    static unsafe bool TryState(Glfw glfw, int jid, out PadState state)
    {
        state = PadState.None;
        if (!glfw.GetGamepadState(jid, out GamepadState raw)) return false;

        uint buttons = 0;
        for (int i = 0; i < 15; i++)
        {
            if (raw.Buttons[i] != 0) buttons |= 1u << i;
        }

        float lt = (raw.Axes[4] + 1) / 2, rt = (raw.Axes[5] + 1) / 2;   // GLFW triggers rest at -1
        if (lt > TriggerPress) buttons |= 1u << (int)PadButton.LeftTrigger;
        if (rt > TriggerPress) buttons |= 1u << (int)PadButton.RightTrigger;

        // GLFW's stick y is down-positive; ours is up-positive (push forward = +y).
        var left = Shape(new Vector2(raw.Axes[0], -raw.Axes[1]));
        var right = Shape(new Vector2(raw.Axes[2], -raw.Axes[3]));
        state = new PadState(true, left, right, lt, rt, buttons);
        return true;
    }

    /// <summary>The first slot GLFW maps as a gamepad that is not dropped and not on Steam's ignore list; searched at most once a second.</summary>
    static bool Search(Glfw glfw)
    {
        if (Stopwatch.GetElapsedTime(s_lastSearch).TotalSeconds < SearchGapSeconds) return false;

        s_lastSearch = Stopwatch.GetTimestamp();
        var ignored = SteamIgnored(Environment.GetEnvironmentVariable("SDL_GAMECONTROLLER_IGNORE_DEVICES"));
        for (int jid = 0; jid <= 15; jid++)
        {
            if (s_dropped.Contains(jid) || !glfw.JoystickPresent(jid) || !glfw.JoystickIsGamepad(jid)) continue;

            string name = glfw.GetGamepadName(jid) ?? "?";
            if (UsbIds(glfw.GetJoystickGUID(jid)) is { } ids && ignored.Contains(ids))
            {
                Log.Line(Strings.Format("log.pad_steam_ignored", name, jid));
                Counters.Add("pad.steam_ignored");
                s_dropped.Add(jid);
                continue;
            }

            (s_slot, s_name, s_gate, s_slotSince) = (jid, name, new PadGate(), Stopwatch.GetTimestamp());
            Counters.Add("pad.found");
            Log.Line(Strings.Format("log.pad_found", name, jid));
            return true;
        }

        Counters.Add("pad.search_none");
        return false;
    }

    /// <summary>
    /// The pads Steam Input took over (it shows the game a clean virtual pad instead): Steam sets SDL_GAMECONTROLLER_IGNORE_DEVICES for the
    /// games it starts, "0xVVVV/0xPPPP" pairs, comma-separated. GLFW does not read it, so the mod does.
    /// </summary>
    public static HashSet<(int Vid, int Pid)> SteamIgnored(string? list)
    {
        var set = new HashSet<(int, int)>();
        if (string.IsNullOrWhiteSpace(list)) return set;

        foreach (string pair in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('/');
            if (parts.Length == 2 && Hex(parts[0]) is { } vid && Hex(parts[1]) is { } pid) set.Add((vid, pid));
        }
        return set;
    }

    /// <summary>USB vendor and product from GLFW's SDL-style joystick GUID (little-endian 16-bit words at bytes 4 and 8); null when not USB-shaped.</summary>
    public static (int Vid, int Pid)? UsbIds(string? guid)
    {
        if (guid is null || guid.Length != 32) return null;

        int? vid = Hex(guid.Substring(10, 2) + guid.Substring(8, 2));
        int? pid = Hex(guid.Substring(18, 2) + guid.Substring(16, 2));
        return vid is { } v && pid is { } p && v != 0 ? (v, p) : null;
    }

    static int? Hex(string text)
    {
        string t = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out int v) ? v : null;
    }

    /// <summary>The dead zone, then the rest rescaled to 0..1 and capped at length 1 (a square stick's corners read over 1).</summary>
    public static Vector2 Shape(Vector2 v)
    {
        float len = v.Length();
        if (len <= DeadZone) return Vector2.Zero;

        float scaled = MathF.Min((len - DeadZone) / (1 - DeadZone), 1);
        return v / len * scaled;
    }
}

/// <summary>
/// Whether a pad's readings can be trusted. A pad is used only after it has read idle (no buttons, sticks centred) for half a second, and it
/// is dropped when its buttons go down more than MaxEdgesPerSecond times a second: the Switch Pro controller over USB without Nintendo's
/// handshake streams a running timer GLFW reads as buttons (play test 2026-11-16: stop ~8/s, previous member ~7/s, view ~7/s, untouched).
/// </summary>
public sealed class PadGate
{
    public const double IdleToArm = 0.5;
    public const int MaxEdgesPerSecond = 12;

    readonly Queue<double> _edges = new();
    uint _previous;
    double _idleSince = -1;

    public bool Armed { get; private set; }
    public bool Noisy { get; private set; }
    /// <summary>Armed by the last Feed.</summary>
    public bool JustArmed { get; private set; }

    /// <summary>One reading at <paramref name="now"/> seconds; returns it when the pad is trusted, else None.</summary>
    public PadState Feed(PadState s, double now)
    {
        JustArmed = false;
        uint edges = s.Buttons & ~_previous;
        _previous = s.Buttons;
        if (Noisy) return PadState.None;

        for (uint e = edges; e != 0; e &= e - 1) _edges.Enqueue(now);
        while (_edges.Count > 0 && now - _edges.Peek() > 1) _edges.Dequeue();
        if (_edges.Count > MaxEdgesPerSecond)
        {
            (Noisy, Armed) = (true, false);
            return PadState.None;
        }

        if (Armed) return s;

        bool idle = s.Buttons == 0 && s.Left == Vector2.Zero && s.Right == Vector2.Zero;
        if (!idle) _idleSince = -1;
        else if (_idleSince < 0) _idleSince = now;
        else if (now - _idleSince >= IdleToArm) (Armed, JustArmed) = (true, true);
        return PadState.None;
    }
}
