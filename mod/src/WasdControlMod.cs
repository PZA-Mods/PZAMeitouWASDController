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
        Driver.Group = context.AddSettings(Driver.Settings);   // saved values are applied before this returns
        Driver.On = Driver.Settings.StartOn;

        string version = typeof(WasdControlMod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];

        if (!Start(context.Harmony)) return;
        // The first-person neck limit: the body turns on the spot past 90 degrees (NeckSystem).
        context.AddSystem("neck", _ => new NeckSystem(), SystemAnchor.AfterPart(StandardParts.Movement));

        context.AddDebugCommand(Strings.Get("debug.toggle"), _ => Driver.Toggle("F10"));
        context.AddDebugCommand(Strings.Get("debug.window"), _ => { WasdWindow.Window.Open(); return Strings.Get("debug.window"); });
        // the window and the tiny button: PZA's Meitou UI (compiled in)
        PZA.MeitouUi.PzaUi.Log = s => Log.Line(s);
        PZA.MeitouUi.PzaUi.EachFrame = () => { WasdWindow.EachFrame(); SquadWheel.EachFrame(); ScreenWheel.EachFrame(); };
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
