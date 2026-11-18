// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The game's non-public types and members this mod reaches, looked up ONCE by name (MeitouClient 0.20.1, clone at ae1a402, docs/design.md).
// A missing one turns the mod off with a line naming it, never a crash later.
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Game.Rendering.Frame;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public static class Access
{
    /// <summary>Meitou.Game.PlayerInterface (internal sealed partial class).</summary>
    public static Type PlayerInterface = null!;
    /// <summary>PlayerInterface.Steer(bool final): once a frame on the host thread.</summary>
    public static MethodInfo Steer = null!;
    /// <summary>InputBindings.Resolve(TickInput): the control tick's keys into actions.</summary>
    public static MethodInfo Resolve = null!;
    /// <summary>WorldSession.Tick(): one control tick (input, camera, Ticked).</summary>
    public static MethodInfo SessionTick = null!;

    /// <summary>PlayerInterface.Session (internal readonly field).</summary>
    public static AccessTools.FieldRef<object, WorldSession> Session = null!;
    /// <summary>PlayerInterface.Follow { get; set; } (CharacterId?): the character the strategy camera follows.</summary>
    static PropertyInfo s_follow = null!;
    /// <summary>PlayerInterface.UiEditing { get; }: a text box has the keyboard.</summary>
    static PropertyInfo s_uiEditing = null!;
    /// <summary>ActionState.Set(action, held, pressed, released) (internal).</summary>
    public static Action<ActionState, InputAction, bool, bool, bool> SetAction = null!;

    /// <summary>Resolves everything; returns what is missing (empty = all there).</summary>
    public static List<string> ResolveAll()
    {
        var missing = new List<string>();
        var game = typeof(WorldSession).Assembly;

        PlayerInterface = game.GetType("Meitou.Game.PlayerInterface")!;
        if (PlayerInterface is null)
        {
            missing.Add("type Meitou.Game.PlayerInterface");
            return missing;
        }

        Steer = Need(AccessTools.Method(PlayerInterface, "Steer", [typeof(bool)]), "PlayerInterface.Steer(bool)", missing);
        Resolve = Need(AccessTools.Method(typeof(InputBindings), "Resolve", [typeof(TickInput)]), "InputBindings.Resolve(TickInput)", missing);
        SessionTick = Need(AccessTools.Method(typeof(WorldSession), "Tick", Type.EmptyTypes), "WorldSession.Tick()", missing);

        var sessionField = AccessTools.Field(PlayerInterface, "Session");
        if (sessionField is null || sessionField.FieldType != typeof(WorldSession)) missing.Add("field PlayerInterface.Session");
        else Session = AccessTools.FieldRefAccess<WorldSession>(PlayerInterface, "Session");

        s_follow = AccessTools.Property(PlayerInterface, "Follow")!;
        if (s_follow is null || s_follow.PropertyType != typeof(CharacterId?) || !s_follow.CanWrite) missing.Add("property PlayerInterface.Follow");
        s_uiEditing = AccessTools.Property(PlayerInterface, "UiEditing")!;
        if (s_uiEditing is null || s_uiEditing.PropertyType != typeof(bool)) missing.Add("property PlayerInterface.UiEditing");

        s_mouse = AccessTools.Field(PlayerInterface, "Mouse")!;
        if (s_mouse is null || s_mouse.FieldType != typeof(Vector2)) missing.Add("field PlayerInterface.Mouse");
        s_picking = AccessTools.Property(PlayerInterface, "Picking")!;
        if (s_picking is null) missing.Add("property PlayerInterface.Picking");
        else
        {
            s_pick = AccessTools.Method(s_picking.PropertyType, "Pick", [typeof(Vector2)])!;
            if (s_pick is null || s_pick.ReturnType != typeof(CharacterId?)) missing.Add("method WorldPicking.Pick(Vector2)");
            s_surfaceBelow = AccessTools.Property(s_picking.PropertyType, "SurfaceBelow")!;
            if (s_surfaceBelow is null || s_surfaceBelow.PropertyType != typeof(Func<Vector3, float?>)) missing.Add("property WorldPicking.SurfaceBelow");
        }
        s_select = AccessTools.Method(PlayerInterface, "Select", [typeof(IReadOnlyList<CharacterId>), typeof(bool), typeof(bool)])!;
        if (s_select is null) missing.Add("method PlayerInterface.Select(ids, additive, toggle)");
        ResolveViews(game, missing);
        ResolveLook(game, missing);
        ResolveClicks(missing);
        ResolveMenus(missing);
        s_mouseDown = Need(AccessTools.Method(PlayerInterface, "MouseDown", [typeof(MouseButton), typeof(Vector2), typeof(bool), typeof(bool), typeof(bool)]),
            "PlayerInterface.MouseDown", missing);
        s_gameHotkeys = AccessTools.Property(PlayerInterface, "GameHotkeys")!;
        s_hotkeyKey = s_gameHotkeys is null ? null! : AccessTools.Method(s_gameHotkeys.PropertyType, "Key", [typeof(Key), typeof(bool)])!;
        if (s_hotkeyKey is null) missing.Add("method GameHotkeys.Key(Key, bool) via PlayerInterface.GameHotkeys");
        s_notice = AccessTools.Method(PlayerInterface, "Notice", [typeof(string), typeof(bool), typeof(string)])!;
        if (s_notice is null) missing.Add("method PlayerInterface.Notice(text, error, logText)");

        var set = AccessTools.Method(typeof(ActionState), "Set", [typeof(InputAction), typeof(bool), typeof(bool), typeof(bool)]);
        if (set is null) missing.Add("method ActionState.Set");
        else SetAction = AccessTools.MethodDelegate<Action<ActionState, InputAction, bool, bool, bool>>(set);

        return missing;
    }

    /// <summary>PlayerInterface.GameHotkeys and its Key(key, shift): the game view's hotkeys as the game loop feeds a key-down.</summary>
    static PropertyInfo s_gameHotkeys = null!;
    static MethodInfo s_hotkeyKey = null!;

    /// <summary>Presses a game hotkey as the keyboard would (the orders, members, run speed...).</summary>
    public static void PressHotkey(object player, Key key) => s_hotkeyKey.Invoke(s_gameHotkeys.GetValue(player), [key, false]);

    #region Clicks (PlayerInterface's world clicks)

    public static MethodInfo MouseUp = null!, SelectMethod = null!, PickedRightDown = null!, MoveMethod = null!;
    static MethodInfo s_overHud = null!, s_inspect = null!, s_pickAt = null!, s_contextPress = null!;
    static FieldInfo s_steering = null!, s_contextPending = null!;
    static PropertyInfo s_contextWindow = null!, s_contextOpen = null!;

    static void ResolveClicks(List<string> missing)
    {
        MouseUp = Need(AccessTools.Method(PlayerInterface, "MouseUp", [typeof(MouseButton), typeof(Vector2), typeof(bool)]), "PlayerInterface.MouseUp", missing);
        SelectMethod = s_select;
        PickedRightDown = Need(AccessTools.Method(PlayerInterface, "PickedRightDown", [typeof(Vector2), typeof(bool), typeof(bool)]),
            "PlayerInterface.PickedRightDown", missing);
        MoveMethod = Need(AccessTools.Method(PlayerInterface, "Move", [typeof(Vector3), typeof(bool)]), "PlayerInterface.Move", missing);
        s_overHud = Need(AccessTools.Method(PlayerInterface, "OverHud", [typeof(Vector2)]), "PlayerInterface.OverHud", missing);
        s_inspect = Need(AccessTools.Method(PlayerInterface, "Inspect", [typeof(CharacterId)]), "PlayerInterface.Inspect", missing);
        s_steering = AccessTools.Field(PlayerInterface, "Steering")!;
        if (s_steering is null || s_steering.FieldType != typeof(bool)) missing.Add("field PlayerInterface.Steering");
        if (s_picking is not null) s_pickAt = Need(AccessTools.Method(s_picking.PropertyType, "PickAt", [typeof(Vector2)]), "WorldPicking.PickAt", missing);
        s_contextWindow = AccessTools.Property(PlayerInterface, "ContextMenuWindow")!;
        if (s_contextWindow is null)
        {
            missing.Add("property PlayerInterface.ContextMenuWindow");
            return;
        }

        var cm = s_contextWindow.PropertyType;
        s_contextPress = Need(AccessTools.Method(cm, "ContextPress", [typeof(Vector2), typeof(Action)]), "ContextMenuWindow.ContextPress", missing);
        s_contextPending = AccessTools.Field(cm, "_contextPending")!;
        if (s_contextPending is null) missing.Add("field ContextMenuWindow._contextPending");
        s_contextOpen = AccessTools.Property(cm, "ContextMenuOpen")!;
        if (s_contextOpen is null) missing.Add("property ContextMenuWindow.ContextMenuOpen");
    }

    static MethodInfo s_mouseDown = null!;

    /// <summary>Where the interface's pointer is (PlayerInterface.Mouse; in a view, the pinned crosshair).</summary>
    public static Vector2 Pointer(object player) => (Vector2)s_mouse.GetValue(player)!;

    /// <summary>A mouse button pressed / let go at the pointer, as the game loop feeds a real one (the pad's A and B).</summary>
    public static void Click(object player, MouseButton button, bool down, bool alt = false)
    {
        var p = Pointer(player);
        if (down) s_mouseDown.Invoke(player, [button, p, false, false, alt]);
        else MouseUp.Invoke(player, [button, p, false]);
    }

    public static bool OverHud(object player, Vector2 p) => (bool)s_overHud.Invoke(player, [p])!;

    public static void Inspect(object player, CharacterId id) => s_inspect.Invoke(player, [id]);

    public static void StopSteering(object player) => s_steering.SetValue(player, false);

    /// <summary>What the ray at <paramref name="p"/> picks first (WorldPicking.Picked's name: Character, Door, GroundItem, None...).</summary>
    public static string PickKind(object player, Vector2 p)
    {
        var picking = s_picking.GetValue(player);
        var result = s_pickAt.Invoke(picking, [p]);
        return result?.GetType().GetField("Item1")?.GetValue(result)?.ToString() ?? "None";
    }

    public static bool ContextMenuOpen(object player) =>
        s_contextWindow.GetValue(player) is { } cm && (bool)s_contextOpen.GetValue(cm)!;

    /// <summary>The character's context menu, opened on the next frame with no quick order: ContextPress with no quick order, its hold timer
    /// set as already run out (UpdateContextMenu opens the menu once the hold has passed).</summary>
    /// <summary>The character's context menu, opened now with no quick order: ContextPress (no quick order) builds the pending press; its menu
    /// is made at once (pending.Menu(pending.At), as UpdateContextMenu does after the hold) and set as OpenMenu, the press cleared. True when open.</summary>
    public static bool OpenContextMenuNow(object player, Vector2 p)
    {
        var cm = s_contextWindow.GetValue(player)!;
        if (!(bool)s_contextPress.Invoke(cm, [p, null])!) return false;

        object? pending = s_contextPending.GetValue(cm);
        if (pending is null) return false;

        var t = pending.GetType();
        if (t.GetField("Item1")?.GetValue(pending) is not Delegate make || t.GetField("Item2")?.GetValue(pending) is not Vector2 at) return false;

        var menu = make.DynamicInvoke(at);
        s_contextPending.SetValue(cm, null);
        if (menu is null) return false;

        s_openMenu.SetValue(cm, menu);
        return true;
    }

    static FieldInfo s_openMenu = null!;
    static MethodInfo s_contextAction = null!, s_contextClose = null!, s_mouseMove = null!;
    static FieldInfo s_uiWidth = null!, s_uiHeight = null!;
    /// <summary>ContextMenuWindow.ContextMenuRelease(button, p): a mouse release while a menu is open.</summary>
    public static MethodInfo ContextMenuRelease = null!;

    static void ResolveMenus(List<string> missing)
    {
        if (s_contextWindow is null) return;

        var cm = s_contextWindow.PropertyType;
        s_openMenu = AccessTools.Field(cm, "OpenMenu")!;
        if (s_openMenu is null) missing.Add("field ContextMenuWindow.OpenMenu");
        s_contextAction = Need(AccessTools.Method(cm, "ContextAction", [typeof(string)]), "ContextMenuWindow.ContextAction", missing);
        s_contextClose = Need(AccessTools.Method(cm, "CloseContextMenu", Type.EmptyTypes), "ContextMenuWindow.CloseContextMenu", missing);
        ContextMenuRelease = Need(AccessTools.Method(cm, "ContextMenuRelease", [typeof(MouseButton), typeof(Vector2)]), "ContextMenuWindow.ContextMenuRelease", missing);
        s_mouseMove = Need(AccessTools.Method(PlayerInterface, "MouseMove", [typeof(Vector2)]), "PlayerInterface.MouseMove", missing);
        s_uiWidth = AccessTools.Field(PlayerInterface, "Width")!;
        s_uiHeight = AccessTools.Field(PlayerInterface, "Height")!;
        if (s_uiWidth is null || s_uiHeight is null) missing.Add("PlayerInterface.Width / Height");
    }

    static object? OpenMenu(object player) => s_contextWindow.GetValue(player) is { } cm ? s_openMenu.GetValue(cm) : null;

    /// <summary>How many verbs the open menu has (0 without one).</summary>
    public static int ContextMenuRows(object player) =>
        OpenMenu(player) is { } m && m.GetType().GetProperty("Verbs")?.GetValue(m) is System.Collections.ICollection v ? v.Count : 0;

    /// <summary>Where the open menu was opened (its layout's anchor), or null.</summary>
    public static Vector2? ContextMenuAt(object player) =>
        OpenMenu(player) is { } m && m.GetType().GetProperty("At")?.GetValue(m) is Vector2 at ? at : null;

    /// <summary>Gives verb <paramref name="row"/> of the open menu and closes it (the game's own pick).</summary>
    public static void ContextMenuPick(object player, int row) =>
        s_contextAction.Invoke(s_contextWindow.GetValue(player), [row.ToString(System.Globalization.CultureInfo.InvariantCulture)]);

    public static void ContextMenuClose(object player) => s_contextClose.Invoke(s_contextWindow.GetValue(player), []);

    /// <summary>The interface's size in its pixels (PlayerInterface.Width / Height).</summary>
    public static (int W, int H) UiSize(object player) => ((int)s_uiWidth.GetValue(player)!, (int)s_uiHeight.GetValue(player)!);

    /// <summary>Puts the interface's pointer at <paramref name="p"/> (its hover follows), as a mouse move does.</summary>
    public static void MovePointer(object player, Vector2 p) => s_mouseMove.Invoke(player, [p]);

    #endregion

    #region Mouse-look (GameHost.MouseLook, GameLoop's pointer)

    /// <summary>GameHost.MouseLook(): the game's "mouse turns the camera, pointer pinned" switch.</summary>
    public static MethodInfo MouseLook = null!;
    /// <summary>GameLoop.PlaceSceneCameras(): once a frame, where the pointer is centred.</summary>
    public static MethodInfo PlaceSceneCameras = null!;
    static FieldInfo s_loopWindow = null!, s_loopMice = null!, s_loopLastMouse = null!, s_loopAnchor = null!;
    static PropertyInfo s_modalOpen = null!, s_uiCaptures = null!;

    static void ResolveLook(Assembly game, List<string> missing)
    {
        var host = game.GetType("Meitou.Game.GameHost");
        var loop = game.GetType("Meitou.Game.Host.GameLoop");
        if (host is null || loop is null)
        {
            missing.Add("type GameHost / GameLoop");
            return;
        }

        MouseLook = Need(AccessTools.Method(host, "MouseLook", Type.EmptyTypes), "GameHost.MouseLook()", missing);
        PlaceSceneCameras = Need(AccessTools.Method(loop, "PlaceSceneCameras", Type.EmptyTypes), "GameLoop.PlaceSceneCameras()", missing);
        s_loopWindow = Field(loop, "_window", missing);
        s_loopMice = Field(loop, "_mice", missing);
        s_loopLastMouse = Field(loop, "_lastMouse", missing);
        s_loopAnchor = Field(loop, "_orbitAnchor", missing);
        s_loopWindowCursor = AccessTools.Field(loop, "_windowCursor");          // optional: without them the pin waits for a mouse move (as 0.9.1)
        s_loopCursorBefore = AccessTools.Field(loop, "_cursorBeforeOrbit");
        if (s_loopLastMouse is not null && s_loopLastMouse.FieldType != typeof(Vector2?)) missing.Add("GameLoop._lastMouse is not Vector2?");
        if (s_loopAnchor is not null && s_loopAnchor.FieldType != typeof(Vector2?)) missing.Add("GameLoop._orbitAnchor is not Vector2?");
        s_modalOpen = AccessTools.Property(PlayerInterface, "ModalOpen")!;
        s_uiCaptures = AccessTools.Property(PlayerInterface, "UiCapturesPointer")!;
        if (s_modalOpen is null || s_uiCaptures is null) missing.Add("PlayerInterface.ModalOpen / UiCapturesPointer");
    }

    static FieldInfo Field(Type t, string name, List<string> missing)
    {
        var f = AccessTools.Field(t, name);
        if (f is null) missing.Add($"field {t.Name}.{name}");
        return f!;
    }

    public static bool ModalOpen(object player) => (bool)s_modalOpen.GetValue(player)!;

    public static bool UiCapturesPointer(object player) => (bool)s_uiCaptures.GetValue(player)!;

    /// <summary>The middle of the game window in window coordinates (the mouse's), or null without a window.</summary>
    public static Vector2? LoopWindowCentre(object loop)
    {
        if (s_loopWindow.GetValue(loop) is not { } window) return null;

        var size = window.GetType().GetProperty("Size")?.GetValue(window);
        if (size is null) return null;

        var t = size.GetType();
        object? x = t.GetField("X")?.GetValue(size), y = t.GetField("Y")?.GetValue(size);
        return x is int w && y is int h && w > 0 && h > 0 ? new Vector2(w / 2f, h / 2f) : null;
    }

    /// <summary>The loop's pinned point (GameLoop._orbitAnchor), null before Meitou pins the pointer.</summary>
    public static Vector2? LoopAnchor(object loop) => (Vector2?)s_loopAnchor.GetValue(loop);

    static FieldInfo? s_loopWindowCursor, s_loopCursorBefore;

    /// <summary>
    /// Pins the pointer at <paramref name="centre"/> now, as GameLoop.OnMouseMove does on the first move after MouseLook turns true (the anchor,
    /// the cursor mode before, the window cursor Disabled, the last position read back from GLFW). Play test 2026-11-17: Meitou pins only on a
    /// mouse move, so until the mouse moved the pointer was drawn where it had been, not at the centre. False when a member is missing.
    /// </summary>
    public static bool PinNow(object loop, Vector2 centre)
    {
        if (s_loopWindowCursor is null || s_loopCursorBefore is null || s_loopAnchor.GetValue(loop) is not null) return false;
        if (s_loopMice.GetValue(loop) is not System.Collections.IEnumerable mice) return false;
        object? mouse = null;
        foreach (var m in mice) { mouse = m; break; }
        if (mouse is null) return false;

        var position = mouse.GetType().GetProperty("Position");
        var cursor = mouse.GetType().GetProperty("Cursor")?.GetValue(mouse);
        var mode = cursor?.GetType().GetProperty("CursorMode");
        if (position is null || cursor is null || mode is null) return false;

        position.SetValue(mouse, centre);
        s_loopAnchor.SetValue(loop, (Vector2?)centre);
        s_loopCursorBefore.SetValue(loop, s_loopWindowCursor.GetValue(loop));
        object disabled = Enum.Parse(mode.PropertyType, "Disabled");
        mode.SetValue(cursor, disabled);
        s_loopWindowCursor.SetValue(loop, disabled);
        s_loopLastMouse.SetValue(loop, (Vector2?)(Vector2)position.GetValue(mouse)!);
        return true;
    }

    /// <summary>Puts the OS pointer, the loop's last pointer position and (when already pinned) the pinned point at <paramref name="centre"/>.</summary>
    public static void CentrePointer(object loop, Vector2 centre)
    {
        if (s_loopMice.GetValue(loop) is System.Collections.IEnumerable mice)
        {
            foreach (var mouse in mice)
            {
                mouse.GetType().GetProperty("Position")?.SetValue(mouse, centre);
                break;
            }
        }

        s_loopLastMouse.SetValue(loop, (Vector2?)centre);
        if (s_loopAnchor.GetValue(loop) is not null) s_loopAnchor.SetValue(loop, (Vector2?)centre);
    }

    #endregion

    #region Views (once a frame, host thread)

    /// <summary>SandboxScenario.FollowCamera(): Meitou's follow camera, once a frame (GameLoop.PlaceSceneCameras), before the eye clearance.</summary>
    public static MethodInfo FollowCamera = null!;
    /// <summary>The scenario's GameHost (its primary constructor's parameter, found by type).</summary>
    static FieldInfo s_scenarioHost = null!;
    /// <summary>GameHost.Camera: the frame's WorldCamera (orbit: Target, Yaw, Pitch, Distance).</summary>
    static FieldInfo s_hostCamera = null!;
    /// <summary>GameHost.DrawnPosition(CharacterSnapshot) and GameHost.Tracked(CharacterSnapshot): where a character is drawn this frame, and the
    /// one the camera centres on for it (a carried body: its carrier).</summary>
    static MethodInfo s_drawnPosition = null!, s_tracked = null!;
    /// <summary>PlayerInterface.StealthView and its DrawnHead (Func&lt;CharacterId, Vector3?&gt;: the drawn "Bip01 Head" bone, set by GameHost).</summary>
    static PropertyInfo s_stealthView = null!, s_drawnHead = null!;

    static void ResolveViews(Assembly game, List<string> missing)
    {
        var scenario = game.GetType("Meitou.Game.Scenarios.SandboxScenario");
        var host = game.GetType("Meitou.Game.GameHost");
        if (scenario is null || host is null)
        {
            missing.Add("type SandboxScenario / GameHost");
            return;
        }

        FollowCamera = Need(AccessTools.Method(scenario, "FollowCamera", Type.EmptyTypes), "SandboxScenario.FollowCamera()", missing);
        s_scenarioHost = AccessTools.GetDeclaredFields(scenario).FirstOrDefault(f => f.FieldType == host)!;
        if (s_scenarioHost is null) missing.Add("SandboxScenario's GameHost field");
        s_hostCamera = AccessTools.Field(host, "Camera")!;
        if (s_hostCamera is null || s_hostCamera.FieldType != typeof(WorldCamera)) missing.Add("field GameHost.Camera (WorldCamera)");
        s_drawnPosition = Need(AccessTools.Method(host, "DrawnPosition", [typeof(CharacterSnapshot)]), "GameHost.DrawnPosition(CharacterSnapshot)", missing);
        s_tracked = Need(AccessTools.Method(host, "Tracked", [typeof(CharacterSnapshot)]), "GameHost.Tracked(CharacterSnapshot)", missing);
        s_stealthView = AccessTools.Property(PlayerInterface, "StealthView")!;
        s_drawnHead = s_stealthView is null ? null! : AccessTools.Property(s_stealthView.PropertyType, "DrawnHead")!;
        if (s_drawnHead is null || s_drawnHead.PropertyType != typeof(Func<CharacterId, Vector3?>)) missing.Add("property StealthView.DrawnHead");
    }

    /// <summary>The scenario's host (the FollowCamera postfix's instance's).</summary>
    public static object? ScenarioHost(object scenario) => s_scenarioHost.GetValue(scenario);

    public static WorldCamera HostCamera(object host) => (WorldCamera)s_hostCamera.GetValue(host)!;

    public static CharacterSnapshot Tracked(object host, CharacterSnapshot c) => (CharacterSnapshot)s_tracked.Invoke(host, [c])!;

    public static Vector3 DrawnPosition(object host, CharacterSnapshot c) => (Vector3)s_drawnPosition.Invoke(host, [c])!;

    /// <summary>The drawn head bone; null when the character is not drawn this frame.</summary>
    public static Vector3? DrawnHead(object player, CharacterId id) =>
        s_stealthView.GetValue(player) is { } view && s_drawnHead.GetValue(view) is Func<CharacterId, Vector3?> head ? head(id) : null;

    #endregion

    static MethodInfo Need(MethodInfo? m, string name, List<string> missing)
    {
        if (m is null) missing.Add("method " + name);
        return m!;
    }

    /// <summary>The character the camera follows.</summary>
    public static CharacterId? GetFollow(object player) => (CharacterId?)s_follow.GetValue(player);

    /// <summary>Makes the camera follow a character (null lets go).</summary>
    public static void SetFollow(object player, CharacterId? id) => s_follow.SetValue(player, id);

    /// <summary>A text box has the keyboard.</summary>
    public static bool UiEditing(object player) => (bool)s_uiEditing.GetValue(player)!;

    /// <summary>PlayerInterface.Mouse (internal field): the pointer in window pixels.</summary>
    static FieldInfo s_mouse = null!;
    /// <summary>PlayerInterface.Picking (internal WorldPicking) and its Pick(pixel): the player's character under the pointer.</summary>
    static PropertyInfo s_picking = null!;
    static MethodInfo s_pick = null!;
    /// <summary>PlayerInterface.Select(ids, additive, toggle): the game's own selection command (the click's).</summary>
    static MethodInfo s_select = null!;

    /// <summary>The player's own character under the mouse pointer, as a left click picks it (WorldPicking.Pick: IsPlayer only).</summary>
    public static CharacterId? PickOwnUnderMouse(object player)
    {
        var picking = s_picking.GetValue(player);
        if (picking is null) return null;
        return (CharacterId?)s_pick.Invoke(picking, [(Vector2)s_mouse.GetValue(player)!]);
    }

    /// <summary>PlayerInterface.Notice(text, error, logText): the message panel's line (4 s each, in turn) and the message log, as "Saved" is.</summary>
    static MethodInfo s_notice = null!;

    /// <summary>WorldPicking.SurfaceBelow: the host's navmesh height (highest walkable surface at a point's x, z not above it), set by GameHost.</summary>
    static PropertyInfo s_surfaceBelow = null!;

    /// <summary>The navmesh's surface lookup as the right click uses it; null before the host has set it.</summary>
    public static Func<Vector3, float?>? SurfaceBelow(object player) =>
        s_picking.GetValue(player) is { } picking ? (Func<Vector3, float?>?)s_surfaceBelow.GetValue(picking) : null;

    /// <summary>Shows a line in Meitou's message panel (and its message log).</summary>
    public static void Notice(object player, string text) => s_notice.Invoke(player, [text, false, null]);

    static MethodInfo? s_pickWhereAt, s_verbs;

    /// <summary>
    /// The character the ray at <paramref name="p"/> meets first, any character (WorldPicking.PickWhereAt, the right click's own character pick:
    /// the drawn body, else a capsule; hidden ones not); null for none or when the member is missing.
    /// </summary>
    public static CharacterId? CharacterAt(object player, Vector2 p)
    {
        if (s_picking.GetValue(player) is not { } picking) return null;
        s_pickWhereAt ??= AccessTools.Method(picking.GetType(), "PickWhereAt");
        if (s_pickWhereAt is null) return null;
        Func<CharacterSnapshot, bool> any = _ => true;
        var hit = s_pickWhereAt.Invoke(picking, [p, any]);
        return hit is System.Runtime.CompilerServices.ITuple t && t[0] is CharacterId id ? id : null;
    }

    /// <summary>
    /// The right-click menu's verbs for <paramref name="target"/> with the selection as it is (ContextMenuWindow.Verbs: caption and the order it
    /// gives), so an action through it is exactly the menu's. Empty when a member is missing.
    /// </summary>
    public static IReadOnlyList<(string Caption, Action Give)> Verbs(object player, CharacterSnapshot target)
    {
        if (s_contextWindow.GetValue(player) is not { } cm) return [];
        s_verbs ??= AccessTools.Method(cm.GetType(), "Verbs", [typeof(CharacterSnapshot)]);
        return s_verbs?.Invoke(cm, [target]) as List<(string Caption, Action Give)> ?? [];
    }

    static MethodInfo? s_escapeKey;

    /// <summary>The interface's own Esc (PlayerInterface.EscapeKey): closes the trade or the loot with the inventories, else the top window.</summary>
    public static void Escape(object player)
    {
        s_escapeKey ??= AccessTools.Method(PlayerInterface, "EscapeKey", Type.EmptyTypes);
        s_escapeKey?.Invoke(player, null);
    }

    static PropertyInfo? s_openScreens;

    /// <summary>The interface's open screens, bottom to top (PlayerInterface.OpenScreens: "inventory", "trader", "loot", ...); empty when missing.</summary>
    public static IReadOnlyList<string> OpenScreens(object player)
    {
        s_openScreens ??= AccessTools.Property(PlayerInterface, "OpenScreens");
        return s_openScreens?.GetValue(player) as IReadOnlyList<string> ?? [];
    }

    /// <summary>Selects exactly this character (not additive), as a left click on it does.</summary>
    public static void SelectOnly(object player, CharacterId id) => s_select.Invoke(player, [new[] { id }, false, false]);

    static PropertyInfo? s_portraitBar;
    static MethodInfo? s_squadBarMembers;

    /// <summary>
    /// The squad bar's characters, the active squad's in its order (PlayerInterface.PortraitBar.SquadBarMembers, at most 30). Meitou draws each
    /// one's face into the portrait atlas cell of its index every frame (PortraitBar.PortraitMembers), so the squad wheel shows those cells.
    /// Empty when a member is missing.
    /// </summary>
    public static IReadOnlyList<CharacterSnapshot> SquadBarMembers(object player)
    {
        s_portraitBar ??= AccessTools.Property(PlayerInterface, "PortraitBar");
        if (s_portraitBar?.GetValue(player) is not { } bar) return [];
        s_squadBarMembers ??= AccessTools.Method(bar.GetType(), "SquadBarMembers", Type.EmptyTypes);
        return s_squadBarMembers?.Invoke(bar, null) as IReadOnlyList<CharacterSnapshot> ?? [];
    }
}
