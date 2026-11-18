// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// Tap A: the safe extra option for the person the crosshair (or the pointer) is on, and the target line under the crosshair that names it
// (0.10, user 2026-11-18: "Yes, safe extra"). A press only ever gives an everyday verb from the game's own right-click menu for that person
// (ContextMenuWindow.Verbs, so it is exactly the menu's order): talk, trade, first aid, splints, pick up, loot, put in bed, stand up. Attacks,
// knock-outs, kidnapping, stealing and anything else stay on hold ZR (the full menu), so one press of A never starts a fight or a crime.
// The idea of a safe extra button and of naming it on screen is BeatTheCount's (their Gamepad mod's Y and its second line); our code is our own.
// Since 0.10 also doors (open, close, lock, unlock: never lockpicking, tools, strength or smashing) and your own faction's containers
// (inventory / loot of what is yours: another owner's container stays on hold ZR, as taking from it is theft), through the game's own
// furniture picks (FurnitureUse.PickDoor / PickContainer) and their menus (ContextMenuWindow.DoorVerbs / ContainerVerbs). Beds and
// buildings stay on ZR for now. People come first, then doors, then containers.
using System.Numerics;
using Meitou.Game.Rendering.Overlays;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public static class SafeExtra
{
    /// <summary>The everyday verbs A may give, best first (captions as Meitou's menu writes them; matched from the start).</summary>
    public static readonly string[] Safe = ["Talk To", "Trade", "First Aid", "Splint", "Pick Up", "Loot", "Put in Bed", "Stand Up"];

    /// <summary>A door's verbs A may give, best first (exact captions: "Pick Lock", "Use Tools", "Use Strength", "Smash" never).</summary>
    public static readonly string[] SafeDoor = ["Open", "Close", "Unlock", "Lock"];
    /// <summary>A container's verbs A may give, only when it is the player's faction's.</summary>
    public static readonly string[] SafeOwnContainer = ["Inventory", "Loot"];

    /// <summary>The safe verb for a door among its menu's captions, or null.</summary>
    public static string? PickDoor(IEnumerable<string> captions) => Exact(SafeDoor, captions);

    /// <summary>The safe verb for a container: only when it is ours; or null.</summary>
    public static string? PickContainer(IEnumerable<string> captions, bool ours) => ours ? Exact(SafeOwnContainer, captions) : null;

    static string? Exact(string[] safe, IEnumerable<string> captions)
    {
        var list = captions.ToList();
        foreach (var s in safe)
            if (list.FirstOrDefault(c => string.Equals(c, s, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
        return null;
    }

    /// <summary>The safe verb A gives among a menu's captions, or null.</summary>
    public static string? Pick(IEnumerable<string> captions)
    {
        var list = captions.ToList();
        foreach (var s in Safe)
            if (list.FirstOrDefault(c => c.StartsWith(s, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
        return null;
    }

    // ---------------------------------------------------------------- the target line (refreshed a few times a second from the pad tick)
    const long RefreshMs = 150;
    static long s_at;
    static string s_line = "";
    static string? s_verb;

    /// <summary>The safe option A gives now (for the prompt strip), or null.</summary>
    public static string? Verb => s_verb;

    /// <summary>The line under the crosshair now ("" for nothing).</summary>
    public static string Line => s_line;

    /// <summary>Each pad tick while WASD Control is on.</summary>
    public static void Refresh(object player)
    {
        long now = Environment.TickCount64;
        if (now - s_at < RefreshMs) return;
        s_at = now;
        s_line = "";
        s_verb = null;
        if (Find(player) is not { } f || f.Verbs.Count == 0) return;
        s_verb = f.Safe;
        s_line = f.Safe is null ? Strings.Format("target.menu_only", f.Name) : Strings.Format("target.line", f.Name, f.Safe);
    }

    /// <summary>Tap A: gives the safe verb for what the crosshair is on (a person, a door, a container of ours), or says there is none.</summary>
    public static void Give(object player)
    {
        if (Find(player) is not { } f)
        {
            Counters.Add("extra.nobody");
            return;
        }
        if (f.Safe is not { } caption)
        {
            Counters.Add("extra.none." + f.Kind);
            Access.Notice(player, Strings.Format("extra.none", f.Name));
            return;
        }
        f.Verbs.First(v => v.Caption == caption).Give();
        Counters.Add("extra." + f.Kind + "." + caption.Split(' ')[0].ToLowerInvariant());
    }

    sealed record Found(string Kind, string Name, IReadOnlyList<(string Caption, Action Give)> Verbs, string? Safe);

    /// <summary>What the crosshair (in a view) or the pointer is on: a person first, then a door, then a container.</summary>
    static Found? Find(object player)
    {
        var p = CrosshairAttack.Aim(player);
        if (Person(player, p) is { } t)
        {
            var verbs = Access.Verbs(player, t);
            return new("person", t.Name, verbs, Pick(verbs.Select(v => v.Caption)));
        }
        if (Access.DoorVerbsAt(player, p) is { Count: > 0 } door) return new("door", Strings.Get("target.door"), door, PickDoor(door.Select(v => v.Caption)));
        if (Access.ContainerAt(player, p) is { } c && c.Verbs.Count > 0)
            return new("container", Strings.Get(c.Ours ? "target.our_container" : "target.container"), c.Verbs, PickContainer(c.Verbs.Select(v => v.Caption), c.Ours));
        return null;
    }

    /// <summary>The person at the point; not your own standing people (pointing at them selects them).</summary>
    static CharacterSnapshot? Person(object player, System.Numerics.Vector2 p)
    {
        if ((Access.CharacterAt(player, p) ?? Clicks.PersonNear(player, p)) is not { } id) return null;
        var t = Access.Session(player).CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id);
        return t is null || t.Id == Driver.ControlledId ? null : t;
    }

    /// <summary>Draws the line under the crosshair (in a view, no dial or window open).</summary>
    internal static void Draw(DebugOverlay o, int w, int h)
    {
        if (s_line.Length == 0 || !Look.On) return;
        float s = h / 1080f;
        SquadWheel.Centred(o, s_line, w / 2f, h / 2f + 34 * s, SquadWheel.Ink, 0.9f * s);
        o.Flush(w, h);
    }
}
