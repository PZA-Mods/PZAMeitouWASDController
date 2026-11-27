// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The behaviour (docs/design.md "How v0.1 works"). Everything here runs on the host thread: the input resolve and the control tick
// (WorldSession.Tick) and the frame's Steer, all called by the game loop one after another, so plain statics are safe.
// The world is only reached through its locked command queue, with ordinary player commands.
using System.Diagnostics;
using System.Numerics;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Simulation;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Player;

namespace PZA.WASDControl.Meitou;

public static class Driver
{
    /// <summary>WASD Control is on.</summary>
    public static bool On;
    /// <summary>The settings object (Settings > Mods); the page writes it on the same thread.</summary>
    public static DcSettings Settings = new();
    /// <summary>The settings group (Settings > Mods): the window writes through it, so the page shows the change and it is saved.</summary>
    public static global::Meitou.Modding.ModSettingsGroup? Group;

    /// <summary>Changes a setting as the Settings page would (saved, shown).</summary>
    public static void SetSetting(string key, object value) => PZA.MeitouUi.PzaSettings.Set(Group, Settings, key, value);
    /// <summary>The player interface seen last (PlayerInterface.Steer prefix).</summary>
    public static object? Player;

    /// <summary>This control tick's held direction: x = right, y = forward (each -1, 0 or 1).</summary>
    static Vector2 s_intent;
    /// <summary>The last world tick an order went out (one order per world tick at most).</summary>
    static long s_orderTick = long.MinValue;
    /// <summary>The character the last order went to; a stop goes to it on release.</summary>
    static CharacterId? s_moving;
    /// <summary>The controlled character last reported (a line when it changes).</summary>
    static CharacterId? s_controlled;
    /// <summary>The left stick's push (0..1) while it steers this control tick, else 0.</summary>
    static float s_stickPush;
    /// <summary>The left stick steered last (not the keys).</summary>
    static bool s_padSteering;
    /// <summary>The character WASD Control drives (the first selected), or null.</summary>
    public static CharacterId? ControlledId => s_controlled;

    /// <summary>V: the toggle, as in the RE_Kenshi plugin (the user's choice, 2026-11-15, over the Shift-letter rule; V is unbound in Meitou
    /// and the install's controls.cfg). Plain V only: Ctrl+V is the free camera's paste, and Ctrl (orbit) and P (photo mode) are never ours.</summary>
    /// <summary>G: take control of your character under the pointer, as in the plugin; plain G only, unbound in Meitou; only while on.
    /// (No speed key of ours: the install's controls.cfg binds cycle_run_speed to X and Meitou reads it, so X is Meitou's run speed; the user
    /// chose that over a copy of it, 2026-11-16.)</summary>
    /// <summary>Z: over-the-shoulder view on / off; N: first person on / off (the plugin had Ctrl and P; the user chose Z and N, 2026-11-16).
    /// Plain keys, unbound in Meitou and the install's controls.cfg; only while on.</summary>

    /// <summary>G went down this control tick (set in the input step, used in the tick step right after it).</summary>
    static bool s_takePressed;
    /// <summary>Shift+F went down this control tick: toggle Follow Me for the active squad.</summary>
    static bool s_followPressed;

    #region Input (InputBindings.Resolve postfix)

    /// <summary>The toggle key, then the four move actions taken out of the camera's hands while on.</summary>
    public static void OnResolve(TickInput input, ActionState state)
    {
        s_intent = Vector2.Zero;
        s_padTiltRate = 0;   // set again by the pad's tick when it tilts a view
        s_stickPush = 0;     // set again when the left stick steers
        if (input.IsDown(Key.W) || input.IsDown(Key.A) || input.IsDown(Key.S) || input.IsDown(Key.D)) s_padSteering = false;   // the keys steer
        s_takePressed = false;
        // A text box has the keyboard: the mod's KEYS wait (they would type), the controller does not (play test 2026-11-20: "jump's not
        // working again"; log skip.typing +86 after the last jump: a focused text box had silenced the whole pad).
        bool editing = Player is not null && Access.UiEditing(Player);
        if (editing) Counters.Add("skip.typing_keys");

        bool shift = !editing && (input.IsDown(Key.LeftShift) || input.IsDown(Key.RightShift));
        bool ctrl = input.IsDown(Key.LeftControl) || input.IsDown(Key.RightControl);
        bool alt = input.IsDown(Key.LeftAlt) || input.IsDown(Key.RightAlt);
        bool plain = !editing && !ctrl && !alt && !shift;
        if (!editing && Player is not null && Keys.Capture(input, Access.Session(Player).Bindings)) return;   // the window's Keys tab waits for a key
        if (plain && input.WasPressed(Keys.Of(KeyAction.Toggle))) Toggle(Keys.Of(KeyAction.Toggle).ToString());
        var pad = Player is null ? PadState.None : Pad.Read();
        Prompts.Note(pad);
        if (pad.Connected && Pad.WasPressed(PadButton.View)) Toggle("pad");
        if (PadHighlight && (!pad.Connected || !On)) SetHighlight(Player is null ? null : Access.Session(Player), false);
        ReleaseLost(pad);   // before anything below can skip the pad: what it no longer holds is let go
        if (!pad.Connected) FrontendNow = false;   // a pad disconnected on the title must not leave Look.WindowOpen stuck true
        if (Player is not null) ManualCombatLink.Sync(Access.Session(Player));
        if (pad.Connected && Player is not null && FrontendPad(pad)) return;   // the title menu and its screens (2026-11-20), on or off
        if (!On) return;

        s_takePressed = plain && input.WasPressed(Keys.Of(KeyAction.Take));
        // Shift+U: jump (user 2026-11-20: not Shift+I, I is the inventory key; U is the one letter no game, Direct Control or PZA key uses)
        if (shift && !ctrl && !alt && input.WasPressed(Key.U) && JumpOn) s_jumpPressed = true;
        bool shoulder = plain && input.WasPressed(Keys.Of(KeyAction.Shoulder)), first = plain && input.WasPressed(Keys.Of(KeyAction.First));
        if (shoulder || first) Views.Set(Views.Next(Views.Current, new ViewInput(shoulder, first)), ShowNotice);

        if (Player is not null && Access.Session(Player).Camera.IsFree)
        {
            Counters.Add("skip.free_camera");
            return;
        }

        // Shift+F: toggle Follow Me for the active squad (not in Free Camera, where F flies down; GameHotkeys.Key prefix skips FocusCharacter)
        if (shift && !ctrl && !alt && input.WasPressed(Key.F)) s_followPressed = true;

        float fwd = editing ? 0 : (state.Held(Act.MoveForward) ? 1 : 0) - (state.Held(Act.MoveBack) ? 1 : 0);
        float right = editing ? 0 : (state.Held(Act.MoveRight) ? 1 : 0) - (state.Held(Act.MoveLeft) ? 1 : 0);
        s_intent = new Vector2(right, fwd);   // typing W A S D never steers

        foreach (var a in MoveActions)
        {
            if (state.Held(a) || state.Pressed(a) || state.Released(a)) Access.SetAction(state, a, false, false, false);
        }

        if (Player is null) return;   // no world yet (the title screen, the offline check)
        if (pad.Connected) ApplyPad(pad, Access.Session(Player), state);
        Look.Turn(Access.Session(Player), state);
    }

    #region Game controller (docs/controller-design.md)

    /// <summary>Right stick: radians a second at full tilt (turn, tilt).</summary>
    public const float PadYawRate = 2.5f, PadPitchRate = 1.2f;
    static long s_lastPad;

    /// <summary>The pad's part of a control tick (WASD Control on, no text box, no free camera): the left stick steers when no key does, the
    /// right stick turns and tilts the rig, the buttons press Meitou's own actions (docs/controller-design.md "Proposed layout").</summary>
    static void ApplyPad(PadState pad, WorldSession session, ActionState state)
    {
        float dt = PadSeconds();
        if (s_intent == Vector2.Zero && pad.Left != Vector2.Zero)
        {
            s_intent = pad.Left;
            s_stickPush = Math.Min(1, pad.Left.Length());
            s_padSteering = true;
            Counters.Add("pad.steer");
        }

        var rig = session.Camera.Strategy;
        float look = Keys.LookFactor;
        bool window = Look.WindowOpen(Player!);   // trade, inventory, loot...: the right stick moves the pointer (PadPointer)
        PadPointer.Stick(window ? pad.Right : Vector2.Zero);
        bool menuNow = window || Access.ContextMenuOpen(Player!) || SquadWheel.IsOpen || ScreenWheel.IsOpen || OrdersDial.IsOpen;   // the right stick picks rows in a menu / faces on the wheel, moves the pointer in a window, not the view
        // Play test clip 2026-11-17 20:17: the push that aimed at a face on the squad dial went on into the look once the dial closed (the view
        // tilted to the ground). After any dial, menu or window closes the right stick looks again only once it has come back to the centre.
        bool menu = !LookAfterMenu(ref s_menuWasOpen, ref s_waitStickCentre, menuNow, pad.Right.Length());
        if (menu && !menuNow) Counters.Add("pad.look_wait_centre");
        // PZA's Director's Kit's 45-degree lock (2026-11-20): left / right flick to the next quadrant instead of turning (CameraFlick)
        bool flick = !menu && Views.Current == View.Normal && CameraFlick.Step(rig, pad.Right.X, dt);
        if (!flick && !menu && pad.Right.X != 0) rig.Rotate(-pad.Right.X * PadYawRate * look * dt);
        if (!menu && pad.Right.Y != 0)
        {
            if (Views.Current == View.Normal) rig.PitchStep(-pad.Right.Y * PadPitchRate * dt);
            else s_padTiltRate = -pad.Right.Y * PadPitchRate * look * Keys.InvertSign;   // applied every frame (OnFrame), not in 30 Hz steps
        }
        if (pad.Right != Vector2.Zero) Counters.Add("pad.look");

        SafeExtra.Refresh(Player!);   // the target line under the crosshair (0.10)
        // Fight Mode (ManualCombatLink): when ManualCombat is live in a fight and no window/menu/dial is open, the pad drives attack/block/dodge/target/footwork directly.
        bool mcFight = ManualCombatLink.Live && !window && !menuNow && !BuildPad.On && !ConversationPad.Open(session);
        if (!mcFight) PadR3(session);   // before the menu's early return, so a release is never lost and a look back never sticks
        // the Esc menu and its save / load screens (user 2026-11-20): the D-pad and left stick walk it, a name box gets the controller keyboard
        if (MenuNav.Applies(Access.OpenScreens(Player!)))
        {
            bool typing = OnScreenKeyboard.Open;
            MenuNav.Tick(Player!, pad);
            if (!typing && !OnScreenKeyboard.Open) PadClicks();
            return;
        }
        MenuNav.Away();
        // a PZA window on top (Cat Crawlers' table, the SlowMo / Kit windows): the D-pad walks its buttons and lists, A presses the focused one
        string? pzaTop = MenuNav.PzaTop(Access.OpenScreens(Player!));
        s_pzaWindowTop = pzaTop is not null;
        if (pzaTop is not null) MenuNav.WindowTick(Player!, pzaTop, pad);
        // LB + RB together (0.10): the Orders dial; while the chord is on, neither bumper's own tap or dial happens.
        bool mayDial = !window && !Access.ContextMenuOpen(Player!);
        // A conversation (2026-11-20): D-pad up / down choose a reply, A says it (ConversationPad); B, the stick and the clicks as in a window
        if (ConversationPad.Step(session, ShowNotice))
        {
            PadClicks();
            return;
        }
        // Build mode (0.10.1): the bumpers rotate, the D-pad sets height and slope (LT + D-pad: the floor), X undoes, Y confirms (BuildPad)
        if (BuildPad.Step(Player!, pad))
        {
            PadClicks();
            PadControlTick(rig, state);
            return;
        }
        bool chord = OrdersDial.Chord.Step(Dial.Clock(), pad.Down(PadButton.LeftBumper), pad.Down(PadButton.RightBumper), Pad.WasPressed(PadButton.LeftBumper),
                Pad.WasPressed(PadButton.RightBumper), mayDial && !SquadWheel.IsOpen && !ScreenWheel.IsOpen);
        if (DialRules.OrdersHasPad(chord, OrdersDial.IsOpen))   // open: the release of both bumpers on one tick still reaches it (DialRules)
        {
            SquadWheel.Abort();
            ScreenWheel.Dial.Abort();
            OrdersDial.Pad(Player!, pad, mayDial);
            return;   // the chord has the pad (the dial, or the bumpers still held after a pick)
        }
        switch (SquadWheel.Pad(Player!, pad, s_controlled))
        {
            case WheelResult.Tap: PressGame(session, Act.NextCharacter); break;   // RB tapped: the next member, as before
            case WheelResult.Everyone: PressGame(session, Act.SelectAll); return;   // the dial's top slice: the whole squad, as ~
            case WheelResult.SquadPace: ToggleSquadPace(session); return;   // the dial's bottom slice
            case WheelResult.FollowMe: FollowMe(session); return;           // the dial's left slice
            case WheelResult.Open: return;   // RB held: the squad wheel has the pad (stick, D-pad / LB pages, B cancels)
        }
        // the interface hidden (the screens dial's HIDE UI, or F7): LB brings it back (the dial cannot draw while it is hidden)
        if (Access.InterfaceHidden == true && Pad.WasPressed(PadButton.LeftBumper))
        {
            Access.SetInterfaceHidden(false);
            Counters.Add("pad.show_ui");
            return;
        }
        switch (ScreenWheel.Pad(Player!, pad))
        {
            case WheelResult.Tap: PressGame(session, Act.PreviousCharacter); break;   // LB tapped: the previous member, as before
            case WheelResult.Open: return;   // LB held: the screens dial has the pad
        }
        // A outside windows and menus (0.10): the safe extra option for what the crosshair is on (the Orders dial moved to LB + RB).
        // A outside windows and menus: the safe action on what the crosshair is on; with nothing under it, the jump (JumpRules).
        // Skip while LT+A is held for ManualCombat's out-of-fight crossbow reload.
        bool mcReloading = pad.Down(PadButton.LeftTrigger) && ManualCombatLink.Active && !ManualCombatLink.InFight && ManualCombatLink.HasCrossbow && ManualCombatLink.Empty;
        if (mayDial && !mcReloading && Pad.WasPressed(PadButton.South))
        {
            // a safe action only when there is one (play test 2026-11-20, after a fight: the pointer or crosshair on a body or a person with no
            // safe option took A, said "nothing to do", and the character never jumped; headless run: 3 of 3 A presses went to extra.none.person)
            if (JumpOn && JumpRules.APress(SafeExtra.Verb is not null) == TapResult.Jump) s_jumpPressed = true;
            else SafeExtra.Give(Player!);
        }
        if (Menus.Pad(Player!, pad)) return;   // a menu is open: the pad picks in it (D-pad, interact, select)

        if (!window) PadViews(pad);   // Y / X switch views only outside windows (in a PZA window they are its own; play test 2026-11-20)
        PadHotkeys(session);
        if (ManualCombatLink.StepPad(Player!, pad, session, window || menuNow || BuildPad.On || ConversationPad.Open(session)))
        {
            SetHighlight(session, false);   // never hold Alt in Fight Mode (LeftAlt disables ManualCombat clicks/aim)
            PadControlTick(rig, state, window);
            return;
        }
        PadTriggers(pad, session);
        PadClicks();
        PadControlTick(rig, state, window);
    }

    /// <summary>
    /// The pad on Meitou's front end (the title, new game, load...; Look.Frontend): either stick moves the pointer, A is the left button, B is
    /// Esc (back), as in a game window (PadClicks). Works whether WASD Control is on or off. False when the front end is not showing.
    /// </summary>
    /// <summary>The pad works Meitou's front end this tick (FrontendPad).</summary>
    public static bool FrontendNow { get; private set; }

    static bool FrontendPad(PadState pad)
    {
        bool frontend;
        try
        {
            var open = Access.OpenScreens(Player!);
            bool playerInWorld = Access.Session(Player!).CurrentSnapshot?.Characters.Any(c => c.IsPlayer) ?? false;
            frontend = Look.Frontend(open, playerInWorld);
        }
        catch (Exception)
        {
            Counters.Add("pad.frontend_unreadable");   // no session yet: the pad waits (counted, not logged every tick)
            FrontendNow = false;
            return false;
        }
        FrontendNow = frontend;
        if (!frontend) return false;

        // the D-pad and left stick walk the menu, A presses, a name box gets the controller keyboard (MenuNav, user 2026-11-20)
        if (MenuNav.Applies(Access.OpenScreens(Player!)))
        {
            bool typing = OnScreenKeyboard.Open;
            MenuNav.Tick(Player!, pad);
            if (typing || OnScreenKeyboard.Open) return true;   // the keyboard has the whole pad
            PadPointer.Stick(pad.Right);
        }
        else
        {
            MenuNav.Away();
            PadPointer.Stick(pad.Right != Vector2.Zero ? pad.Right : pad.Left);
        }
        PadClicks();
        Counters.Add("pad.frontend");
        return true;
    }

    /// <summary>Seconds since the last pad tick, 0..0.1 (a long gap, as after a pause screen, must not spin the camera).</summary>
    static float PadSeconds()
    {
        long now = Stopwatch.GetTimestamp();
        float dt = s_lastPad == 0 ? 0 : (float)Math.Min(Stopwatch.GetElapsedTime(s_lastPad, now).TotalSeconds, 0.1);
        s_lastPad = now;
        return dt;
    }

    /// <summary>Top face button: over-the-shoulder (as Z); left face button: first person (as N). LT+X is ManualCombat's swap when it has a crossbow.</summary>
    static void PadViews(PadState pad)
    {
        bool north = Pad.WasPressed(PadButton.North);
        bool west = Pad.WasPressed(PadButton.West) && !(pad.Down(PadButton.LeftTrigger) && ManualCombatLink.Active && ManualCombatLink.HasCrossbow);
        if (north || west) Views.Set(Views.Next(Views.Current, new ViewInput(north, west)), ShowNotice);
    }

    /// <summary>The pad's mouse buttons at the pointer (in a view, the crosshair): the bottom face button is the right button (interact:
    /// tap for the action, hold for the menu, as the mouse), the right face button the left one (select, or inspect your own in a view).
    /// Press and release are passed on as they happen, so holding works as with the mouse.</summary>
    static void PadClicks()
    {
        // B (the right face button) closes a game window, as Esc does (play test 2026-11-17: "once I'm done with the trade pressing like, B or
        // something intuitive doesn't close it out"): trade and loot close with the inventories, else the top window. Its release is the close's.
        CheckStuckClose();
        bool panel = Access.SettingsPanelOpen();
        if (Pad.WasPressed(PadButton.East) && !panel && BuildPad.Drop(Player!))
        {
            s_eastClosed = true;   // build mode: B drops the held building (as the right click's Clear Building); with none held B is Esc below
        }
        else if (Pad.WasPressed(PadButton.East) && panel && Access.SettingsPanelEscape())
        {
            s_eastClosed = true;   // the panel's Esc, as the keyboard's (a choice or search first, else it closes); not the game menu under it
            Counters.Add("pad.close_settings");
        }
        else if (Pad.WasPressed(PadButton.East) && Look.WindowOpen(Player!))
        {
            s_closeBefore = [.. Access.OpenScreens(Player!)];
            s_closeAt = Dial.Clock();
            Access.Escape(Player!);
            s_eastClosed = true;
            Counters.Add("pad.close_window");
        }
        // A is the left button while a game window is open (pick up, drop, buy, sell); otherwise free (0.9.21).
        if (Look.WindowOpen(Player!) || s_aDown)
        {
            if (Pad.WasPressed(PadButton.South) && !MenuNav.TookA)   // a press the menu took (a focused button, the keyboard) is no click
            {
                s_aThroughLoop = panel && Access.LoopClick(MouseButton.Left, true);   // the settings panel takes the mouse from the game loop
                if (!s_aThroughLoop) Click(MouseButton.Left, true);
                else Counters.Add("pad.click_settings");
                s_aDown = true;
            }
            if (Pad.WasReleased(PadButton.South) && s_aDown)
            {
                if (!(s_aThroughLoop && Access.LoopClick(MouseButton.Left, false))) Click(MouseButton.Left, false);
                s_aDown = false;
            }
        }
        foreach (var (button, mouse) in PadMouse)
        {
            bool closing = button == PadButton.East && s_eastClosed;
            if (Pad.WasPressed(button) && !closing)
            {
                Click(mouse, true);
                s_clicks.Down(button);
            }
            if (Pad.WasReleased(button))
            {
                if (!closing && s_clicks.Up(button)) Click(mouse, false);   // only a press this step sent is let go (once: ReleaseLost)
                if (button == PadButton.East) s_eastClosed = false;
            }
        }
    }

    /// <summary>A went down as the left button in a window: its release is the button's too, even if the window closed meanwhile.</summary>
    static bool s_aDown;
    /// <summary>That press went through the game loop (Meitou's settings panel), so its release does too.</summary>
    static bool s_aThroughLoop;
    /// <summary>A dial, menu or window had the right stick last tick; after it closes the stick looks again only once back under <see cref="StickCentre"/>.</summary>
    static bool s_menuWasOpen, s_waitStickCentre;
    public const float StickCentre = 0.2f;

    /// <summary>The right stick's look after a dial, menu or window closed (for the offline check): false until the stick has come back.</summary>
    public static bool LookAfterMenu(ref bool wasOpen, ref bool wait, bool menuNow, float push)
    {
        if (wasOpen && !menuNow) wait = true;
        wasOpen = menuNow;
        if (wait && push < StickCentre) wait = false;
        return !menuNow && !wait;
    }

    /// <summary>The B press closed a window: neither its press nor its release is a click.</summary>
    static bool s_eastClosed;

    // 0.10.1 (play test 2026-11-18: caught stealing from a container, then arrested, the loot window stayed open "no matter how many times I
    // tried to press B"; the log shows four closes, each the game's own Esc, PlayerInterface.EscapeKey, which left it open). A moment after
    // B, if exactly the same screens are still open, the top one is closed directly, as its own close button does, with a log line naming it.
    static long s_closeAt = -1;
    static string[] s_closeBefore = [];
    public const long CloseCheckMs = 250;

    /// <summary>The screen to close when the game's Esc left the screens as they were (null: Esc worked, or nothing is open).</summary>
    public static string? StuckScreen(IReadOnlyList<string> before, IReadOnlyList<string> after) =>
        after.Count > 0 && after.SequenceEqual(before) ? after[^1] : null;

    static void CheckStuckClose()
    {
        if (s_closeAt < 0 || Dial.Clock() - s_closeAt < CloseCheckMs) return;
        s_closeAt = -1;
        var after = Access.OpenScreens(Player!);
        if (StuckScreen(s_closeBefore, after) is not { } id) return;
        Counters.Add("pad.close_stuck");
        Log.Line(Strings.Format("log.close_stuck", id, string.Join(", ", after)));
        try { Access.CloseScreen(Player!, id); }
        catch (Exception e) { Log.Exception("close stuck screen", e); }
    }

    /// <summary>The pad's mouse buttons down in the game (HeldClicks).</summary>
    static readonly HeldClicks s_clicks = new();

    /// <summary>
    /// Every control tick, before anything can skip the pad (a dial, a menu, the front end, WASD Control off, the free camera): what the pad
    /// held and no longer holds is let go now, if the tick that saw its release did not (the review after the 2026-11-20 stuck dial):
    /// the mouse buttons B and ZR pressed in the game (else Meitou's drag box or its steer to the pointer goes on), A as a window's left
    /// button, ZL's highlight (Alt), R3's look-back (else the view stays turned round and the stick steers backwards), the Menu button's
    /// timing and B's "this release closed a window" (else the next B press is swallowed). Each is counted as pad.release_lost.*.
    /// </summary>
    static void ReleaseLost(PadState pad)
    {
        if (Player is null) return;
        try
        {
            var session = Access.Session(Player);
            if (PadHighlight && !pad.Down(PadButton.LeftTrigger))
            {
                SetHighlight(session, false);
                Counters.Add("pad.release_lost.highlight");
            }
            // Meitou's held right-click menu answers ZR's release itself (Menus): the game owns that press now
            if (s_clicks.Held(PadButton.RightTrigger) && Access.ContextMenuOpen(Player)) s_clicks.Forget(PadButton.RightTrigger);
            foreach (var b in s_clicks.Lost(pad.Down))
            {
                var mouse = PadMouse.FirstOrDefault(m => m.Button == b).Mouse;
                Click(mouse, false);
                Counters.Add("pad.release_lost." + b);
            }
            if (s_aDown && !pad.Down(PadButton.South))
            {
                if (!(s_aThroughLoop && Access.LoopClick(MouseButton.Left, false))) Click(MouseButton.Left, false);
                s_aDown = false;
                Counters.Add("pad.release_lost.a");
            }
            bool r3Released = Pad.WasReleased(PadButton.RightStick);   // a release this tick is PadR3's own (tap: behind; held: look back ends)
            if (s_r3Since >= 0 && !pad.Down(PadButton.RightStick) && !r3Released)
            {
                s_r3Since = -1;
                if (s_lookBack != 0)
                {
                    session.Camera.Strategy.Rotate(-s_lookBack);
                    s_lookBack = 0;
                }
                Counters.Add("pad.release_lost.r3");
            }
            if (s_menuSince >= 0 && !pad.Down(PadButton.Menu) && !Pad.WasReleased(PadButton.Menu))
            {
                s_menuSince = -1;
                Counters.Add("pad.release_lost.menu");
            }
            if (s_eastClosed && !pad.Down(PadButton.East) && !Pad.WasReleased(PadButton.East))
            {
                s_eastClosed = false;
                Counters.Add("pad.release_lost.b_closed");
            }
            if (!pad.Connected || !On || !ManualCombatLink.Live) ManualCombatLink.ReleasePadState(session);
        }
        catch (Exception e)
        {
            Log.Exception("pad release guard", e);
        }
    }

    static readonly (PadButton Button, MouseButton Mouse)[] PadMouse =
        [(PadButton.RightTrigger, MouseButton.Right), (PadButton.East, MouseButton.Left)];   // ZR interact (0.9.19; was A), B select

    static void Click(MouseButton mouse, bool down)
    {
        Access.Click(Player!, mouse, down, alt: PadHighlight);
        Counters.Add("pad.click." + mouse.ToString().ToLowerInvariant() + (down ? ".down" : ".up"));
    }

    static readonly (PadButton Button, InputAction Action)[] PadGameKeys =
    [
        (PadButton.LeftStick, Act.ToggleSneak),   // L3: the user's choice (2026-11-17)
    ];

    /// <summary>Buttons that press a game hotkey: the key this install binds to the action (controls.cfg or Meitou's default), through the
    /// game's own GameHotkeys, so the pad does exactly what the keyboard does. An unbound action is counted, never guessed.</summary>
    static void PadHotkeys(WorldSession session)
    {
        foreach (var (button, action) in PadGameKeys)
        {
            if (!Pad.WasPressed(button)) continue;

            PressGame(session, action);
        }
    }

    /// <summary>Presses a game action now (the dials' slices).</summary>
    internal static void Press(InputAction action)
    {
        if (Player is { } p) PressGame(Access.Session(p), action);
    }

    /// <summary>Presses the key this install binds to a game action, through the game's own GameHotkeys; an unbound action is counted.</summary>
    static void PressGame(WorldSession session, InputAction action)
    {
        var key = session.Bindings.Get(action).FirstOrDefault(b => !b.IsMouse);
        if (key == default)
        {
            Counters.Add("pad.unbound." + action);
            return;
        }

        Access.PressHotkey(Player!, key.Key);
        Counters.Add("pad." + action);
    }

    #region Triggers and R3 (user, 2026-11-17)

    /// <summary>ZL is held: the game's highlight (Alt) is held for the pad, so loot names show and A becomes Alt + right click (loot, steal).</summary>
    public static bool PadHighlight { get; private set; }

    /// <summary>
    /// ZL (hold) highlight, so ZL + ZR (interact) is loot / steal; ZL + B stop; A is free (0.9.20). The user,
    /// 2026-11-17: "ZR should actually probably do what A does. A should be the button to attack a hostile"; the stop moved from ZL + ZR to
    /// ZL + B (ZL + interact had been loot / steal since 0.8.1).
    /// </summary>
    static void PadTriggers(PadState pad, WorldSession session)
    {
        bool zl = pad.Down(PadButton.LeftTrigger);
        SetHighlight(session, zl);

        if (zl && Pad.WasPressed(PadButton.East))
        {
            PressGame(session, Act.StopMovement);
            s_eastClosed = true;   // this B is the stop's, not a click
            Counters.Add("pad.stop_chord");
        }
        // A is free since 0.9.20 (the user: "Leave A for something new"): ZR's tap already attacks a hostile (the right click's quick
        // order) and its held menu has every attack. CrosshairAttack stays in the source for a later use.
    }

    /// <summary>
    /// The squad dial's FOLLOW ME: the rest of the dial's squad follows the controlled character (Meitou's FollowOrder, as the right-click
    /// menu's Follow gives it).
    /// </summary>
    static void FollowMe(WorldSession session)
    {
        if (s_controlled is not { } leader) { Counters.Add("follow.no_leader"); return; }

        var live = Player is not null ? Access.SquadBarMembers(Player).Select(c => c.Id).ToArray() : [];
        var squad = live.Length > 0 ? live : SquadWheel.Members;
        var others = squad.Where(i => i != leader).ToArray();
        if (others.Length == 0) { Counters.Add("follow.alone"); return; }

        long tick = session.World.Tick;
        if (FollowingNow(leader))
        {
            // Chosen again while the squad follows this leader: they stop (user, 2026-11-17: "selecting again from radial menu should turn it
            // off and the character being followed should say 'Stop following!'"). Meitou's stop clears a follow order (PlayerSystem.Stop).
            session.World.Commands.Enqueue(new StopCommand(others) { Tick = tick });
            session.World.Commands.Enqueue(new BarkCommand(leader, Strings.Get("bark.stop_following")) { Tick = tick });
            s_followLeader = null;
            Counters.Add("follow.stopped");
            return;
        }

        session.World.Commands.Enqueue(new global::Meitou.Simulation.FollowOrder(others, leader) { Tick = tick });
        // the leader says it (user, 2026-11-17: "should make the selected character say 'Follow me!' with a text bubble over their head")
        session.World.Commands.Enqueue(new BarkCommand(leader, Strings.Get("bark.follow_me")) { Tick = tick });
        s_followLeader = leader;
        Counters.Add("follow.me");
        Log.Line(Strings.Format("log.follow_me", others.Length));
    }

    /// <summary>The leader the squad was last told to follow from the dial (null after a stop), for the dial's toggle.</summary>
    static CharacterId? s_followLeader;

    /// <summary>The squad follows <paramref name="leader"/> from the dial's last FOLLOW ME (the dial shows "Stop following" then).</summary>
    public static bool FollowingNow(CharacterId leader) => s_followLeader == leader;

    /// <summary>
    /// The squad dial's SQUAD PACE: Meitou's group speed for the dial's squad (everyone at the slowest member), or off again (full run).
    /// One RunSpeedCommand for them all, the controlled character first: every one takes the first one's new mode (Orders.cs, RunSpeedCommand).
    /// </summary>
    static void ToggleSquadPace(WorldSession session)
    {
        var ids = SquadWheel.Members;
        if (ids.Count == 0) return;

        var first = s_controlled is { } c && ids.Contains(c) ? c : ids[0];
        if (session.CurrentSnapshot.Characters.FirstOrDefault(x => x.Id == first) is not { } lead) return;

        byte group = global::Meitou.Simulation.Movement.SpeedMode.Group, free = global::Meitou.Simulation.Movement.SpeedMode.Free;
        byte want = lead.RunSpeed == group ? free : group;
        CharacterId[] order = [first, .. ids.Where(i => i != first)];
        session.World.Commands.Enqueue(new global::Meitou.Simulation.Player.RunSpeedCommand(order, StickSpeed.Step(lead.RunSpeed, want)) { Tick = session.World.Tick });
        Counters.Add(want == group ? "pace.on" : "pace.off");
    }

    /// <summary>Holds or lets go of the game's highlight key (Alt) for the pad, on the input the control tick reads (InputState.SetKey).</summary>
    public static void SetHighlight(WorldSession? session, bool on)
    {
        if (on == PadHighlight || session is null) return;

        PadHighlight = on;
        session.Input.SetKey(Key.LeftAlt, on);
        Counters.Add(on ? "pad.highlight_on" : "pad.highlight_off");
    }

    /// <summary>How long R3 must be held to look back instead of snapping behind (ms).</summary>
    public const long LookBackHoldMs = 250;
    /// <summary>The turn the look-back put on the rig (pi while R3 is held past LookBackHoldMs, else 0): steering subtracts it, so walking
    /// keeps going the way the character went, and the release takes it off again.</summary>
    static float s_lookBack;
    static long s_r3Since = -1;

    /// <summary>
    /// R3 (play test 2026-11-17: "doesn't look behind, seems to just look straight forward"): a tap snaps the camera behind the character as
    /// before; held, the view turns round to look back (GTA's look-behind) until it is let go, and W A S D / the stick keep their way meanwhile.
    /// </summary>
    static void PadR3(WorldSession session)
    {
        var rig = session.Camera.Strategy;
        long now = Environment.TickCount64;
        if (Pad.WasPressed(PadButton.RightStick) && Dive(session)) return;   // R3 mid-jump: the dive, no look back or camera snap
        if (Pad.WasPressed(PadButton.RightStick)) s_r3Since = now;

        if (s_r3Since >= 0 && s_lookBack == 0 && now - s_r3Since >= LookBackHoldMs && !Pad.WasReleased(PadButton.RightStick))
        {
            s_lookBack = MathF.PI;
            rig.Rotate(s_lookBack);
            Counters.Add("pad.look_back");
        }

        if (!Pad.WasReleased(PadButton.RightStick) || s_r3Since < 0) return;

        s_r3Since = -1;
        if (s_lookBack != 0)
        {
            rig.Rotate(-s_lookBack);
            s_lookBack = 0;
            Counters.Add("pad.look_back_end");
        }
        else CameraBehind(session);
    }

    /// <summary>R3 while a jump plays (GTA's dive): a tumble thrown forward along the character's facing (LedgeRules.DivePush). Needs the jump
    /// on and the ledge patch (TumbleSystem's ragdoll); false when it does not apply, and R3 keeps its look-back / camera-behind meaning.</summary>
    static bool Dive(WorldSession session)
    {
        if (!JumpOn || !LedgePatch.Installed || s_controlled is not { } id) return false;
        if (!JumpSystem.Jumping(session.World, id, session.World.Tick)) return false;
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is not { } lead) return false;

        var facing = JumpRules.Facing(lead.Yaw);
        Counters.Add("jump.dive");
        Log.Line(Strings.Format("log.jump_dive", s_speed / 10f));
        Tumble(session, lead, facing, LedgeRules.DivePush(facing));
        s_r3Since = -1;
        return true;
    }

    /// <summary>R3 tap: the camera turns to look where the steered character faces (GTA style). Meitou's character yaw faces (sin, cos) on the
    /// ground (AiSystem, CarrySystem: Yaw = Atan2(dx, dz)); the camera looks along (-sin, -cos) of its own yaw, so behind = yaw + pi.</summary>
    static void CameraBehind(WorldSession session)
    {
        // the game's own focus on the selected character first (user 2026-11-20: "how do we focus the character we have selected?"; Meitou's
        // FocusCharacter, F on the keyboard)
        PressGame(session, Act.FocusCharacter);
        if (s_controlled is not { } id || session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is not { } lead)
        {
            Counters.Add("pad.behind_no_character");
            return;
        }

        var rig = session.Camera.Strategy;
        rig.Rotate(BehindTurn(lead.Yaw, rig.Yaw));
        if (Views.Current != View.Normal) Views.Pitch = Views.StartPitch;
        Counters.Add("pad.behind");
    }

    /// <summary>The turn (radians, -pi..pi) that puts a camera of yaw <paramref name="cameraYaw"/> behind a character of yaw <paramref name="characterYaw"/>.</summary>
    public static float BehindTurn(float characterYaw, float cameraYaw)
    {
        float d = characterYaw + MathF.PI - cameraYaw;
        d = MathF.IEEERemainder(d, 2 * MathF.PI);
        return d;
    }

    #endregion

    /// <summary>D-pad up / down zoom the rig; D-pad left / right and Menu set the control tick's own time actions (WorldSession.Tick reads
    /// them right after this: TimeSlower / TimeFaster / Pause).</summary>
    /// <summary>Menu / + held this long opens the game menu (ms; 2 s felt too long, the user 2026-11-17).</summary>
    public const long MenuHoldMs = 1000;
    static long s_menuSince = -1;
    /// <summary>A PZA window is the top screen this tick: the D-pad is its (MenuNav.WindowTick), not zoom or game speed.</summary>
    static bool s_pzaWindowTop;
    static bool s_menuHeld;

    static void PadControlTick(global::Meitou.Game.Host.Cameras.StrategyCamera rig, ActionState state, bool window = false)
    {
        if (!BuildPad.On && !s_pzaWindowTop)   // in build mode the D-pad is height, slope and floor (BuildPad); in a PZA window its buttons (MenuNav)
        {
            // D-pad up / down zoom, except with a window open (a PZA window's list, a trade: the camera stays; play test 2026-11-20)
            if (!window && Pad.WasPressed(PadButton.DPadUp)) rig.Zoom(1);
            if (!window && Pad.WasPressed(PadButton.DPadDown)) rig.Zoom(-1);
            // D-pad left / right: Meitou's speeds, with slow motion one step below 1x (0.10.2, DpadSlow)
            foreach (var (button, left) in new[] { (PadButton.DPadLeft, true), (PadButton.DPadRight, false) })
            {
                if (!Pad.WasPressed(button) || Player is null) continue;
                if (DpadSlow.Press(left, Access.Session(Player).Simulation, ShowNotice) is { } meitou) Press(state, meitou);
            }
        }
        // Menu / +: a tap pauses (on its release); held MenuHoldMs it opens the game's Esc menu instead (the user, 2026-11-17: "if you HOLD
        // the Pause button for like 2-3 seconds, it will bring up the Escape menu").
        long now = Environment.TickCount64;
        if (Pad.WasPressed(PadButton.Menu)) { s_menuSince = now; s_menuHeld = false; }
        if (s_menuSince >= 0 && !s_menuHeld && Pad.Last.Down(PadButton.Menu) && now - s_menuSince >= MenuHoldMs)
        {
            s_menuHeld = true;
            Access.Escape(Player!);   // the interface's own Esc: closes a window if one is open, else the game menu
            Counters.Add("pad.menu_hold");
        }
        if (Pad.WasReleased(PadButton.Menu) && s_menuSince >= 0)
        {
            if (!s_menuHeld) Press(state, Act.Pause);
            s_menuSince = -1;
        }
    }

    static void Press(ActionState state, InputAction a)
    {
        Access.SetAction(state, a, true, true, false);
        Counters.Add("pad." + a);
    }

    #endregion

    static readonly InputAction[] MoveActions =
        [Act.MoveForward, Act.MoveBack, Act.MoveLeft, Act.MoveRight];

    /// <summary>Flips WASD Control (the key, the F10 row).</summary>
    public static string Toggle(string from)
    {
        On = !On;
        Counters.Add(On ? "toggle.on" : "toggle.off");
        if (!On) StopIfMoving(Player is null ? null : Access.Session(Player), "toggle off");
        if (!On) ViewLens.Release("off");
        if (!On) Views.Current = View.Normal;   // the views belong to WASD Control: off gives the camera back whole
        s_controlled = null;
        string line = Strings.Format(On ? "log.on" : "log.off", from);
        Log.Line(line);
        ShowNotice(Strings.Get(On ? "notice.on" : "notice.off"));
        return line;
    }

    /// <summary>The on-screen line (Meitou's message panel, as its "Saved"); counted, and logged when it cannot show (no game interface yet).</summary>
    public static void ShowNotice(string text)
    {
        if (Player is null)
        {
            Counters.Add("notice.no_interface");
            return;
        }

        Access.Notice(Player, text);
        Counters.Add("notice.shown");
    }

    #endregion

    #region Views (SandboxScenario.FollowCamera postfix, once a frame)

    /// <summary>Moves the eye to the steered character's shoulder or head, after Meitou's follow camera placed the view and before its eye
    /// clearance (GameLoop.KeepEyeAboveGround) runs. Yaw and pitch stay the rig's, so turning, tilting and camera-relative W A S D keep working.</summary>
    /// <summary>The stick's tilt rate this control tick (radians a second), applied each frame; and the last frame's time.</summary>
    static float s_padTiltRate;
    static long s_frameAt;

    public static void OnFrame(object host)
    {
        WasdApi.Apply();   // another mod's SetNormalView, on the host thread
        ViewLens.FreeCamera = Player is not null && Access.Session(Player).Camera.IsFree;
        if (!On || Views.Current == View.Normal) ViewLens.Release("no view");
        if (!On || Views.Current == View.Normal || Player is null) return;

        var session = Access.Session(Player);
        if (session.Camera.IsFree)
        {
            Counters.Add("view.skip_free_camera");
            return;
        }
        // a conversation (2026-11-20, Dialogue Unframed): Meitou's or that mod's speaker shot has the camera; the view comes back after
        if (ConversationPad.Open(session))
        {
            ViewLens.Release("conversation");
            Counters.Add("view.skip_conversation");
            return;
        }
        // a PZA's Director's Kit shot (kill cam, called shot) owns the camera and lens while it plays (agreed with the Kit, 2026-11-21: the
        // play test's stuck camera was the view and the shots both writing it); the view comes back the frame after, on its own lens
        if (CameraFlick.Api.ShotBusy())
        {
            ViewLens.Yield();
            Counters.Add("view.skip_kit_shot");
            return;
        }

        if (s_controlled is not { } id || session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is not { } lead)
        {
            ViewLens.Release("no character");
            Counters.Add("view.skip_no_character");
            return;
        }

        var camera = Access.HostCamera(host);
        if (Views.Current != View.Normal) ViewLens.Frame(camera, Views.Current == View.First ? Settings.FirstFov : Settings.ShoulderFov);
        var feet = Access.DrawnPosition(host, Access.Tracked(host, lead));
        Vector3? head = Views.Current == View.First ? Access.DrawnHead(Player, id) : null;
        if (Views.Current == View.First && head is null) Counters.Add("view.head_fallback");

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        float dt = s_frameAt == 0 ? 0 : Math.Clamp((float)System.Diagnostics.Stopwatch.GetElapsedTime(s_frameAt, now).TotalSeconds, 0, 0.1f);
        s_frameAt = now;
        if (s_padTiltRate != 0) Views.Tilt(s_padTiltRate * dt);   // the stick's tilt, frame by frame
        float pitch = Views.EasePitch();
        camera.Pitch = pitch;   // the views' own tilt, past the rig's limit
        bool carrying = !lead.Carrying.IsNone;
        if (carrying) Counters.Add("view.carrying");
        float groundRefY = feet.Y + (float)Settings.ShoulderHeight;
        var (target, distance) = Views.Pose(Views.Current, feet, head, camera.Yaw, pitch, Keys.ShoulderSign, carrying,
            (float)Settings.ShoulderHeight, camera.FieldOfView, (x, z) => Access.EyeGroundAt(session.Camera, x, z, groundRefY));
        camera.Target = target;
        camera.Distance = distance;
        Counters.Add("view.frames");
    }

    #endregion

    #region Control tick (WorldSession.Tick postfix)

    /// <summary>The camera follows the controlled character; a held direction becomes a move order once per world tick, release a stop.</summary>
    public static void OnTick(WorldSession session)
    {
        if (Player is null || !ReferenceEquals(Access.Session(Player), session)) return;
        if (s_worlds.IsNew(session.World)) NewWorld();
        if (TestHook.Live) TestHook.Tick(session, session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Selected && c.IsPlayer));   // headless tests only
        if (Player is { } p0)   // menus while the interface is hidden (UiPeek), WASD Control on or off; never fight Photo Mode
            UiPeek.Tick(p0, Access.ContextMenuOpen(p0) || Look.WindowOpen(p0) || ConversationPad.Open(session), Access.PhotoModeActive(session));
        if (!On)
        {
            TumbleSystem.SetSteered(null);
            WasdApi.Clear();   // holds end, nothing steered is reported
            return;
        }
        Verifier.Tick(Player);   // the dials' checks after a pick (0.10)

        if (s_takePressed) TakeControl(session);
        var lead = Controlled(session.CurrentSnapshot);
        // in a bed, a cage or a seat (play test 2026-11-20: "placed in a prison cage ... you end up outside of it"; log: the steered character
        // in a cage still got move orders and tumble asks, refused not_free): no steering, jump or fall until the game lets them out
        if (lead is not null && InSomething(session, lead.Id)) lead = Skip("skip.in_something");
        TumbleSystem.SetSteered(lead?.Id);   // only this character may ever tumble (TumbleSystem)
        // WasdApi v2: who is steered and the push, for other mods; a combat mod's hold keeps our move / stop orders out of its fight
        bool apiHeld = WasdApi.Tick(lead?.Id, s_intent, lead is null ? Vector2.Zero : WorldDirection(session));
        if (lead is null)
        {
            StopIfMoving(session, "no character");
            ForgetStuck();
            return;
        }

        Report(lead);
        if (s_followPressed) { s_followPressed = false; FollowMe(session); }
        MeasureSpeed(session, lead);
        if (s_jumpPressed || TestHook.TakeJump())
        {
            s_jumpPressed = false;
            switch (JumpRules.Press(session.World.Tick, JumpSystem.ReadyAt(session.World, lead.Id), session.World.TickSeconds))
            {
                case PressResult.Now: RequestJump(session, lead); break;
                case PressResult.Buffer: s_jumpBuffered = true; Counters.Add("jump.buffered"); break;
                default: Counters.Add("jump.press_dropped"); break;
            }
        }
        else if (s_jumpBuffered && session.World.Tick >= JumpSystem.ReadyAt(session.World, lead.Id))
        {
            s_jumpBuffered = false;
            Counters.Add("jump.buffer_fired");
            RequestJump(session, lead);
        }
        if (JumpSystem.PlayingClip(session.World, lead.Id, session.World.Tick) is { } playing && !JumpRules.Stride(playing))   // a stride jump: the walk or run goes on
        {
            Counters.Add("jump.hold_steer");   // the clip moves the character; no move order fights it
            ForgetStuck();
            return;
        }
        if (Settings.CameraFollows && Access.GetFollow(Player) != lead.Id)
        {
            Access.SetFollow(Player, lead.Id);
            Counters.Add("follow.set");
        }


        if (Views.Current == View.First && s_intent == Vector2.Zero) Neck(session, lead);
        // The push straight from the pad's last reading (play test 2026-11-17: "basically going straight into running"; the push kept from the
        // input step read 0 here, so the speed never changed). Only while the stick steers: W A S D keys keep the chosen speed.
        float push = s_padSteering && Pad.Last.Connected ? Math.Min(1, Pad.Last.Left.Length()) : 0;
        StickSpeed.Tick(session, lead, push);   // walk / jog / run by the stick's push

        if (TestHook.Hold is not null) s_intent = new Vector2(0, 1);   // the headless test's held W (TestHook)
        if (apiHeld)
        {
            // another mod holds the steering (WasdApi.HoldSteering): no move and no stop (a stop ends a fight); the walk under way is theirs
            s_moving = null;
            ForgetStuck();
            Counters.Add("api.steering_skipped");
            return;
        }
        if (s_intent == Vector2.Zero)
        {
            StopIfMoving(session, "released");
            ForgetStuck();
            return;
        }

        Steer(session, lead);
    }

    static readonly WorldSwitch s_worlds = new();

    /// <summary>The push on the ground plane, as Steer turns it (the camera's yaw, a look back not counted); length 0 to 1.</summary>
    static Vector2 WorldDirection(WorldSession session)
    {
        if (s_intent == Vector2.Zero) return Vector2.Zero;
        float yaw = session.Camera.Strategy.Yaw - s_lookBack;
        var forward = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
        var right = new Vector2(MathF.Cos(yaw), -MathF.Sin(yaw));
        var d = forward * s_intent.Y + right * s_intent.X;
        return d.Length() > 1 ? Vector2.Normalize(d) : d;
    }

    /// <summary>The first control tick of a new world (a new game, a load, back from the title): every per-world value goes, so no character id,
    /// tick or hold of the old world reaches this one (an old id can name another character here; an old tick throttled orders or waits).</summary>
    static void NewWorld()
    {
        s_intent = default; s_orderTick = long.MinValue; s_moving = null; s_controlled = null; s_stickPush = 0; s_padSteering = false;
        s_takePressed = false; s_followPressed = false; s_followLeader = null; s_lookBack = 0;
        s_reattachedSeen = 0; s_tumbleAskedUntil = long.MinValue; s_waitSince = -1; s_waitLogged = 0;
        s_jumpPressed = false; s_lipSince = -1; s_jumpBuffered = false;
        s_speed = 0; s_speedId = default; s_speedAt = default; s_speedTick = -1;
        s_lastWalkable = null; s_lastWalkableOf = default; s_aheadTries = 0;
        s_neckSent = null; s_neckTick = 0;
        FrontendNow = false;
        ManualCombatLink.Reset();
        ForgetStuck();
        UiPeek.Reset();
        TumbleSystem.SetSteered(null);
        WasdApi.Clear();
        DialTime.End();          // a dial's slow time: SlowMo's hold given back
        DpadSlow.Forget();       // the D-pad's slow motion: the same, the old world's clock untouched
        if (Views.Current != View.Normal) Views.Reset();   // the old camera's lens is not this world's
        ViewLens.Release("new world");
        Counters.Add("world.new");
        Log.Line(Strings.Get("log.new_world"));
    }

    /// <summary>The first selected player character, when it can walk; null (with a counted reason) otherwise.</summary>
    static CharacterSnapshot? Controlled(WorldSnapshot snapshot)
    {
        var lead = snapshot.Characters.FirstOrDefault(c => c.Selected && c.IsPlayer);
        if (lead is null) return Skip("skip.none_selected");
        if (lead.Body is { Dead: true }) return Skip("skip.dead");
        if (lead.Down || lead.Body is { Unconscious: true }) return Skip("skip.down");
        if (!lead.CarriedBy.IsNone) return Skip("skip.carried");
        return lead;
    }

    /// <summary>Whether the character is in a bed, a cage or another piece of furniture that holds it (CharacterCold.InSomething).</summary>
    static bool InSomething(WorldSession session, CharacterId id)
    {
        var table = session.World.Characters;
        return table.TryResolveNext(id, out int slot) && table.Cold(slot) is { } cold && cold.InSomething != default;
    }

    static CharacterSnapshot? Skip(string reason)
    {
        Counters.Add(reason);
        return null;
    }

    static long s_reattachedSeen;

    /// <summary>The world tick before which no new tumble is asked for (one request, not one a tick).</summary>
    static long s_tumbleAskedUntil = long.MinValue;
    /// <summary>When (Environment.TickCount64) the driver began waiting at a lip, -1: not waiting; and when the long wait was last logged.</summary>
    static long s_waitSince = -1, s_waitLogged;
    /// <summary>World ticks between two tumble requests (the tick system has its own cooldown; this only keeps the queue quiet).</summary>
    const long TumbleAskTicks = 15;

    /// <summary>At a lip with Ledges on Fall: ask the tick system for a tumble with a throw along the push (TumbleSystem decides).</summary>
    static void Tumble(WorldSession session, CharacterSnapshot lead, Vector2 dir, Vector3? push = null)
    {
        var world = session.World;
        // play test 2026-11-20: at lips for minutes the driver only waited (ledge.ask_waiting rose, ledge.asked did not). A wait further ahead
        // than one ask can be is from another world's clock: cleared. A long wait says why in the log.
        if (s_tumbleAskedUntil != long.MinValue && s_tumbleAskedUntil - world.Tick > TumbleAskTicks)   // MinValue: no ask yet (the subtraction would wrap)
        {
            Log.Line(Strings.Format("log.ledge_wait_reset", s_tumbleAskedUntil - world.Tick));
            Counters.Add("ledge.wait_reset");
            s_tumbleAskedUntil = long.MinValue;
        }
        if (TumbleSystem.Tumbling is not null || world.Tick < s_tumbleAskedUntil)
        {
            Counters.Add("ledge.ask_waiting");
            long now = Environment.TickCount64;
            if (s_waitSince < 0) s_waitSince = now;
            else if (now - s_waitSince > 3000 && now - s_waitLogged > 5000)
            {
                s_waitLogged = now;
                Log.Line(Strings.Format("log.ledge_wait_long", (now - s_waitSince) / 1000.0, TumbleSystem.Tumbling is not null,
                    Math.Max(0, s_tumbleAskedUntil - world.Tick)));
            }
            return;
        }
        s_waitSince = -1;

        world.Commands.Enqueue(new TumbleCommand(lead.Id, push ?? LedgeRules.Push(dir)) { Tick = world.Tick });
        s_tumbleAskedUntil = world.Tick + TumbleAskTicks;
        Counters.Add("ledge.asked");
    }

    static bool s_jumpPressed;
    /// <summary>The world tick the walker reached a lip and kept pushing at it (-1: not at one).</summary>
    static long s_lipSince = -1;
    /// <summary>A press kept for the moment the character is free again (JumpRules.Press).</summary>
    static bool s_jumpBuffered;
    /// <summary>The steered character's ground speed (world units/s) over the last MeasureTicks, for the stride jumps (JumpRules.Choose).</summary>
    static float s_speed;
    static CharacterId s_speedId;
    /// <summary>The measured ground speed (world units/s), for the test hook.</summary>
    public static float MeasuredSpeed => s_speed;
    static Vector3 s_speedAt;
    static long s_speedTick = -1;
    const long MeasureTicks = 6;

    /// <summary>Measures the lead's ground speed from its position every MeasureTicks of game time (the snapshot carries no velocity).</summary>
    static void MeasureSpeed(WorldSession session, CharacterSnapshot lead)
    {
        long t = session.World.Tick;
        if (s_speedTick < 0 || t < s_speedTick || lead.Id != s_speedId)   // a new world, a load, or another character: start again
        {
            (s_speedAt, s_speedTick, s_speed, s_speedId) = (lead.Position, t, 0, lead.Id);
            return;
        }
        long dt = t - s_speedTick;
        if (dt < MeasureTicks) return;
        float secs = dt * session.World.TickSeconds;
        float d = Flat(lead.Position - s_speedAt);
        s_speed = JumpRules.SpeedReading(d, secs);   // a teleport reads 0, not a run
        (s_speedAt, s_speedTick) = (lead.Position, t);
    }
    /// <summary>The jump is on: the Jump setting, and this Meitou can play it (JumpSystem.Registered).</summary>
    public static bool JumpOn => Settings.Jump != "Off" && JumpSystem.Registered;
    /// <summary>How far ahead and behind the jump reads the slope, world units (1 m).</summary>
    const float JumpSlopeReach = 10f;

    /// <summary>The jump key (Shift+U, the pad's A with nothing under the crosshair, the test hook): the clip by JumpRules from the ground under and ahead of the character
    /// along its facing (jump_forward relocates that way), sent to JumpSystem, which decides.</summary>
    static void RequestJump(WorldSession session, CharacterSnapshot lead)
    {
        var world = session.World;
        var surface = Access.SurfaceBelow(Player!);
        // A character's yaw faces (sin, cos) on X / Z (headless run 2026-11-19: yaw 3.14 and jump_forward went 1.8 m toward -Z), the opposite
        // of the strategy camera's convention.
        var facing = JumpRules.Facing(lead.Yaw);
        bool pushed = s_intent != Vector2.Zero || TestHook.Hold is not null || TestHook.PushOnce;
        TestHook.PushOnce = false;
        float slope = 0;
        bool clear = false;
        if (surface is not null)
        {
            var p = lead.Position;
            // the slope over 1 m each side (play test 2026-11-20: over 25 cm each side a bump or a stair's nosing read as too steep, log
            // jump.choose.slope); the probes start a whole step-up above the feet's height so a rise still has its surface found
            var ahead = p + new Vector3(facing.X, 0, facing.Y) * JumpSlopeReach;
            var behind = p - new Vector3(facing.X, 0, facing.Y) * JumpSlopeReach;
            float probe = p.Y + JumpSlopeReach * MathF.Tan(JumpRules.MaxSlopeDegrees * MathF.PI / 180f) + MaxStepUpPerSample;
            float? h1 = surface(new Vector3(ahead.X, probe, ahead.Z));
            float? h0 = surface(new Vector3(behind.X, probe, behind.Z));
            slope = h0 is { } a && h1 is { } b ? JumpRules.SlopeDegrees(a, b, 2 * JumpSlopeReach) : 0;   // no reading: the open ground of the world
            if (pushed && Settings.Ledges == "Fall" && LedgePatch.Installed)
            {
                // the leap: a drop of 1.5 m or more within 2.5 m ahead (the cliff test as Steer's, margin 0) tumbles instead of a clip
                var walk = world.Walkability;
                var lipAt = PlanTarget(p, facing, LedgeRules.LeapReach, surface, 0, out _, out bool ld, out _, out float ldepth, q => walk.GroundHeight(q.X, q.Z, q.Y));
                // the planned leap first (prototype): walkable ground across the drop within reach is jumped to, not tumbled into
                if (LedgeRules.LeapOff(ld, ldepth) && PlanLeap(world, p, facing, Flat(lipAt - p)) is { } across)
                {
                    SendLeap(session, lead, across);
                    return;
                }
                if (LedgeRules.LeapOff(ld, ldepth))
                {
                    Counters.Add("jump.leap");
                    Log.Line(Strings.Format("log.jump_leap", ldepth / 10f, s_speed / 10f));
                    Tumble(session, lead, facing, LedgeRules.LeapPush(facing));
                    return;
                }
            }
            if (pushed)
            {
                var end = PlanTarget(p, facing, JumpRules.ClearAhead, surface, out string how);
                float walked = new Vector2(end.X - p.X, end.Z - p.Z).Length();
                // the walkable height under the feet (the body can stand a little under the navmesh, as PlanTarget's reattach knows)
                float startY = surface(new Vector3(p.X, p.Y + MaxRisePerSample, p.Z)) ?? surface(new Vector3(p.X, p.Y + ReattachLift, p.Z)) ?? p.Y;
                clear = JumpRules.Clear(how, walked, startY, end.Y);
                if (!clear) Counters.Add("jump.not_clear." + how);
                // the planned leap (prototype): past what stopped the walk ahead, onto walkable ground the arc can reach
                if (!clear && PlanLeap(world, p with { Y = startY }, facing, walked) is { } land)
                {
                    SendLeap(session, lead, land);
                    return;
                }
            }
        }

        var clip = JumpRules.Choose(pushed, clear, slope, s_speed, out string why);
        if (why.Length > 0) Counters.Add("jump.choose." + why);
        Log.Line(Strings.Format("log.jump_ask", JumpRules.Name(clip), s_speed / 10f, slope, why.Length > 0 ? why : "-"));
        if (clip == JumpClip.None) return;

        if (!JumpRules.Stride(clip)) StopIfMoving(session, "jump");
        world.Commands.Enqueue(new JumpCommand(lead.Id, clip, pushed) { Tick = world.Tick });   // pushed: the quick start (JumpRules.From)
        Counters.Add("jump.asked");
    }

    /// <summary>The planned leap's landing from <paramref name="feet"/> along <paramref name="facing"/> past an obstacle
    /// <paramref name="past"/> world units ahead (LeapRules.Plan on Meitou's walkable surface), or null (counted and logged once).</summary>
    /// <remarks>The landing must be ON the navmesh (IsOnSurface), not only walkable ground: headless run 2026-11-20, two landings under a
    /// walkway, off the mesh, were rushed back along the old goal by Meitou.</remarks>
    /// <summary>Real floor under (x, z) at height y (Meitou's FloorNear gives the spot itself only then; headless run 2026-11-21: a landing the
    /// walkable test passed lay 2.2 m from any navmesh face).</summary>
    static bool OnFloor(IWalkability walk, float x, float z, float y) =>
        walk.FloorNear(x, z, y, 2f) is { } f && MathF.Abs(f.X - x) < 0.5f && MathF.Abs(f.Z - z) < 0.5f;

    static Vector3? PlanLeap(World world, Vector3 feet, Vector2 facing, float past)
    {
        var walk = world.Walkability;
        var plan = LeapRules.Plan(feet, facing, past, (x, z, y) => walk.GroundHeight(x, z, y), (x, z, y) => walk.IsWalkable(x, z, y) && walk.IsOnSurface(x, z, y) && OnFloor(walk, x, z, y), out string why);
        if (plan is { } ok) return ok.To;
        Counters.Add("leap.plan." + why);
        Log.Line(Strings.Format("log.leap_plan", why, past / 10f));
        return null;
    }

    /// <summary>Sends the leap: jump_forward with the landing (JumpSystem checks the body's way against the buildings, LeapSystem flies it).</summary>
    static void SendLeap(WorldSession session, CharacterSnapshot lead, Vector3 land)
    {
        var world = session.World;
        StopIfMoving(session, "leap");
        world.Commands.Enqueue(new JumpCommand(lead.Id, JumpClip.Forward, true) { Tick = world.Tick, Land = land });
        Counters.Add("leap.asked");
        Counters.Add("jump.asked");
    }

    /// <summary>Where and since which tick the pushed walker has not moved (UnstickRules); null: not tracking (released, jumping).</summary>
    static Vector3? s_stuckAt;
    static long s_stuckSince, s_unstickUntil = long.MinValue;
    /// <summary>Ways ahead sent since the walker last moved; the last spot the planner could walk from (null: none yet).</summary>
    static int s_aheadTries;
    static Vector3? s_lastWalkable;
    static CharacterId s_lastWalkableOf;

    /// <summary>Forget the stuck tracking (the stick released, a jump, a tumble): the next push starts a fresh count.</summary>
    static void ForgetStuck()
    {
        s_stuckAt = null;
        s_aheadTries = 0;
        s_lipSince = -1;   // a lip hold starts again with the next push
        s_unstickUntil = long.MinValue;
    }

    /// <summary>The way out of a spot the planner cannot step from (UnstickRules): one plain move order Reach ahead at the world's ground.</summary>
    static bool Unstick(WorldSession session, CharacterSnapshot lead, Vector2 dir, bool bigDrop, bool fall, bool atDrop)
    {
        var world = session.World;
        var p = lead.Position;
        if (s_stuckAt is not { } at || Flat(p - at) > UnstickRules.Progress || world.Tick < s_stuckSince)
        {
            s_stuckAt = p;
            s_stuckSince = world.Tick;
            s_aheadTries = 0;
            return false;
        }

        double seconds = (world.Tick - s_stuckSince) * (double)world.TickSeconds;
        var q = p + new Vector3(dir.X, 0, dir.Y) * UnstickRules.Reach;
        float ground = world.Walkability.GroundHeight(q.X, q.Z, p.Y);
        bool aheadOk = UnstickRules.Escape(seconds, bigDrop, p.Y, ground, UnstickRules.Limit(fall));
        float back = s_lastWalkable is { } w && s_lastWalkableOf == lead.Id ? Flat(w - p) : float.NaN;
        var way = UnstickRules.Choose(seconds >= UnstickRules.After, aheadOk, s_aheadTries, back, atDrop);
        if (way == UnstickRules.Way.None)
        {
            if (seconds >= UnstickRules.After) Counters.Add(bigDrop ? "unstick.no.ledge" : "unstick.no.ground");
            return false;
        }

        var target = way == UnstickRules.Way.Back ? s_lastWalkable!.Value : new Vector3(q.X, ground, q.Z);
        if (way == UnstickRules.Way.Ahead) s_aheadTries++;
        else
        {
            Counters.Add("unstick.back");
            Log.Line(Strings.Format("log.unstick_back", back / 10f));
        }
        world.Commands.Enqueue(new MoveOrder([lead.Id], target) { Tick = world.Tick });
        s_orderTick = world.Tick;
        s_moving = lead.Id;
        s_stuckAt = p;
        s_stuckSince = world.Tick;
        s_unstickUntil = world.Tick + (long)Math.Ceiling(UnstickRules.Hold / world.TickSeconds);
        Counters.Add("unstick.order");
        return true;
    }

    /// <summary>A move order a few metres ahead along the camera's heading (StrategyCamera.Move's axes on X/Z).</summary>
    static void Steer(WorldSession session, CharacterSnapshot lead)
    {
        var world = session.World;
        if (world.Tick == s_orderTick)
        {
            Counters.Add("order.same_tick");
            return;
        }
        if (world.Tick < s_unstickUntil)
        {
            Counters.Add("unstick.holding");   // the way out's move order runs (UnstickRules.Hold)
            return;
        }

        float yaw = session.Camera.Strategy.Yaw - s_lookBack;   // looking back (R3 held) does not turn the walk round
        var forward = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
        var right = new Vector2(MathF.Cos(yaw), -MathF.Sin(yaw));
        var dir = TestHook.Hold ?? Vector2.Normalize(forward * s_intent.Y + right * s_intent.X);
        float reach = Math.Clamp(Settings.SteerDistance, DcSettings.MinSteer, DcSettings.MaxSteer);
        // 0.10.2 (ledge-fall): with Ledges on Fall a step aims at the lip itself, and a push outward at the lip starts a tumble (LedgeRules).
        bool fall = Settings.Ledges == "Fall" && LedgePatch.Installed;
        if (Settings.Ledges == "Fall" && !LedgePatch.Installed) Counters.Add("ledge.fall_unavailable");
        var walk = world.Walkability;
        var target = PlanStep(lead.Position, dir, reach, Access.SurfaceBelow(Player!), fall ? 0 : DropMargin, out string how, out bool dropped, out var lip,
            out float depth, fall ? p => walk.GroundHeight(p.X, p.Z, p.Y) : null);
        Counters.Add("surface." + how);
        if (Reattached != s_reattachedSeen) { s_reattachedSeen = Reattached; Counters.Add("surface.reattached"); }
        if (fall && dropped && depth < LedgeRules.MinFall) Counters.Add("ledge.small_drop");   // a stair's steep bit, a curb: as Stop
        if (LedgeRules.AtLip(fall, dropped, lead.Position, lip, depth))
        {
            // a fall needs a push held at the lip (play test 2026-11-20, watchtower stairs: walking down past an open stairwell's edge
            // tumbled the walker for 0.9 m drops); the walker stands at the lip meanwhile
            if (s_lipSince < 0) s_lipSince = world.Tick;
            if (LedgeRules.Committed((world.Tick - s_lipSince) * (double)world.TickSeconds))
            {
                Tumble(session, lead, dir);
                return;
            }
            Counters.Add("ledge.lip_hold");
            StopIfMoving(session, "lip");
            return;
        }
        s_lipSince = -1;

        s_waitSince = -1;   // not at a lip: no wait to time
        // a spot the planner could step from (any real step, a wall's short one too): standing at a wall it is where the walker stands
        if (how is not ("edge" or "no_surface") && !dropped && Flat(target - lead.Position) >= SampleStep)
            (s_lastWalkable, s_lastWalkableOf) = (lead.Position, lead.Id);
        if (Unstick(session, lead, dir, dropped && depth >= LedgeRules.MinFall, fall, dropped)) return;
        if (how == "edge")
        {
            StopIfMoving(session, "edge");   // at a lip or a wall: stand (another direction still walks)
            return;
        }

        world.Commands.Enqueue(new MoveOrder([lead.Id], target) { Tick = world.Tick });
        s_orderTick = world.Tick;
        s_moving = lead.Id;
        Counters.Add("order.move");
    }

    /// <summary>Distance between surface samples along the step, world units (a quarter metre: a wall's gap in the navmesh, the wall plus the
    /// walker's radius on both sides, is wider, so no sample steps over one; play test 2026-11-17: at half a metre the last sample before a
    /// ledge could sit past its lip and the walker ran off it).</summary>
    public const float SampleStep = 2.5f;
    /// <summary>Most a walkable surface may fall between two samples (1.5 per unit: steeper than the game's stairs, short of a ledge).</summary>
    public const float MaxRisePerSample = 3.75f;
    /// <summary>
    /// Most it may RISE between two samples (0.10.1, Workshop comment 2026-11-18: "the character gets stuck on stairs ... and inclines"): a
    /// stair's riser can stand taller than 1.5 per unit over a quarter metre, and a rise never carries the walker over a lip, so going up
    /// is allowed more than going down (the drop rule above stays as it was).
    /// </summary>
    public const float MaxStepUpPerSample = 5.5f;
    /// <summary>
    /// Most the surface may fall between two samples while the walker is already going down a stair (0.10.1, headless run 2026-11-19: The Hub's
    /// tower stairs step down 0.66 m within one sample near their foot, then a 25 cm strip with no surface before the ground, and a walk down
    /// them stopped halfway). A fall over MaxRisePerSample is taken only when the sample before it fell at least StairDescent (on flat ground
    /// a 0.75 m curb still stops the walker), and never over 0.75 m (a ledge stops as before).
    /// </summary>
    public const float StairDropPerSample = 7.5f;
    /// <summary>The least fall of the sample before (10 cm over 25 cm) that counts as going down a stair.</summary>
    public const float StairDescent = 1f;
    /// <summary>The angles (degrees, each side) a blocked step tries next, in order (0.10.1: slide along walls, columns and corners).</summary>
    public static readonly float[] SlideAngles = [20f, 40f];
    /// <summary>A step blocked closer than this share of its reach tries the slide angles.</summary>
    public const float SlideIfShorterThan = 0.6f;
    /// <summary>With Ledges on Fall, how many samples past a gap a step looks for a lower surface (50 cm: Meitou's cut between a lip and the
    /// ground below is narrower; a wall's gap, the wall and the walker's radius both sides, is wider).</summary>
    public const int GapLook = 2;

    /// <summary>Whether the gap that starts at <paramref name="d"/> along the step is a drop's: within GapLook samples past it, a surface more
    /// than MaxRisePerSample below <paramref name="y"/>.</summary>
    static bool GapDrop(Func<Vector3, float?> surface, Vector3 start, Vector2 dir, float d, float y, out float depth)
    {
        depth = 0;
        for (int k = 1; k <= GapLook; k++)
        {
            var q = start + new Vector3(dir.X, 0, dir.Y) * (d + k * SampleStep);
            if (surface(new Vector3(q.X, y + MaxStepUpPerSample, q.Z)) is not { } h) continue;
            depth = y - h;
            return y - h > MaxRisePerSample;
        }
        return false;
    }

    /// <summary>How far above a character standing under the navmesh the mesh may be and still be the one it walks on (1.2 m).</summary>
    public const float ReattachLift = 12f;
    /// <summary>How many plans started from the mesh above the character (ReattachLift); counted as surface.reattached by Steer.</summary>
    public static long Reattached;

    /// <summary>With Ledges on Fall, how many samples past the end of the walkable surface the ground is asked for a cliff (1 m).</summary>
    public const int CliffLook = 4;

    /// <summary>
    /// Whether the end of the walkable surface at <paramref name="d"/> is a cliff's top: within CliffLook samples from there, the world's ground
    /// (the walkable height, else the terrain) at least LedgeRules.MinFall below <paramref name="y"/>, i.e. steeper than about 56 degrees on
    /// average. Headless run 2026-11-19 at World's End: the plateau's navmesh ends at the slope's top (5046) and the next surface is metres away
    /// and far below, so no gap rule ever saw it. A wall at ground level has ground at the same height past it; a building's wall over lower
    /// ground passes here and is stopped by the tumble's clear-way rays.
    /// </summary>
    static bool CliffDrop(Func<Vector3, float> ground, Vector3 start, Vector2 dir, float d, float y, out float depth)
    {
        depth = 0;
        for (int k = 0; k <= CliffLook; k++)
        {
            var q = start + new Vector3(dir.X, 0, dir.Y) * (d + k * SampleStep);
            float g = ground(new Vector3(q.X, y, q.Z));
            if (!float.IsFinite(g) || y - g < LedgeRules.MinFall) continue;
            depth = y - g;
            return true;
        }
        return false;
    }

    /// <summary>How far short of a drop's lip a step aims (75 cm: a run stops in under a metre at Meitou's braking).</summary>
    public const float DropMargin = 7.5f;

    /// <summary>
    /// Where a step aims: the walkable surface along the way, not a point at the character's height. A move order's floor is the one within
    /// MovementSystem.FloorSlack (20) of the target's height (GoToFloor, FloorNear), so 4 m ahead on a stair a target at the character's height
    /// fell to the ground beside it and the character walked along the stair's side (play test 2026-11-16). Samples every half metre follow the
    /// surface (the host's SurfaceBelow: the navmesh's highest surface not above a probe) and stop at a jump (a ledge) or a gap (a wall), so the
    /// target never lies past a step the character could not walk. <paramref name="how"/>: climb / flat / stopped / edge (no step at all) / no_surface.
    /// </summary>
    public static Vector3 PlanTarget(Vector3 start, Vector2 dir, float reach, Func<Vector3, float?>? surface, out string how) =>
        PlanTarget(start, dir, reach, surface, out how, out _);

    /// <summary>
    /// The step the stick or W A S D asks for (0.10.1): the straight step (PlanTarget), unless it is blocked close by something that is not a
    /// drop (a wall, a column, a furniture corner: a gap in the walkable surface, or a rise too steep). Then the same reach angled 20 and 40
    /// degrees to each side, nearer angles first, and the one that gets furthest wins ("slide"), so the walker glides along a wall or round a
    /// corner instead of standing at it. A step stopped by a drop never slides: at a lip the walker stops as before.
    /// </summary>
    public static Vector3 PlanStep(Vector3 start, Vector2 dir, float reach, Func<Vector3, float?>? surface, out string how) =>
        PlanStep(start, dir, reach, surface, DropMargin, out how, out _, out _);

    /// <summary>
    /// PlanStep with the drop margin given (0 with Ledges on Fall: aim at the lip), also saying whether the straight step stopped at a drop and
    /// the lip's last walkable sample (null when the first sample is already over the drop).
    /// </summary>
    public static Vector3 PlanStep(Vector3 start, Vector2 dir, float reach, Func<Vector3, float?>? surface, float margin, out string how,
        out bool dropped, out Vector3? lip) => PlanStep(start, dir, reach, surface, margin, out how, out dropped, out lip, out _);

    /// <summary>PlanStep, also with the depth of the drop (world units from the last walkable height to the first surface past it; 0 without a
    /// drop): Ledges on Fall tumbles only past LedgeRules.MinFall.</summary>
    public static Vector3 PlanStep(Vector3 start, Vector2 dir, float reach, Func<Vector3, float?>? surface, float margin, out string how,
        out bool dropped, out Vector3? lip, out float depth, Func<Vector3, float>? ground = null)
    {
        var straight = PlanTarget(start, dir, reach, surface, margin, out how, out bool drop, out lip, out depth, ground);
        dropped = drop;
        if (surface is null || drop || how is "flat" or "climb" or "no_surface") return straight;
        float got = how == "edge" ? 0 : Flat(straight - start);
        if (got >= reach * SlideIfShorterThan) return straight;

        Vector3 best = straight;
        float bestGot = got;
        foreach (float deg in SlideAngles)
        {
            foreach (int side in new[] { 1, -1 })
            {
                float a = side * deg * MathF.PI / 180f, c = MathF.Cos(a), sn = MathF.Sin(a);
                var turned = new Vector2(dir.X * c - dir.Y * sn, dir.X * sn + dir.Y * c);
                var p = PlanTarget(start, turned, reach, surface, margin, out string h, out bool d, out _);
                if (h is "edge" or "no_surface") continue;
                float g = Flat(p - start);
                if (g > bestGot + SampleStep) { best = p; bestGot = g; }
            }
            if (bestGot >= reach * SlideIfShorterThan) break;   // nearer angles first: take the smallest turn that frees the step
        }
        if (bestGot > got) how = "slide";
        return best;
    }

    static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>PlanTarget, also saying whether the step stopped at a drop (a ledge) rather than at a wall or a too-steep rise.</summary>
    public static Vector3 PlanTarget(Vector3 start, Vector2 dir, float reach, Func<Vector3, float?>? surface, out string how, out bool dropped) =>
        PlanTarget(start, dir, reach, surface, DropMargin, out how, out dropped, out _);

    /// <summary>PlanTarget with the drop margin given (0: aim at the lip's last walkable sample), and that sample (<paramref name="lip"/>, null
    /// unless the step stopped at a drop after at least one walkable sample).</summary>
    public static Vector3 PlanTarget(Vector3 start, Vector2 dir, float reach, Func<Vector3, float?>? surface, float margin, out string how,
        out bool dropped, out Vector3? lip) => PlanTarget(start, dir, reach, surface, margin, out how, out dropped, out lip, out _);

    /// <summary>PlanTarget, also with the drop's depth (see PlanStep). <paramref name="ground"/> (with Ledges on Fall): the world's ground at a
    /// point near its height (IWalkability.GroundHeight: the walkable height, else the terrain), for cliffs the navmesh does not cross.</summary>
    public static Vector3 PlanTarget(Vector3 start, Vector2 dir, float reach, Func<Vector3, float?>? surface, float margin, out string how,
        out bool dropped, out Vector3? lip, out float depth, Func<Vector3, float>? ground = null)
    {
        dropped = false;
        lip = null;
        depth = 0;
        if (!float.IsFinite(margin) || margin < 0) margin = DropMargin;
        var flat = start + new Vector3(dir.X, 0, dir.Y) * reach;
        if (surface is null)
        {
            how = "no_surface";
            return flat;
        }

        float y = start.Y;
        // Back on the mesh (0.10.2): a character that got up from a ragdoll stands on the terrain or floor the body lay on, which can be under
        // the navmesh's polygon there (headless run 2026-11-18: up at 1560.4 under the mesh's 1569 after a fall); the samples, probing only
        // MaxStepUpPerSample above, then found nothing and the character could not be steered. The mesh just above (at most ReattachLift) is
        // the height the walk starts from; Meitou's own move orders snap the same way.
        if (surface(new Vector3(start.X, start.Y + MaxRisePerSample, start.Z)) is null
            && surface(new Vector3(start.X, start.Y + ReattachLift, start.Z)) is { } above && above > start.Y)
        {
            y = above;
            start.Y = above;
            Reattached++;
        }
        Vector3? last = null;
        var walked = new List<Vector3>();
        bool drop = false;
        how = "flat";
        // how far the surface fell into the last sample; for the first, the sample behind the walker (already on the way down a stair)
        var behind = start - new Vector3(dir.X, 0, dir.Y) * SampleStep;
        float fell = surface(new Vector3(behind.X, start.Y + MaxStepUpPerSample, behind.Z)) is { } hb ? hb - start.Y : 0;
        for (float d = SampleStep; d <= reach + 0.01f; d += SampleStep)
        {
            var q = start + new Vector3(dir.X, 0, dir.Y) * d;
            if (surface(new Vector3(q.X, y + MaxStepUpPerSample, q.Z)) is not { } h)
            {
                // Going down a stair, one sample with no surface and lower ground right after it (the strip between a stair's mesh and the
                // ground's): stepped over. A wall's gap (the wall plus the walker's radius on both sides) is wider than one sample.
                var q2 = start + new Vector3(dir.X, 0, dir.Y) * (d + SampleStep);
                if (fell >= StairDescent && d + SampleStep <= reach + 0.01f
                    && surface(new Vector3(q2.X, y + MaxStepUpPerSample, q2.Z)) is { } h2 && h2 < y && y - h2 <= MaxRisePerSample + StairDropPerSample)
                {
                    Counters.Add("step.stair_gap");
                    fell = y - h2;
                    y = h2;
                    last = new Vector3(q2.X, h2, q2.Z);
                    walked.Add(last.Value);
                    d += SampleStep;
                    continue;
                }
                how = last is null ? "no_surface" : "stopped";
                // With Ledges on Fall: a gap of up to GapLook samples with a much lower surface past it is a drop, not a wall. Meitou's
                // navmesh cuts the lower level back from a wall face, so a walkway's lip and the ground below are a strip apart (headless run
                // 2026-11-18, The Hub's east wall walkway at 1635: no surface for 25 cm past the lip, the ground at 1536 beyond).
                if (margin < DropMargin && GapDrop(surface, start, dir, d, y, out float gd)) { how = "stopped"; drop = true; depth = gd; }
                else if (margin < DropMargin && ground is not null && CliffDrop(ground, start, dir, d, y, out float cd))
                {
                    how = "stopped";
                    drop = true;
                    depth = cd;
                }
                break;
            }

            bool stairDown = y - h > MaxRisePerSample && y - h <= StairDropPerSample && fell >= StairDescent;
            if (h - y > MaxStepUpPerSample || (y - h > MaxRisePerSample && !stairDown))
            {
                how = "stopped";
                drop = h < y;
                if (drop) depth = y - h;
                break;
            }
            if (stairDown) Counters.Add("step.stair_drop");

            fell = y - h;
            y = h;
            last = new Vector3(q.X, h, q.Z);
            walked.Add(last.Value);
        }

        // Before a drop, aim DropMargin short of the last walkable sample: Meitou brakes into a target, and a run carried the walker over a lip
        // it was aimed right at. A wall needs no margin (the navmesh already keeps the walker's radius from it).
        dropped = drop;
        if (drop)
        {
            if (walked.Count > 0) lip = walked[^1];
            int keep = walked.Count - (int)MathF.Ceiling(margin / SampleStep);
            last = keep > 0 ? walked[keep - 1] : null;
        }

        if (last is not { } aim)
        {
            // Nothing walkable even one sample ahead. With no navmesh under the character either, the old flat point (no surface data here);
            // otherwise the character stands at a lip or a wall: no step. Play test 2026-11-17 (clip 10.58.07): at the edge of the Hub's wall
            // this returned the flat point 4 m out at the wall's height and the character ran out over the drop.
            if (surface(new Vector3(start.X, start.Y + MaxRisePerSample, start.Z)) is null)
            {
                how = "no_surface";
                return flat;
            }

            how = "edge";
            return start;
        }
        if (how == "flat" && MathF.Abs(aim.Y - start.Y) > 5) how = "climb";
        return aim;
    }

    /// <summary>One stop for the character the last order went to (crisp stop on release).</summary>
    static void StopIfMoving(WorldSession? session, string why)
    {
        if (s_moving is not { } id) return;

        s_moving = null;
        if (session is null || !Settings.StopOnRelease)
        {
            Counters.Add("stop.skipped");
            return;
        }

        session.World.Commands.Enqueue(new StopCommand([id]) { Tick = session.World.Tick });
        Counters.Add("stop." + why.Replace(' ', '_'));
    }

    /// <summary>G: your character under the pointer becomes the only selection (the game's own click), so control and the camera move to it.</summary>
    static void TakeControl(WorldSession session)
    {
        if (Access.PickOwnUnderMouse(Player!) is not { } id)
        {
            Counters.Add("take.nobody_under_pointer");
            return;
        }

        if (id == s_controlled)
        {
            Counters.Add("take.already");
            return;
        }

        StopIfMoving(session, "handed over");
        Access.SelectOnly(Player!, id);
        Counters.Add("take.done");
        Log.Line(Strings.Format("log.take", id.ToString()));
    }

    /// <summary>The last body turn asked for (first-person neck limit) and when: one request per change, not one per tick.</summary>
    static float? s_neckSent;
    static long s_neckTick;

    /// <summary>First person, standing: past the neck limit the body turns on the spot to follow the view (NeckSystem). The rig's yaw looks
    /// along (-sin, -cos); a character faces (sin, cos) of its yaw, so the view in character-yaw terms is the rig's yaw + pi.</summary>
    static void Neck(WorldSession session, CharacterSnapshot lead)
    {
        if (s_lookBack != 0) return;   // a look back (R3 held) is the neck's, not the body's
        float view = session.Camera.Strategy.Yaw + MathF.PI;
        if (NeckSystem.Follow(lead.Yaw, view) is not { } want)
        {
            s_neckSent = null;
            return;
        }

        long tick = session.World.Tick;
        if (s_neckSent is { } sent && MathF.Abs(NeckSystem.Delta(sent, want)) < 0.15f && tick - s_neckTick < 15) return;

        session.World.Commands.Enqueue(new TurnCommand(lead.Id, want) { Tick = tick });
        (s_neckSent, s_neckTick) = (want, tick);
        Counters.Add("neck.turn");
    }

    /// <summary>A line when the controlled character changes.</summary>
    static void Report(CharacterSnapshot lead)
    {
        if (s_controlled == lead.Id) return;

        s_controlled = lead.Id;
        Log.Line(Strings.Format("log.controlled", lead.Id.ToString()));
    }

    #endregion
}
