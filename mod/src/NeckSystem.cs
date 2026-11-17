// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The first-person neck limit (user, 2026-11-17: "in first person, please can look 360 degrees around. Should body move/steer once turning neck
// past certain point?"; the approved mock-up): the view turns freely within NeckLimit of the body; past it the body turns on the spot to follow,
// so the view can go all the way round. Walking needs none of this (W A S D steer the body toward the view already).
// The host sends a TurnCommand (the world's command queue, as every order); this tick system, right after Meitou's movement, turns the named
// character's yaw toward it at TurnRate while it stands idle and still, and drops the turn as soon as it walks or is gone. Meitou's yaw faces
// (sin, cos) on the ground (AiSystem, CarrySystem: Yaw = Atan2(dx, dz)).
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Movement;

namespace PZA.WASDControl.Meitou;

/// <summary>Turn <see cref="Id"/> on the spot to face <see cref="Yaw"/> (Meitou's character yaw).</summary>
public sealed record TurnCommand(CharacterId Id, float Yaw) : SimCommand;

public sealed class NeckSystem : ITickSystem
{
    /// <summary>How far the view may turn from the body before the body follows (90 degrees).</summary>
    public const float NeckLimit = MathF.PI / 2;
    /// <summary>Where a following body stops: the view this far to the side (45 degrees), so it does not turn back and forth at the limit.</summary>
    public const float FollowTo = MathF.PI / 4;
    /// <summary>The body's turn on the spot: half Meitou's walking turn (MovementSystem.TurnRate, 720 degrees / s), so it reads as a turn.</summary>
    public const float Rate = MovementSystem.TurnRate / 2;

    /// <summary>The wanted yaw per character; written in Inputs and SlowWorld (serial), read in Move (parallel).</summary>
    readonly Dictionary<CharacterId, float> _turning = [];

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var c in commands)
        {
            if (c is TurnCommand turn) _turning[turn.Id] = turn.Yaw;
        }
    }

    public void Move(World world, Partition partition)
    {
        if (_turning.Count == 0) return;

        var table = world.Characters;
        foreach (var (id, yaw) in _turning)
        {
            if (!table.TryResolveNext(id, out int slot) || slot < partition.Start || slot >= partition.End) continue;

            ref var n = ref table.Next[slot];
            if (!Standing(n)) continue;

            n.Yaw = Step(n.Yaw, yaw, Rate * world.TickSeconds);
        }
    }

    /// <summary>Serial: a turn that is done, or whose character walks or is gone, leaves the list.</summary>
    public void SlowWorld(World world)
    {
        if (_turning.Count == 0) return;

        var table = world.Characters;
        List<CharacterId>? done = null;
        foreach (var (id, yaw) in _turning)
        {
            bool gone = !table.TryResolveNext(id, out int slot);
            if (gone || !Standing(table.Next[slot]) || MathF.Abs(Delta(table.Next[slot].Yaw, yaw)) < 1e-3f) (done ??= []).Add(id);
        }
        if (done is not null) foreach (var id in done) _turning.Remove(id);
    }

    static bool Standing(in CharacterHot n) =>
        n.Task == (byte)CharacterTask.Idle && new Vector2(n.Velocity.X, n.Velocity.Z).LengthSquared() < 1;

    /// <summary>The signed angle from <paramref name="from"/> to <paramref name="to"/>, -pi..pi.</summary>
    public static float Delta(float from, float to) => MathF.IEEERemainder(to - from, MathF.Tau);

    /// <summary><paramref name="yaw"/> turned toward <paramref name="target"/> by at most <paramref name="most"/>, the short way.</summary>
    public static float Step(float yaw, float target, float most)
    {
        float d = Delta(yaw, target);
        return MathF.Abs(d) <= most ? target : yaw + MathF.CopySign(most, d);
    }

    /// <summary>
    /// The body yaw a first-person view at <paramref name="viewYaw"/> (the way the eye looks, in character-yaw terms) wants from a body at
    /// <paramref name="bodyYaw"/>: null within the neck limit, else the yaw that leaves the view FollowTo to the same side.
    /// </summary>
    public static float? Follow(float bodyYaw, float viewYaw)
    {
        float d = Delta(bodyYaw, viewYaw);
        if (MathF.Abs(d) <= NeckLimit) return null;

        return viewYaw - MathF.CopySign(FollowTo, d);
    }
}
