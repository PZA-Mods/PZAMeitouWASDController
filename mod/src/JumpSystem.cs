// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The jump in the world (0.11.0, 2026-11-19; rules: JumpRules).
// - The host (Driver) sends a JumpCommand for the character it steers, with the clip JumpRules chose. This tick system takes it in its serial
//   Inputs step: only the steered player character, conscious, not carried, not in a bed or cage, not tumbling, one jump at a time per
//   character with JumpRules.Cooldown after it. A refusal is counted and logged once per reason.
// - A combat-move source (ModContext.AddCombatMove, Meitou 0.21.0+, found by reflection: this mod also loads on 0.20.1, where the jump stays
//   off with one log line) answers the clip's technique and its progress for that character while the jump lasts. Meitou asks it only while
//   the game plays nothing of its own, plays the technique's clip on the whole body, and moves the character by the clip's ground travel along
//   walkable ground (F164). The source reads only the table this system writes in its serial steps (deterministic, read-only, thread-safe).
// - The techniques are the data mod's disabled COMBAT_TECHNIQUEs (PZA's kmod tool), found here by their anim name.
// - Nothing is saved: a save loaded mid-jump has none, and the character stands. The jumps and counts go into the state hash.
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Population;
using Technique = Meitou.Data.Gameplay.Combat.CombatTechnique;

namespace PZA.WASDControl.Meitou;

/// <summary>Start <see cref="Clip"/> for <see cref="Id"/>.</summary>
public sealed record JumpCommand(CharacterId Id, JumpClip Clip, bool Quick = false) : SimCommand
{
    /// <summary>The planned leap's landing (LeapRules.Plan by the host; 0.11.0): set with Clip Forward, the
    /// character is carried there along the arc (LeapSystem) instead of the clip's own ground travel.</summary>
    public Vector3? Land { get; init; }
}

public sealed class JumpSystem : ITickSystem, IStateHashed
{
    /// <summary>One playing jump: the clip's technique, its first tick, its length in ticks, the tick its cooldown ends.</summary>
    readonly record struct Playing(JumpClip Clip, long Start, int Ticks, long Until, bool Quick, LeapPlan? Leap = null);

    /// <summary>Each world mapped to its own JumpSystem (bound on its first serial Inputs / SlowWorld tick, never in the constructor: a failed
    /// or cancelled load builds systems before ReplaceWorld and must never overwrite the live world's entry).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, JumpSystem> s_byWorld = new();
    /// <summary>The source was registered (Meitou 0.21.0+).</summary>
    public static bool Registered { get; private set; }

    readonly global::Meitou.Simulation.Combat.CombatSystem? _combat;
    readonly Dictionary<CharacterId, Playing> _playing = [];
    readonly Dictionary<JumpClip, Technique> _techniques = [];
    World? _world;
    string? _lastRefusal;
    long _lastRefusalTick, _started;

    public JumpSystem(global::Meitou.Simulation.Combat.CombatSystem? combat)
    {
        _combat = combat;
        FindTechniques();
    }

    void Bind(World world)
    {
        if (ReferenceEquals(_world, world)) return;
        _world = world;
        s_byWorld.AddOrUpdate(world, this);
    }

    static JumpSystem? Of(object? world) => world is not null && s_byWorld.TryGetValue(world, out var s) ? s : null;

    /// <summary>The character playing a jump now in <paramref name="world"/>, for the host (it steers nobody during the clip); false when none.</summary>
    public static bool Jumping(object world, CharacterId id, long tick) => PlayingClip(world, id, tick) is not null;

    /// <summary>This world's combat system (the jump's animation override reads it; World.SystemOf is internal).</summary>
    public static global::Meitou.Simulation.Combat.CombatSystem? CombatOf(object world) => Of(world)?._combat;

    /// <summary>The tick <paramref name="id"/> may jump again (its jump's cooldown end); long.MinValue when it may now.</summary>
    public static long ReadyAt(object world, CharacterId id) =>
        Of(world) is { } s && s._playing.TryGetValue(id, out var p) ? p.Until : long.MinValue;

    /// <summary>The clip <paramref name="id"/> plays now in <paramref name="world"/>, or null.</summary>
    public static JumpClip? PlayingClip(object world, CharacterId id, long tick) =>
        Of(world) is { } s && s._playing.TryGetValue(id, out var p) && tick < p.Start + p.Ticks ? p.Clip : null;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        Bind(world);
        foreach (var c in commands)
            if (c is JumpCommand j) Start(world, j);
    }

    static long s_staleSeen;

    public void SlowWorld(World world)
    {
        Bind(world);
        long fired = Interlocked.Read(ref StaleStumblePatch.Fired) + Interlocked.Read(ref JumpOverPatch.Fired);
        if (fired != s_staleSeen)
        {
            s_staleSeen = fired;
            Counters.Add("jump.stale_stumble_answered");   // ticks the ModMove postfix answered the jump past a stale stumble
        }
        if (_playing.Count == 0) return;

        // serial: drop the jumps whose cooldown has ended (the source never sees a table being changed)
        foreach (var id in _playing.Where(kv => world.Tick >= kv.Value.Until).Select(kv => kv.Key).ToList())
        {
            _playing.Remove(id);
            Counters.Add("jump.ended");
        }
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_playing.Count);
        foreach (var (id, p) in _playing.OrderBy(kv => kv.Key.Slot))
        {
            hasher.Add(id);
            hasher.Add((int)p.Clip);
            hasher.Add(p.Start);
            hasher.Add(p.Ticks);
            hasher.Add(p.Quick ? 1 : 0);
            if (p.Leap is { } l)
            {
                hasher.Add(l.From.X); hasher.Add(l.From.Y); hasher.Add(l.From.Z);
                hasher.Add(l.To.X); hasher.Add(l.To.Y); hasher.Add(l.To.Z);
            }
        }
        hasher.Add(_started);
    }

    #region Start

    void Start(World world, JumpCommand j)
    {
        var table = world.Characters;
        string? why;
        long tick = world.Tick;
        if (!Registered) why = "no_source";
        else if (!_techniques.ContainsKey(j.Clip)) why = "no_technique";
        else if (!table.TryResolveNext(j.Id, out int slot) || table.Cold(slot) is not { } cold) why = "gone";
        else if (!cold.IsPlayer) why = "not_player";
        else if (!TumbleSystem.SteeredIs(j.Id)) why = "not_steered";
        else
        {
            bool down = cold.Medical is not { Dead: false, Incapacitated: false };
            bool crippled = cold.Medical is { Crawling: true } or { PlayingDead: true };
            long until = _playing.TryGetValue(j.Id, out var p) ? p.Until : long.MinValue;
            why = JumpRules.Refuse(down, !cold.CarriedBy.IsNone, cold.InSomething != default, TumbleSystem.Tumbling == j.Id, tick, until, crippled);
            why ??= CombatHold(j.Id, tick);
        }

        LeapPlan? leap = null;
        if (why is null && j.Land is { } land && j.Clip == JumpClip.Forward && table.TryResolveNext(j.Id, out int ls))
        {
            var plan = new LeapPlan(table.Next[ls].Position, land);
            string? blocked = LeapBlocked(plan);
            if (blocked is null)
            {
                leap = plan;
                // no walk survives the leap (headless run 2026-11-20: the walk ordered before the jump went on from the landing, along
                // its old path up a walkway and down stairs, 9 m away); Meitou's own stand, as its teleport
                global::Meitou.Simulation.Ai.TaskStart.Stand(table, ls);
                Counters.Add("leap.started");
                Log.Line(Strings.Format("log.leap_start", j.Id.Slot, Vector2.Distance(new(plan.From.X, plan.From.Z), new(land.X, land.Z)) / 10f,
                    (land.Y - plan.From.Y) / 10f));
            }
            else
            {
                Counters.Add("leap.refused." + blocked);
                Log.Line(Strings.Format("log.leap_refused", j.Id.Slot, blocked));
                j = j with { Clip = JumpClip.Up, Land = null };   // the honest jump in place, never into a wall
            }
        }

        if (why is null)
        {
            int ticks = JumpRules.Ticks(j.Clip, world.TickSeconds, j.Quick);
            long until = tick + ticks + (long)MathF.Ceiling(JumpRules.Cooldown / world.TickSeconds);
            _playing[j.Id] = new Playing(j.Clip, tick, ticks, until, j.Quick, leap);
            Counters.Add(j.Quick ? "jump.quick" : "jump.full");
            _started++;
            Counters.Add("jump.started." + JumpRules.Name(j.Clip));
            Log.Line(Strings.Format("log.jump_start", j.Id.Slot, JumpRules.Name(j.Clip), ticks));
            return;
        }

        Counters.Add("jump.refused." + why);
        if (why != _lastRefusal || tick - _lastRefusalTick >= 150)
        {
            Log.Line(Strings.Format("log.jump_refused", j.Id.Slot, why));
            (_lastRefusal, _lastRefusalTick) = (why, tick);
        }
    }

    readonly List<Vector3> _triangles = [];

    /// <summary>Why the leap's body would hit a building (LeapRules.BodyClear on the building collision near the arc), or null when clear:
    /// "blocked", "unread" (not read yet), "no_buildings" (no collision source: refused, as the ledge fall's way test).</summary>
    string? LeapBlocked(LeapPlan plan)
    {
        if (_combat?.Buildings is not { } env) return "no_buildings";
        var (min, max) = LeapRules.Box(plan);
        _triangles.Clear();
        if (!env.Buildings(min, max, _triangles)) return "unread";
        return LeapRules.BodyClear(plan, _triangles) ? null : "blocked";
    }

    /// <summary>
    /// Where the leaper <paramref name="id"/> stands at <paramref name="tick"/> (LeapRules.At on its clip's time), or null when it leaps
    /// not. Read by LeapSystem in its parallel Act: the table is only changed in this system's serial steps.
    /// </summary>
    public static Vector3? LeapAt(object world, CharacterId id, long tick)
    {
        if (Of(world) is not { } s || !s._playing.TryGetValue(id, out var p) || p.Leap is not { } leap) return null;
        if (tick < p.Start || tick >= p.Until) return null;
        float window = JumpRules.Progress(p.Start, p.Ticks, tick) ?? 1f;
        float clipTime = JumpRules.ClipProgress(p.Clip, p.Quick, window) * JumpRules.Length(p.Clip);
        return LeapRules.At(leap, clipTime, JumpRules.Takeoff(p.Clip), JumpRules.Landing(p.Clip));
    }

    /// <summary>The characters with a leap playing in <paramref name="world"/> (for LeapSystem's loop; a copy is not needed: changed only in serial steps).</summary>
    public static IEnumerable<CharacterId> LeapersOf(object world) => Of(world) is { } s ? s._playing.Where(kv => kv.Value.Leap is not null).Select(kv => kv.Key) : [];

    /// <summary>
    /// Why Meitou would not play a mod move for <paramref name="id"/> now (CombatSystem.ModMove asks the sources only for a character it holds in
    /// no move of its own: not down, attacking, reacting, stumbling or stunned), or null. Play test 2026-11-20: after a fight one character's
    /// jumps were "played" by this system and never shown, while another character's were; this names the field and the technique holding it.
    /// </summary>
    string? CombatHold(CharacterId id, long tick)
    {
        if (_combat is null) return null;
        var s = _combat.StateOf(id);
        // When JumpOverPatch is installed on AnimationSystem.TechniqueOf, stumbles and post-hit staggers no longer lock out jumping.
        bool allowOverStumble = JumpOverPatch.Installed;
        string? which = s.Down ? "down" : s.AttackTech >= 0 ? "attack " + Tech(s.AttackTech) : s.ReactTech >= 0 ? "reaction " + Tech(s.ReactTech)
            : !allowOverStumble && s.StumbleClip >= 0 && (tick < s.StunUntil || !StaleStumblePatch.Installed) ? "stumble " + s.StumbleClip
            : !allowOverStumble && tick < s.StunUntil ? $"stunned until {s.StunUntil}" : null;
        if (which is null) return null;
        Log.Line(Strings.Format("log.jump_combat_hold", id.Slot, which, tick));
        return "combat_" + which.Split(' ')[0];
    }

    string Tech(int index) => _combat!.Techniques is { } t && index < t.Count ? $"{index} {t[index].AnimName}" : index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The data mod's techniques by anim name (the first of each name).</summary>
    void FindTechniques()
    {
        if (_combat is null) return;

        foreach (JumpClip c in Enum.GetValues<JumpClip>())
        {
            if (c == JumpClip.None) continue;
            if (_combat.Techniques.FirstOrDefault(t => t.AnimName == JumpRules.Name(c)) is { } t) _techniques[c] = t;
        }
        if (_techniques.Count == 0) Log.Line(Strings.Get("log.jump_no_data"));
        Counters.Add("jump.techniques." + _techniques.Count);
    }

    #endregion

    #region The combat-move source (Meitou 0.21.0+)

    /// <summary>The technique and progress for <paramref name="id"/> at <paramref name="tick"/>, or null. Read-only: called on any thread.</summary>
    public static (Technique Technique, float Progress)? Answer(object world, CharacterId id, long tick)
    {
        if (Of(world) is not { } s || !s._playing.TryGetValue(id, out var p)) return null;
        if (JumpRules.Progress(p.Start, p.Ticks, tick) is not { } window) return null;
        return s._techniques.TryGetValue(p.Clip, out var t) ? (t, JumpRules.ClipProgress(p.Clip, p.Quick, window)) : null;
    }

    /// <summary>Registers the source through ModContext.AddCombatMove when this Meitou has it; false (and one log line) when it does not.</summary>
    public static bool Register(object context)
    {
        var add = context.GetType().GetMethod("AddCombatMove", BindingFlags.Public | BindingFlags.Instance);
        var source = add?.GetParameters() is [var only] ? SourceFor(only.ParameterType) : null;
        if (add is null || source is null)
        {
            Log.Line(Strings.Get("log.jump_no_api"));
            Counters.Add("jump.no_api");
            return false;
        }

        add.Invoke(context, [source]);
        Registered = true;
        Counters.Add("jump.registered");
        return true;
    }

    /// <summary>A source of Meitou's CombatMoveSource type <paramref name="sourceType"/>, or null when its shape is not the one known (0.21.0+:
    /// (World, CharacterId, long) to CombatMove?, CombatMove(CombatTechnique, float)). The offline check builds and calls it too.</summary>
    public static Delegate? SourceFor(Type sourceType)
    {
        var moveType = sourceType.GetMethod("Invoke")?.ReturnType is { } r ? Nullable.GetUnderlyingType(r) : null;
        var ctor = moveType?.GetConstructor([typeof(Technique), typeof(float)]);
        if (moveType is null || ctor is null || sourceType.GetMethod("Invoke")!.GetParameters().Length != 3) return null;
        return BuildSource(sourceType, moveType, ctor);
    }

    /// <summary>A delegate of Meitou's CombatMoveSource type that calls <see cref="Answer"/> and wraps its answer in a CombatMove.</summary>
    static Delegate BuildSource(Type sourceType, Type moveType, ConstructorInfo ctor)
    {
        var invoke = sourceType.GetMethod("Invoke")!;
        var ps = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        var answer = Expression.Variable(typeof((Technique, float)?), "answer");
        var call = Expression.Call(typeof(JumpSystem).GetMethod(nameof(Answer))!, Expression.Convert(ps[0], typeof(object)), ps[1], ps[2]);
        var value = Expression.Property(answer, "Value");
        var move = Expression.New(ctor, Expression.Field(value, "Item1"), Expression.Field(value, "Item2"));
        var nullable = typeof(Nullable<>).MakeGenericType(moveType);
        var body = Expression.Block(nullable, [answer],
            Expression.Assign(answer, call),
            Expression.Condition(Expression.Property(answer, "HasValue"), Expression.Convert(move, nullable), Expression.Default(nullable)));
        return Expression.Lambda(sourceType, body, ps).Compile();
    }

    #endregion
}
