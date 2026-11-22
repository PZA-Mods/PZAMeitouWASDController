// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// Meitou's game actions by NAME (play test 2026-11-20: "L3 currently is just switching between characters?"). The mod compiles against Meitou
// 0.20.1, and an enum value is a number fixed at build time: Meitou 0.22 inserted eight build-mode actions (BuildRotateLeft ... BuildApply)
// before ToggleSneak, so the 0.20.1 numbers of ToggleSneak, Rest and PutDown named BuildTiltDecrease, ToggleJobs and ToggleRanged on 0.22.
// Every action the mod presses or reads is looked up here by its name in the running game's InputAction (Enum.Parse on the runtime type).
using Meitou.Game.Host.Input;

namespace PZA.WASDControl.Meitou;

public static class Act
{
    /// <summary>The running game's value of the action called <paramref name="name"/> (nameof(InputAction.X) at the call site); a name this
    /// Meitou does not have keeps the compiled value and is counted.</summary>
    public static InputAction Of(string name, InputAction compiled)
    {
        if (Enum.TryParse(typeof(InputAction), name, false, out var v) && v is InputAction a) return a;
        Counters.Add("act.missing." + name);
        return compiled;
    }

    public static readonly InputAction MoveForward = Of(nameof(InputAction.MoveForward), InputAction.MoveForward);
    public static readonly InputAction MoveBack = Of(nameof(InputAction.MoveBack), InputAction.MoveBack);
    public static readonly InputAction MoveLeft = Of(nameof(InputAction.MoveLeft), InputAction.MoveLeft);
    public static readonly InputAction MoveRight = Of(nameof(InputAction.MoveRight), InputAction.MoveRight);
    public static readonly InputAction Pause = Of(nameof(InputAction.Pause), InputAction.Pause);
    public static readonly InputAction TimeFaster = Of(nameof(InputAction.TimeFaster), InputAction.TimeFaster);
    public static readonly InputAction TimeSlower = Of(nameof(InputAction.TimeSlower), InputAction.TimeSlower);
    public static readonly InputAction SelectAll = Of(nameof(InputAction.SelectAll), InputAction.SelectAll);
    public static readonly InputAction NextCharacter = Of(nameof(InputAction.NextCharacter), InputAction.NextCharacter);
    public static readonly InputAction PreviousCharacter = Of(nameof(InputAction.PreviousCharacter), InputAction.PreviousCharacter);
    public static readonly InputAction FocusCharacter = Of(nameof(InputAction.FocusCharacter), InputAction.FocusCharacter);
    public static readonly InputAction StopMovement = Of(nameof(InputAction.StopMovement), InputAction.StopMovement);
    public static readonly InputAction ToggleBuild = Of(nameof(InputAction.ToggleBuild), InputAction.ToggleBuild);
    public static readonly InputAction ToggleSneak = Of(nameof(InputAction.ToggleSneak), InputAction.ToggleSneak);
    public static readonly InputAction Rest = Of(nameof(InputAction.Rest), InputAction.Rest);
    public static readonly InputAction PutDown = Of(nameof(InputAction.PutDown), InputAction.PutDown);
}
