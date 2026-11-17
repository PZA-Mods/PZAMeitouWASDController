// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// Walk or run by how far the left stick is pushed (user, 2026-11-17: "Can walk/run speed be controlled by how much tilt is on the L3
// joystick?"). Meitou keeps a speed mode per character (SpeedMode: walk, run, free, group; CharacterSnapshot.RunSpeed) that its own X /
// ZR / speed arrows step with a RunSpeedCommand. While the stick steers:
// - a light push (under PushRun, with a little hysteresis so a half push does not flicker) walks;
// - a full push uses the speed the player chose (the mode it had when the stick took over, or one set with ZR / X meanwhile);
// - let go, the chosen speed is put back.
// Only the controlled character is stepped (the command names it). W A S D on the keyboard are all-or-nothing and keep the chosen speed.
using Meitou.Game.Host;
using Meitou.Simulation;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Player;

namespace PZA.WASDControl.Meitou;

public static class StickSpeed
{
    /// <summary>Past this push (0..1 after the dead zone) the chosen speed; below WalkBelow walking. Between them the last choice holds.</summary>
    public const float RunAbove = 0.62f, WalkBelow = 0.52f;
    /// <summary>A sent change is waited for this many ticks before it is sent again (the snapshot shows it a tick or two later).</summary>
    const int Settle = 8;

    static CharacterId? s_who;
    static byte s_chosen;
    static byte? s_sent;
    static long s_sentTick;
    static bool s_walking;

    /// <summary>The mode a push of <paramref name="tilt"/> wants: walk, the chosen mode, or (between the thresholds) what it was.</summary>
    public static byte Want(float tilt, bool wasWalking, byte chosen, out bool walking)
    {
        walking = tilt < WalkBelow || (wasWalking && tilt < RunAbove);
        return walking ? SpeedMode.Walk : chosen;
    }

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
            // the stick takes over (or the control moved): the speed now is the player's choice
            Release(session, null);
            s_who = lead.Id;
            s_chosen = lead.RunSpeed;
            s_walking = false;
            s_sent = null;
        }
        else if (settled && s_sent is { } sent && lead.RunSpeed != sent)
        {
            // changed by the player meanwhile (ZR / X / the arrows): that is the new choice, and it runs at once
            s_chosen = lead.RunSpeed;
            s_walking = false;
            s_sent = lead.RunSpeed;
            Counters.Add("speed.player_changed");
            return;
        }

        byte want = Want(tilt, s_walking, s_chosen, out s_walking);
        if (!settled || lead.RunSpeed == want) { s_sent ??= lead.RunSpeed; return; }

        Send(session, lead, want);
        Counters.Add(want == SpeedMode.Walk ? "speed.walk" : "speed.chosen");
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
        s_walking = false;
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
