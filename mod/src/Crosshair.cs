// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The views' crosshair (user, 2026-11-17: the native arrow as the crosshair, "which I HATE"): while mouse-look is on, the plain arrow pointer
// is drawn as a small cross at the pinned point instead. The game's action pointers (talk, loot, attack, door, ...) still show there, since
// they say what a click does. A prefix on UiPainter.DrawPointer (the software pointer, drawn last in the frame, GameLoop.DrawPointer); with
// Windows' hardware cursor Meitou draws no software pointer and this does nothing.
using System.Numerics;
using System.Reflection;
using HarmonyLib;

namespace PZA.WASDControl.Meitou;

public static class Crosshair
{
    /// <summary>Kenshi's main ink (#afa68b) over a dark edge, as the game's own text.</summary>
    static readonly Vector4 Ink = new(175 / 255f, 166 / 255f, 139 / 255f, 0.95f), Edge = new(0.06f, 0.03f, 0.02f, 0.85f);

    static FieldInfo? s_overlay;
    static MethodInfo? s_rect, s_flush;

    /// <summary>Patches UiPainter.DrawPointer when every member is there; a line saying what happened. Optional: without it the arrow stays.</summary>
    public static string Apply(Harmony h)
    {
        var painter = AccessTools.TypeByName("Meitou.Game.Rendering.Ui.UiPainter");
        var draw = painter is null ? null : AccessTools.Method(painter, "DrawPointer");
        s_overlay = painter is null ? null : AccessTools.Field(painter, "_overlay");
        var overlayType = s_overlay?.FieldType;
        s_rect = overlayType is null ? null : AccessTools.Method(overlayType, "Rect", [typeof(float), typeof(float), typeof(float), typeof(float), typeof(Vector4)]);
        s_flush = overlayType?.GetMethods().FirstOrDefault(m => m.Name == "Flush" && m.GetParameters().Length == 4);
        if (draw is null || s_overlay is null || s_rect is null || s_flush is null) return "crosshair: UiPainter members missing, the arrow stays";

        h.Patch(draw, prefix: new HarmonyMethod(AccessTools.Method(typeof(Crosshair), nameof(DrawPointerPrefix))));
        return "patched UiPainter.DrawPointer prefix (crosshair)";
    }

    static bool DrawPointerPrefix(object __instance, string name, Vector2 mouse, int width, int height, object? pass)
    {
        try
        {
            if (!Look.On || name != "arrow" || s_overlay?.GetValue(__instance) is not { } overlay) return true;

            float s = Math.Max(1, height / 1080f);
            var rects = Arms(mouse, s);
            s_flush!.Invoke(overlay, [width, height, null, pass]);
            foreach (var r in rects) Rect(overlay, Grow(r, s), Edge);
            foreach (var r in rects) Rect(overlay, r, Ink);
            s_flush.Invoke(overlay, [width, height, null, pass]);
            Counters.Add("look.crosshair");
            return false;
        }
        catch (Exception e)
        {
            Log.Exception("crosshair", e);
            return true;
        }
    }

    /// <summary>The four arms (x0, y0, x1, y1) round <paramref name="c"/> at scale <paramref name="s"/>: a 3 px gap, 7 px long, 2 px thick at 1080p.</summary>
    public static Vector4[] Arms(Vector2 c, float s)
    {
        float gap = 3 * s, len = 7 * s, half = s;
        float x = MathF.Round(c.X), y = MathF.Round(c.Y);
        return
        [
            new(x - gap - len, y - half, x - gap, y + half),
            new(x + gap, y - half, x + gap + len, y + half),
            new(x - half, y - gap - len, x + half, y - gap),
            new(x - half, y + gap, x + half, y + gap + len),
        ];
    }

    static Vector4 Grow(Vector4 r, float by) => new(r.X - by, r.Y - by, r.Z + by, r.W + by);

    static void Rect(object overlay, Vector4 r, Vector4 colour) => s_rect!.Invoke(overlay, [r.X, r.Y, r.Z, r.W, colour]);
}
