// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// Context menus in the views (the user's clip 2026-11-17 and the approved mock-up): Kenshi's menus want the right button held while the
// pointer slides to a row, but in a view the mouse turns the camera and a pad has no pointer. In a view:
// - a right click (or the pad's interact button) opens the menu at once and the release that follows does not close it;
// - while it is open mouse-look is off (Look.Wanted), so the cursor is free on it: a left click on a row picks it, a second right click closes it;
// - the pad: D-pad up / down or the right stick move the highlight (the game's own hover, by placing the interface's pointer on the row),
//   interact picks, select closes; and (user, 2026-11-17: "wish it would instantly pick your choice once releasing") a menu opened with
//   interact held picks the reached row when interact is let go, if the highlight was moved; let go without moving, the menu stays open.
// Meitou: ContextMenuWindow.OpenMenu (Title, At, Verbs), ContextAction(index) gives verb index and closes, CloseContextMenu closes, and the layout
// (AddContextMenu): width max(200, W / 8), a row of max(64, H x 0.0685) split 47.3 % title / rest per verb, placed at At + 12 and kept on screen.
using System.Numerics;
using Meitou.Game.Host.Input;

namespace PZA.WASDControl.Meitou;

public static class Menus
{
    /// <summary>The release of the press that opened a view's menu is swallowed once (the menu stays open).</summary>
    static bool s_swallowRelease;
    /// <summary>The verb the pad's D-pad has reached (0 = the first).</summary>
    static int s_padRow;
    /// <summary>The menu opened while interact (South) was held: its release picks, if the highlight moved.</summary>
    static bool s_holdPick;
    static bool s_moved;
    static bool s_wasOpen;
    /// <summary>The right stick's last step (-1 up, +1 down, 0 centred) and when it stepped (repeats while held).</summary>
    static int s_stickStep;
    static long s_stickAt;

    /// <summary>A held right stick steps again after this long (ms).</summary>
    public const long StickRepeatMs = 250;
    /// <summary>The right stick steps past this tilt (0..1).</summary>
    public const float StickStep = 0.5f;

    /// <summary>A view's menu was just opened at once (Access.OpenContextMenuNow): keep it open through the coming release; the pad starts at row 0.</summary>
    public static void Opened(object player)
    {
        s_swallowRelease = true;
        s_padRow = 0;
        s_holdPick = true;   // a mouse-opened menu never sees the pad's release; reset when the menu closes
        s_moved = false;
        s_wasOpen = true;
        HoverRow(player, 0);
        Counters.Add("menu.opened");
    }

    /// <summary>ContextMenuWindow.ContextMenuRelease prefix: true when this release is the opening press's and is swallowed.</summary>
    public static bool SwallowRelease(MouseButton button)
    {
        if (!s_swallowRelease || button != MouseButton.Right) return false;

        s_swallowRelease = false;
        Counters.Add("menu.release_kept_open");
        return true;
    }

    /// <summary>
    /// The pad while a menu is open: D-pad up / down or the right stick move, interact picks (or its release, for a menu it opened while held
    /// and a highlight that moved), select closes. True when the pad was the menu's this tick.
    /// </summary>
    public static bool Pad(object player, PadState pad)
    {
        if (!Access.ContextMenuOpen(player))
        {
            s_wasOpen = s_holdPick = s_moved = false;
            s_stickStep = 0;
            return false;
        }

        if (!s_wasOpen)
        {
            // Opened by the game itself (outside the views: Meitou's held right click), not by Opened.
            s_wasOpen = true;
            s_padRow = 0;
            s_moved = false;
            s_holdPick = pad.Down(PadButton.South);
        }

        int rows = Access.ContextMenuRows(player);
        if (Pad_(PadButton.DPadUp)) Move(player, -1, rows);
        if (Pad_(PadButton.DPadDown)) Move(player, +1, rows);
        int dir = StickDir(pad.Right.Y);
        long now = Environment.TickCount64;
        if (dir != 0 && (dir != s_stickStep || now - s_stickAt >= StickRepeatMs))
        {
            Move(player, dir, rows);
            s_stickAt = now;
        }
        s_stickStep = dir;

        if (Pad_(PadButton.East))
        {
            Access.ContextMenuClose(player);
            s_holdPick = false;
            Counters.Add("menu.pad_close");
        }
        else if (s_holdPick && global::PZA.WASDControl.Meitou.Pad.WasReleased(PadButton.South))
        {
            s_holdPick = false;
            if (s_moved)
            {
                Access.ContextMenuPick(player, s_padRow);
                Counters.Add("menu.pad_release_pick");
            }
            else Counters.Add("menu.pad_release_kept_open");
        }
        else if (Pad_(PadButton.South))
        {
            Access.ContextMenuPick(player, s_padRow);
            Counters.Add("menu.pad_pick");
        }
        return true;
    }

    /// <summary>The right stick's row step: up (positive Y) is the row above.</summary>
    public static int StickDir(float y) => y > StickStep ? -1 : y < -StickStep ? +1 : 0;

    static bool Pad_(PadButton b) => global::PZA.WASDControl.Meitou.Pad.WasPressed(b);

    static void Move(object player, int step, int rows)
    {
        if (rows <= 0) return;

        s_padRow = ((s_padRow + step) % rows + rows) % rows;
        s_moved = true;
        HoverRow(player, s_padRow);
        Counters.Add("menu.pad_move");
    }

    /// <summary>Hovers verb <paramref name="row"/> (the game's highlight follows the interface's pointer).</summary>
    static void HoverRow(object player, int row)
    {
        if (Access.ContextMenuAt(player) is not { } at) return;

        var (w, h) = Access.UiSize(player);
        Access.MovePointer(player, RowCentre(at, row, Access.ContextMenuRows(player), w, h));
    }

    /// <summary>The centre of verb <paramref name="row"/> of a menu opened at <paramref name="at"/> on a <paramref name="w"/> x <paramref name="h"/>
    /// interface, as Meitou lays it out (ContextMenuWindow.AddContextMenu).</summary>
    public static Vector2 RowCentre(Vector2 at, int row, int rows, int w, int h)
    {
        float width = MathF.Max(200, w * 0.125f), full = MathF.Max(64, h * 0.0685185f);
        float titleH = full * 0.472973f, optionH = full - titleH;
        float x = Math.Clamp(at.X + 12, 0, Math.Max(0, w - width));
        float y = Math.Clamp(at.Y, 0, Math.Max(0, h - (titleH + optionH * rows + 6)));
        return new Vector2(x + width / 2, y + titleH + optionH * (row + 0.5f));
    }
}
