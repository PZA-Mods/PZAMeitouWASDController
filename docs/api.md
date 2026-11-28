# WasdApi: PZA's WASD & Controller Support for other mods

`PZA.WASDControl.Meitou.WasdApi` is a static class. Find it by its type name, so your mod does not need ours to build or run. Read `Version` first. Members are only ever added, never renamed or removed. Every member is safe to call from any thread and never throws.

**Use this class only.** WASD's other types (`Driver`, `Views` and the rest) are internal and change between versions. Reading `Driver.On` by reflection stops working when it changes; read `WasdApi.On` instead.

## Version 1 (0.11.1)

| Member | What it is |
|---|---|
| `const int Version` | The contract version (2 since 0.11.1) |
| `bool On` | WASD Control is on: W A S D and the pad steer the first selected character |
| `bool ViewActive` | One of WASD's views (over the shoulder, first person) owns the camera |
| `string ViewName` | `"Normal"`, `"Shoulder"` or `"First"` |
| `bool SetNormalView(string caller)` | Back to the normal camera on WASD's next frame. True when a view was on |

## Version 2 (0.11.1): for mods that fight the steered character by hand

| Member | What it is |
|---|---|
| `CharacterId? Steered` | The character WASD steers now, or null (WASD off, nobody selected, down, carried, in a bed or cage). Updated every control tick |
| `Vector2 Intent` | The player's push relative to the camera: X right, Y forward, length 0 to 1. The keys and the left stick give the same numbers, so one code path covers keyboard and pad. Reported while the steering is held too |
| `Vector2 WorldDirection` | `Intent` on the world's ground plane (X, Z of a world position), as WASD would walk |
| `bool HoldSteering(string caller, int ticks = 30)` | WASD sends **no move and no stop order** for the steered character for the next `ticks` world ticks (1 to 600; 30 is about a second at speed 1). Call it again every tick to keep the hold. A hold that is not renewed ends by itself, so if your mod stops calling it (turned off, crashed), the player is never stuck. No stop is sent when the hold starts, because a stop ends a Meitou fight. Holds end on a new world and when WASD is turned off. Returns true when the hold is taken (false while WASD is off) |
| `bool ReleaseSteering(string caller)` | Ends your hold now. True when you had one |
| `bool SteeringHeld` | Some mod holds the steering now |
| `string[] Keys` | WASD's keys now, as `"name=key"`: `toggle=V`, `shoulder=Z`, `first=N`, `take=G`, `jump=Shift+U`, `move=W,A,S,D`. The player can rebind them; read this list rather than assuming the defaults |

**A fight, the WASD way.** While the steered character fights:
1. Call `HoldSteering("your.mod.id")` every tick. Meitou's `CombatSystem` ends a fight on any move order or stop command, and WASD's steering sends both. The hold replaces a patch on `CommandQueue.TakeDue`.
2. Read `Intent` for footwork or the swing's direction. It covers W A S D and the stick alike.
3. Send your own orders.

When the fight ends, call `ReleaseSteering` (or just stop calling `HoldSteering`) and WASD steers again.

**Keys and the pad.** WASD's layout is fixed; other mods adapt to it. Before you bind a key, check `Keys`. Shift + a letter belongs to WASD where it is listed (the jump is Shift+U). The pad layout is in `docs/controller-layout.png`: every pad button is in use, so a combat mod should read the pad through `Intent`, not take buttons for itself.

## Example (reflection, no reference to our DLL)

```csharp
static Type? s_wasd;
static Type? Wasd => s_wasd ??= AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("PZA.WASDControl.Meitou.WasdApi", false)).FirstOrDefault(t => t is not null);

static int WasdVersion => Wasd?.GetField("Version")?.GetValue(null) is int v ? v : 0;
static bool WasdOn => Wasd?.GetProperty("On")?.GetValue(null) is true;
static CharacterId? WasdSteered => WasdVersion >= 2 ? (CharacterId?)Wasd!.GetProperty("Steered")!.GetValue(null) : null;
static Vector2 WasdIntent => WasdVersion >= 2 && Wasd!.GetProperty("Intent")!.GetValue(null) is Vector2 v ? v : Vector2.Zero;

// every tick while the steered character fights:
if (WasdVersion >= 2) Wasd!.GetMethod("HoldSteering")!.Invoke(null, ["my.combat.mod", 30]);
```

Cache the `MethodInfo` / `PropertyInfo` objects rather than looking them up every tick.

Questions or a member you need: open an issue on the public repo, PZA-Mods/PZAMeitouWASDController.
