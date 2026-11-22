// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// The pad's mouse buttons that are down in the game (play test 2026-11-20, "LB dial got stuck"; the review after it: a pad button's release
// reached the game only when the pad's click step ran on that very tick, so a release while a dial, a menu, the free camera or WASD Control off
// skipped the step left Meitou's button down: its drag box or its steer towards the pointer went on). Pure: the offline check and the fuzzer
// drive it; Driver keeps one and lets go every tick (Driver.ReleaseLost) of each button the pad no longer holds.
// - Down: the pad sent the game this button's press. Up: the click step sends its release, once, and only for a button it pressed.
// - Lost: every button still down in the game whose pad button is up now, cleared as it is returned (each release is sent exactly once).
// - Forget: a button the game took over (Meitou's held right-click menu answers its own release: Menus), dropped without a release.
namespace PZA.WASDControl.Meitou;

public sealed class HeldClicks
{
    uint _held;

    static uint Bit(PadButton b) => (uint)b < 32 ? 1u << (int)b : 0;

    public bool Held(PadButton b) => (_held & Bit(b)) != 0;

    public void Down(PadButton b) => _held |= Bit(b);

    /// <summary>The click step's release: true (send it) only when this button's press was sent and not yet let go.</summary>
    public bool Up(PadButton b)
    {
        bool was = Held(b);
        _held &= ~Bit(b);
        return was;
    }

    public void Forget(PadButton b) => _held &= ~Bit(b);

    /// <summary>The buttons down in the game that <paramref name="downNow"/> says the pad no longer holds; they count as let go.</summary>
    public List<PadButton> Lost(Func<PadButton, bool> downNow)
    {
        var lost = new List<PadButton>();
        for (int i = 0; i < 32; i++)
        {
            var b = (PadButton)i;
            if (!Held(b) || downNow(b)) continue;
            lost.Add(b);
            _held &= ~Bit(b);
        }
        return lost;
    }

    public void Clear() => _held = 0;
}
