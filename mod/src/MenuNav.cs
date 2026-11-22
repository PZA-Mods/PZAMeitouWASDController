// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The pad in Meitou's menus (user 2026-11-20: "At the title menu (and the menu where you save/load saves) I want to be able to use the D-Pad
// and the joysticks to navigate the options... and also is there no way to do like, a keyboard pop-up when typing into the save name").
// Meitou's own keyboard routing already walks a menu (MyGuiScreenHost: Tab / Shift+Tab move between its buttons and text boxes, with the focus
// ring in the Meitou look; Enter / Space press the focused button; Up / Down move the list that has the keyboard; TextInput types into the
// focused box). The pad drives exactly that, so nothing guesses where a button is:
// - D-pad up / down: the list (the save list) when one has the keyboard, else the previous / next button or box. D-pad left / right: the
//   previous / next button or box. The left stick does the same (a push past StickPush steps once, held it repeats).
// - A: the focused button is pressed; on a text box the controller keyboard opens (OnScreenKeyboard). After the right stick moved the
//   pointer, A clicks at the pointer again (the pointer is in charge until the D-pad or left stick moves the focus).
// - B: back (Esc), as before. The right stick: the pointer, as before.
// - A list with no keyboard yet (the load list before any click): the first D-pad up / down gives it the keyboard (MenuAccess.FocusList).
// Pure parts (StickRepeat, the screen rule) are driven by the offline check and the fuzzer; MenuAccess reaches Meitou by name, so a Meitou
// without one of the members just leaves that part off (counted).
using System.Numerics;
using System.Reflection;
using HarmonyLib;

namespace PZA.WASDControl.Meitou;

public enum NavStep { None, Up, Down, Left, Right }

/// <summary>A held stick as single steps: a push past <see cref="Push"/> steps at once, held on it repeats after FirstMs, then every EveryMs.</summary>
public sealed class StickRepeat
{
    public const float Push = 0.6f, Release = 0.35f;
    public const long FirstMs = 400, EveryMs = 150;
    NavStep _held;
    long _next;

    public NavStep Held => _held;

    /// <summary>One tick: the stick (Y up positive) at time <paramref name="now"/> ms; the step to take now, or None.</summary>
    public NavStep Step(Vector2 stick, long now)
    {
        if (!float.IsFinite(stick.X) || !float.IsFinite(stick.Y)) stick = Vector2.Zero;
        float len = stick.Length();
        if (_held != NavStep.None && len < Release) _held = NavStep.None;   // let go (a little hysteresis: no flicker at the edge)
        if (len < Push && _held == NavStep.None) return NavStep.None;
        var dir = MathF.Abs(stick.X) > MathF.Abs(stick.Y) ? (stick.X > 0 ? NavStep.Right : NavStep.Left) : (stick.Y > 0 ? NavStep.Up : NavStep.Down);
        if (len < Push) return NavStep.None;   // held but eased off below the push: no repeat, no new step
        if (dir != _held)
        {
            _held = dir;
            _next = now + FirstMs;
            return dir;
        }
        if (now < _next) return NavStep.None;
        _next = now + EveryMs;
        return dir;
    }

    public void Reset() => _held = NavStep.None;
}

public static class MenuNav
{
    /// <summary>The menus the pad walks: the front end, the Esc menu and its save / load / file screens, message boxes.</summary>
    public static readonly HashSet<string> Screens = ["title", "new-game", "import", "menu", "save", "load", "file", "message"];

    /// <summary>The top screen is one of <see cref="Screens"/>.</summary>
    public static bool Applies(IReadOnlyList<string> open) => open.Count > 0 && Screens.Contains(open[^1]);

    /// <summary>The D-pad's step this tick (pressed only), else the stick's.</summary>
    public static NavStep Of(bool up, bool down, bool left, bool right, NavStep stick) =>
        up ? NavStep.Up : down ? NavStep.Down : left ? NavStep.Left : right ? NavStep.Right : stick;

    static readonly StickRepeat s_stick = new();
    /// <summary>The D-pad or left stick moved the focus last (A presses the focused button); the right stick hands A back to the pointer.</summary>
    static bool s_focusMode;
    /// <summary>A was the menu's this press: its release is not a click either.</summary>
    public static bool TookA { get; private set; }

    /// <summary>
    /// One pad tick in a menu (<see cref="Applies"/>); true when the pad was the menu's this tick (the caller still runs the clicks for B and
    /// the pointer, minus A while <see cref="TookA"/>).
    /// </summary>
    public static bool Tick(object player, PadState pad)
    {
        if (!s_inMenu)
        {
            s_inMenu = true;
            s_focusMode = true;   // a menu just opened: the D-pad and A are in charge until the right stick moves the pointer
            s_pointerClick = false;
        }
        if (OnScreenKeyboard.Open) return OnScreenKeyboard.Tick(player, pad);
        if (TookA && !pad.Down(PadButton.South)) TookA = false;
        // A clicked a name box with the pointer: once let go (Meitou has focused the box by then) the keyboard opens as for the D-pad
        if (s_pointerClick && !pad.Down(PadButton.South))
        {
            s_pointerClick = false;
            if (!s_editBeforeClick && MenuAccess.EditFocused(player))   // this click put the focus in the box (not a click elsewhere while it had it)
            {
                OnScreenKeyboard.Show(swallowA: false);
                Counters.Add("menu_nav.keyboard_pointer");
                return true;
            }
        }

        if (pad.Right.Length() > 0.3f) s_focusMode = false;   // the pointer is in charge again
        var step = Of(Pad.WasPressed(PadButton.DPadUp), Pad.WasPressed(PadButton.DPadDown), Pad.WasPressed(PadButton.DPadLeft),
            Pad.WasPressed(PadButton.DPadRight), s_stick.Step(pad.Left, Environment.TickCount64));
        if (step != NavStep.None)
        {
            s_focusMode = true;
            bool moved = step is NavStep.Up or NavStep.Down && MenuAccess.ListStep(player, step == NavStep.Up)
                || MenuAccess.FocusNext(player, backwards: step is NavStep.Up or NavStep.Left);
            Counters.Add(moved ? "menu_nav.step" : "menu_nav.step_nothing");
        }

        if (Pad.WasPressed(PadButton.South)) s_editBeforeClick = MenuAccess.EditFocused(player);
        if (Pad.WasPressed(PadButton.South) && !s_focusMode) s_pointerClick = true;   // a click at the pointer (PadClicks): looked at once let go
        if (Pad.WasPressed(PadButton.South) && s_focusMode)
        {
            if (MenuAccess.EditFocused(player))
            {
                OnScreenKeyboard.Show();
                TookA = true;
                Counters.Add("menu_nav.keyboard");
            }
            else if (MenuAccess.ActivateFocused(player))
            {
                TookA = true;
                Counters.Add("menu_nav.press");
            }
            else
            {
                s_pointerClick = true;
                Counters.Add("menu_nav.press_pointer");   // no focused button: A clicks at the pointer as before
            }
        }
        return true;
    }

    /// <summary>
    /// A PZA window on top (user 2026-11-21: "when you're in ANY dialogue menu, the D-Pad can be used to make a choice"; PZA's Cat Crawlers'
    /// table is a conversation drawn as a PZA window): the D-pad alone walks its buttons and lists through Meitou's focus, A presses the focused
    /// one. Unlike the menus the rest of the pad stays the world's (the left stick steers, the right stick the pointer), since PZA windows
    /// (SlowMo, the Director's Kit) may stay open while playing. True when the D-pad was the window's this tick.
    /// </summary>
    public static bool WindowTick(object player, string top, PadState pad)
    {
        if (top != s_window)
        {
            s_window = top;
            s_windowFocus = false;   // a new window: A is the pointer's until the D-pad picks something
        }
        if (TookA && !pad.Down(PadButton.South)) TookA = false;
        if (pad.Right.Length() > 0.3f) s_windowFocus = false;
        var step = Of(Pad.WasPressed(PadButton.DPadUp), Pad.WasPressed(PadButton.DPadDown), Pad.WasPressed(PadButton.DPadLeft),
            Pad.WasPressed(PadButton.DPadRight), NavStep.None);
        bool used = step != NavStep.None;
        if (used)
        {
            s_windowFocus = true;
            bool moved = step is NavStep.Up or NavStep.Down && MenuAccess.ListStep(player, step == NavStep.Up)
                || MenuAccess.FocusNext(player, backwards: step is NavStep.Up or NavStep.Left);
            Counters.Add(moved ? "menu_nav.window_step" : "menu_nav.window_step_nothing");
        }
        if (Pad.WasPressed(PadButton.South) && s_windowFocus && MenuAccess.ActivateFocused(player))
        {
            TookA = true;
            Counters.Add("menu_nav.window_press");
        }
        return used;
    }

    /// <summary>A PZA window (pza.&lt;mod&gt;) is the top screen.</summary>
    public static string? PzaTop(IReadOnlyList<string> open) => open.Count > 0 && open[^1].StartsWith("pza.", StringComparison.Ordinal) ? open[^1] : null;

    static string? s_window;
    static bool s_windowFocus;

    /// <summary>Leaving the menus: the keyboard closes, the stick forgets its hold.</summary>
    public static void Away()
    {
        if (OnScreenKeyboard.Open) OnScreenKeyboard.Hide("away");
        s_stick.Reset();
        s_focusMode = false;
        s_inMenu = false;
        s_pointerClick = false;
        TookA = false;
    }

    static bool s_inMenu, s_pointerClick, s_editBeforeClick;
}

/// <summary>Meitou's screen host's keyboard members, by name (a Meitou without one leaves that part off; counted once).</summary>
public static class MenuAccess
{
    static bool s_bound;
    static MethodInfo? s_focusNext, s_activate, s_listKey, s_textInput, s_backspace;
    static PropertyInfo? s_hasFocus;
    static FieldInfo? s_keyFocus;
    static object? s_up, s_down;
    static MethodInfo? s_controls;

    static object? Host(object player)
    {
        try { return PZA.MeitouUi.Access.FaithfulScreens(player); }
        catch (Exception) { return null; }
    }

    static void Bind(object host)
    {
        if (s_bound) return;
        s_bound = true;
        var t = host.GetType();
        s_focusNext = AccessTools.Method(t, "FocusNext", [typeof(bool)]);
        s_activate = AccessTools.Method(t, "ActivateFocused", []);
        s_textInput = AccessTools.Method(t, "TextInput", [typeof(char)]);
        s_backspace = AccessTools.Method(t, "Backspace", []);
        s_hasFocus = AccessTools.Property(t, "HasKeyboardFocus");
        s_keyFocus = AccessTools.Field(t, "_keyFocus");
        s_controls = AccessTools.Method(t, "Controls", [typeof(string)]);
        var listKey = AccessTools.TypeByName("Meitou.Game.Ui.UiListKey");
        if (listKey is not null)
        {
            s_listKey = AccessTools.Method(t, "ListKey", [listKey]);
            s_up = Enum.Parse(listKey, "Up");
            s_down = Enum.Parse(listKey, "Down");
        }
        string missing = string.Join(", ", new (string, object?)[] { ("FocusNext", s_focusNext), ("ActivateFocused", s_activate), ("ListKey", s_listKey),
            ("TextInput", s_textInput), ("Backspace", s_backspace), ("HasKeyboardFocus", s_hasFocus) }.Where(m => m.Item2 is null).Select(m => m.Item1));
        Log.Line(missing.Length == 0 ? Strings.Get("log.menu_nav_on") : Strings.Format("log.menu_nav_missing", missing));
    }

    static object? Invoke(object host, MethodInfo? m, object?[] args)
    {
        if (m is null) return null;
        try { return m.Invoke(host, args); }
        catch (Exception e)
        {
            Log.Exception("menu " + m.Name, e);
            return null;
        }
    }

    static object? Bound(object player)
    {
        if (Host(player) is not { } host) return null;
        Bind(host);
        return host;
    }

    public static bool FocusNext(object player, bool backwards) =>
        Bound(player) is { } h && Invoke(h, s_focusNext, [backwards]) is true;

    public static bool ActivateFocused(object player) => Bound(player) is { } h && Invoke(h, s_activate, []) is true;

    public static bool EditFocused(object player)
    {
        if (Bound(player) is not { } h || s_hasFocus is null) return false;
        try { return s_hasFocus.GetValue(h) is true; }
        catch (Exception) { return false; }
    }

    static FieldInfo? s_editField;
    static PropertyInfo? s_editText, s_editCaret;

    /// <summary>The focused box's text and caret (MyGuiScreenHost's edit buffer), for the keyboard's own line; null without one.</summary>
    public static (string Text, int Caret)? EditText(object player)
    {
        if (Bound(player) is not { } h) return null;
        try
        {
            s_editField ??= AccessTools.Field(h.GetType(), "_edit");
            if (s_editField?.GetValue(h) is not { } edit) return null;
            s_editText ??= AccessTools.Property(edit.GetType(), "Text");
            s_editCaret ??= AccessTools.Property(edit.GetType(), "Caret");
            return (s_editText?.GetValue(edit) as string ?? "", s_editCaret?.GetValue(edit) is int c ? c : -1);
        }
        catch (Exception) { return null; }
    }

    public static bool Type(object player, char c) => Bound(player) is { } h && Invoke(h, s_textInput, [c]) is true;

    public static bool Backspace(object player) => Bound(player) is { } h && Invoke(h, s_backspace, []) is true;

    /// <summary>
    /// Up / Down for the list that has the keyboard. A top screen whose list has not been clicked yet (the load list) gets the keyboard on
    /// its list first (the screen's last list control, as a click on it would give).
    /// </summary>
    public static bool ListStep(object player, bool up)
    {
        if (Bound(player) is not { } h || s_listKey is null) return false;
        if (Invoke(h, s_listKey, [up ? s_up : s_down]) is true) return true;
        if (!GiveListTheKeyboard(player, h)) return false;
        Counters.Add("menu_nav.list_focused");
        return Invoke(h, s_listKey, [up ? s_up : s_down]) is true;
    }

    static bool GiveListTheKeyboard(object player, object host)
    {
        if (s_keyFocus is null || s_controls is null) return false;
        var open = Access.OpenScreens(player);
        if (open.Count == 0) return false;
        string top = open[^1];
        try
        {
            if (s_controls.Invoke(host, [top]) is not System.Collections.IEnumerable controls) return false;
            string? key = null;
            foreach (var c in controls)
            {
                var props = AccessTools.Property(c.GetType(), "Properties")?.GetValue(c) as IReadOnlyDictionary<string, string>;
                if (props is not null && props.TryGetValue("ListKey", out var k) && props.ContainsKey("ScrollKey")) key = k;
            }
            if (key is null) return false;
            var ft = s_keyFocus.FieldType;   // (string Screen, string Key)?
            var inner = Nullable.GetUnderlyingType(ft) ?? ft;
            object? value = Activator.CreateInstance(inner, top, key);
            s_keyFocus.SetValue(host, value);
            return true;
        }
        catch (Exception e)
        {
            Log.Exception("menu list focus", e);
            return false;
        }
    }
}
