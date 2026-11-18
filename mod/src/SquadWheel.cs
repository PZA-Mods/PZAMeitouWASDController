// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The squad wheel (user, 2026-11-17: "Holding RB for a squad wheel is AMAZING!!!"; the approved mock-up), controller only:
// - RB tapped: the next squad member, as before (the game's own NextCharacter key);
// - RB held: a round dial of the active squad's faces opens in the middle of the screen; the right stick points at a slice (it stays lit when
//   the stick comes back); letting go of RB takes control of that member; B cancels; nothing lit, nothing changes;
// - the top slice is EVERYONE (user, 2026-11-17: "how do you select EVERYONE in the squad (on keyboard it's tilde ~)"): letting go on it
//   presses the game's own SelectAll key, as ~ does; the bottom slice is SQUAD PACE (0.9.8: it moved here off ZR): letting go there switches
//   squad pace on or off for the squad (Meitou's group speed, everyone at the slowest); the left slice is FOLLOW ME (0.9.9, user: "another
//   button to make everyone follow whoever is selected/being controlled"): the rest of the squad follows the controlled character (Meitou's
//   own FollowOrder, the right-click menu's Follow);
// - squads of any size: up to 7 members a dial of 8, up to 11 a dial of 12, more: pages (EVERYONE + 11 faces each), flipped with D-pad
//   left / right or LB while RB is held; it opens on the page of the member under control. The members are Meitou's squad bar's (at most 30,
//   its own cap); their faces are the cells Meitou already draws for the bar every frame (portrait atlas cell = squad bar index).
// 0.9.5 (user: "doesn't HIGHLIGHT the portrait yet ... also isn't a circle"): the dial is drawn by the mod itself with Meitou's own overlay
// (triangles, lines, images, text) right after the interface (a GameLoop.DrawInterface postfix), no longer a square Kenshi window whose
// button highlight did not show.
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Rendering.Overlays;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public enum WheelResult { None, Tap, Open, Everyone, SquadPace, FollowMe }

public static class SquadWheel
{
    /// <summary>RB held this long opens the wheel; a shorter press is a tap (ms).</summary>
    public const long HoldMs = 220;
    /// <summary>The stick points at a slice past this tilt (0..1).</summary>
    public const float Aim = 0.5f;
    public const int SmallRing = 8, BigRing = 12;
    /// <summary>A slice's content: EVERYONE (the top slice of every page), nothing, or a member index (0 and up).</summary>
    public const int Everyone = -2, Empty = -1, SquadPace = -3, FollowMe = -4;
    const int None = int.MinValue;

    /// <summary>The fixed slices: EVERYONE at the top, SQUAD PACE at the bottom, FOLLOW ME on the left.</summary>
    const int Fixed = 3;

    /// <summary>The dial's slices for <paramref name="count"/> members (the three fixed slices take three): 8 up to 5 members, else 12.</summary>
    public static int Slots(int count) => count + Fixed <= SmallRing ? SmallRing : BigRing;

    /// <summary>Faces on a page: the slices less the fixed ones.</summary>
    static int PerPage(int count) => Slots(count) - Fixed;

    /// <summary>Pages: one, or as many as it takes at 9 faces each.</summary>
    public static int Pages(int count) => Math.Max(1, (count + PerPage(count) - 1) / PerPage(count));

    /// <summary>The fixed slice at <paramref name="slot"/> of a dial of <paramref name="slots"/>, or Empty for a face's slice.</summary>
    public static int FixedAt(int slot, int slots) =>
        slot == 0 ? Everyone : slot == slots / 2 ? SquadPace : slot == slots * 3 / 4 ? FollowMe : Empty;

    /// <summary>What slice <paramref name="slot"/> of page <paramref name="page"/> holds: a fixed slice, a member index, or Empty.</summary>
    public static int EntryAt(int page, int slot, int count)
    {
        int slots = Slots(count);
        if (slot < 0 || slot >= slots) return Empty;
        if (FixedAt(slot, slots) is var f and not Empty) return f;
        int k = 0;
        for (int i = 0; i < slot; i++) if (FixedAt(i, slots) == Empty) k++;
        int m = page * PerPage(count) + k;
        return m < count ? m : Empty;
    }

    /// <summary>The page a member index is on.</summary>
    public static int PageOf(int member, int count) => member < 0 ? 0 : member / PerPage(count);

    /// <summary>The slice the stick points at (0 at the top, clockwise), or -1 inside the aim radius. Stick Y is up.</summary>
    public static int SlotAt(Vector2 stick, int slots)
    {
        if (stick.Length() < Aim) return -1;
        float a = MathF.Atan2(stick.X, stick.Y);   // 0 up, +pi/2 right
        if (a < 0) a += MathF.Tau;
        return (int)MathF.Round(a / (MathF.Tau / slots)) % slots;
    }

    /// <summary>Slice <paramref name="slot"/>'s centre on screen: <paramref name="radius"/> from <paramref name="centre"/>, 0 at the top, clockwise.</summary>
    public static Vector2 SlotCentre(Vector2 centre, int slot, int slots, float radius)
    {
        float a = slot * MathF.Tau / slots;
        return centre + new Vector2(radius * MathF.Sin(a), -radius * MathF.Cos(a));
    }

    /// <summary>The dial's sizes on a screen <paramref name="h"/> pixels high: face-centre radius, face size, inner and outer rim.</summary>
    public static (float Radius, float Face, float Inner, float Outer) Sizes(int slots, float h)
    {
        float s = h / 1080f, radius = 250 * s, face = (slots == SmallRing ? 118 : 90) * s;
        return (radius, face, radius - face * 0.72f, radius + face * 0.72f);
    }

    // ---------------------------------------------------------------- state (control tick and the draw, both on the host thread)
    static long s_rbSince = -1;
    static bool s_open;
    static int s_page, s_lit = None;
    static CharacterId[] s_members = [];
    static bool s_paceOn;
    /// <summary>The squad the dial was opened on (squad bar order), for the squad pace switch.</summary>
    public static IReadOnlyList<CharacterId> Members => s_members;
    static string[] s_names = [];

    public static bool IsOpen => s_open;

    /// <summary>The pad's RB this control tick. Tap: the caller presses NextCharacter; Everyone: SelectAll; Open: the wheel has the pad.</summary>
    public static WheelResult Pad(object player, PadState pad, CharacterId? controlled)
    {
        long now = Environment.TickCount64;
        if (Pressed(PadButton.RightBumper)) s_rbSince = now;
        bool released = global::PZA.WASDControl.Meitou.Pad.WasReleased(PadButton.RightBumper);

        if (!s_open)
        {
            if (s_rbSince < 0) return WheelResult.None;
            if (released)
            {
                s_rbSince = -1;
                Counters.Add("wheel.tap");
                return WheelResult.Tap;
            }
            if (!pad.Down(PadButton.RightBumper))
            {
                s_rbSince = -1;   // lost the release (pad gone): nothing
                return WheelResult.None;
            }
            if (now - s_rbSince < HoldMs) return WheelResult.None;
            if (!OpenWheel(player, controlled)) { s_rbSince = -1; return WheelResult.None; }
        }

        int count = s_members.Length, pages = Pages(count);
        if (pages > 1)
        {
            int flip = (Pressed(PadButton.DPadRight) ? 1 : 0) - (Pressed(PadButton.DPadLeft) || Pressed(PadButton.LeftBumper) ? 1 : 0);
            if (flip != 0)
            {
                s_page = ((s_page + flip) % pages + pages) % pages;
                s_lit = None;
                Counters.Add("wheel.page");
            }
        }

        int slot = SlotAt(pad.Right, Slots(count));
        if (slot >= 0 && EntryAt(s_page, slot, count) is var e and not Empty && e != s_lit)
        {
            s_lit = e;
            Counters.Add("wheel.aim");
        }

        if (Pressed(PadButton.East))
        {
            Close("wheel.cancel");
            s_rbSince = -1;
            return WheelResult.Open;   // the B press is the wheel's, not a select
        }

        if (!released && pad.Down(PadButton.RightBumper)) return WheelResult.Open;

        int lit = s_lit;
        s_rbSince = -1;
        if (lit == Everyone)
        {
            Close("wheel.everyone");
            return WheelResult.Everyone;
        }
        if (lit == SquadPace)
        {
            Close("wheel.squad_pace");
            return WheelResult.SquadPace;
        }
        if (lit == FollowMe)
        {
            Close("wheel.follow_me");
            return WheelResult.FollowMe;
        }
        if (lit >= 0 && lit < count) Take(player, s_members[lit], controlled);
        else Counters.Add("wheel.released_empty");
        Close(null);
        return WheelResult.Open;
    }

    static bool Pressed(PadButton b) => global::PZA.WASDControl.Meitou.Pad.WasPressed(b);

    static bool OpenWheel(object player, CharacterId? controlled)
    {
        var members = Access.SquadBarMembers(player);
        if (members.Count == 0)
        {
            Counters.Add("wheel.no_members");
            return false;
        }

        s_members = [.. members.Select(c => c.Id)];
        s_names = [.. members.Select(c => c.Name)];
        s_paceOn = members.FirstOrDefault(m => m.Id == controlled)?.RunSpeed == global::Meitou.Simulation.Movement.SpeedMode.Group;
        int at = controlled is { } c ? Array.IndexOf(s_members, c) : -1;
        s_page = PageOf(at, s_members.Length);
        s_lit = None;
        s_open = true;
        Counters.Add("wheel.open");
        Counters.Add("wheel.size_" + (Pages(s_members.Length) > 1 ? "pages" : Slots(s_members.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return true;
    }

    static void Take(object player, CharacterId id, CharacterId? controlled)
    {
        if (id == controlled)
        {
            Counters.Add("wheel.take_already");
            return;
        }

        Access.SelectOnly(player, id);
        Counters.Add("wheel.take");
    }

    static void Close(string? why)
    {
        s_open = false;
        s_lit = None;
        if (why is not null) Counters.Add(why);
    }

    /// <summary>Once a frame: the wheel shuts when WASD Control is off (no RB release can come).</summary>
    public static void EachFrame()
    {
        if (s_open && !Driver.On) Close("wheel.closed_off");
    }

    // ---------------------------------------------------------------- the dial (GameLoop.DrawInterface postfix)
    static FieldInfo? s_loopOverlay, s_uiPainter;
    static MethodInfo? s_loadTexture;
    internal static readonly Vector4 Back = new(0.07f, 0.06f, 0.05f, 0.80f), Hub = new(0.04f, 0.035f, 0.03f, 0.85f),
        Rim = new(0.69f, 0.65f, 0.55f, 0.85f), Seam = new(0.69f, 0.65f, 0.55f, 0.30f),
        Gold = new(0.91f, 0.71f, 0.35f, 1f), GoldWash = new(0.91f, 0.71f, 0.35f, 0.32f),
        Ink = new(0.86f, 0.82f, 0.72f, 1f), Faint = new(0.69f, 0.65f, 0.55f, 0.9f), Dim = new(0.62f, 0.62f, 0.62f, 1f);

    /// <summary>Patches GameLoop.DrawInterface when every member is there; a line saying what happened.</summary>
    public static string Apply(Harmony h)
    {
        var loop = AccessTools.TypeByName("Meitou.Game.Host.GameLoop");
        var draw = loop is null ? null : AccessTools.Method(loop, "DrawInterface");
        s_loopOverlay = loop is null ? null : AccessTools.Field(loop, "_overlay");
        s_uiPainter = AccessTools.Field(Access.PlayerInterface, "UiPainter");
        var painter = AccessTools.TypeByName("Meitou.Game.Rendering.Ui.UiPainter");
        s_loadTexture = painter is null ? null : AccessTools.Method(painter, "LoadTexture", [typeof(string)]);
        if (draw is null || s_loopOverlay is null || s_uiPainter is null || s_loadTexture is null) return "squad wheel: drawing members missing, the dial stays hidden";

        h.Patch(draw, postfix: new HarmonyMethod(AccessTools.Method(typeof(SquadWheel), nameof(DrawInterfacePostfix))));
        return "patched GameLoop.DrawInterface postfix (squad wheel)";
    }

    static void DrawInterfacePostfix(object __instance, object[] __args)
    {
        if (!s_open && !ScreenWheel.IsOpen) return;
        try
        {
            if (s_loopOverlay!.GetValue(__instance) is not DebugOverlay overlay || __args.Length == 0 || __args[0] is not { } size) return;
            var t = size.GetType();
            if (t.GetField("X")?.GetValue(size) is not int w || t.GetField("Y")?.GetValue(size) is not int hgt || w <= 0 || hgt <= 0) return;
            if (s_open) Draw(overlay, w, hgt);
            else ScreenWheel.Draw(overlay, w, hgt);
            Counters.Add("wheel.drawn");
        }
        catch (Exception e)
        {
            Log.Exception("squad wheel draw", e);
        }
    }

    static void Draw(DebugOverlay o, int w, int h)
    {
        int count = s_members.Length, slots = Slots(count), pages = Pages(count);
        var c = new Vector2(w / 2f, h / 2f);
        var (radius, face, inner, outer) = Sizes(slots, h);
        float step = MathF.Tau / slots, s = h / 1080f;

        o.Flush(w, h);
        Ring(o, c, inner, outer, 0, MathF.Tau, Back);
        Disc(o, c, inner, Hub);
        int litSlot = -1;
        for (int i = 0; i < slots; i++) if (s_lit != None && EntryAt(s_page, i, count) == s_lit) litSlot = i;
        if (litSlot >= 0) Ring(o, c, inner, outer, litSlot * step - step / 2, litSlot * step + step / 2, GoldWash);
        for (int i = 0; i < slots; i++)
        {
            float a = i * step + step / 2;
            var d = new Vector2(MathF.Sin(a), -MathF.Cos(a));
            o.Line(c.X + d.X * inner, c.Y + d.Y * inner, c.X + d.X * outer, c.Y + d.Y * outer, Math.Max(1, s), Seam);
        }
        Circle(o, c, inner, Math.Max(1, 1.5f * s), Rim);
        Circle(o, c, outer, Math.Max(1, 2f * s), Rim);
        if (litSlot >= 0) Arc(o, c, outer, litSlot * step - step / 2, litSlot * step + step / 2, 4 * s, Gold);
        o.Flush(w, h);

        // the faces: the squad bar's own portrait cells
        if (Faces(out var texture, out float tw, out float th))
        {
            for (int i = 1; i < slots; i++)
            {
                int m = EntryAt(s_page, i, count);
                if (m < 0) continue;
                bool lit = m == s_lit;
                float f = face * (lit ? 1.12f : 1f);
                var p = SlotCentre(c, i, slots, radius);
                var r = Cell(m);
                o.Image(texture, new Vector4(p.X - f / 2, p.Y - f / 2, p.X + f / 2, p.Y + f / 2),
                    new Vector4(r.X / tw, (r.Y + r.W) / th, (r.X + r.Z) / tw, r.Y / th), lit ? Vector4.One : Dim);
            }
            o.Flush(w, h);
        }
        for (int i = 1; i < slots; i++)
        {
            int m = EntryAt(s_page, i, count);
            if (m < 0) continue;
            bool lit = m == s_lit;
            float f = face * (lit ? 1.12f : 1f);
            var p = SlotCentre(c, i, slots, radius);
            Frame(o, p, f, lit ? 3 * s : 1.2f * s, lit ? Gold : Seam);
        }
        o.Flush(w, h);

        // EVERYONE on the top slice, the lit name, the page and the hint in the middle
        var top = SlotCentre(c, 0, slots, radius);
        Centred(o, Strings.Get("wheel.all"), top.X, top.Y - 10 * s, s_lit == Everyone ? Gold : Ink, 1.1f * s);
        Centred(o, Strings.Format("wheel.all_count", count), top.X, top.Y + 12 * s, Faint, 0.85f * s);
        var left = SlotCentre(c, slots * 3 / 4, slots, radius);
        Centred(o, Strings.Get("wheel.follow"), left.X, left.Y - 10 * s, s_lit == FollowMe ? Gold : Ink, 1.0f * s);
        Centred(o, Strings.Get("wheel.follow_sub"), left.X, left.Y + 12 * s, Faint, 0.85f * s);
        var bottom = SlotCentre(c, slots / 2, slots, radius);
        Centred(o, Strings.Get("wheel.pace"), bottom.X, bottom.Y - 10 * s, s_lit == SquadPace ? Gold : Ink, 1.0f * s);
        Centred(o, Strings.Get(s_paceOn ? "wheel.pace_on" : "wheel.pace_off"), bottom.X, bottom.Y + 12 * s, Faint, 0.85f * s);
        string name = s_lit == Everyone ? Strings.Get("wheel.everyone")
            : s_lit == SquadPace ? Strings.Get(s_paceOn ? "wheel.pace_turn_off" : "wheel.pace_turn_on")
            : s_lit == FollowMe ? Strings.Get(Driver.ControlledId is { } who && Driver.FollowingNow(who) ? "wheel.unfollow_name" : "wheel.follow_name")
            : s_lit >= 0 && s_lit < s_names.Length ? s_names[s_lit] : "";
        Centred(o, name, c.X, c.Y - 30 * s, Gold, 1.35f * s);
        if (pages > 1) Centred(o, Strings.Format("wheel.page", s_page + 1, pages), c.X, c.Y + 4 * s, Faint, 0.95f * s);
        Centred(o, Strings.Get(pages > 1 ? "wheel.hint_pages" : "wheel.hint"), c.X, c.Y + (pages > 1 ? 28 : 8) * s, Faint, 0.85f * s);
        o.Flush(w, h);
    }

    /// <summary>The atlas rectangle (x, y, w, h) of squad bar member <paramref name="m"/>'s face.</summary>
    static Vector4 Cell(int m)
    {
        var p = PZA.MeitouUi.PzaPortrait.Rect(m).Split(' ');
        return new Vector4(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Meitou's portrait atlas ("meitou:portraits", the squad bar's faces) and its size, through the interface painter.</summary>
    static bool Faces(out SampledTexture texture, out float width, out float height)
    {
        texture = default; width = height = 0;
        if (Driver.Player is not { } player || s_uiPainter!.GetValue(player) is not { } painter) return false;
        if (s_loadTexture!.Invoke(painter, ["meitou:portraits"]) is not ITuple pair || pair[0] is not GpuTexture gpu || pair[1] is not SampledTexture sampled) return false;
        texture = sampled;
        width = gpu.Desc.Width;
        height = gpu.Desc.Height;
        return width > 0 && height > 0;
    }

    internal static void Ring(DebugOverlay o, Vector2 c, float r0, float r1, float a0, float a1, Vector4 colour)
    {
        int n = Math.Max(4, (int)MathF.Ceiling((a1 - a0) / (MathF.Tau / 96)));
        for (int i = 0; i < n; i++)
        {
            float x = a0 + (a1 - a0) * i / n, y = a0 + (a1 - a0) * (i + 1) / n;
            Vector2 P(float a, float r) => c + new Vector2(MathF.Sin(a), -MathF.Cos(a)) * r;
            o.Triangle(P(x, r0), P(x, r1), P(y, r1), colour);
            o.Triangle(P(x, r0), P(y, r1), P(y, r0), colour);
        }
    }

    internal static void Disc(DebugOverlay o, Vector2 c, float r, Vector4 colour)
    {
        const int n = 64;
        for (int i = 0; i < n; i++)
        {
            float a = MathF.Tau * i / n, b = MathF.Tau * (i + 1) / n;
            o.Triangle(c, c + new Vector2(MathF.Sin(a), -MathF.Cos(a)) * r, c + new Vector2(MathF.Sin(b), -MathF.Cos(b)) * r, colour);
        }
    }

    internal static void Circle(DebugOverlay o, Vector2 c, float r, float thick, Vector4 colour) => Arc(o, c, r, 0, MathF.Tau, thick, colour);

    internal static void Arc(DebugOverlay o, Vector2 c, float r, float a0, float a1, float thick, Vector4 colour)
    {
        int n = Math.Max(4, (int)MathF.Ceiling((a1 - a0) / (MathF.Tau / 96)));
        for (int i = 0; i < n; i++)
        {
            float x = a0 + (a1 - a0) * i / n, y = a0 + (a1 - a0) * (i + 1) / n;
            o.Line(c.X + MathF.Sin(x) * r, c.Y - MathF.Cos(x) * r, c.X + MathF.Sin(y) * r, c.Y - MathF.Cos(y) * r, thick, colour);
        }
    }

    internal static void Frame(DebugOverlay o, Vector2 p, float f, float thick, Vector4 colour)
    {
        float x0 = p.X - f / 2, y0 = p.Y - f / 2, x1 = p.X + f / 2, y1 = p.Y + f / 2;
        o.Line(x0, y0, x1, y0, thick, colour);
        o.Line(x1, y0, x1, y1, thick, colour);
        o.Line(x1, y1, x0, y1, thick, colour);
        o.Line(x0, y1, x0, y0, thick, colour);
    }

    internal static void Centred(DebugOverlay o, string text, float x, float top, Vector4 colour, float scale)
    {
        if (text.Length == 0) return;
        o.TextScaled(text, x - o.MeasureText(text, scale) / 2, top, colour, scale);
    }
}
