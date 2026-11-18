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

    #region Input (InputBindings.Resolve postfix)

    /// <summary>The toggle key, then the four move actions taken out of the camera's hands while on.</summary>
    public static void OnResolve(TickInput input, ActionState state)
    {
        s_intent = Vector2.Zero;
        s_padTiltRate = 0;   // set again by the pad's tick when it tilts a view
        s_stickPush = 0;     // set again when the left stick steers
        if (input.IsDown(Key.W) || input.IsDown(Key.A) || input.IsDown(Key.S) || input.IsDown(Key.D)) s_padSteering = false;   // the keys steer
        s_takePressed = false;
        bool editing = Player is not null && Access.UiEditing(Player);
        if (editing)
        {
            Counters.Add("skip.typing");
            return;
        }

        bool shift = input.IsDown(Key.LeftShift) || input.IsDown(Key.RightShift);
        bool ctrl = input.IsDown(Key.LeftControl) || input.IsDown(Key.RightControl);
        bool alt = input.IsDown(Key.LeftAlt) || input.IsDown(Key.RightAlt);
        bool plain = !ctrl && !alt && !shift;
        if (Player is not null && Keys.Capture(input, Access.Session(Player).Bindings)) return;   // the window's Keys tab waits for a key
        if (plain && input.WasPressed(Keys.Of(KeyAction.Toggle))) Toggle(Keys.Of(KeyAction.Toggle).ToString());
        var pad = Player is null ? PadState.None : Pad.Read();
        if (pad.Connected && Pad.WasPressed(PadButton.View)) Toggle("pad");
        if (PadHighlight && (!pad.Connected || !On)) SetHighlight(Player is null ? null : Access.Session(Player), false);
        if (!On) return;

        s_takePressed = plain && input.WasPressed(Keys.Of(KeyAction.Take));
        bool shoulder = plain && input.WasPressed(Keys.Of(KeyAction.Shoulder)), first = plain && input.WasPressed(Keys.Of(KeyAction.First));
        if (shoulder || first) Views.Set(Views.Next(Views.Current, new ViewInput(shoulder, first)), ShowNotice);

        if (Player is not null && Access.Session(Player).Camera.IsFree)
        {
            Counters.Add("skip.free_camera");
            return;
        }

        float fwd = (state.Held(InputAction.MoveForward) ? 1 : 0) - (state.Held(InputAction.MoveBack) ? 1 : 0);
        float right = (state.Held(InputAction.MoveRight) ? 1 : 0) - (state.Held(InputAction.MoveLeft) ? 1 : 0);
        s_intent = new Vector2(right, fwd);

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
        bool menu = window || Access.ContextMenuOpen(Player!) || SquadWheel.IsOpen || ScreenWheel.IsOpen;   // the right stick picks rows in a menu / faces on the wheel, moves the pointer in a window, not the view
        if (!menu && pad.Right.X != 0) rig.Rotate(-pad.Right.X * PadYawRate * look * dt);
        if (!menu && pad.Right.Y != 0)
        {
            if (Views.Current == View.Normal) rig.PitchStep(-pad.Right.Y * PadPitchRate * dt);
            else s_padTiltRate = -pad.Right.Y * PadPitchRate * look * Keys.InvertSign;   // applied every frame (OnFrame), not in 30 Hz steps
        }
        if (pad.Right != Vector2.Zero) Counters.Add("pad.look");

        PadR3(session);   // before the menu's early return, so a release is never lost and a look back never sticks
        switch (SquadWheel.Pad(Player!, pad, s_controlled))
        {
            case WheelResult.Tap: PressGame(session, InputAction.NextCharacter); break;   // RB tapped: the next member, as before
            case WheelResult.Everyone: PressGame(session, InputAction.SelectAll); return;   // the dial's top slice: the whole squad, as ~
            case WheelResult.SquadPace: ToggleSquadPace(session); return;   // the dial's bottom slice
            case WheelResult.FollowMe: FollowMe(session); return;           // the dial's left slice
            case WheelResult.Open: return;   // RB held: the squad wheel has the pad (stick, D-pad / LB pages, B cancels)
        }
        switch (ScreenWheel.Pad(Player!, pad, a => PressGame(session, a)))
        {
            case WheelResult.Tap: PressGame(session, InputAction.PreviousCharacter); break;   // LB tapped: the previous member, as before
            case WheelResult.Open: return;   // LB held: the screens dial has the pad
        }
        if (Menus.Pad(Player!, pad)) return;   // a menu is open: the pad picks in it (D-pad, interact, select)

        PadViews();
        PadHotkeys(session);
        PadTriggers(pad, session);
        PadClicks();
        PadControlTick(rig, state);
    }

    /// <summary>Seconds since the last pad tick, 0..0.1 (a long gap, as after a pause screen, must not spin the camera).</summary>
    static float PadSeconds()
    {
        long now = Stopwatch.GetTimestamp();
        float dt = s_lastPad == 0 ? 0 : (float)Math.Min(Stopwatch.GetElapsedTime(s_lastPad, now).TotalSeconds, 0.1);
        s_lastPad = now;
        return dt;
    }

    /// <summary>Top face button: over-the-shoulder (as Z); left face button: first person (as N).</summary>
    static void PadViews()
    {
        bool north = Pad.WasPressed(PadButton.North), west = Pad.WasPressed(PadButton.West);
        if (north || west) Views.Set(Views.Next(Views.Current, new ViewInput(north, west)), ShowNotice);
    }

    /// <summary>The pad's mouse buttons at the pointer (in a view, the crosshair): the bottom face button is the right button (interact:
    /// tap for the action, hold for the menu, as the mouse), the right face button the left one (select, or inspect your own in a view).
    /// Press and release are passed on as they happen, so holding works as with the mouse.</summary>
    static void PadClicks()
    {
        // B (the right face button) closes a game window, as Esc does (play test 2026-11-17: "once I'm done with the trade pressing like, B or
        // something intuitive doesn't close it out"): trade and loot close with the inventories, else the top window. Its release is the close's.
        if (Pad.WasPressed(PadButton.East) && Look.WindowOpen(Player!))
        {
            Access.Escape(Player!);
            s_eastClosed = true;
            Counters.Add("pad.close_window");
        }
        // A is the left button while a game window is open (pick up, drop, buy, sell); otherwise free (0.9.21).
        if (Look.WindowOpen(Player!) || s_aDown)
        {
            if (Pad.WasPressed(PadButton.South)) { Click(MouseButton.Left, true); s_aDown = true; }
            if (Pad.WasReleased(PadButton.South) && s_aDown) { Click(MouseButton.Left, false); s_aDown = false; }
        }
        foreach (var (button, mouse) in PadMouse)
        {
            bool closing = button == PadButton.East && s_eastClosed;
            if (Pad.WasPressed(button) && !closing) Click(mouse, true);
            if (Pad.WasReleased(button))
            {
                if (!closing) Click(mouse, false);
                if (button == PadButton.East) s_eastClosed = false;
            }
        }
    }

    /// <summary>A went down as the left button in a window: its release is the button's too, even if the window closed meanwhile.</summary>
    static bool s_aDown;

    /// <summary>The B press closed a window: neither its press nor its release is a click.</summary>
    static bool s_eastClosed;

    static readonly (PadButton Button, MouseButton Mouse)[] PadMouse =
        [(PadButton.RightTrigger, MouseButton.Right), (PadButton.East, MouseButton.Left)];   // ZR interact (0.9.19; was A), B select

    static void Click(MouseButton mouse, bool down)
    {
        Access.Click(Player!, mouse, down, alt: PadHighlight);
        Counters.Add("pad.click." + mouse.ToString().ToLowerInvariant() + (down ? ".down" : ".up"));
    }

    static readonly (PadButton Button, InputAction Action)[] PadGameKeys =
    [
        (PadButton.LeftStick, InputAction.ToggleSneak),   // L3: the user's choice (2026-11-17)
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
            PressGame(session, InputAction.StopMovement);
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

        var others = SquadWheel.Members.Where(i => i != leader).ToArray();
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

    /// <summary>R3 tap: the camera turns to look where the steered character faces (GTA style). Meitou's character yaw faces (sin, cos) on the
    /// ground (AiSystem, CarrySystem: Yaw = Atan2(dx, dz)); the camera looks along (-sin, -cos) of its own yaw, so behind = yaw + pi.</summary>
    static void CameraBehind(WorldSession session)
    {
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
    static bool s_menuHeld;

    static void PadControlTick(global::Meitou.Game.Host.Cameras.StrategyCamera rig, ActionState state)
    {
        if (Pad.WasPressed(PadButton.DPadUp)) rig.Zoom(1);
        if (Pad.WasPressed(PadButton.DPadDown)) rig.Zoom(-1);
        if (Pad.WasPressed(PadButton.DPadLeft)) Press(state, InputAction.TimeSlower);
        if (Pad.WasPressed(PadButton.DPadRight)) Press(state, InputAction.TimeFaster);
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
            if (!s_menuHeld) Press(state, InputAction.Pause);
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
        [InputAction.MoveForward, InputAction.MoveBack, InputAction.MoveLeft, InputAction.MoveRight];

    /// <summary>Flips WASD Control (the key, the F10 row).</summary>
    public static string Toggle(string from)
    {
        On = !On;
        Counters.Add(On ? "toggle.on" : "toggle.off");
        if (!On) StopIfMoving(Player is null ? null : Access.Session(Player), "toggle off");
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
        if (!On || Views.Current == View.Normal || Player is null) return;

        var session = Access.Session(Player);
        if (session.Camera.IsFree)
        {
            Counters.Add("view.skip_free_camera");
            return;
        }

        if (s_controlled is not { } id || session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is not { } lead)
        {
            Counters.Add("view.skip_no_character");
            return;
        }

        var camera = Access.HostCamera(host);
        var feet = Access.DrawnPosition(host, Access.Tracked(host, lead));
        Vector3? head = Views.Current == View.First ? Access.DrawnHead(Player, id) : null;
        if (Views.Current == View.First && head is null) Counters.Add("view.head_fallback");

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        float dt = s_frameAt == 0 ? 0 : Math.Clamp((float)System.Diagnostics.Stopwatch.GetElapsedTime(s_frameAt, now).TotalSeconds, 0, 0.1f);
        s_frameAt = now;
        if (s_padTiltRate != 0) Views.Tilt(s_padTiltRate * dt);   // the stick's tilt, frame by frame
        float pitch = Views.EasePitch();
        camera.Pitch = pitch;   // the views' own tilt, past the rig's limit
        var (target, distance) = Views.Pose(Views.Current, feet, head, camera.Yaw, pitch, Keys.ShoulderSign);
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
        if (!On) return;

        if (s_takePressed) TakeControl(session);
        var lead = Controlled(session.CurrentSnapshot);
        if (lead is null)
        {
            StopIfMoving(session, "no character");
            return;
        }

        Report(lead);
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

        if (s_intent == Vector2.Zero)
        {
            StopIfMoving(session, "released");
            return;
        }

        Steer(session, lead);
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

    static CharacterSnapshot? Skip(string reason)
    {
        Counters.Add(reason);
        return null;
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

        float yaw = session.Camera.Strategy.Yaw - s_lookBack;   // looking back (R3 held) does not turn the walk round
        var forward = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
        var right = new Vector2(MathF.Cos(yaw), -MathF.Sin(yaw));
        var dir = Vector2.Normalize(forward * s_intent.Y + right * s_intent.X);
        float reach = Math.Clamp(Settings.SteerDistance, DcSettings.MinSteer, DcSettings.MaxSteer);
        var target = PlanTarget(lead.Position, dir, reach, Access.SurfaceBelow(Player!), out string how);
        Counters.Add("surface." + how);
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
    /// <summary>Most a walkable surface may rise or fall between two samples (1.5 per unit: steeper than the game's stairs, short of a wall).</summary>
    public const float MaxRisePerSample = 3.75f;
    /// <summary>How far short of a drop's lip a step aims (75 cm: a run stops in under a metre at Meitou's braking).</summary>
    public const float DropMargin = 7.5f;

    /// <summary>
    /// Where a step aims: the walkable surface along the way, not a point at the character's height. A move order's floor is the one within
    /// MovementSystem.FloorSlack (20) of the target's height (GoToFloor, FloorNear), so 4 m ahead on a stair a target at the character's height
    /// fell to the ground beside it and the character walked along the stair's side (play test 2026-11-16). Samples every half metre follow the
    /// surface (the host's SurfaceBelow: the navmesh's highest surface not above a probe) and stop at a jump (a ledge) or a gap (a wall), so the
    /// target never lies past a step the character could not walk. <paramref name="how"/>: climb / flat / stopped / edge (no step at all) / no_surface.
    /// </summary>
    public static Vector3 PlanTarget(Vector3 start, Vector2 dir, float reach, Func<Vector3, float?>? surface, out string how)
    {
        var flat = start + new Vector3(dir.X, 0, dir.Y) * reach;
        if (surface is null)
        {
            how = "no_surface";
            return flat;
        }

        float y = start.Y;
        Vector3? last = null;
        var walked = new List<Vector3>();
        bool drop = false;
        how = "flat";
        for (float d = SampleStep; d <= reach + 0.01f; d += SampleStep)
        {
            var q = start + new Vector3(dir.X, 0, dir.Y) * d;
            if (surface(new Vector3(q.X, y + MaxRisePerSample, q.Z)) is not { } h)
            {
                how = last is null ? "no_surface" : "stopped";
                break;
            }

            if (MathF.Abs(h - y) > MaxRisePerSample)
            {
                how = "stopped";
                drop = h < y;
                break;
            }

            y = h;
            last = new Vector3(q.X, h, q.Z);
            walked.Add(last.Value);
        }

        // Before a drop, aim DropMargin short of the last walkable sample: Meitou brakes into a target, and a run carried the walker over a lip
        // it was aimed right at. A wall needs no margin (the navmesh already keeps the walker's radius from it).
        if (drop)
        {
            int keep = walked.Count - (int)MathF.Ceiling(DropMargin / SampleStep);
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
