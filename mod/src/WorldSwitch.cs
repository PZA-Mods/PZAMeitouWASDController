// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// WorldSwitch: "is this a world I have not steered yet?" (a new game, a load, back from the title). Meitou builds a new WorldSession for each;
// on a load the new world's Read / Ready run BEFORE the old world's systems are disposed (MeitouClient WorldSession), so nothing here resets on
// a dispose: Driver resets its per-world state on the FIRST control tick of a new session, which can only be the new world's.
// A weak reference: the old world is never kept alive by us.
namespace PZA.WASDControl.Meitou;

public sealed class WorldSwitch
{
    WeakReference<object>? _last;

    /// <summary>True the first time a world is seen (the first world too), false for every later call with that same world.</summary>
    public bool IsNew(object world)
    {
        if (_last is not null && _last.TryGetTarget(out var seen) && ReferenceEquals(seen, world)) return false;
        _last = new WeakReference<object>(world);
        return true;
    }
}

/// <summary>When WASD's view stands aside for PZA's Director's Kit.</summary>
public static class KitRules
{
    /// <summary>CameraApi.Holding when the Kit has it (0.6.1: false on the way back, which then lands on our view), else CameraApi.Busy.</summary>
    public static bool StandAside(bool? holding, bool busy) => holding ?? busy;
}
