// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// Walk, jog or run by how far the left stick is pushed (user, 2026-11-17: "Can walk/run speed be controlled by how much tilt is on the L3
// joystick?", then "does it have jog too? ... Does that free up ZR"). Meitou keeps a speed mode per character (SpeedMode: walk, run = the
// HUD's jog, capped at RunCap; free = full run; group = squad pace; CharacterSnapshot.RunSpeed), stepped by a RunSpeedCommand. While the
// stick steers:
// - a light push walks, a medium push jogs, a full push runs (thresholds with a margin either side, so the edge does not flicker);
// - squad pace (set on the squad dial's bottom slice, or with X) is left alone: everyone keeps to the slowest;
// - let go, the speed the character had before the stick took over is put back (or one the player set meanwhile, with X).
// Only the controlled character is stepped (the command names it). W A S D on the keyboard are all-or-nothing and keep the chosen speed.
// ZR no longer steps the speed: it is free (0.9.8).
using Meitou.Game.Host;
using Meitou.Simulation;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Player;

namespace PZA.WASDControl.Meitou;

public static class StickSpeed
{
    /// <summary>The zone edges (push 0..1 after the dead zone): walk below JogAt, jog below RunAt, run above; each edge has Margin either side.</summary>
    public const float JogAt = 0.40f, RunAt = 0.78f, Margin = 0.05f;
    /// <summary>A sent change is waited for this many ticks before it is sent again (the snapshot shows it a tick or two later).</summary>
    const int Settle = 8;

    static CharacterId? s_who;
    static byte s_chosen;
    static byte? s_sent;
    static long s_sentTick;
    static int s_zone = -1;

    /// <summary>The zone a push of <paramref name="tilt"/> is in (0 walk, 1 jog, 2 run), keeping <paramref name="previous"/> within the margins.</summary>
    public static int Zone(float tilt, int previous)
    {
        int raw = tilt < JogAt ? 0 : tilt < RunAt ? 1 : 2;
        if (previous < 0 || raw == previous) return raw;
        // stay in the previous zone until the push is past the edge by the margin
        float edge = Math.Max(raw, previous) == 1 ? JogAt : RunAt;
        if (Math.Abs(raw - previous) == 1 && Math.Abs(tilt - edge) < Margin) return previous;
        return raw;
    }

    /// <summary>The mode a zone wants: walk, jog (SpeedMode.Run), run (SpeedMode.Free).</summary>
    public static byte ModeOf(int zone) => zone switch { 0 => SpeedMode.Walk, 1 => SpeedMode.Run, _ => SpeedMode.Free };

    /// <summary>The RunSpeedCommand step that turns <paramref name="from"/> into <paramref name="to"/> (modes wrap, SpeedMode.Count).</summary>
    public static int Step(byte from, byte to) => ((to - from) % SpeedMode.Count + SpeedMode.Count) % SpeedMode.Count;

    /// <summary>Each control tick (after the pad): <paramref name="tilt"/> is the stick's push while it steers, else 0.</summary>
    public static void Tick(WorldSession session, CharacterSnapshot lead, float tilt)
    {
        if (!Driver.Settings.StickWalk) { Release(session, lead); return; }
        long tick = session.World.Tick;
        bool settled = s_sent is null || tick - s_sentTick >= Settle;

        if (tilt <= 0)
        {
            Release(session, lead);
            return;
        }

        if (s_who != lead.Id)
        {
            // the stick takes over (or the control moved): the speed now is the one to put back
            Release(session, null);
            s_who = lead.Id;
            s_chosen = lead.RunSpeed;
            s_zone = -1;
            s_sent = null;
        }
        else if (settled && s_sent is { } sent && lead.RunSpeed != sent)
        {
            // changed by the player meanwhile (X / the HUD arrows / the dial's squad pace): the one to put back, and squad pace is left alone
            s_chosen = lead.RunSpeed;
            s_sent = lead.RunSpeed;
            Counters.Add("speed.player_changed");
            return;
        }

        if (lead.RunSpeed == SpeedMode.Group || s_chosen == SpeedMode.Group) { s_sent ??= lead.RunSpeed; return; }   // squad pace: left alone

        s_zone = Zone(tilt, s_zone);
        byte want = ModeOf(s_zone);
        if (!settled || lead.RunSpeed == want) { s_sent ??= lead.RunSpeed; return; }

        Send(session, lead, want);
        Counters.Add(s_zone switch { 0 => "speed.walk", 1 => "speed.jog", _ => "speed.run" });
    }

    static void Release(WorldSession session, CharacterSnapshot? lead)
    {
        if (s_who is not { } id) return;

        if (lead is not null && lead.Id == id && lead.RunSpeed != s_chosen && s_sent is { } ours && lead.RunSpeed == ours)   // ours only: a speed the player set meanwhile stays
        {
            Send(session, lead, s_chosen);
            Counters.Add("speed.restored");
        }
        s_who = null;
        s_sent = null;
        s_zone = -1;
    }

    static void Send(WorldSession session, CharacterSnapshot lead, byte want)
    {
        int step = Step(lead.RunSpeed, want);
        if (step == 0) return;

        session.World.Commands.Enqueue(new RunSpeedCommand([lead.Id], step) { Tick = session.World.Tick });
        s_sent = want;
        s_sentTick = session.World.Tick;
    }
}
