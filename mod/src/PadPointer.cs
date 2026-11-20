// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// A pointer for the controller in the game's windows (user, 2026-11-17: A "picks in a menu like when inventory is open with a trader/shop and
// otherwise free'd up for something"; the README's plan "a pointer for the controller in windows"). While a cursor window is open (trade,
// inventory, loot, the overview...: Look.CursorWindows) the right stick moves the mouse pointer, faster the further it is pushed, and A is the
// left button there (pick up, drop, buy, sell); ZR stays the right button and B closes, as before.
// The pointer moves where Meitou's own does: the OS cursor (the loop's first mouse, as GameLoop draws it), the input state's position and the
// interface's pointer (PlayerInterface.MouseMove, so hover and drags follow), once a frame from the GameLoop.PlaceSceneCameras prefix.
using System.Numerics;

namespace PZA.WASDControl.Meitou;

public static class PadPointer
{
    /// <summary>Pixels a second at full tilt at 1080 lines (scaled with the window height); the push is squared for fine aim.</summary>
    public const float Speed = 1400;

    static Vector2 s_stick;
    static long s_at;

    /// <summary>The right stick this pad tick, while a window has the pad (else zero).</summary>
    public static void Stick(Vector2 right) => s_stick = right;

    /// <summary>The pointer's move for <paramref name="dt"/> seconds of <paramref name="stick"/> on a window <paramref name="height"/> high (y down).</summary>
    public static Vector2 Step(Vector2 stick, float dt, float height)
    {
        float len = stick.Length();
        if (len <= 0) return Vector2.Zero;
        var dir = stick / len;
        float speed = Speed * (height / 1080f) * len * len;
        return new Vector2(dir.X, -dir.Y) * speed * dt;
    }

    /// <summary>Once a frame (GameLoop.PlaceSceneCameras prefix): moves the pointer by the stick.</summary>
    public static void Frame(object gameLoop)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        float dt = s_at == 0 ? 0 : Math.Clamp((float)System.Diagnostics.Stopwatch.GetElapsedTime(s_at, now).TotalSeconds, 0, 0.1f);
        s_at = now;
        if (s_stick == Vector2.Zero || Driver.Player is not { } player || Access.LoopWindowCentre(gameLoop) is not { } centre) return;

        var size = centre * 2;
        var at = Access.LoopPointer(gameLoop);
        if (at is not { } from) return;
        var to = Vector2.Clamp(from + Step(s_stick, dt, size.Y), Vector2.Zero, size - Vector2.One);
        Access.CentrePointer(gameLoop, to);   // the OS cursor and the loop's last position
        Access.Session(player).Input.SetMousePosition(to.X, to.Y);
        var (w, _) = Access.UiSize(player);
        Access.MovePointer(player, to * (w / size.X));   // the interface's pointer, in its pixels
        Access.SettingsPanelMove(to);   // Meitou's settings panel, when it is shown (0.10.1)
        Counters.Add("pointer.moved");
    }
}
