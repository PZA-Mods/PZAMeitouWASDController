// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// Optional reflection bridge to the third-party ManualCombat mod (Workshop 3843247570, id "manualcombat"):
// 1. Persisted ON / OFF toggle (DcSettings.ManualCombat, synced with ManualCombat.InputWatch.Enabled so it stays off across restarts
//    for vanilla combat or CATS, and reflects ManualCombat's own O key back into our setting).
// 2. Full gamepad support while ManualCombat is live in a fight (RT attack/shoot, LT block/steady, B dodge, R3 switch target,
//    LT+B leave fight, LT+X swap crossbow/melee, Left Stick footwork, Right Stick / Left Stick directional aim), plus out-of-fight
//    crossbow swap (LT+X) and reload (hold LT+A). Never modifies ManualCombat.dll.
using System.Numerics;
using System.Reflection;
using Meitou.Game.Host;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public static class ManualCombatLink
{
    public const int DirNone = 0, DirOverhead = 1, DirLeft = 2, DirRight = 3, DirThrust = 4;

    static bool s_resolved, s_available, s_logged, s_syncedOnce;
    static long s_lastProbe;
    static bool s_lastSetting = true, s_lastMcEnabled = true;

    // Reflected ManualCombat.InputWatch members
    static PropertyInfo? s_enabled, s_active, s_live, s_aimNow, s_gamePaused;
    static MethodInfo? s_crosshairRay;
    static FieldInfo? s_controlled, s_mouseDir, s_keyDir, s_blockSent, s_blockByMouse, s_blockSentDir, s_reloadSent, s_reloadByMouse;

    // Reflected ManualCombat.Hud & Settings members
    static PropertyInfo? s_inFight, s_hasCrossbow, s_empty, s_ranged;
    static PropertyInfo? s_settingsProp, s_aimWithProp;

    // Reflected ManualCombat SimCommand constructors and optional init/set properties
    static ConstructorInfo? s_ctorControl, s_ctorMove, s_ctorAttack, s_ctorAttackAim, s_ctorAttackRelease;
    static ConstructorInfo? s_ctorBlock, s_ctorDodge, s_ctorTarget, s_ctorLeave, s_ctorSwap, s_ctorShot, s_ctorReload;
    static PropertyInfo? s_tickProp, s_attackHeld, s_attackInPause, s_releaseInPause, s_blockInPause, s_dodgeInPause;

    // Pad combat state (released cleanly on disconnect / window / dial / fight end / new world)
    static bool s_padAttackHeld, s_padBlockHeld, s_padReloadHeld, s_padMoveActive;
    static int s_padAim = DirNone, s_padAttackDir = DirNone, s_padBlockDir = DirNone;

    /// <summary>Optional test override for offline checks (null in game).</summary>
    internal static Func<bool>? AvailableOverride;

    /// <summary>Whether the ManualCombat mod is loaded and its public API resolved.</summary>
    public static bool Available
    {
        get
        {
            if (AvailableOverride is not null) return AvailableOverride();
            EnsureResolved();
            return s_available;
        }
    }

    public static bool Enabled
    {
        get => Available && s_enabled?.GetValue(null) is true;
        set
        {
            if (!Available || s_enabled is null) return;
            try { s_enabled.SetValue(null, value); }
            catch (Exception e) { Log.Exception("ManualCombat Enabled", e); }
        }
    }

    public static bool Active => Available && s_active?.GetValue(null) is true;
    public static bool Live => Available && s_live?.GetValue(null) is true;
    public static bool InFight => Available && s_inFight?.GetValue(null) is true;
    public static bool HasCrossbow => Available && s_hasCrossbow?.GetValue(null) is true;
    public static bool Empty => Available && s_empty?.GetValue(null) is true;
    public static bool Ranged => Available && s_ranged?.GetValue(null) is true;
    static bool GamePaused => s_gamePaused?.GetValue(null) is true;

    static string AimWith
    {
        get
        {
            try
            {
                var st = s_settingsProp?.GetValue(null);
                return s_aimWithProp?.GetValue(st) as string ?? "Mouse flick";
            }
            catch { return "Mouse flick"; }
        }
    }

    static CharacterId Controlled =>
        s_controlled?.GetValue(null) is CharacterId id ? id : default;

    static void EnsureResolved()
    {
        if (s_resolved) return;
        long now = Environment.TickCount64;
        if (s_lastProbe != 0 && now - s_lastProbe < 5000) return;
        s_lastProbe = now;

        Assembly? asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "ManualCombat", StringComparison.OrdinalIgnoreCase));
        if (asm is null) return;

        try
        {
            var iw = asm.GetType("ManualCombat.InputWatch");
            var hud = asm.GetType("ManualCombat.Hud");
            var mod = asm.GetType("ManualCombat.ManualCombatMod");
            if (iw is null || hud is null) return;

            const BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
            const BindingFlags priv = BindingFlags.NonPublic | BindingFlags.Static;

            s_enabled = iw.GetProperty("Enabled", pub);
            s_active = iw.GetProperty("Active", pub);
            s_live = iw.GetProperty("Live", pub);
            s_aimNow = iw.GetProperty("AimNow", pub);
            s_gamePaused = iw.GetProperty("GamePaused", pub);
            s_crosshairRay = iw.GetMethod("CrosshairRay", pub);

            s_controlled = iw.GetField("s_controlled", priv);
            s_mouseDir = iw.GetField("s_mouseDir", priv);
            s_keyDir = iw.GetField("s_keyDir", priv);
            s_blockSent = iw.GetField("s_blockSent", priv);
            s_blockByMouse = iw.GetField("s_blockByMouse", priv);
            s_blockSentDir = iw.GetField("s_blockSentDir", priv);
            s_reloadSent = iw.GetField("s_reloadSent", priv);
            s_reloadByMouse = iw.GetField("s_reloadByMouse", priv);

            s_inFight = hud.GetProperty("InFight", pub);
            s_hasCrossbow = hud.GetProperty("HasCrossbow", pub);
            s_empty = hud.GetProperty("Empty", pub);
            s_ranged = hud.GetProperty("Ranged", pub);

            s_settingsProp = mod?.GetProperty("Settings", pub);
            s_aimWithProp = s_settingsProp?.PropertyType.GetProperty("AimWith", BindingFlags.Public | BindingFlags.Instance);

            s_tickProp = typeof(SimCommand).GetProperty(nameof(SimCommand.Tick), BindingFlags.Public | BindingFlags.Instance);

            s_ctorControl = asm.GetType("ManualCombat.ManualControl")?.GetConstructor([typeof(CharacterId), typeof(bool)]);
            s_ctorMove = asm.GetType("ManualCombat.ManualMove")?.GetConstructor([typeof(CharacterId), typeof(Vector2)]);
            var tAttack = asm.GetType("ManualCombat.ManualAttack");
            s_ctorAttack = tAttack?.GetConstructor([typeof(CharacterId), typeof(int), typeof(Vector2)]);
            s_attackHeld = tAttack?.GetProperty("Held");
            s_attackInPause = tAttack?.GetProperty("InPause");
            s_ctorAttackAim = asm.GetType("ManualCombat.ManualAttackAim")?.GetConstructor([typeof(CharacterId), typeof(int)]);
            var tRel = asm.GetType("ManualCombat.ManualAttackRelease");
            s_ctorAttackRelease = tRel?.GetConstructor([typeof(CharacterId)]);
            s_releaseInPause = tRel?.GetProperty("InPause");
            var tBlock = asm.GetType("ManualCombat.ManualBlock");
            s_ctorBlock = tBlock?.GetConstructor([typeof(CharacterId), typeof(bool), typeof(int)]);
            s_blockInPause = tBlock?.GetProperty("InPause");
            var tDodge = asm.GetType("ManualCombat.ManualDodge");
            s_ctorDodge = tDodge?.GetConstructor([typeof(CharacterId)]);
            s_dodgeInPause = tDodge?.GetProperty("InPause");
            s_ctorTarget = asm.GetType("ManualCombat.ManualTarget")?.GetConstructor([typeof(CharacterId), typeof(Vector2)]);
            s_ctorLeave = asm.GetType("ManualCombat.ManualLeave")?.GetConstructor([typeof(CharacterId)]);
            s_ctorSwap = asm.GetType("ManualCombat.ManualSwap")?.GetConstructor([typeof(CharacterId)]);
            s_ctorShot = asm.GetType("ManualCombat.ManualShot")?.GetConstructor([typeof(CharacterId), typeof(Vector3), typeof(Vector3)]);
            s_ctorReload = asm.GetType("ManualCombat.ManualReload")?.GetConstructor([typeof(CharacterId), typeof(bool)]);

            s_available = s_enabled is not null && s_active is not null && s_live is not null && s_inFight is not null;
            s_resolved = true;
            if (s_available && !s_logged)
            {
                s_logged = true;
                Log.Line(Strings.Get("log.manual_combat_linked"));
            }
        }
        catch (Exception e)
        {
            s_resolved = true;
            s_available = false;
            Log.Exception("ManualCombat resolve", e);
        }
    }

    /// <summary>
    /// Keeps ManualCombat.InputWatch.Enabled in sync with Driver.Settings.ManualCombat:
    /// - First tick: applies our persisted setting (so OFF survives restarts despite InputWatch..cctor defaulting to true).
    /// - Setting changed in Settings > Mods or WasdWindow: pushes to InputWatch.Enabled and immediately releases any bound fighter on OFF.
    /// - User pressed ManualCombat's O key: mirrors the new state back into our persisted setting.
    /// </summary>
    public static void Sync(WorldSession? session)
    {
        if (!Available) return;
        bool want = Driver.Settings.ManualCombat;
        bool now = Enabled;

        if (!s_syncedOnce)
        {
            s_syncedOnce = true;
            s_lastSetting = want;
            if (now != want)
            {
                Enabled = want;
                if (!want) ForceRelease(session);
                Log.Line(Strings.Format("log.manual_combat_toggle", want ? "ON" : "OFF", "saved setting"));
            }
            s_lastMcEnabled = want;
            return;
        }

        if (want != s_lastSetting)
        {
            s_lastSetting = want;
            s_lastMcEnabled = want;
            if (now != want) Enabled = want;
            if (!want) ForceRelease(session);
            Counters.Add(want ? "mc.on" : "mc.off");
            Log.Line(Strings.Format("log.manual_combat_toggle", want ? "ON" : "OFF", "setting"));
            return;
        }

        if (now != s_lastMcEnabled)
        {
            s_lastMcEnabled = now;
            s_lastSetting = now;
            Driver.SetSetting(nameof(DcSettings.ManualCombat), now);
            if (!now) ReleasePadState(session);
            Counters.Add(now ? "mc.on_ext" : "mc.off_ext");
        }
    }

    /// <summary>Explicitly turns ManualCombat ON or OFF from our window or a command, persisted across restarts.</summary>
    public static void SetEnabled(bool on, WorldSession? session, string source)
    {
        Driver.SetSetting(nameof(DcSettings.ManualCombat), on);
        if (!Available) return;
        s_syncedOnce = true;
        s_lastSetting = on;
        s_lastMcEnabled = on;
        Enabled = on;
        if (!on) ForceRelease(session);
        Counters.Add(on ? "mc.on" : "mc.off");
        Log.Line(Strings.Format("log.manual_combat_toggle", on ? "ON" : "OFF", source));
    }

    static void ForceRelease(WorldSession? session)
    {
        ReleasePadState(session);
        if (session is null) return;
        var id = Controlled;
        if (!id.IsNone) Enqueue(session, s_ctorControl?.Invoke([id, false]));
    }

    /// <summary>Pure directional aim from a stick vector (0 None, 1 Overhead, 2 Left, 3 Right, 4 Thrust).</summary>
    public static int AimFromStick(Vector2 stick, float threshold = 0.45f)
    {
        float ax = MathF.Abs(stick.X), ay = MathF.Abs(stick.Y);
        if (MathF.Max(ax, ay) < threshold) return DirNone;
        if (ax > ay * 1.2f) return stick.X < 0 ? DirLeft : DirRight;
        if (ay > ax * 1.2f) return stick.Y > 0 ? DirOverhead : DirThrust;
        return DirNone;
    }

    /// <summary>Pure world XZ unit direction from a stick vector and strategy camera yaw (matches Driver.Steer).</summary>
    public static Vector2 WorldMove(Vector2 stick, float cameraYaw)
    {
        if (stick.LengthSquared() < 1e-4f) return Vector2.Zero;
        var forward = new Vector2(-MathF.Sin(cameraYaw), -MathF.Cos(cameraYaw));
        var right = new Vector2(MathF.Cos(cameraYaw), -MathF.Sin(cameraYaw));
        var dir = forward * stick.Y + right * stick.X;
        float len = dir.Length();
        return len > 1e-4f ? dir / len : Vector2.Zero;
    }

    /// <summary>Camera horizontal forward unit vector (matches StrategyCamera yaw).</summary>
    public static Vector2 CameraForward(float cameraYaw) => new(-MathF.Sin(cameraYaw), -MathF.Cos(cameraYaw));

    /// <summary>
    /// Full gamepad handler while ManualCombat is active in a fight (returns true when Fight Mode owns RT/LT/B/R3/footwork).
    /// Also handles out-of-fight crossbow swap (LT+X) and reload (hold LT+A) while returning false so normal WASD controls stay active.
    /// </summary>
    public static bool StepPad(object player, PadState pad, WorldSession session, bool windowOrMenu)
    {
        if (!Available || !Active || windowOrMenu)
        {
            ReleasePadState(session);
            return false;
        }

        var id = Controlled;
        if (id.IsNone && Driver.ControlledId is { } cid) id = cid;
        if (id.IsNone)
        {
            ReleasePadState(session);
            return false;
        }

        bool lt = pad.Down(PadButton.LeftTrigger);

        // Out-of-fight crossbow helpers: LT+X swaps melee/crossbow, hold LT+A reloads when empty.
        if (!InFight)
        {
            ReleaseFightOnlyState(session, id);
            if (HasCrossbow && lt && Pad.WasPressed(PadButton.West))
            {
                Enqueue(session, s_ctorSwap?.Invoke([id]));
                Counters.Add("mc.pad.swap");
            }
            bool wantReload = HasCrossbow && Empty && lt && pad.Down(PadButton.South);
            if (wantReload != s_padReloadHeld)
            {
                s_padReloadHeld = wantReload;
                Enqueue(session, s_ctorReload?.Invoke([id, wantReload]));
                TrySet(s_reloadSent, wantReload);
                Counters.Add(wantReload ? "mc.pad.reload_on" : "mc.pad.reload_off");
            }
            return false;
        }

        // --- FIGHT MODE (ManualCombat.InputWatch.Live) ---
        float yaw = session.Camera.Strategy.Yaw;
        var look = CameraForward(yaw);
        bool inPause = GamePaused;

        // 1. In-fight footwork from left stick (normal MoveOrder is stripped by FightHold).
        var move = WorldMove(pad.Left, yaw);
        if (move != Vector2.Zero)
        {
            Enqueue(session, s_ctorMove?.Invoke([id, move]));
            s_padMoveActive = true;
            Counters.Add("mc.pad.move");
        }
        else if (s_padMoveActive)
        {
            Enqueue(session, s_ctorMove?.Invoke([id, Vector2.Zero]));
            s_padMoveActive = false;
        }

        // 2. Directional aim from stick (synced into InputWatch so HUD arrows match).
        bool keyAim = string.Equals(AimWith, "Movement keys", StringComparison.OrdinalIgnoreCase);
        int stickDir = AimFromStick(keyAim ? pad.Left : pad.Right);
        if (stickDir != DirNone)
        {
            s_padAim = stickDir;
            TrySet(keyAim ? s_keyDir : s_mouseDir, stickDir);
        }
        int aim = s_aimNow?.GetValue(null) is int a && a != DirNone ? a : (s_padAim != DirNone ? s_padAim : DirRight);

        // 3. LT + B: Leave Fight (matches out-of-fight LT + B = Stop!).
        if (lt && Pad.WasPressed(PadButton.East))
        {
            ReleaseBlock(session, id, inPause);
            Enqueue(session, s_ctorLeave?.Invoke([id]));
            Counters.Add("mc.pad.leave");
            return true;
        }

        // 4. LT + X: Swap crossbow / melee.
        if (HasCrossbow && lt && Pad.WasPressed(PadButton.West))
        {
            Enqueue(session, s_ctorSwap?.Invoke([id]));
            Counters.Add("mc.pad.swap");
        }

        // 5. B (without LT): Dodge.
        if (!lt && Pad.WasPressed(PadButton.East))
        {
            var cmd = s_ctorDodge?.Invoke([id]);
            s_dodgeInPause?.SetValue(cmd, inPause);
            Enqueue(session, cmd);
            Counters.Add("mc.pad.dodge");
        }

        // 6. R3 tap: Switch target.
        if (Pad.WasPressed(PadButton.RightStick))
        {
            Enqueue(session, s_ctorTarget?.Invoke([id, look]));
            Counters.Add("mc.pad.target");
        }

        // 7. LT (hold): Block / Parry (melee) or Steady (ranged).
        if (lt)
        {
            if (!s_padBlockHeld || aim != s_padBlockDir)
            {
                var cmd = s_ctorBlock?.Invoke([id, true, aim]);
                s_blockInPause?.SetValue(cmd, inPause);
                Enqueue(session, cmd);
                s_padBlockHeld = true;
                s_padBlockDir = aim;
                TrySet(s_blockSent, true);
                TrySet(s_blockByMouse, true);
                TrySet(s_blockSentDir, aim);
                Counters.Add("mc.pad.block");
            }
        }
        else if (s_padBlockHeld)
        {
            ReleaseBlock(session, id, inPause);
        }

        // 8. RT: Melee Attack (hold to ready, flick to re-aim, release to strike) OR Crossbow Fire / Reload.
        bool rtDown = pad.Down(PadButton.RightTrigger);
        if (Ranged)
        {
            if (Empty)
            {
                if (rtDown != s_padReloadHeld)
                {
                    s_padReloadHeld = rtDown;
                    Enqueue(session, s_ctorReload?.Invoke([id, rtDown]));
                    TrySet(s_reloadSent, rtDown);
                    TrySet(s_reloadByMouse, rtDown);
                    Counters.Add(rtDown ? "mc.pad.reload_on" : "mc.pad.reload_off");
                }
            }
            else if (Pad.WasPressed(PadButton.RightTrigger))
            {
                FireCrossbow(player, session, id);
            }
        }
        else
        {
            if (Pad.WasPressed(PadButton.RightTrigger))
            {
                var cmd = s_ctorAttack?.Invoke([id, aim, look]);
                s_attackHeld?.SetValue(cmd, true);
                s_attackInPause?.SetValue(cmd, inPause);
                Enqueue(session, cmd);
                s_padAttackHeld = true;
                s_padAttackDir = aim;
                Counters.Add("mc.pad.attack");
            }
            else if (s_padAttackHeld && rtDown && aim != s_padAttackDir)
            {
                Enqueue(session, s_ctorAttackAim?.Invoke([id, aim]));
                s_padAttackDir = aim;
            }
            else if (s_padAttackHeld && !rtDown)
            {
                ReleaseAttack(session, id, inPause);
            }
        }

        return true;
    }

    static void FireCrossbow(object player, WorldSession session, CharacterId id)
    {
        if (s_crosshairRay is null || s_ctorShot is null) return;
        try
        {
            object? ray = s_crosshairRay.Invoke(null, [player]);
            if (ray is ValueTuple<Vector3, Vector3> r)
            {
                Enqueue(session, s_ctorShot.Invoke([id, r.Item1, r.Item2]));
                Counters.Add("mc.pad.shot");
            }
        }
        catch (Exception e) { Log.Exception("ManualCombat shot", e); }
    }

    /// <summary>Releases any pad-held combat commands when a window, dial, disconnect, or world switch interrupts the fight.</summary>
    public static void ReleasePadState(WorldSession? session)
    {
        var id = Controlled;
        if (id.IsNone && Driver.ControlledId is { } cid) id = cid;
        ReleaseFightOnlyState(session, id);
        if (s_padReloadHeld)
        {
            s_padReloadHeld = false;
            if (session is not null && !id.IsNone) Enqueue(session, s_ctorReload?.Invoke([id, false]));
            TrySet(s_reloadSent, false);
            TrySet(s_reloadByMouse, false);
        }
    }

    static void ReleaseFightOnlyState(WorldSession? session, CharacterId id)
    {
        bool inPause = GamePaused;
        if (s_padAttackHeld) ReleaseAttack(session, id, inPause);
        if (s_padBlockHeld) ReleaseBlock(session, id, inPause);
        if (s_padMoveActive)
        {
            s_padMoveActive = false;
            if (session is not null && !id.IsNone) Enqueue(session, s_ctorMove?.Invoke([id, Vector2.Zero]));
        }
    }

    static void ReleaseAttack(WorldSession? session, CharacterId id, bool inPause)
    {
        s_padAttackHeld = false;
        s_padAttackDir = DirNone;
        if (session is null || id.IsNone) return;
        var cmd = s_ctorAttackRelease?.Invoke([id]);
        s_releaseInPause?.SetValue(cmd, inPause);
        Enqueue(session, cmd);
    }

    static void ReleaseBlock(WorldSession? session, CharacterId id, bool inPause)
    {
        s_padBlockHeld = false;
        s_padBlockDir = DirNone;
        TrySet(s_blockSent, false);
        TrySet(s_blockByMouse, false);
        if (session is null || id.IsNone) return;
        var cmd = s_ctorBlock?.Invoke([id, false, DirNone]);
        s_blockInPause?.SetValue(cmd, inPause);
        Enqueue(session, cmd);
    }

    static void Enqueue(WorldSession session, object? cmdObj)
    {
        if (cmdObj is not SimCommand cmd) return;
        try
        {
            s_tickProp?.SetValue(cmd, session.World.Tick);
            session.World.Commands.Enqueue(cmd);
        }
        catch (Exception e) { Log.Exception("ManualCombat enqueue", e); }
    }

    static void TrySet(FieldInfo? f, object value)
    {
        if (f is null) return;
        try { f.SetValue(null, value); }
        catch { }
    }

    /// <summary>Called on NewWorld: drops cached pad combat state.</summary>
    public static void Reset()
    {
        s_padAttackHeld = s_padBlockHeld = s_padReloadHeld = s_padMoveActive = false;
        s_padAim = s_padAttackDir = s_padBlockDir = DirNone;
    }
}
