// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The planned leap's flight (0.11.0, 2026-11-20; LeapRules, finding F171): registered at the very end of the tick, so
// it writes the leaper's place after Meitou's movement and after the animation's root motion (step 5, Act), which it overrides. Each tick of a
// leap: the place on the line take-off -> landing for the clip's time (the clip draws the rise), its velocity along the line, standing, no
// path. A leaper is not a walker (no step), so Meitou's movement never puts it back on the ground mid-air (MovementSystem.Place runs only for a
// non-zero step). Counters: leap.flight_ticks, leap.pinned (a tick the place had moved away from the line before this system).
using System.Numerics;
using Meitou.Simulation;

namespace PZA.WASDControl.Meitou;

public sealed class LeapSystem : ITickSystem
{
    public void Act(World world, Partition partition, EffectBuffer effects)
    {
        var table = world.Characters;
        long tick = world.Tick;
        float dt = world.TickSeconds;
        foreach (var id in JumpSystem.LeapersOf(world))
        {
            if (!table.TryResolveNext(id, out int slot) || slot < partition.Start || slot >= partition.End) continue;
            if (JumpSystem.LeapAt(world, id, tick) is not { } at) continue;
            ref var n = ref table.Next[slot];
            var was = n.Position;
            if (Vector3.DistanceSquared(was, at) > 0.25f) Counters.Add("leap.pinned");   // something moved it (root motion, a push)
            var prev = JumpSystem.LeapAt(world, id, tick - 1) ?? at;
            n.Position = at;
            n.Velocity = dt > 0 ? (at - prev) / dt : Vector3.Zero;
            Counters.Add("leap.flight_ticks");
        }
    }
}
