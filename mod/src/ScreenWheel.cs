// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The screens dial (user, 2026-11-17: "another radial menu (LB?) that opens up some other menus ie the SQD, TEC, MAP"), on the dial engine
// since 0.10 (Dials.cs):
// - LB tapped: the previous squad member, as before (the game's own PreviousCharacter key);
// - LB held: the eight screens, always in the same place (0.10, user 2026-11-18: PUT DOWN and CLEAR JOBS moved to the Orders dial's Tasks
//   page, so nothing here comes and goes): Map up, then clockwise Inventory, Character, Squads, Build, Crafting, Tech, Faction.
// Each opens the way its own HUD button or key does: inventory / character through PlayerInterface.ExecuteUiAction "inventory" / "stats" (the
// I / C keys' call), map / faction / tech / crafting / squads as the overview's tabs 0 / 1 / 2 / 3 / 4, build with the game's Build key. A
// screen that is open closes, as its button does. Every pick is checked (the screen's open state changed, or the tab is showing).
using HarmonyLib;
using Meitou.Game.Host.Input;

namespace PZA.WASDControl.Meitou;

public static class ScreenWheel
{
    /// <summary>A screen: its caption key, and how it opens: an interface action, an overview tab, or a game key.</summary>
    public sealed record Entry(string Key, string? UiAction = null, int Tab = -1, InputAction? Action = null);

    /// <summary>The eight screens, clockwise from the top (fixed: rule 2 of the dial plan).</summary>
    public static readonly Entry[] Entries =
    [
        new("screen.map", Tab: 0),
        // ExecuteUiAction takes "inventory" / "stats" (the I / C keys' own call); "meitou:..." is only the HUD buttons' widget action (0.9.22).
        new("screen.inventory", UiAction: "inventory"),
        new("screen.character", UiAction: "stats"),
        new("screen.squads", Tab: 4),
        new("screen.build", Action: InputAction.ToggleBuild),
        new("screen.crafting", Tab: 3),
        new("screen.tech", Tab: 2),
        new("screen.faction", Tab: 1),
    ];

    public static readonly Dial Dial = new("screens", [PadButton.LeftBumper], "screens.title", [[.. Entries.Select(Make)]]);

    public static bool IsOpen => Dial.IsOpen;

    static Slice Make(Entry e) => new(e.Key, p => Open(p, e), Expect: p => ExpectFor(p, e), World: e.Action is not null);

    /// <summary>The pad's LB this tick (Tap: the caller presses PreviousCharacter).</summary>
    public static WheelResult Pad(object player, PadState pad) => Dial.Pad(player, pad);

    public static void EachFrame() => Dial.EachFrame();

    // ---------------------------------------------------------------- opening a screen, as its HUD button does
    static System.Reflection.MethodInfo? s_updatePause;

    static void Open(object player, Entry e)
    {
        if (e.UiAction is { } ui) Access.UiAction(player, ui);
        else if (e.Tab >= 0)
        {
            var host = PZA.MeitouUi.Access.FaithfulScreens(player);
            if (OverviewOn(player, e.Tab)) host.Close("overview");
            else
            {
                host.Open("overview");
                host.SetTab("overview", "TabsMain", e.Tab);
            }
            s_updatePause ??= AccessTools.Method(Access.PlayerInterface, "UpdateMenuPause");
            s_updatePause?.Invoke(player, null);
        }
        else if (e.Action is { } a) Driver.Press(a);
    }

    static bool OverviewOn(object player, int tab)
    {
        var host = PZA.MeitouUi.Access.FaithfulScreens(player);
        return host.IsOpen("overview") && host.GetTab("overview", "TabsMain") == tab;
    }

    /// <summary>What a pick must change: a tab is showing (or gone, if it was), a window's open state flipped; the build key is unchecked.</summary>
    static Func<object, bool>? ExpectFor(object player, Entry e)
    {
        if (e.Tab >= 0)
        {
            bool was = OverviewOn(player, e.Tab);
            return p => OverviewOn(p, e.Tab) != was;
        }
        if (e.UiAction is { } ui)
        {
            bool was = Access.OpenScreens(player).Contains(ui);
            return p => Access.OpenScreens(p).Contains(ui) != was;
        }
        return null;
    }
}
