// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The headless test hook (0.10.2, ledge-fall; tests\headless\ledge_run.py). Inert unless the environment variable PZA_WASD_TEST_DIR names a
// folder when the game starts: then, once a control tick, the driver reads <dir>\wasd.cmd (one command a line; the file is deleted once read)
// and writes what it did to <dir>\wasd.out. Meitou's agent channel gives orders, the camera, tp and step, but no keys, so this stands in for a
// held W: the real steering path (PlanStep, the lip trigger, the TumbleCommand) runs as it does for the player.
// Commands: on | off | fall on|off | hold <dx> <dz> (W held toward that world direction) | release | goto <x> <y> <z> (a move order with a
// height, so the character takes the stairs to an upper floor) | tumble <slot> <gen> (ask for a tumble for any character: the NPC guard's test)
// | trace on|off (a line every 3 ticks: tick, position, tumbling, postfix count) | follow on|off (the camera follow, for pictures) | probe <dx> <dz>
// (the navmesh surface along a push and the Fall plan) | sweep (the drops in 16 directions: lip distance, depth) | edgescan <radius> <step> (the deepest
// places a push would tumble, on a grid round the character) | where.
using System.Globalization;
using System.Numerics;
using Meitou.Simulation;
using Meitou.Game.Host;
using Meitou.Simulation.Movement;

namespace PZA.WASDControl.Meitou;

public static class TestHook
{
    static readonly string? s_dir = Environment.GetEnvironmentVariable("PZA_WASD_TEST_DIR") is { Length: > 0 } d && Directory.Exists(d) ? d : null;
    static bool s_trace;

    /// <summary>The test holds W toward this world direction (X, Z); null: no test steering.</summary>
    public static Vector2? Hold { get; private set; }

    static bool s_jump;
    /// <summary>The next jump counts as pushed without the character walking first ("jumppush": the leap's headless test).</summary>
    public static bool PushOnce;

    /// <summary>A "jump" line was read (once): the driver asks for a jump on its next control tick.</summary>
    public static bool TakeJump()
    {
        bool j = s_jump;
        s_jump = false;
        return j;
    }

    /// <summary>The hook is live (the variable named a folder).</summary>
    public static bool Live => s_dir is not null;

    // ---------------------------------------------------------------- the fake controller (2026-11-20: headless tests of the pad)
    static bool s_fake;
    static Vector2 s_fakeLeft, s_fakeRight;
    static float s_fakeLt, s_fakeRt;
    static uint s_fakeHeld;
    /// <summary>Buttons pressed for a number of pad reads, then let go (a tap).</summary>
    static readonly Dictionary<PadButton, int> s_fakeTaps = [];

    /// <summary>The fake pad's reading this read (null: the real controller). Only in a test run ("pad on" turns it on).</summary>
    public static PadState? FakePad()
    {
        if (!Live || !s_fake) return null;
        uint buttons = s_fakeHeld;
        foreach (var b in s_fakeTaps.Keys.ToList())
        {
            buttons |= 1u << (int)b;
            if (--s_fakeTaps[b] <= 0) s_fakeTaps.Remove(b);
        }
        if (s_fakeLt >= 0.5f) buttons |= 1u << (int)PadButton.LeftTrigger;
        if (s_fakeRt >= 0.5f) buttons |= 1u << (int)PadButton.RightTrigger;
        return new PadState(true, s_fakeLeft, s_fakeRight, s_fakeLt, s_fakeRt, buttons);
    }

    /// <summary>"pad on|off", "pad tap B [reads]", "pad hold B", "pad release [B]", "pad stick left|right x y", "pad trigger l|r v", "pad state".</summary>
    static string FakePadCommand(string[] a)
    {
        string what = a.Length > 1 ? a[1] : "state";
        PadButton Button(int i) => Enum.Parse<PadButton>(a[i], true);
        switch (what)
        {
            case "on": s_fake = true; break;
            case "off": s_fake = false; s_fakeHeld = 0; s_fakeTaps.Clear(); s_fakeLeft = s_fakeRight = Vector2.Zero; s_fakeLt = s_fakeRt = 0; break;
            case "tap": s_fake = true; s_fakeTaps[Button(2)] = a.Length > 3 ? Math.Max(1, (int)F(3)) : 2; break;
            case "hold": s_fake = true; s_fakeHeld |= 1u << (int)Button(2); break;
            case "release": s_fakeHeld = a.Length > 2 ? s_fakeHeld & ~(1u << (int)Button(2)) : 0; break;
            case "stick":
                s_fake = true;
                var v = new Vector2(F(3), F(4));
                if (a[2] == "left") s_fakeLeft = v; else s_fakeRight = v;
                break;
            case "trigger": s_fake = true; if (a[2] == "l") s_fakeLt = F(3); else s_fakeRt = F(3); break;
        }
        return $"pad {(s_fake ? "fake" : "real")}: held {s_fakeHeld:X}, taps [{string.Join(",", s_fakeTaps.Select(t => t.Key + "x" + t.Value))}], left {s_fakeLeft}, right {s_fakeRight}, lt {s_fakeLt} rt {s_fakeRt} | {Counters.Prefixed("pad.btn.")}";

        float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Once a control tick (Driver.OnTick), before the steering.</summary>
    public static void Tick(WorldSession session, CharacterSnapshot? lead)
    {
        if (s_dir is null) return;
        try
        {
            Commands(session, lead);
            if (s_trace && session.World.Tick % 3 == 0 && lead is not null) Out($"trace {session.World.Tick} {V(lead.Position)} tumbling {TumbleSystem.Tumbling?.Slot.ToString(CultureInfo.InvariantCulture) ?? "-"} fired {LedgePatch.Fired} down {lead.Down}");
        }
        catch (Exception e)
        {
            Out("error " + e.GetType().Name + ": " + e.Message);
        }
    }

    static void Commands(WorldSession session, CharacterSnapshot? lead)
    {
        string cmd = Path.Combine(s_dir!, "wasd.cmd");
        if (!File.Exists(cmd)) return;
        var lines = File.ReadAllLines(cmd);
        File.Delete(cmd);
        foreach (var raw in lines)
        {
            var a = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) continue;
            Out($"cmd {session.World.Tick} {raw.Trim()} -> {Run(session, lead, a)}");
        }
    }

    static string Run(WorldSession session, CharacterSnapshot? lead, string[] a)
    {
        float F(int i) => float.Parse(a[i], CultureInfo.InvariantCulture);
        switch (a[0])
        {
            case "pad": return FakePadCommand(a);
            case "weapon":   // weapon [auto|hip|fists]: the Orders dial's Weapons pick for the lead, then what its Fighter holds
            {
                if (lead is null) return "no lead";
                if (a.Length > 1) session.World.Commands.Enqueue(new WeaponPickCommand(lead.Id, Enum.Parse<WeaponPick>(a[1], true)) { Tick = session.World.Tick });
                var table = session.World.Characters;
                var f = table.TryResolveNext(lead.Id, out int slot) ? table.Cold(slot)?.Fighter : null;
                string W(global::Meitou.Simulation.Combat.WeaponInstance? w) => w is null ? "-" : w.Data.ToString() ?? "?";
                return f is null ? "no fighter" : $"pick {WeaponChoiceSystem.Of(lead.Id)} | in hand {W(f.Weapon)} | back {W(f.BackWeapon)} | hip {W(f.HipWeapon)} | patch {WeaponChoiceSystem.Installed} | {Counters.Prefixed("weapon.")}";
            }
            case "counters": return Counters.Prefixed(a.Length > 1 ? a[1] : "pad.");
            case "edittext": return Driver.Player is { } pl && MenuAccess.EditText(pl) is { } e ? $"box '{e.Text}' caret {e.Caret} keyboard {OnScreenKeyboard.Open}" : $"no box, keyboard {OnScreenKeyboard.Open}";
            case "kitshot":   // kitshot [seconds]: a PZA's Director's Kit shot (CameraApi.Shot) on the lead and the nearest other character
            {
                if (lead is null) return "no lead";
                var api = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("PZA.Camera.Meitou.CameraApi", false)).FirstOrDefault(x => x is not null);
                if (api is null) return "no CameraApi";
                var other = session.CurrentSnapshot.Characters.Where(ch => ch.Id != lead.Id)
                    .OrderBy(ch => Vector3.DistanceSquared(ch.Position, lead.Position)).FirstOrDefault();
                if (other is null) return "nobody else near";
                var shot = api.GetMethod("Shot");
                var busy = api.GetProperty("Busy");
                object? ok = shot?.Invoke(null, ["wasd.test", lead.Id, other.Id, "Auto", a.Length > 1 ? (double)F(1) : 3.0, false]);
                return $"kitshot -> {ok} busy {busy?.GetValue(null)} on {other.Id.Slot}.{other.Id.Generation}";
            }
            case "kitbusy":
            {
                var api = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("PZA.Camera.Meitou.CameraApi", false)).FirstOrDefault(x => x is not null);
                return api is null ? "no CameraApi" : $"busy {api.GetProperty("Busy")?.GetValue(null)} playing '{api.GetProperty("Playing")?.GetValue(null)}' available {api.GetProperty("Available")?.GetValue(null)}";
            }
            case "dials":return $"squad {SquadWheel.IsOpen} screens {ScreenWheel.IsOpen} orders {OrdersDial.IsOpen} chord {OrdersDial.Chord.Active} | {Counters.Prefixed("orders.")} {Counters.Prefixed("screens.")}";
            case "on": Driver.On = true; return "on";
            case "off": Driver.On = false; Hold = null; return "off";
            case "fall": Driver.Settings.Ledges = a.Length > 1 && a[1] == "on" ? "Fall" : "Stop"; return "ledges " + Driver.Settings.Ledges + (LedgePatch.Installed ? "" : " (no patch)");
            case "hold": Hold = Vector2.Normalize(new Vector2(F(1), F(2))); return "hold " + Hold;
            case "release": Hold = null; return "released";
            case "follow": Driver.Settings.CameraFollows = a.Length > 1 && a[1] == "on"; return "camera follows " + Driver.Settings.CameraFollows;
            case "trace": s_trace = a.Length > 1 && a[1] == "on"; return "trace " + s_trace;
            case "goto":
                if (lead is null) return "no lead";
                session.World.Commands.Enqueue(new MoveOrder([lead.Id], new Vector3(F(1), F(2), F(3))) { Tick = session.World.Tick });
                return "move order for " + lead.Id.Slot;
            case "jump":
                s_jump = true;
                return "jump asked";
            case "jumppush":   // a pushed jump from standing (the leap test): no walk before it
                s_jump = true;
                PushOnce = true;
                return "pushed jump asked";
            case "tumble":
                var id = new CharacterId(int.Parse(a[1], CultureInfo.InvariantCulture), int.Parse(a[2], CultureInfo.InvariantCulture));
                session.World.Commands.Enqueue(new TumbleCommand(id, LedgeRules.Push(new Vector2(1, 0))) { Tick = session.World.Tick });
                return "tumble asked for " + id.Slot + "." + id.Generation;
            case "probe":
                if (lead is null || Driver.Player is null) return "no lead";
                var surf = Access.SurfaceBelow(Driver.Player);
                if (surf is null) return "no surface lookup";
                var d = Vector2.Normalize(new Vector2(F(1), F(2)));
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i <= 12; i++)
                {
                    var q = lead.Position + new Vector3(d.X, 0, d.Y) * (i * Driver.SampleStep);
                    float? hi = surf(new Vector3(q.X, lead.Position.Y + Driver.MaxStepUpPerSample, q.Z));
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $" {i * Driver.SampleStep:0.#}:{(hi is { } h ? h.ToString("0.0", CultureInfo.InvariantCulture) : "null")}"));
                }
                var wk = session.World.Walkability;
                var plan = Driver.PlanStep(lead.Position, d, Driver.Settings.SteerDistance, surf, 0, out string how, out bool dropped, out var lip,
                    out float depth, p => wk.GroundHeight(p.X, p.Z, p.Y));
                sb.Append(" | ground");
                for (int i = 0; i <= 8; i++)
                {
                    var q = lead.Position + new Vector3(d.X, 0, d.Y) * (i * Driver.SampleStep);
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $" {wk.GroundHeight(q.X, q.Z, lead.Position.Y):0.0}"));
                }
                return $"surface{sb} | plan {how} {V(plan)} dropped {dropped} depth {depth:0.0} lip {(lip is { } l ? V(l) : "-")}";
            case "sweep":
                if (lead is null || Driver.Player is null) return "no lead";
                var sf = Access.SurfaceBelow(Driver.Player);
                if (sf is null) return "no surface lookup";
                var rows = new System.Text.StringBuilder();
                for (int k = 0; k < 16; k++)
                {
                    float ang = k * MathF.Tau / 16;
                    var dd = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                    var wk2 = session.World.Walkability;
                    Driver.PlanStep(lead.Position, dd, 80, sf, 0, out string hw, out bool dr, out var lp, out float dep, p => wk2.GroundHeight(p.X, p.Z, p.Y));
                    if (!dr) continue;
                    float far = lp is { } q ? new Vector2(q.X - lead.Position.X, q.Z - lead.Position.Z).Length() : 0;
                    rows.Append(string.Create(CultureInfo.InvariantCulture, $" [{dd.X:0.00} {dd.Y:0.00}: lip {far / 10:0.0} m, down {dep / 10:0.0} m{(dep >= LedgeRules.MinFall ? " FALL" : "")}]"));
                }
                return rows.Length == 0 ? "no drops within 8 m" : "drops" + rows;
            case "edgescan":
                if (lead is null || Driver.Player is null) return "no lead";
                var es = Access.SurfaceBelow(Driver.Player);
                if (es is null) return "no surface lookup";
                var wg = session.World.Walkability;
                float radius = F(1), stepXZ = F(2);
                var found = new List<(float Depth, Vector3 At, Vector2 Dir, string Clear)>();
                for (float gx = -radius; gx <= radius; gx += stepXZ)
                    for (float gz = -radius; gz <= radius; gz += stepXZ)
                    {
                        var probe = lead.Position + new Vector3(gx, 300, gz);
                        if (es(probe) is not { } top) continue;
                        var at = new Vector3(probe.X, top, probe.Z);
                        for (int k = 0; k < 8; k++)
                        {
                            float an = k * MathF.Tau / 8;
                            var dv = new Vector2(MathF.Cos(an), MathF.Sin(an));
                            Driver.PlanStep(at, dv, 10, es, 0, out _, out bool dd, out var lp2, out float dp, p => wg.GroundHeight(p.X, p.Z, p.Y));
                            if (dd && LedgeRules.AtLip(true, dd, at, lp2, dp)) found.Add((dp, at, dv, ""));
                        }
                    }
                var best = new List<(float Depth, Vector3 At, Vector2 Dir, string Clear)>();
                foreach (var f in found.OrderByDescending(f => f.Depth))
                    if (best.All(b => Vector3.Distance(b.At, f.At) > 150) && best.Count < 12) best.Add(f);
                return $"{found.Count} fall spots; deepest:" + string.Concat(best.Select(b => string.Create(CultureInfo.InvariantCulture,
                    $" [{b.At.X:0} {b.At.Y:0} {b.At.Z:0} dir {b.Dir.X:0.0} {b.Dir.Y:0.0} down {b.Depth / 10:0.0} m]")));
            case "leapscan":   // leapscan <radius> <step>: spots where the walk ahead stops but a planned leap lands (prototype)
            {
                if (lead is null || Driver.Player is null) return "no lead";
                var ls = Access.SurfaceBelow(Driver.Player);
                if (ls is null) return "no surface lookup";
                var lw = session.World.Walkability;
                float lr = F(1), lstep = F(2);
                var spots = new List<string>();
                for (float gx = -lr; gx <= lr && spots.Count < 12; gx += lstep)
                    for (float gz = -lr; gz <= lr && spots.Count < 12; gz += lstep)
                    {
                        var top = lead.Position + new Vector3(gx, 300, gz);
                        if (ls(top) is not { } ty) continue;
                        var at = new Vector3(top.X, ty, top.Z);
                        for (int k = 0; k < 8; k++)
                        {
                            float an = k * MathF.Tau / 8;
                            var dv = new Vector2(MathF.Cos(an), MathF.Sin(an));
                            var end = Driver.PlanTarget(at, dv, JumpRules.ClearAhead, ls, out string lhow);
                            float walked = new Vector2(end.X - at.X, end.Z - at.Z).Length();
                            if (JumpRules.Clear(lhow, walked, at.Y, end.Y)) continue;
                            if (LeapRules.Plan(at, dv, walked, (x, z, y) => lw.GroundHeight(x, z, y), (x, z, y) => lw.IsWalkable(x, z, y) && lw.IsOnSurface(x, z, y) && lw.FloorNear(x, z, y, 2f) is { } fl && MathF.Abs(fl.X - x) < 0.5f && MathF.Abs(fl.Z - z) < 0.5f, out _) is not { } lp) continue;
                            spots.Add(string.Create(CultureInfo.InvariantCulture,
                                $" [{at.X:0} {at.Y:0} {at.Z:0} dir {dv.X:0.00} {dv.Y:0.00} {lhow} at {walked / 10:0.0} m -> {Vector2.Distance(new(at.X, at.Z), new(lp.To.X, lp.To.Z)) / 10:0.0} m, {(lp.To.Y - at.Y) / 10:0.0} m up]"));
                            break;
                        }
                    }
                return spots.Count == 0 ? "no leap spots" : "leap spots" + string.Concat(spots);
            }
            case "leapplan":   // leapplan <dx> <dz>: the leap the jump would plan that way from where the lead stands
            {
                if (lead is null || Driver.Player is null) return "no lead";
                var ls = Access.SurfaceBelow(Driver.Player);
                if (ls is null) return "no surface lookup";
                var lw = session.World.Walkability;
                var dv = Vector2.Normalize(new Vector2(F(1), F(2)));
                var end = Driver.PlanTarget(lead.Position, dv, JumpRules.ClearAhead, ls, out string lhow);
                float walked = new Vector2(end.X - lead.Position.X, end.Z - lead.Position.Z).Length();
                var lp = LeapRules.Plan(lead.Position, dv, walked, (x, z, y) => lw.GroundHeight(x, z, y), (x, z, y) => lw.IsWalkable(x, z, y) && lw.IsOnSurface(x, z, y) && lw.FloorNear(x, z, y, 2f) is { } fl && MathF.Abs(fl.X - x) < 0.5f && MathF.Abs(fl.Z - z) < 0.5f, out string lwhy);
                return $"walk {lhow} {walked / 10:0.0} m clear {JumpRules.Clear(lhow, walked, lead.Position.Y, end.Y)} | leap " +
                    (lp is { } p ? $"{V(p.From)} -> {V(p.To)}" : "none (" + lwhy + ")");
            }
            case "talk":   // talk info | down | up | say: the conversation's replies through ConversationPad (the pad's D-pad / A)
            {
                if (session.CurrentSnapshot.Conversation is not { } talk) return "no conversation";
                string what = a.Length > 1 ? a[1] : "info";
                if (what == "down" || what == "up") ConversationPad.Choose(talk, what == "down" ? 1 : -1, _ => { });
                else if (what == "say") ConversationPad.Say(session, talk);
                return $"conversation with {talk.SpeakerName}: {talk.Choices.Count} replies, chosen {ConversationPad.Index} ({(talk.Choices.Count > 0 ? talk.Choices[ConversationPad.Move(ConversationPad.Index, talk.Choices.Count, 0)] : "-")}) | {Counters.Prefixed("talk.")} {Counters.Prefixed("view.skip_conversation")}";
            }
            case "view":   // view normal | shoulder | first
                Views.Set(a.Length > 1 && a[1] == "first" ? View.First : a.Length > 1 && a[1] == "shoulder" ? View.Shoulder : View.Normal, _ => { });
                return "view " + Views.Current;
            case "jumpinfo":
                return lead is null ? "no lead" : $"lead at {V(lead.Position)} yaw {lead.Yaw.ToString("0.00", CultureInfo.InvariantCulture)} jumping {JumpSystem.PlayingClip(lead.Id, session.World.Tick)?.ToString() ?? "-"} speed {Driver.MeasuredSpeed / 10:0.0} m/s mode {lead.RunSpeed} combat [{(JumpSystem.Combat is { } cs ? $"own {cs.Playing(lead.Id, session.World.Tick) is not null} pose {cs.RangedPoseOf(lead.Id, session.World.Tick, session.World.TickSeconds)?.Pose.ToString() ?? "-"} stumble {cs.StateOf(lead.Id).StumbleClip} stun {cs.StateOf(lead.Id).StunUntil}" : "-")}] over {JumpOverPatch.Fired} stale {StaleStumblePatch.Fired} registered {JumpSystem.Registered} | {Counters.Prefixed("jump.")} | {Counters.Prefixed("leap.")}";
            case "pace":   // pace walk | jog | run: the steered character's speed mode (the stride jumps' test)
                if (lead is null) return "no lead";
                byte want = (a.Length > 1 ? a[1] : "") switch { "walk" => global::Meitou.Simulation.Movement.SpeedMode.Walk, "jog" => global::Meitou.Simulation.Movement.SpeedMode.Run, _ => global::Meitou.Simulation.Movement.SpeedMode.Free };
                int step = StickSpeed.Step(lead.RunSpeed, want);
                if (step != 0) session.World.Commands.Enqueue(new global::Meitou.Simulation.Player.RunSpeedCommand([lead.Id], step) { Tick = session.World.Tick });
                return $"pace {want} (step {step})";
            case "face":
                if (lead is null) return "no lead";
                session.World.Commands.Enqueue(new TurnCommand(lead.Id, JumpRules.YawOf(F(1), F(2))) { Tick = session.World.Tick });
                return "turn asked";
            case "where":
                return lead is null ? "no lead" : $"lead {lead.Id.Slot}.{lead.Id.Generation} at {V(lead.Position)} down {lead.Down} counters {Counters.Get("ledge.tumble_started")}";
            default: return "unknown";
        }
    }

    static string V(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:0.0} {v.Y:0.0} {v.Z:0.0}");

    static void Out(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(s_dir!, "wasd.out"), line + Environment.NewLine);
        }
        catch (IOException)
        {
            // the harness reads the file; a clash drops one line
        }
    }
}
