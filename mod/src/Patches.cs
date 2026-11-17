// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The three Harmony patches (docs/design.md). Each body is wrapped: a throw is logged (at most once a second) and never reaches the game.
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;

namespace PZA.WASDControl.Meitou;

public static class Patches
{
    /// <summary>Applies all eleven or none; returns one line per patch.</summary>
    public static List<string> Apply(Harmony h)
    {
        var lines = new List<string>();
        try
        {
            h.Patch(Access.Steer, prefix: Hook(nameof(SteerPrefix)));
            lines.Add("patched PlayerInterface.Steer prefix");
            h.Patch(Access.Resolve, postfix: Hook(nameof(ResolvePostfix)));
            lines.Add("patched InputBindings.Resolve postfix");
            h.Patch(Access.SessionTick, postfix: Hook(nameof(TickPostfix)));
            lines.Add("patched WorldSession.Tick postfix");
            h.Patch(Access.FollowCamera, postfix: Hook(nameof(FollowCameraPostfix)));
            lines.Add("patched SandboxScenario.FollowCamera postfix");
            h.Patch(Access.MouseLook, postfix: Hook(nameof(MouseLookPostfix)));
            lines.Add("patched GameHost.MouseLook postfix");
            h.Patch(Access.PlaceSceneCameras, prefix: Hook(nameof(PlaceSceneCamerasPrefix)));
            lines.Add("patched GameLoop.PlaceSceneCameras prefix");
            h.Patch(Access.MouseUp, prefix: Hook(nameof(MouseUpPrefix)), postfix: Hook(nameof(MouseUpPostfix)));
            lines.Add("patched PlayerInterface.MouseUp prefix postfix");
            h.Patch(Access.SelectMethod, prefix: Hook(nameof(SelectPrefix)));
            lines.Add("patched PlayerInterface.Select prefix");
            h.Patch(Access.PickedRightDown, prefix: Hook(nameof(RightDownPrefix)), postfix: Hook(nameof(RightDownPostfix)));
            lines.Add("patched PlayerInterface.PickedRightDown prefix postfix");
            h.Patch(Access.MoveMethod, prefix: Hook(nameof(MovePrefix)));
            lines.Add("patched PlayerInterface.Move prefix");
            h.Patch(Access.ContextMenuRelease, prefix: Hook(nameof(MenuReleasePrefix)));
            lines.Add("patched ContextMenuWindow.ContextMenuRelease prefix");
        }
        catch
        {
            h.UnpatchAll(h.Id);
            throw;
        }
        return lines;
    }

    static HarmonyMethod Hook(string name) => new(AccessTools.Method(typeof(Patches), name));

    static void SteerPrefix(object __instance)
    {
        try
        {
            Driver.Player = __instance;
            Log.Tick();
        }
        catch (Exception e)
        {
            Log.Exception("Steer prefix", e);
        }
    }

    static void ResolvePostfix(TickInput input, ActionState __result)
    {
        try
        {
            Driver.OnResolve(input, __result);
        }
        catch (Exception e)
        {
            Log.Exception("Resolve postfix", e);
        }
    }

    static void FollowCameraPostfix(object __instance)
    {
        try
        {
            if (Access.ScenarioHost(__instance) is { } host) Driver.OnFrame(host);
        }
        catch (Exception e)
        {
            Log.Exception("FollowCamera postfix", e);
        }
    }

    static void MouseLookPostfix(ref bool __result)
    {
        try
        {
            __result = Look.Answer(__result);
        }
        catch (Exception e)
        {
            Log.Exception("MouseLook postfix", e);
        }
    }

    static void PlaceSceneCamerasPrefix(object __instance)
    {
        try
        {
            Look.Frame(__instance);
        }
        catch (Exception e)
        {
            Log.Exception("PlaceSceneCameras prefix", e);
        }
    }

    static void MouseUpPrefix(object __instance, global::Meitou.Game.Host.Input.MouseButton button, System.Numerics.Vector2 p)
    {
        try
        {
            Clicks.LeftUpStarts(__instance, (int)button, p);
        }
        catch (Exception e)
        {
            Log.Exception("MouseUp prefix", e);
        }
    }

    static void MouseUpPostfix() => Clicks.LeftUpEnds();

    static bool SelectPrefix(object __instance, IReadOnlyList<global::Meitou.Simulation.CharacterId> ids)
    {
        try
        {
            return Clicks.AllowSelect(__instance, ids);
        }
        catch (Exception e)
        {
            Log.Exception("Select prefix", e);
            return true;
        }
    }

    static bool RightDownPrefix(object __instance, ref System.Numerics.Vector2 p)
    {
        try
        {
            return Clicks.RightDown(__instance, ref p);
        }
        catch (Exception e)
        {
            Log.Exception("PickedRightDown prefix", e);
            return true;
        }
    }

    static void RightDownPostfix(object __instance)
    {
        try
        {
            Clicks.RightDownEnds(__instance);
        }
        catch (Exception e)
        {
            Log.Exception("PickedRightDown postfix", e);
        }
    }

    static bool MovePrefix() => Clicks.AllowMove();

    /// <summary>The release of the press that opened a view's menu is the menu's (true) and the menu stays open.</summary>
    static bool MenuReleasePrefix(global::Meitou.Game.Host.Input.MouseButton button, ref bool __result)
    {
        try
        {
            if (!Menus.SwallowRelease(button)) return true;

            __result = true;
            return false;
        }
        catch (Exception e)
        {
            Log.Exception("ContextMenuRelease prefix", e);
            return true;
        }
    }

    static void TickPostfix(WorldSession __instance)
    {
        try
        {
            Driver.OnTick(__instance);
        }
        catch (Exception e)
        {
            Log.Exception("Tick postfix", e);
        }
    }
}
