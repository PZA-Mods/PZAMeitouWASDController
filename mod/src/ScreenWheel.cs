// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The screens dial (user, 2026-11-17: "we should probably have another radial menu (LB?) that opens up some other menus ie the SQD, TEC, MAP,
// etc..."), controller only, drawn like the squad dial (SquadWheel's helpers, the same GameLoop.DrawInterface postfix):
// - LB tapped: the previous squad member, as before (the game's own PreviousCharacter key);
// - LB held: a dial of the game's screens; the right stick points at one (it stays lit), letting go of LB opens it, B cancels.
// Each screen opens the way its own HUD button or key does: inventory / character / help through the interface's own actions
// (PlayerInterface.ExecuteUiAction "meitou:inventory" / "meitou:stats"), map / faction / tech / crafting / squads as the overview's tabs
// 0 / 1 / 2 / 3 / 4 (the HUD's MAP / TEC / SQD buttons), build with the game's Build key. A screen that is open closes, as its button does.
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Game.Host.Input;
using Meitou.Game.Rendering.Overlays;

namespace PZA.WASDControl.Meitou;

public static class ScreenWheel
{
    /// <summary>A slice: its caption key (Strings), and how it opens: an interface action, an overview tab, or a game key.</summary>
    public sealed record Entry(string Key, string? UiAction = null, int Tab = -1, InputAction? Action = null);

    /// <summary>The slices, clockwise from the top.</summary>
    public static readonly Entry[] Entries =
    [
        new("screen.inventory", UiAction: "meitou:inventory"),
        new("screen.character", UiAction: "meitou:stats"),
        new("screen.map", Tab: 0),
        new("screen.squads", Tab: 4),
        new("screen.tech", Tab: 2),
        new("screen.crafting", Tab: 3),
        new("screen.build", Action: InputAction.ToggleBuild),
        new("screen.faction", Tab: 1),
    ];

    static long s_since = -1;
    static bool s_open;
    static int s_lit = -1;

    public static bool IsOpen => s_open;

    /// <summary>The pad's LB this tick. Tap: the caller presses PreviousCharacter. Open: the dial has the pad.</summary>
    public static WheelResult Pad(object player, PadState pad, Action<InputAction> pressGame)
    {
        long now = Environment.TickCount64;
        if (global::PZA.WASDControl.Meitou.Pad.WasPressed(PadButton.LeftBumper)) s_since = now;
        bool released = global::PZA.WASDControl.Meitou.Pad.WasReleased(PadButton.LeftBumper);

        if (!s_open)
        {
            if (s_since < 0) return WheelResult.None;
            if (released) { s_since = -1; Counters.Add("screens.tap"); return WheelResult.Tap; }
            if (!pad.Down(PadButton.LeftBumper)) { s_since = -1; return WheelResult.None; }
            if (now - s_since < SquadWheel.HoldMs) return WheelResult.None;
            s_open = true;
            s_lit = -1;
            Counters.Add("screens.open");
        }

        int slot = SquadWheel.SlotAt(pad.Right, Entries.Length);
        if (slot >= 0 && slot != s_lit) { s_lit = slot; Counters.Add("screens.aim"); }

        if (global::PZA.WASDControl.Meitou.Pad.WasPressed(PadButton.East))
        {
            Close("screens.cancel");
            return WheelResult.Open;
        }
        if (!released && pad.Down(PadButton.LeftBumper)) return WheelResult.Open;

        int lit = s_lit;
        Close(null);
        if (lit >= 0) Open(player, Entries[lit], pressGame);
        else Counters.Add("screens.released_empty");
        return WheelResult.Open;
    }

    static void Close(string? why)
    {
        s_open = false;
        s_lit = -1;
        s_since = -1;
        if (why is not null) Counters.Add(why);
    }

    /// <summary>Once a frame: shut when WASD Control is off.</summary>
    public static void EachFrame()
    {
        if (s_open && !Driver.On) Close("screens.closed_off");
    }

    // ---------------------------------------------------------------- opening a screen, as its HUD button does
    static MethodInfo? s_execute, s_updatePause;

    static void Open(object player, Entry e, Action<InputAction> pressGame)
    {
        try
        {
            if (e.UiAction is { } ui)
            {
                s_execute ??= AccessTools.Method(Access.PlayerInterface, "ExecuteUiAction", [typeof(string), typeof(string)]);
                s_execute?.Invoke(player, [ui, ""]);
            }
            else if (e.Tab >= 0)
            {
                var host = PZA.MeitouUi.Access.FaithfulScreens(player);
                if (host.IsOpen("overview") && host.GetTab("overview", "TabsMain") == e.Tab) host.Close("overview");
                else
                {
                    host.Open("overview");
                    host.SetTab("overview", "TabsMain", e.Tab);
                }
                s_updatePause ??= AccessTools.Method(Access.PlayerInterface, "UpdateMenuPause");
                s_updatePause?.Invoke(player, null);
            }
            else if (e.Action is { } a) pressGame(a);
            Counters.Add("screens." + e.Key);
        }
        catch (Exception ex)
        {
            Log.Exception("screens dial", ex);
        }
    }

    // ---------------------------------------------------------------- the dial
    internal static void Draw(DebugOverlay o, int w, int h)
    {
        int slots = Entries.Length;
        var c = new Vector2(w / 2f, h / 2f);
        float s = h / 1080f, radius = 230 * s, inner = radius - 70 * s, outer = radius + 70 * s, step = MathF.Tau / slots;

        o.Flush(w, h);
        SquadWheel.Ring(o, c, inner, outer, 0, MathF.Tau, SquadWheel.Back);
        SquadWheel.Disc(o, c, inner, SquadWheel.Hub);
        if (s_lit >= 0) SquadWheel.Ring(o, c, inner, outer, s_lit * step - step / 2, s_lit * step + step / 2, SquadWheel.GoldWash);
        for (int i = 0; i < slots; i++)
        {
            float a = i * step + step / 2;
            var d = new Vector2(MathF.Sin(a), -MathF.Cos(a));
            o.Line(c.X + d.X * inner, c.Y + d.Y * inner, c.X + d.X * outer, c.Y + d.Y * outer, Math.Max(1, s), SquadWheel.Seam);
        }
        SquadWheel.Circle(o, c, inner, Math.Max(1, 1.5f * s), SquadWheel.Rim);
        SquadWheel.Circle(o, c, outer, Math.Max(1, 2f * s), SquadWheel.Rim);
        if (s_lit >= 0) SquadWheel.Arc(o, c, outer, s_lit * step - step / 2, s_lit * step + step / 2, 4 * s, SquadWheel.Gold);
        o.Flush(w, h);

        for (int i = 0; i < slots; i++)
        {
            var p = SquadWheel.SlotCentre(c, i, slots, radius);
            SquadWheel.Centred(o, Strings.Get(Entries[i].Key), p.X, p.Y - 9 * s, i == s_lit ? SquadWheel.Gold : SquadWheel.Ink, 1.05f * s);
        }
        SquadWheel.Centred(o, s_lit >= 0 ? Strings.Get(Entries[s_lit].Key) : Strings.Get("screens.title"), c.X, c.Y - 26 * s,
            s_lit >= 0 ? SquadWheel.Gold : SquadWheel.Faint, 1.3f * s);
        SquadWheel.Centred(o, Strings.Get("screens.hint"), c.X, c.Y + 8 * s, SquadWheel.Faint, 0.85f * s);
        o.Flush(w, h);
    }
}
