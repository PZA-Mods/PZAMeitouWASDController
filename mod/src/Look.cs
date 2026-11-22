// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// Mouse-look in the views (docs/design.md "Views"), as the RE_Kenshi plugin's over-the-shoulder and first-person views had it: the mouse turns
// the camera, the pointer is pinned at the screen's centre (Meitou's own cursor is the crosshair) and clicks act there as usual; Alt frees it.
// Meitou already has the mode: GameHost.MouseLook() true pins and hides the pointer, feeds the movement to the tick and puts clicks and the
// hover at the pinned point (GameLoop: _orbitAnchor). The mod answers true while a view is on, centres the point and turns the rig.
using System.Numerics;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;

namespace PZA.WASDControl.Meitou;

public static class Look
{
    /// <summary>Radians per pixel of mouse movement (Meitou's own orbit drag turns 0.005).</summary>
    public const float Sensitivity = 0.004f;

    static bool s_wasOn;
    /// <summary>The mouse turns the view this frame (the last MouseLook answer).</summary>
    public static bool On => s_wasOn;
    static bool s_centrePending;

    /// <summary>
    /// The mouse turns the view now: WASD Control on, a view on, the strategy camera (not the free one), no modal window, the interface not
    /// holding the pointer (a drag, a window's widget), no text box, and Alt (Meitou's Highlight) not held: Alt frees the cursor, as in the plugin.
    /// </summary>
    public static bool Wanted()
    {
        if (!Driver.On || Views.Current == View.Normal || Driver.Player is not { } player) return false;

        var session = Access.Session(player);
        if (session.Camera.IsFree || Access.ModalOpen(player) || Access.UiCapturesPointer(player) || Access.UiEditing(player)) return false;
        if (Access.ContextMenuOpen(player)) return false;
        if (WasdWindow.FullOpen) return false;
        if (WindowOpen(player)) return false;   // trade, inventory, loot, conversation...: the cursor is the window's   // the window's buttons need the cursor   // a context menu's rows need the cursor

        var input = session.Input;
        // Alt frees the cursor, but not the pad's ZL highlight (it holds Alt for the game; the crosshair stays).
        if (Driver.PadHighlight) return !input.IsHeld(Binding.Of(Key.RightAlt));
        return !input.IsHeld(Binding.Of(Key.LeftAlt)) && !input.IsHeld(Binding.Of(Key.RightAlt));
    }

    /// <summary>
    /// The game's windows that are worked with the cursor (Meitou's screen ids). Play test 2026-11-17 ("tried to TRADE someone and like, you
    /// can't move the cursor when engaged in that (inventories opening)"): only modal screens freed it, and trade, inventories and loot are not
    /// modal. While any of these is open the mouse does not turn the view.
    /// </summary>
    public static readonly HashSet<string> CursorWindows =
    [
        "inventory", "bag", "loot", "loot-bag", "trader", "limbs", "overview", "prospect", "dialogue", "message", "menu", "build",
        "tutorials", "editor", "stats", "file",
    ];

    /// <summary>
    /// Meitou's front end (user 2026-11-20: "make WASDCTRL work at Meitou's title menu"; FrontendScreens.CloseFrontend's ids). The title, new
    /// game and import screens are front end only; load, save, the menu, files, messages and the editor are when no player character is in
    /// the world (in a game they are the Esc menu's, and the in-game pad keeps them).
    /// </summary>
    static readonly HashSet<string> FrontendOnly = ["title", "new-game", "import"], FrontendShared = ["menu", "save", "load", "message", "editor", "file"];

    /// <summary>Whether the pad works the front end now (pure: the offline check and the fuzzer drive it).</summary>
    public static bool Frontend(IEnumerable<string> open, bool playerInWorld)
    {
        foreach (var s in open)
        {
            if (FrontendOnly.Contains(s)) return true;
            if (!playerInWorld && FrontendShared.Contains(s)) return true;
        }
        return false;
    }

    /// <summary>
    /// A screen the pad works as a window (the pointer on the right stick, A / B as the mouse buttons, no zoom or view switch): the game's
    /// CursorWindows, and every PZA window (PZA's Meitou UI opens them on the game's own screen host as "pza.&lt;mod&gt;"; play test 2026-11-20 at
    /// PZA's Cat Crawlers table: "when that menu comes up I can't use my controller (d-pad, etc) on that menu").
    /// </summary>
    public static bool IsCursorWindow(string? id) =>
        id is not null && (CursorWindows.Contains(id) || id.StartsWith("pza.", StringComparison.Ordinal));

    /// <summary>One of <see cref="CursorWindows"/> is open.</summary>
    public static bool WindowOpen(object player)
    {
        if (Driver.FrontendNow) return true;   // the title menu and its screens: A and B as in a window (Driver.FrontendPad)
        foreach (var s in Access.OpenScreens(player)) if (IsCursorWindow(s)) return true;
        return Access.SettingsPanelOpen();   // Meitou's settings panel (0.10.1): the pointer, A and B as in a window
    }

    /// <summary>GameHost.MouseLook postfix: true while Wanted; an off -> on change asks for the pointer at the centre.</summary>
    public static bool Answer(bool game)
    {
        Update();
        return game || s_wasOn;
    }

    /// <summary>The look's on / off from <see cref="Wanted"/>; an off -> on change asks for the pointer at the centre.</summary>
    static void Update()
    {
        bool on = Wanted();
        if (on && !s_wasOn)
        {
            s_centrePending = true;
            Counters.Add("look.on");
        }
        else if (!on && s_wasOn) Counters.Add("look.off");

        s_wasOn = on;
    }

    /// <summary>Turns the rig by the tick's mouse movement while the look is on (the rig's own turn needs Orbit held, so nothing doubles).</summary>
    public static void Turn(WorldSession session, ActionState state)
    {
        if (!s_wasOn || state.MouseDelta == Vector2.Zero) return;

        var rig = session.Camera.Strategy;
        float f = Sensitivity * Keys.LookFactor;
        rig.Rotate(-state.MouseDelta.X * f);
        Views.Tilt(state.MouseDelta.Y * f * Keys.InvertSign);   // the views' own tilt (the rig stops at about 11 degrees up)
        Counters.Add("look.turn");
    }

    /// <summary>
    /// The interface's pointer (PlayerInterface.Mouse: what is under it, the action pointer, the hover outline) at the centre of the interface.
    /// Play test 2026-11-17 ("doesn't seem to recognize when the pointer is hovered over something interactable"): while the pointer is pinned
    /// Meitou's GameLoop.OnMouseMove only adds the movement and returns, so PlayerInterface.Mouse stayed where the mouse was before the pin;
    /// clicks went to the pinned centre but the hover, the action pointer and the outline looked elsewhere. Moved there every frame it is not.
    /// </summary>
    static void HoverAtCentre(object player)
    {
        var (w, h) = Access.UiSize(player);
        if (w <= 0 || h <= 0) return;

        var centre = new Vector2(w / 2f, h / 2f);
        if (Vector2.DistanceSquared(Access.Pointer(player), centre) < 0.25f) return;

        Access.MovePointer(player, centre);
        Counters.Add("look.hover_centred");
    }

    /// <summary>
    /// Once a frame (GameLoop.PlaceSceneCameras prefix) while the look is on: the pointer, its last position and the pinned point go to the
    /// window's centre whenever the pinned point is not there, so clicks and the cursor (the crosshair) sit in the middle of the view.
    /// Play test 2026-11-17: centring only on the off -> on frame missed whenever Meitou pinned the pointer a frame later (the pin then took the
    /// pointer where it was, off centre), and a resized window moved the centre; checking every frame catches both.
    /// </summary>
    public static void Frame(object gameLoop)
    {
        Access.RememberLoop(gameLoop);
        // Asked here every frame too: Meitou calls GameHost.MouseLook only on a mouse move or while already pinned (GameLoop.PreparePointer
        // short-circuits on a null anchor), so after Z / N the look turned on, pinned and centred only at the first mouse move (play test
        // 2026-11-17: "still relies on player to move the mouse first, then it snaps").
        Update();
        if (!s_wasOn || Driver.Player is not { } player) return;

        HoverAtCentre(player);
        if (Access.LoopWindowCentre(gameLoop) is not { } centre)
        {
            if (s_centrePending) Counters.Add("look.centre_no_window");
            s_centrePending = false;
            return;
        }

        if (Access.LoopAnchor(gameLoop) is { } anchor && Vector2.DistanceSquared(anchor, centre) < 0.25f && !s_centrePending) return;

        // Not pinned yet (Meitou pins on the first mouse move): pin at the centre now, so the crosshair is there without touching the mouse.
        if (Access.LoopAnchor(gameLoop) is null && Access.PinNow(gameLoop, centre))
        {
            Access.Session(player).Input.SetMousePosition(centre.X, centre.Y);
            Counters.Add("look.pinned");
            s_centrePending = false;
            return;
        }

        Access.CentrePointer(gameLoop, centre);
        Access.Session(player).Input.SetMousePosition(centre.X, centre.Y);
        Counters.Add(s_centrePending ? "look.centred" : "look.recentred");
        s_centrePending = false;
    }
}
