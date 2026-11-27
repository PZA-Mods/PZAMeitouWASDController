// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The Settings > Mods group (MeitouClient docs/modding.md "Settings"). The page sets these on the interface thread, the same thread the patches
// run on (the host's), outside the tick.
using Meitou.Modding;

namespace PZA.WASDControl.Meitou;

public sealed class DcSettings
{
    public const float MinSteer = 15, MaxSteer = 120;

    /// <summary>WASD Control is on when a game starts (V flips it any time).</summary>
    [Setting(Strings.En.StartOnLabel, Hint = Strings.En.StartOnHint)]
    public bool StartOn { get; set; }

    /// <summary>How far ahead each move order points, world units (10 is about a metre; Meitou's arrive radius is 10).</summary>
    [Setting(Strings.En.SteerLabel, Hint = Strings.En.SteerHint, Min = MinSteer, Max = MaxSteer, Step = 5)]
    public float SteerDistance { get; set; } = 40;

    /// <summary>Letting go of W A S D stops the character at once (else it walks on to the last point).</summary>
    [Setting(Strings.En.StopLabel, Hint = Strings.En.StopHint)]
    public bool StopOnRelease { get; set; } = true;

    /// <summary>The controller's left stick: a light push walks, a full push uses the chosen run speed (StickSpeed).</summary>
    [Setting(Strings.En.StickWalkLabel, Hint = Strings.En.StickWalkHint)]
    public bool StickWalk { get; set; } = true;

    /// <summary>The camera follows the controlled character while WASD Control is on.</summary>
    [Setting(Strings.En.FollowLabel, Hint = Strings.En.FollowHint)]
    public bool CameraFollows { get; set; } = true;


    /// <summary>Time while a controller dial is open (DialTime): Slow through PZA's SlowMo (pauses without it), Pause, or Normal.</summary>
    [Setting(Strings.En.DialTimeLabel, Hint = Strings.En.DialTimeHint, Options = ["Slow", "Pause", "Normal"])]
    public string DialTime { get; set; } = "Slow";

    /// <summary>At a drop's lip a step stops 75 cm short (Stop), or aims at the lip and a push outward tumbles the character over it (Fall:
    /// LedgeRules, TumbleSystem; legs hurt past 3 m). 0.11.0; OFF (Stop) by default.</summary>
    [Setting(Strings.En.LedgesLabel, Hint = Strings.En.LedgesHint, Options = ["Stop", "Fall"])]
    public string Ledges { get; set; } = "Stop";

    /// <summary>The jump (Shift+U, the pad's A with nothing under the crosshair; JumpSystem): On, or Off (no key, A gives the safe action
    /// only, the clips never play). User 2026-11-20: "both Jump stuff AND the Ledge thing should be toggleable".</summary>
    [Setting(Strings.En.JumpLabel, Hint = Strings.En.JumpHint, Options = ["On", "Off"])]
    public string Jump { get; set; } = "On";

    /// <summary>The prompt strip at the top of the screen while the controller is in use (Prompts).</summary>
    [Setting(Strings.En.PromptsLabel, Hint = Strings.En.PromptsHint)]
    public bool ShowPrompts { get; set; } = true;

    /// <summary>Manual Combat (third-party mod, when installed): on or off, persisted across restarts (ManualCombatLink).</summary>
    public bool ManualCombatFound => ManualCombatLink.Available;
    [Setting(Strings.En.ManualCombatLabel, Hint = Strings.En.ManualCombatHint, VisibleWhen = nameof(ManualCombatFound))]
    public bool ManualCombat { get; set; } = true;

    /// <summary>The keys (the window's Keys tab changes them, refusing keys the game's own controls use). Letters only.</summary>
    public const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [Setting(Strings.En.KeyToggleLabel, Hint = Strings.En.KeyHint, Options = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"], SubGroup = Strings.En.KeysGroup)]
    public string KeyToggle { get; set; } = "V";
    [Setting(Strings.En.KeyShoulderLabel, Hint = Strings.En.KeyHint, Options = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"], SubGroup = Strings.En.KeysGroup)]
    public string KeyShoulder { get; set; } = "Z";
    [Setting(Strings.En.KeyFirstLabel, Hint = Strings.En.KeyHint, Options = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"], SubGroup = Strings.En.KeysGroup)]
    public string KeyFirst { get; set; } = "N";
    [Setting(Strings.En.KeyTakeLabel, Hint = Strings.En.KeyHint, Options = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"], SubGroup = Strings.En.KeysGroup)]
    public string KeyTake { get; set; } = "G";

    public const double MinLook = 0.1, MaxLook = 2, LookStep = 0.05;
    public const double MinFov = ViewLens.MinDeg, MaxFov = ViewLens.MaxDeg, FovStep = 1;
    public const double MinShoulderHeight = 10, MaxShoulderHeight = 28, ShoulderHeightStep = 1;

    /// <summary>How fast the mouse and the right stick turn the views, a factor (1 = the old Normal; the old Slow was 0.6, Fast 1.6). 0.10.1:
    /// a slider (Discord 2026-11-18: "even on slow its too fast"), so it goes down to 0.1.</summary>
    [Setting(Strings.En.LookSpeedLabel, Hint = Strings.En.LookSpeedHint, Min = MinLook, Max = MaxLook, Step = LookStep, SubGroup = Strings.En.ViewsGroup)]
    public double LookScale { get; set; } = 1;
    /// <summary>The over-the-shoulder view's field of view, degrees (Meitou's lens is 50). 0.10.1, the same request: a wider third-person view.</summary>
    [Setting(Strings.En.FovLabel, Hint = Strings.En.FovHint, Min = MinFov, Max = MaxFov, Step = FovStep, SubGroup = Strings.En.ViewsGroup)]
    public double ShoulderFov { get; set; } = ViewLens.DefaultDeg;
    /// <summary>The first-person view's field of view, degrees (fp-fov, user 2026-11-20 after the Action RPG study: its first person uses 90).
    /// 50 is the game's lens, so nothing changes until it is moved.</summary>
    [Setting(Strings.En.FirstFovLabel, Hint = Strings.En.FirstFovHint, Min = MinFov, Max = MaxFov, Step = FovStep, SubGroup = Strings.En.ViewsGroup)]
    public double FirstFov { get; set; } = ViewLens.DefaultDeg;
    /// <summary>Vertical height of the over-the-shoulder camera above the character's feet, world units (default 15 = ~1.5 m).</summary>
    [Setting(Strings.En.ShoulderHeightLabel, Hint = Strings.En.ShoulderHeightHint, Min = MinShoulderHeight, Max = MaxShoulderHeight, Step = ShoulderHeightStep, SubGroup = Strings.En.ViewsGroup)]
    public double ShoulderHeight { get; set; } = Views.ShoulderHeight;
    /// <summary>Mouse / stick forward looks down (flight style).</summary>
    [Setting(Strings.En.InvertLabel, Hint = Strings.En.InvertHint, SubGroup = Strings.En.ViewsGroup)]
    public bool InvertLook { get; set; }
    /// <summary>Which shoulder the over-the-shoulder camera looks over.</summary>
    [Setting(Strings.En.SideLabel, Hint = Strings.En.SideHint, Options = ["Right", "Left"], SubGroup = Strings.En.ViewsGroup)]
    public string ShoulderSide { get; set; } = "Right";

    /// <summary>The window and its tiny button (PZA's Meitou UI). Places are fractions of the screen; the window's is its offset from its first place.</summary>
    [Setting(Strings.En.WindowLabel, Hint = Strings.En.WindowHint, SubGroup = Strings.En.WindowGroup)]
    public bool ShowWindow { get; set; } = true;
    [Setting(Strings.En.TinyLabel, Hint = Strings.En.TinyHint, VisibleWhen = nameof(ShowWindow), SubGroup = Strings.En.WindowGroup)]
    public bool Tiny { get; set; } = true;
    [Setting(Strings.En.PlaceLabel, Hint = Strings.En.PlaceHint, Min = -1, Max = 1, Step = 0.001, VisibleWhen = nameof(ShowWindow), SubGroup = Strings.En.WindowGroup)]
    public double WindowPlaceX { get; set; }
    [Setting(Strings.En.PlaceLabel, Hint = Strings.En.PlaceHint, Min = -1, Max = 1, Step = 0.001, VisibleWhen = nameof(ShowWindow), SubGroup = Strings.En.WindowGroup)]
    public double WindowPlaceY { get; set; }
    [Setting(Strings.En.TinyPlaceLabel, Hint = Strings.En.PlaceHint, Min = 0, Max = 1, Step = 0.001, VisibleWhen = nameof(ShowWindow), SubGroup = Strings.En.WindowGroup)]
    public double TinyPlaceX { get; set; } = 1;
    [Setting(Strings.En.TinyPlaceLabel, Hint = Strings.En.PlaceHint, Min = 0, Max = 1, Step = 0.001, VisibleWhen = nameof(ShowWindow), SubGroup = Strings.En.WindowGroup)]
    public double TinyPlaceY { get; set; } = 0.10;
    [Setting(Strings.En.OpacityLabel, Hint = Strings.En.OpacityHint, Min = 0.35, Max = 1, Step = 0.05, VisibleWhen = nameof(ShowWindow), SubGroup = Strings.En.WindowGroup)]
    public double TinyOpacity { get; set; } = 1;
}
