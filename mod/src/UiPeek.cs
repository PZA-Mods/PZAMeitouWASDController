// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// Menus while the interface is hidden (user 2026-11-20: "if we go to hide the UI we should be able to also ... still see the context menus that
// come up when we want to interact with anything ... F7 hides it all, currently"). Meitou's interface switch (F7, the screens dial's HIDE UI)
// hides every widget, the right-click menu and the windows too. While it is hidden, a context menu, a game window or a conversation opening
// shows the interface for as long as it is open; when it closes the interface hides again. A player who shows (or hides) the interface
// themselves meanwhile wins: the peek ends without touching the switch.

namespace PZA.WASDControl.Meitou;

public enum PeekAction { None, Show, Hide, Forget }

public static class UiPeek
{
    /// <summary>What to do this tick (pure: the check and the fuzzer drive it). <paramref name="hidden"/>: Meitou's switch now;
    /// <paramref name="menuOpen"/>: a context menu, a game window or a conversation is open; <paramref name="peeking"/>: this rule showed the
    /// interface for it.</summary>
    public static PeekAction Decide(bool hidden, bool menuOpen, bool peeking)
    {
        if (!peeking) return hidden && menuOpen ? PeekAction.Show : PeekAction.None;
        if (hidden) return PeekAction.Forget;   // hidden again by the player while the menu is open: theirs stands
        return menuOpen ? PeekAction.None : PeekAction.Hide;
    }

    static bool s_peeking;

    /// <summary>Once a control tick, in the world.</summary>
    public static void Tick(object player, bool menuOpen)
    {
        if (Access.InterfaceHidden is not { } hidden) return;
        switch (Decide(hidden, menuOpen, s_peeking))
        {
            case PeekAction.Show:
                if (Access.SetInterfaceHidden(false)) { s_peeking = true; Counters.Add("peek.show"); }
                break;
            case PeekAction.Hide:
                Access.SetInterfaceHidden(true);
                s_peeking = false;
                Counters.Add("peek.hide");
                break;
            case PeekAction.Forget:
                s_peeking = false;
                Counters.Add("peek.player_hid");
                break;
        }
    }
}
