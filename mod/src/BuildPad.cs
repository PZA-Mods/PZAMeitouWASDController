// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The controller in build mode (0.10.1, play test 2026-11-18: "once we get into Build Mode I can't do any of the stuff I need to do to build
// (rotate / change level / etc)"). Meitou's build mode takes its controls as keys (BuildMode.BuildKey / BuildKeyUp: , . rotate, - = height,
// [ ] slope, held as long as the key is down; Backspace undo) and its window's buttons (BuildMode.BuildAction: ConfirmButton, UndoButton,
// FloorArrowUp / FloorArrowDown). While build mode is on the pad presses those same keys and buttons:
//   LB / RB held          rotate left / right          (, and .)
//   D-pad up / down held  height up / down             (= and -)
//   D-pad left / right    slope down / up              ([ and ])
//   LT + D-pad up / down  floor up / down              (the window's floor arrows; the height keys are let go meanwhile)
//   X                     undo                         (the window's Undo)
//   Y                     confirm                      (the window's Confirm: the queued buildings are planned)
//   A                     place at the pointer         (the left click, as in any window; the right stick moves the pointer)
//   B                     drop the held building       (BuildMode.BuildDiscard, the right click's "Clear Building"); none held: Esc
// The dials, the squad taps, zoom and game speed on the bumpers and the D-pad wait until build mode ends.
using Meitou.Game.Host.Input;

namespace PZA.WASDControl.Meitou;

public static class BuildPad
{
    /// <summary>The held build keys and the pad buttons that hold them.</summary>
    public static readonly (PadButton Button, Key Key)[] Holds =
    [
        (PadButton.LeftBumper, Key.Comma), (PadButton.RightBumper, Key.Period),
        (PadButton.DPadUp, Key.Equal), (PadButton.DPadDown, Key.Minus),
        (PadButton.DPadLeft, Key.LeftBracket), (PadButton.DPadRight, Key.RightBracket),
    ];

    /// <summary>The build keys the pad holds down now (what Meitou's build mode was told).</summary>
    static readonly HashSet<Key> s_held = [];

    /// <summary>Build mode is on (the pad's build rules apply this tick).</summary>
    public static bool On { get; private set; }

    /// <summary>The keys a pad reading holds: each pressed button's key; with LT held, D-pad up / down change the floor instead.</summary>
    public static HashSet<Key> Wanted(PadState pad)
    {
        var want = new HashSet<Key>();
        bool floor = pad.Down(PadButton.LeftTrigger);
        foreach (var (button, key) in Holds)
        {
            if (!pad.Down(button)) continue;
            if (floor && button is PadButton.DPadUp or PadButton.DPadDown) continue;
            want.Add(key);
        }
        return want;
    }

    /// <summary>The keys to hold this tick: the pad's while build mode is on, none once it is off (so nothing stays held).</summary>
    public static HashSet<Key> Target(bool on, PadState pad) => on ? Wanted(pad) : [];

    /// <summary>Keys to press and to let go to go from <paramref name="held"/> to <paramref name="want"/> (let go first).</summary>
    public static (Key[] Up, Key[] Down) Diff(IReadOnlySet<Key> held, IReadOnlySet<Key> want) =>
        ([.. held.Where(k => !want.Contains(k)).Order()], [.. want.Where(k => !held.Contains(k)).Order()]);

    /// <summary>
    /// The pad's part of a control tick. True when build mode is on and the pad was handled here (the caller then skips the dials, the D-pad's
    /// zoom and speed, and the views). Any held key is let go when build mode ends or the pad goes.
    /// </summary>
    public static bool Step(object player, PadState pad)
    {
        On = pad.Connected && Access.BuildModeOn(player);
        Apply(player, Target(On, pad));
        if (!On) return false;

        if (Pad.WasPressed(PadButton.West)) Button(player, "UndoButton", "pad.build_undo");
        if (Pad.WasPressed(PadButton.North)) Button(player, "ConfirmButton", "pad.build_confirm");
        if (pad.Down(PadButton.LeftTrigger) && Pad.WasPressed(PadButton.DPadUp)) Button(player, "FloorArrowUp", "pad.build_floor_up");
        if (pad.Down(PadButton.LeftTrigger) && Pad.WasPressed(PadButton.DPadDown)) Button(player, "FloorArrowDown", "pad.build_floor_down");
        return true;
    }

    /// <summary>Tells build mode the keys that changed.</summary>
    static void Apply(object player, HashSet<Key> want)
    {
        if (s_held.Count == 0 && want.Count == 0) return;

        var (up, down) = Diff(s_held, want);
        foreach (var k in up)
        {
            Access.BuildKeyUp(player, k);
            s_held.Remove(k);
        }
        foreach (var k in down)
        {
            Access.BuildKey(player, k);
            s_held.Add(k);
            Counters.Add("pad.build_key." + k.ToString().ToLowerInvariant());
        }
    }

    static void Button(object player, string widget, string counter)
    {
        Access.BuildButton(player, widget);
        Counters.Add(counter);
    }

    /// <summary>B in build mode: drops the held building (true), or false when none is held (B is then Esc, as in a window).</summary>
    public static bool Drop(object player)
    {
        if (!On || !Access.BuildHolding(player)) return false;

        Access.BuildDiscard(player);
        Counters.Add("pad.build_drop");
        return true;
    }

    /// <summary>For the offline check / fuzz: the keys the pad holds now.</summary>
    internal static IReadOnlySet<Key> Held => s_held;
}
