// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The prompt strip (0.10, the dials plan of 2026-11-18): one line at the top of the screen saying what the
// controller's buttons do right now, for the place you are in: the world, a window, a right-click menu (a dial shows its own hints).
// Shown while the controller is in use (any input in the last ShowMs); setting "Controller prompts" turns it off. The A entry names the safe
// extra option for the person under the crosshair (SafeExtra). The idea of a context prompt strip is BeatTheCount's (their Gamepad mod's
// strip); our code and texts are our own.
using System.Numerics;
using Meitou.Game.Rendering.Overlays;

namespace PZA.WASDControl.Meitou;

public static class Prompts
{
    public enum Place { World, Window, Menu, Dial, Build }

    /// <summary>The strip stays this long after the last controller input (ms).</summary>
    public const long ShowMs = 30000;

    static long s_lastPad = -1;
    static readonly string[] DrawnKeys = [.. Enum.GetNames<Place>().Select(n => "prompts.drawn." + n)];

    /// <summary>Each pad reading: remembers when the controller was last used.</summary>
    public static void Note(PadState pad)
    {
        if (!pad.Connected) return;
        if (pad.Buttons != 0 || pad.Left != Vector2.Zero || pad.Right != Vector2.Zero || pad.LeftTrigger > 0.1f || pad.RightTrigger > 0.1f)
            s_lastPad = Environment.TickCount64;
    }

    public static bool PadInUse => s_lastPad >= 0 && Environment.TickCount64 - s_lastPad < ShowMs;

    /// <summary>The prompts for a place, most useful first (the strip drops from the end when the screen is too narrow).</summary>
    /// <param name="safeVerb">The safe extra option A gives now (world), or null.</param>
    public static string[] For(Place place, string? safeVerb) => place switch
    {
        Place.World =>
        [
            .. safeVerb is { } v ? [Strings.Format("prompt.a_verb", v)] : Array.Empty<string>(),
            Strings.Get("prompt.a_hold"),
            Strings.Get("prompt.rt"), Strings.Get("prompt.lt"), Strings.Get("prompt.lb"), Strings.Get("prompt.rb"), Strings.Get("prompt.views"),
        ],
        Place.Window => [Strings.Get("prompt.win_a"), Strings.Get("prompt.win_b"), Strings.Get("prompt.win_stick")],
        Place.Menu => [Strings.Get("prompt.menu_choose"), Strings.Get("prompt.menu_pick"), Strings.Get("prompt.menu_close")],
        Place.Build =>
        [
            Strings.Get("prompt.build_a"), Strings.Get("prompt.build_rotate"), Strings.Get("prompt.build_height"), Strings.Get("prompt.build_confirm"),
            Strings.Get("prompt.build_undo"), Strings.Get("prompt.build_b"), Strings.Get("prompt.build_floor"), Strings.Get("prompt.build_slope"),
            Strings.Get("prompt.win_stick"),
        ],
        _ => [],
    };

    /// <summary>The place the player is in now.</summary>
    public static Place Now(object player) =>
        SquadWheel.IsOpen || ScreenWheel.IsOpen || OrdersDial.IsOpen ? Place.Dial
        : Access.ContextMenuOpen(player) ? Place.Menu
        : BuildPad.On ? Place.Build
        : Look.WindowOpen(player) ? Place.Window
        : Place.World;

    /// <summary>Joins as many prompts as fit in <paramref name="max"/> (measured by <paramref name="measure"/>), dropping from the end.</summary>
    public static string Fit(IReadOnlyList<string> items, float max, Func<string, float> measure)
    {
        for (int n = items.Count; n > 0; n--)
        {
            string line = string.Join(Strings.Get("prompt.sep"), items.Take(n));
            if (measure(line) <= max) return line;
        }
        return "";
    }

    /// <summary>Draws the strip (no dial open; the dial has its own hints).</summary>
    internal static void Draw(DebugOverlay o, int w, int h, object player)
    {
        if (!Driver.Settings.ShowPrompts || !PadInUse) return;
        var place = Now(player);
        if (place == Place.Dial) return;
        float s = h / 1080f, scale = 0.9f * s, pad = 10 * s;
        // A with nothing under the crosshair jumps: named as the safe verb would be
        string? a = SafeExtra.Verb ?? (Driver.JumpOn ? Strings.Get("prompt.jump_verb") : null);
        string line = Fit(For(place, a), w - 80 * s, t => o.MeasureText(t, scale));
        if (line.Length == 0) return;
        float tw = o.MeasureText(line, scale), x0 = (w - tw) / 2 - pad, x1 = (w + tw) / 2 + pad, y0 = 8 * s, y1 = y0 + 30 * s;
        o.Flush(w, h);
        o.Triangle(new(x0, y0), new(x1, y0), new(x1, y1), SquadWheel.Back);
        o.Triangle(new(x0, y0), new(x1, y1), new(x0, y1), SquadWheel.Back);
        SquadWheel.Centred(o, line, w / 2f, y0 + 6 * s, SquadWheel.Ink, scale);
        o.Flush(w, h);
        Counters.Add(DrawnKeys[(int)place]);
    }
}
