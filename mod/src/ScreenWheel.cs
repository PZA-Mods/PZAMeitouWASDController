// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The screens dial (user, 2026-11-17: "we should probably have another radial menu (LB?) that opens up some other menus ie the SQD, TEC, MAP,
// etc..."), controller only, drawn like the squad dial (SquadWheel's helpers, the same GameLoop.DrawInterface postfix):
// - LB tapped: the previous squad member, as before (the game's own PreviousCharacter key);
// - LB held: a dial of the game's screens; the right stick points at one (it stays lit), letting go of LB opens it, B cancels.
// Each screen opens the way its own HUD button or key does: inventory / character / help through the interface's own actions
// (PlayerInterface.ExecuteUiAction "inventory" / "stats", as the I and C keys), map / faction / tech / crafting / squads as the overview's tabs
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
        // ExecuteUiAction takes "inventory" / "stats" (the I / C keys' own call); "meitou:..." is only the HUD buttons' widget action, which
        // ExecuteUiAction does not know (0.9.21 opened nothing for these two).
        new("screen.inventory", UiAction: "inventory"),
        new("screen.character", UiAction: "stats"),
        new("screen.map", Tab: 0),
        new("screen.squads", Tab: 4),
        new("screen.tech", Tab: 2),
        new("screen.crafting", Tab: 3),
        new("screen.build", Action: InputAction.ToggleBuild),
        new("screen.faction", Tab: 1),
    ];

    /// <summary>While carrying someone (CharacterSnapshot.Carrying) the dial gains PUT DOWN, the game's own Put Down key (K) (user, 2026-11-17).</summary>
    public static readonly Entry PutDown = new("screen.put_down", Action: InputAction.PutDown);

    /// <summary>
    /// While the controlled character has jobs (CharacterSnapshot.Jobs) the dial gains CLEAR JOBS (user, 2026-11-17: "I can't cancel all the jobs
    /// the selected character has can i?"): Meitou only takes one off at a time, with the X on its row of the jobs list (OrdersPanel.RemoveJob,
    /// a RemoveJob command). This sends that same command for every job, last first so the indexes stay right; the job being worked ends as the X
    /// ends it.
    /// </summary>
    public static readonly Entry ClearJobs = new("screen.clear_jobs");

    /// <summary>The slices this time: the screens, plus PUT DOWN while the controlled character carries someone (first, at the top) and CLEAR
    /// JOBS while they have jobs (last, just left of the top).</summary>
    public static Entry[] For(bool carrying, bool hasJobs = false) =>
        [.. carrying ? [PutDown] : Array.Empty<Entry>(), .. Entries, .. hasJobs ? [ClearJobs] : Array.Empty<Entry>()];

    /// <summary>The RemoveJob commands that clear a list of <paramref name="count"/> jobs: the last index first.</summary>
    public static IEnumerable<global::Meitou.Simulation.Production.RemoveJob> ClearCommands(global::Meitou.Simulation.CharacterId who, int count, long tick)
    {
        for (int i = count - 1; i >= 0; i--) yield return new(who, i) { Tick = tick };
    }

    static Entry[] s_entries = Entries;
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
            var me = Me(player);
            s_entries = For(me is not null && !me.Carrying.IsNone, me is not null && me.Jobs.Count > 0);
            Counters.Add("screens.open");
        }

        int slot = SquadWheel.SlotAt(pad.Right, s_entries.Length);
        if (slot >= 0 && slot != s_lit) { s_lit = slot; Counters.Add("screens.aim"); }

        if (global::PZA.WASDControl.Meitou.Pad.WasPressed(PadButton.East))
        {
            Close("screens.cancel");
            return WheelResult.Open;
        }
        if (!released && pad.Down(PadButton.LeftBumper)) return WheelResult.Open;

        int lit = s_lit;
        Close(null);
        if (lit >= 0) Open(player, s_entries[lit], pressGame);
        else Counters.Add("screens.released_empty");
        return WheelResult.Open;
    }

    /// <summary>The controlled character now (null without one).</summary>
    static global::Meitou.Simulation.CharacterSnapshot? Me(object player) =>
        Driver.ControlledId is { } id ? Access.Session(player).CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) : null;

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
            if (ReferenceEquals(e, ClearJobs))
            {
                if (Me(player) is not { } me) return;
                var world = Access.Session(player).World;
                int n = 0;
                foreach (var c in ClearCommands(me.Id, me.Jobs.Count, world.Tick)) { world.Commands.Enqueue(c); n++; }
                for (int k = 0; k < n; k++) Counters.Add("screens.job_removed");
                Log.Line(Strings.Format("log.jobs_cleared", n));
            }
            else if (e.UiAction is { } ui)
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
        int slots = s_entries.Length;
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
            SquadWheel.Centred(o, Strings.Get(s_entries[i].Key), p.X, p.Y - 9 * s, i == s_lit ? SquadWheel.Gold : SquadWheel.Ink, 1.05f * s);
        }
        SquadWheel.Centred(o, s_lit >= 0 ? Strings.Get(s_entries[s_lit].Key) : Strings.Get("screens.title"), c.X, c.Y - 26 * s,
            s_lit >= 0 ? SquadWheel.Gold : SquadWheel.Faint, 1.3f * s);
        SquadWheel.Centred(o, Strings.Get("screens.hint"), c.X, c.Y + 8 * s, SquadWheel.Faint, 0.85f * s);
        SquadWheel.Centred(o, Strings.Get("dial.cancel"), c.X, c.Y + 30 * s, SquadWheel.Faint, 0.85f * s);
        o.Flush(w, h);
    }
}
