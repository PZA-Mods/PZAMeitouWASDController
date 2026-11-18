// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// A (ZR until 0.9.18): attack what the crosshair is on (user, 2026-11-17: "Maybe Z3 can function to just... attack what you're targeting (not manual attack
// from Direct Control, but a start ...) Could probably be used for other stuff, with context... like holding"; then "Yes, ZR").
// The target is the person under the crosshair in a view (the screen's centre), else under the pointer; a near miss looks around it for a
// person (Clicks.PersonNear). The order is the right-click menu's own verb for that target (ContextMenuWindow.Verbs), so Meitou's rules
// and consequences are the game's:
// - tap: "Attack Target" (offered for a hostile one); for anyone else nothing, with a notice that holding attacks anyway;
// - hold (HoldMs): the deliberate one: "Stealth KO" when it is offered (from behind, with its chance), else "Attack Unprovoked" (a crime
//   for a peaceful target, as in the game), else "Attack Target".
// ZL + ZR stays the stop chord (Driver.PadTriggers): ZR pressed while ZL is down never attacks.
using System.Numerics;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public static class CrosshairAttack
{
    /// <summary>The attack button: A (the bottom face button) since 0.9.19 (ZR before; ZR is interact now).</summary>
    public const PadButton Button = PadButton.South;

    /// <summary>The attack button held this long gives the hold's verb (ms).</summary>
    public const long HoldMs = 350;

    static long s_since = -1;
    static bool s_held;

    /// <summary>The verb a tap gives, from the target's menu captions, or null.</summary>
    public static string? TapVerb(IEnumerable<string> captions) => captions.FirstOrDefault(c => c == "Attack Target");

    /// <summary>The verb a hold gives: Stealth KO (its caption carries the chance), else Attack Unprovoked, else Attack Target; or null.</summary>
    public static string? HoldVerb(IReadOnlyCollection<string> captions) =>
        captions.FirstOrDefault(c => c.StartsWith("Stealth KO", StringComparison.Ordinal))
        ?? captions.FirstOrDefault(c => c == "Attack Unprovoked")
        ?? captions.FirstOrDefault(c => c == "Attack Target");

    /// <summary>Each pad tick. <paramref name="zl"/>: the left trigger is down (ZR then belongs to the stop chord).</summary>
    public static void Pad(object player, PadState pad, bool zl)
    {
        long now = Environment.TickCount64;
        if (global::PZA.WASDControl.Meitou.Pad.WasPressed(Button))
        {
            s_since = now;
            s_held = false;
        }
        if (s_since < 0) return;

        bool down = pad.Down(Button);
        if (down && !s_held && now - s_since >= HoldMs)
        {
            s_held = true;
            Act(player, hold: true);
            return;
        }
        if (!down)
        {
            if (!s_held) Act(player, hold: false);
            s_since = -1;
            s_held = false;
        }
    }

    static void Act(object player, bool hold)
    {
        var session = Access.Session(player);
        var p = Aim(player);
        if ((Access.CharacterAt(player, p) ?? Clicks.PersonNear(player, p)) is not { } target)
        {
            Counters.Add("attack.nobody");
            return;
        }
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == target) is not { } t || t.IsPlayer)
        {
            Counters.Add("attack.own_or_gone");
            return;
        }

        var verbs = Access.Verbs(player, t);
        var captions = verbs.Select(v => v.Caption).ToList();
        string? verb = hold ? HoldVerb(captions) : TapVerb(captions);
        if (verb is null)
        {
            Counters.Add(hold ? "attack.hold_none" : "attack.tap_not_hostile");
            if (!hold) Access.Notice(player, Strings.Get("notice.attack_hold"));
            return;
        }

        verbs.First(v => v.Caption == verb).Give();
        Counters.Add(verb.StartsWith("Stealth KO", StringComparison.Ordinal) ? "attack.stealth_ko" : verb == "Attack Unprovoked" ? "attack.unprovoked" : "attack.target");
    }

    /// <summary>Where the attack aims: the screen's centre while the mouse looks (a view), else the pointer.</summary>
    internal static Vector2 Aim(object player)
    {
        if (!Look.On) return Access.Pointer(player);
        var (w, h) = Access.UiSize(player);
        return new Vector2(w / 2f, h / 2f);
    }
}
