// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The controller keyboard (user 2026-11-20: "is there no way to do like, a keyboard pop-up when typing into the save name or whatever?").
// A on a text box in a menu (MenuNav) opens it over the screen; it types into the box Meitou has focused (MyGuiScreenHost.TextInput, the same
// call the real keyboard's characters go through), so the box, its caret and its events stay Meitou's own.
// - D-pad / left stick: move over the keys (wraps round). A: the key. X: delete (held: repeats). Y: space. LB: capitals on / off.
//   CLEAR empties the box (the save box starts with the selected save's name). B, Menu or DONE: close.
// - The layout (KeyGrid) is pure: the offline check and the fuzzer drive it. Drawn on the game's overlay (SquadWheel's helpers and colours).
using System.Numerics;
using Meitou.Game.Rendering.Overlays;

namespace PZA.WASDControl.Meitou;

public enum KeyKind { Char, Space, Delete, Caps, Done, Clear }

/// <summary>The keys and the cursor over them. Rows 0-3 are letters / digits / marks, row 4 the four wide keys.</summary>
public sealed class KeyGrid
{
    public static readonly string[] Rows = ["1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm-_.", ""];
    public static readonly KeyKind[] Bottom = [KeyKind.Caps, KeyKind.Space, KeyKind.Delete, KeyKind.Clear, KeyKind.Done];

    public int Row { get; private set; }
    public int Col { get; private set; }
    public bool Caps { get; private set; }

    public static int Width(int row) => row == 4 ? Bottom.Length : Rows[row].Length;

    public void Reset() { Row = 1; Col = 0; Caps = true; }   // starts on Q with capitals (a save name's first letter)

    /// <summary>Moves the cursor; left / right wrap in the row, up / down wrap over the rows, keeping the column in proportion.</summary>
    public void Move(NavStep step)
    {
        int rows = Rows.Length;
        switch (step)
        {
            case NavStep.Left: Col = (Col - 1 + Width(Row)) % Width(Row); break;
            case NavStep.Right: Col = (Col + 1) % Width(Row); break;
            case NavStep.Up:
            case NavStep.Down:
                int to = (Row + (step == NavStep.Up ? rows - 1 : 1)) % rows;
                Col = Math.Clamp((int)MathF.Round((Col + 0.5f) * Width(to) / Width(Row) - 0.5f), 0, Width(to) - 1);
                Row = to;
                break;
        }
    }

    public KeyKind Kind => Row == 4 ? Bottom[Col] : KeyKind.Char;

    /// <summary>The character under the cursor (capitals when on), or '\0' for a wide key.</summary>
    public char Char => Row == 4 ? '\0' : Caps ? char.ToUpperInvariant(Rows[Row][Col]) : Rows[Row][Col];

    /// <summary>The cursor's key pressed: what to type ('\0' for none) and whether the keyboard closes. Capitals go off after one letter.</summary>
    public (char Type, bool Delete, bool Close, bool Clear) Press()
    {
        switch (Kind)
        {
            case KeyKind.Space: return (' ', false, false, false);
            case KeyKind.Delete: return ('\0', true, false, false);
            case KeyKind.Clear: Caps = true; return ('\0', false, false, true);   // a fresh name starts with a capital
            case KeyKind.Caps: Caps = !Caps; return ('\0', false, false, false);
            case KeyKind.Done: return ('\0', false, true, false);
        }
        char c = Char;
        if (char.IsLetter(c)) Caps = false;
        return (c, false, false, false);
    }

    public void ToggleCaps() => Caps = !Caps;
}

public static class OnScreenKeyboard
{
    static readonly KeyGrid s_grid = new();
    static readonly StickRepeat s_stick = new();

    public static bool Open { get; private set; }
    public static KeyGrid Grid => s_grid;

    public static void Show(bool swallowA = true)
    {
        s_grid.Reset();
        s_stick.Reset();
        Open = true;
        s_fed = Environment.TickCount64;
        s_swallowA = swallowA;   // the A that opened it is not a key (still held)
        Counters.Add("keyboard.opened");
    }

    static long s_fed = long.MinValue;

    /// <summary>Each drawn frame: a keyboard the pad has not reached for DialRules.FeedMs closes (the pad unplugged, WASD Control turned
    /// off, the game left the menu without the pad noticing); a keyboard on screen that nothing can close is the stuck dial again.</summary>
    public static void CheckFed()
    {
        if (Open && DialRules.Unfed(true, Environment.TickCount64, s_fed)) Hide("unfed");
    }

    public static void Hide(string why)
    {
        Open = false;
        Counters.Add("keyboard.closed_" + why);
    }

    static bool s_swallowA;

    /// <summary>One pad tick while open: the keyboard has the whole pad.</summary>
    /// <summary>The box's text as last read (drawn on the keyboard's top line: the keyboard covers the box).</summary>
    static string s_text = "";
    static int s_caret = -1;

    public static bool Tick(object player, PadState pad)
    {
        s_fed = Environment.TickCount64;
        try
        {
            return TickInner(player, pad);
        }
        finally
        {
            if (MenuAccess.EditText(player) is { } e) (s_text, s_caret) = e;
        }
    }

    static bool TickInner(object player, PadState pad)
    {
        if (s_swallowA)
        {
            if (!pad.Down(PadButton.South)) s_swallowA = false;
        }
        if (!MenuAccess.EditFocused(player)) { Hide("box_gone"); return true; }   // the box lost the focus (the screen closed)

        var step = MenuNav.Of(Pad.WasPressed(PadButton.DPadUp), Pad.WasPressed(PadButton.DPadDown), Pad.WasPressed(PadButton.DPadLeft),
            Pad.WasPressed(PadButton.DPadRight), s_stick.Step(pad.Left, Environment.TickCount64));
        if (step != NavStep.None) s_grid.Move(step);

        if (Pad.WasPressed(PadButton.East) || Pad.WasPressed(PadButton.Menu)) { Hide("back"); return true; }
        if (Pad.WasPressed(PadButton.LeftBumper)) s_grid.ToggleCaps();
        // X deletes; held, it repeats (a long name goes quickly)
        long now = Environment.TickCount64;
        if (Pad.WasPressed(PadButton.West)) { Delete(player); s_xNext = now + StickRepeat.FirstMs; }
        else if (pad.Down(PadButton.West) && now >= s_xNext) { Delete(player); s_xNext = now + 60; }
        if (Pad.WasPressed(PadButton.North)) Type(player, ' ');
        if (Pad.WasPressed(PadButton.South) && !s_swallowA)
        {
            var (c, delete, close, clear) = s_grid.Press();
            if (close) Hide("done");
            else if (clear) Clear(player);
            else if (delete) Delete(player);
            else if (c != '\0') Type(player, c);
        }
        return true;
    }

    static void Type(object player, char c) => Counters.Add(MenuAccess.Type(player, c) ? "keyboard.typed" : "keyboard.type_refused");

    static long s_xNext;

    /// <summary>CLEAR: everything before the caret goes (the save box starts with the selected save's name).</summary>
    static void Clear(object player)
    {
        int n = MenuAccess.EditText(player) is { } e ? Math.Max(e.Caret, 0) : 0;
        for (int i = 0; i < n && i < 512; i++) MenuAccess.Backspace(player);
        Counters.Add("keyboard.cleared");
    }

    static void Delete(object player) => Counters.Add(MenuAccess.Backspace(player) ? "keyboard.deleted" : "keyboard.delete_refused");

    // ---------------------------------------------------------------- drawing (the dial's helpers and colours)
    internal static void Draw(DebugOverlay o, int w, int h)
    {
        float s = h / 1080f, key = 64 * s, gap = 8 * s;
        int cols = 10;
        float line = 48 * s;   // the box's text above the keys
        float width = cols * key + (cols - 1) * gap, height = line + gap + 5 * key + 4 * gap + 40 * s;
        float x0 = (w - width) / 2, y0 = h - height - 60 * s;
        o.Flush(w, h);
        SquadWheel.Rect(o, x0 - 16 * s, y0 - 16 * s, width + 32 * s, height + 32 * s, SquadWheel.Back);
        SquadWheel.Rect(o, x0, y0, width, line, SquadWheel.Hub);
        SquadWheel.Frame(o, x0, y0, width, line, Math.Max(1, s), SquadWheel.Rim);
        float ky = y0 + line + gap;   // the keys start under the text line
        for (int r = 0; r < KeyGrid.Rows.Length; r++)
        {
            int n = KeyGrid.Width(r);
            float kw = r == 4 ? (width - (n - 1) * gap) / n : key;
            float rowX = r == 4 ? x0 : x0 + (width - (n * key + (n - 1) * gap)) / 2;
            for (int c = 0; c < n; c++)
            {
                float x = rowX + c * (kw + gap), y = ky + r * (key + gap);
                bool lit = r == s_grid.Row && c == s_grid.Col;
                SquadWheel.Rect(o, x, y, kw, key, lit ? SquadWheel.GoldWash : SquadWheel.Hub);
                SquadWheel.Frame(o, x, y, kw, key, Math.Max(1, (lit ? 2.5f : 1f) * s), lit ? SquadWheel.Gold : SquadWheel.Seam);
            }
        }
        o.Flush(w, h);
        for (int r = 0; r < KeyGrid.Rows.Length; r++)
        {
            int n = KeyGrid.Width(r);
            float kw = r == 4 ? (width - (n - 1) * gap) / n : key;
            float rowX = r == 4 ? x0 : x0 + (width - (n * key + (n - 1) * gap)) / 2;
            for (int c = 0; c < n; c++)
            {
                float x = rowX + c * (kw + gap), y = ky + r * (key + gap);
                bool lit = r == s_grid.Row && c == s_grid.Col;
                string label = r == 4 ? Strings.Get("keyboard." + KeyGrid.Bottom[c].ToString().ToLowerInvariant())
                    : (s_grid.Caps ? char.ToUpperInvariant(KeyGrid.Rows[r][c]) : KeyGrid.Rows[r][c]).ToString();
                if (r == 4 && KeyGrid.Bottom[c] == KeyKind.Caps && s_grid.Caps) label = Strings.Get("keyboard.caps_on");
                SquadWheel.Centred(o, label, x + kw / 2, y + key * 0.3f, lit ? SquadWheel.Gold : SquadWheel.Ink, (r == 4 ? 0.95f : 1.2f) * s);
            }
        }
        SquadWheel.Centred(o, Strings.Get("keyboard.hint"), w / 2f, ky + 5 * (key + gap) + 12 * s, SquadWheel.Faint, 0.85f * s);
        string shown = s_caret >= 0 && s_caret <= s_text.Length ? s_text.Insert(s_caret, "|") : s_text + "|";
        if (shown.Length > 48) shown = "..." + shown[^45..];   // the end of a long name, where the typing is
        SquadWheel.Centred(o, shown, w / 2f, y0 + line * 0.28f, SquadWheel.Ink, 1.25f * s);
        o.Flush(w, h);
    }
}
