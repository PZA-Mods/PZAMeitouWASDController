// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The code mod's entry (meitou.json "entry"). Patches are applied inside Initialize, before the game's JIT warm-up (MeitouClient docs/modding.md
// "Harmony"). Anything missing -> the mod stays off with a line saying why; the game goes on.
using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public sealed class WasdControlMod : IMod
{
    public void Initialize(ModContext context)
    {
        Log.Sink = context.Log;
        foreach (var line in Strings.InitLanguage(context.Folder, global::Meitou.Localisation.Loc.Language?.Code ?? "en")) Log.Line("WASD Control: " + line);
        Driver.Group = context.AddSettings(Driver.Settings);   // saved values are applied before this returns
        // Meitou keeps a value only when the Settings page changes it (before 0.22.2): the window's places, its Keys tab and every other value
        // set from code were lost at each restart. PZA's Meitou UI keeps them all in the mod's own file in its folder (as SlowMo and the Kit do).
        foreach (var line in PZA.MeitouUi.PzaSettings.Keep(Driver.Group, context.Folder)) Log.Line("WASD Control: " + line);
        Driver.On = Driver.Settings.StartOn;

        string version = typeof(WasdControlMod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];

        if (!Start(context.Harmony)) return;
        // The first-person neck limit: the body turns on the spot past 90 degrees (NeckSystem).
        context.AddSystem("neck", _ => new NeckSystem(), SystemAnchor.AfterPart(StandardParts.Movement));
        // Falling off ledges (0.10.2): after the animation part, which holds Meitou's ragdolls, so the tracker reads where the body landed.
        Log.Line(Strings.Format("log.patch", LedgePatch.Apply(context.Harmony)));   // optional: without it Ledges stays on Stop
        context.AddSystem("ledge", b => new TumbleSystem(b.Data, b.Find<global::Meitou.Simulation.Ragdolls.RagdollSystem>(),
            b.Find<global::Meitou.Simulation.Combat.CombatSystem>()),
            SystemAnchor.AfterPart(StandardParts.Animation));

        // The jump (0.11.0): the combat-move source (Meitou 0.21.0+) and the system that starts and ends each jump.
        if (JumpSystem.Register(context))
        {
            Log.Line(StaleStumblePatch.Apply(context.Harmony));   // Meitou's stale stumble (2026-11-20): optional, logged
            Log.Line(JumpOverPatch.Apply(context.Harmony));       // the jump over leftover crossbow poses and stumbles where the clip is chosen
        }
        // manual weapon switching (2026-11-20): the choice per character and the patch on Fighter.Weapon
        Log.Line(WeaponChoiceSystem.Apply(context.Harmony));
        if (WeaponChoiceSystem.Installed) context.AddSystem("weapon", _ => new WeaponChoiceSystem(), SystemAnchor.AfterPart(StandardParts.Movement));
        if (JumpSystem.Registered)
        {
            context.AddSystem("jump", b => new JumpSystem(b.Find<global::Meitou.Simulation.Combat.CombatSystem>()), SystemAnchor.AfterPart(StandardParts.Movement));
            // the planned leap's flight (prototype): at the very end, after the root motion it overrides (LeapSystem)
            context.AddSystem("leap", _ => new LeapSystem(), SystemAnchor.End);
        }

        context.AddDebugCommand(Strings.Get("debug.toggle"), _ => Driver.Toggle("F10"));
        context.AddDebugCommand(Strings.Get("debug.window"), _ => { WasdWindow.Window.Open(); return Strings.Get("debug.window"); });
        // the shoulder view from F10 (the Director's Kit session's headless runs, 2026-11-21: the agent channel has no key presses)
        context.AddDebugCommand(Strings.Get("debug.shoulder"), _ => { Views.Set(Views.Current == View.Shoulder ? View.Normal : View.Shoulder, Driver.ShowNotice); return Strings.Format("debug.shoulder_now", Views.Current); });
        // the window and the tiny button: PZA's Meitou UI (compiled in)
        PZA.MeitouUi.PzaUi.Log = s => Log.Line(s);
        PZA.MeitouUi.PzaUi.EachFrame = () => { WasdWindow.EachFrame(); SquadWheel.EachFrame(); ScreenWheel.EachFrame(); OrdersDial.EachFrame(); DialTime.EachFrame(); };
        WasdWindow.Setup();
        if (!PZA.MeitouUi.PzaUi.Install(context)) Log.Line(Strings.Get("log.ui_off"));
        Log.Line(Strings.Format("log.loaded", version));
    }

    /// <summary>Resolves the game's members and patches; false (with a line) when anything is missing. Public for the offline check.</summary>
    public static bool Start(HarmonyLib.Harmony harmony)
    {
        var missing = Access.ResolveAll();
        if (missing.Count > 0)
        {
            Log.Line(Strings.Format("log.missing", string.Join("; ", missing)));
            return false;
        }

        try
        {
            foreach (var line in Patches.Apply(harmony)) Log.Line(Strings.Format("log.patch", line));
            Log.Line(Strings.Format("log.patch", Crosshair.Apply(harmony)));   // optional: without it the arrow stays
            Log.Line(Strings.Format("log.patch", SquadWheel.Apply(harmony)));   // optional: without it the dial is not drawn
            Log.Line(Strings.Format("log.patch", PickThrough.Apply(harmony)));  // optional: your character can block the crosshair without it
            return true;
        }
        catch (Exception e)
        {
            Log.Line(Strings.Format("log.patch_failed", e.GetType().Name + ": " + e.Message));
            return false;
        }
    }
}
