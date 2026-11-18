// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The Orders dial (0.10), on the dial engine (Dials.cs). Hold LB + RB together (user 2026-11-18, play test: holding A kept the right thumb off
// the stick that aims; it was "Hold A: Orders dial" before), outside windows and menus; the D-pad left / right turns the page while it is open.
// A is now only a tap: the safe extra option for what the crosshair is on.
// - Stances page: Kenshi's eight character orders (CharacterOrders), each shown on / off from the controlled character's orders and flipped for
//   everyone selected through the interface's own action "order.<Name>" (the orders panel's buttons; Meitou's OrdersCommand). Lighting each
//   stance from the character's own orders is an idea from BeatTheCount's Gamepad mod (their LT stance layer); our code is our own.
// - Tasks page: Stop, Medic, Rescue, Prospect, Put down, Clear jobs, Rest, Select all. Put down and Clear jobs moved here from the screens
//   dial (user 2026-11-18) and stay in place, dimmed, when they don't apply.
using Meitou.Game.Host.Input;
using Meitou.Simulation;
using Meitou.Simulation.Player;

namespace PZA.WASDControl.Meitou;

public static class OrdersDial
{
    /// <summary>The stances clockwise from the top (the plan's mock-up).</summary>
    public static readonly CharacterOrders[] Stances =
        [CharacterOrders.Hold, CharacterOrders.Block, CharacterOrders.Passive, CharacterOrders.Ranged, CharacterOrders.Jobs, CharacterOrders.Taunt,
         CharacterOrders.Sneak, CharacterOrders.Chase];

    public static readonly Slice[] StancePage = [.. Stances.Select(Stance)];

    public static readonly Slice[] TaskPage =
    [
        new("task.stop", p => Access.UiAction(p, "stop")),
        new("task.medic", p => Access.UiAction(p, "medic")),
        new("task.rescue", p => Access.UiAction(p, "rescue")),
        new("task.prospect", p => Access.UiAction(p, "prospect"),
            Expect: p => { bool was = Access.OpenScreens(p).Contains("prospect"); return q => Access.OpenScreens(q).Contains("prospect") != was; }, World: false),
        new("screen.put_down", _ => Driver.Press(InputAction.PutDown), Available: p => Me(p) is { } me && !me.Carrying.IsNone,
            Expect: _ => p => Me(p) is not { } me || me.Carrying.IsNone),
        new("screen.clear_jobs", ClearJobs, Available: p => Me(p) is { } me && me.Jobs.Count > 0,
            Expect: _ => p => Me(p) is not { } me || me.Jobs.Count == 0),
        new("task.rest", _ => Driver.Press(InputAction.Rest)),
        new("task.select_all", _ => Driver.Press(InputAction.SelectAll)),
    ];

    public static readonly Dial Dial = new("orders", [PadButton.LeftBumper, PadButton.RightBumper], "orders.title", [StancePage, TaskPage], ["orders.stances", "orders.tasks"]);

    /// <summary>LB + RB pressed together (BumperChord).</summary>
    public static readonly BumperChord Chord = new();

    public static bool IsOpen => Dial.IsOpen;

    /// <summary>The pad's LB + RB chord this tick (the caller only asks while the chord is on).</summary>
    public static WheelResult Pad(object player, PadState pad, bool mayOpen) => Dial.Pad(player, pad, mayOpen);

    public static void EachFrame() => Dial.EachFrame();

    static Slice Stance(CharacterOrders o) => new("stance." + o.ToString().ToLowerInvariant(), p => Access.UiAction(p, "order." + o),
        State: p => Me(p) is { } me ? me.Orders.HasFlag(o) : null,
        Expect: p => Me(p) is { } me ? Flipped(me.Id, o, me.Orders.HasFlag(o)) : null);

    static Func<object, bool> Flipped(CharacterId id, CharacterOrders o, bool was) =>
        p => Access.Session(p).CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is { } c && c.Orders.HasFlag(o) != was;

    /// <summary>The controlled character now (null without one).</summary>
    public static CharacterSnapshot? Me(object player) =>
        Driver.ControlledId is { } id ? Access.Session(player).CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) : null;

    /// <summary>The RemoveJob commands that clear a list of <paramref name="count"/> jobs: the last index first (0.9.24).</summary>
    public static IEnumerable<global::Meitou.Simulation.Production.RemoveJob> ClearCommands(CharacterId who, int count, long tick)
    {
        for (int i = count - 1; i >= 0; i--) yield return new(who, i) { Tick = tick };
    }

    static void ClearJobs(object player)
    {
        if (Me(player) is not { } me) return;
        var world = Access.Session(player).World;
        int n = 0;
        foreach (var c in ClearCommands(me.Id, me.Jobs.Count, world.Tick)) { world.Commands.Enqueue(c); n++; }
        Log.Line(Strings.Format("log.jobs_cleared", n));
    }
}
