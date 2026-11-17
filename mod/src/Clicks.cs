// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// Clicks in the views (user, 2026-11-17), on the world only (the interface: portraits, windows, keeps its own behaviour):
// - a left click on one of your own characters inspects it (its info in the panels) instead of selecting it, so control stays; a left click on
//   the ground keeps the selection (selecting nobody would drop control);
// - a right click on a character opens its context menu at once, with no quick order (a short right click on anyone not yours is an attack);
//   on an object it does the object's own click but never falls back to a move; on bare ground it does nothing (W A S D move in a view), and
//   with nothing exactly under the crosshair it looks a little around it first (aim help);
// - while a context menu is open the cursor is free (Look.Wanted), so its rows can be reached.
using System.Numerics;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public static class Clicks
{
    /// <summary>Pixels around the crosshair the aim help tries, nearest ring first.</summary>
    public static readonly float[] AimRings = [18, 36, 60];

    /// <summary>A left button release on the world is being handled (PlayerInterface.MouseUp).</summary>
    static bool s_leftUp;
    /// <summary>A right press in a view is being handled (PickedRightDown): its fallback move is refused.</summary>
    static bool s_rightDown;

    /// <summary>The views' click rules apply: WASD Control on and a view on.</summary>
    public static bool InView => Driver.On && Views.Current != View.Normal;

    #region Left click (PlayerInterface.MouseUp prefix / postfix, Select prefix)

    public static void LeftUpStarts(object player, int button, Vector2 p)
    {
        s_leftUp = InView && button == 0 && !Access.OverHud(player, p);
    }

    public static void LeftUpEnds() => s_leftUp = false;

    /// <summary>Select prefix: false skips the game's select (an own character is inspected instead; a ground click keeps the selection).</summary>
    public static bool AllowSelect(object player, IReadOnlyList<CharacterId> ids)
    {
        if (!s_leftUp) return true;

        if (ids.Count == 0)
        {
            Counters.Add("click.left_ground_kept");
            return false;
        }

        if (ids.Count != 1) return true;

        var snapshot = Access.Session(player).CurrentSnapshot;
        if (snapshot.Characters.FirstOrDefault(c => c.Id == ids[0]) is not { IsPlayer: true } own) return true;

        Access.Inspect(player, own.Id);
        Counters.Add("click.left_inspect_own");
        return false;
    }

    #endregion

    #region Right click (PlayerInterface.PickedRightDown prefix / postfix, Move prefix)

    /// <summary>PickedRightDown prefix: true runs the game's click (at <paramref name="p"/>, possibly moved by the aim help).</summary>
    public static bool RightDown(object player, ref Vector2 p)
    {
        if (!InView) return true;

        string kind = Access.PickKind(player, p);
        if (kind is "None" or "Site")
        {
            if (AimHelp(player, p) is not { } near)
            {
                Counters.Add("click.right_ground_ignored");
                return false;
            }

            (p, kind) = near;
            Counters.Add("click.right_aim_help");
        }

        if (kind == "Character")
        {
            if (Access.OpenContextMenuNow(player, p)) Menus.Opened(player);
            Counters.Add("click.right_menu");
            return false;
        }

        s_rightDown = true;
        Counters.Add("click.right_object");
        return true;
    }

    /// <summary>PickedRightDown postfix: no steering after a view's right click.</summary>
    public static void RightDownEnds(object player)
    {
        if (!s_rightDown) return;

        s_rightDown = false;
        Access.StopSteering(player);
    }

    /// <summary>Move prefix: false refuses the move a view's right click falls back to.</summary>
    public static bool AllowMove()
    {
        if (!s_rightDown) return true;

        Counters.Add("click.right_move_refused");
        return false;
    }

    /// <summary>The nearest point around the crosshair with something on it (not ground, not a build site), and what it is.</summary>
    static (Vector2 P, string Kind)? AimHelp(object player, Vector2 p)
    {
        foreach (float r in AimRings)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.PI / 4;
                var q = p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                string kind = Access.PickKind(player, q);
                if (kind is not ("None" or "Site")) return (q, kind);
            }
        }
        return null;
    }

    #endregion
}
