// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The string table: every user-facing or log string goes through here so ru / zh / ja can be added
// (PZA's localization plan). English is the source and the fallback. Setting labels are attribute constants (En.*).
using System.Globalization;

namespace PZA.WASDControl.Meitou;

public static class Strings
{
    /// <summary>English strings that must be constants (attribute arguments).</summary>
    public static class En
    {
        public const string StickWalkLabel = "Stick push: walk, jog or run";
        public const string StickWalkHint = "Controller: a light push on the left stick walks, a medium push jogs, a full push runs. With squad pace on (squad dial, or X) the full push keeps the squad together. Let go and your speed is back.";
        public const string KeysGroup = "Keys";
        public const string ViewsGroup = "Views";
        public const string WindowGroup = "Window";
        public const string KeyToggleLabel = "On / off key";
        public const string KeyShoulderLabel = "Over-the-shoulder key";
        public const string KeyFirstLabel = "First-person key";
        public const string KeyTakeLabel = "Take-control key";
        public const string KeyHint = "Easier in the window's Keys tab, which refuses keys the game already uses.";
        public const string LookSpeedLabel = "Look speed";
        public const string LookSpeedHint = "How fast the mouse and the right stick turn the camera in the views: 1 is normal, lower is slower (down to 0.1), higher faster (up to 2).";
        public const string FovLabel = "Over-the-shoulder field of view";
        public const string FovHint = "How wide the over-the-shoulder view sees, in degrees (the game's camera is 50). Photo mode and PZA's Camera keep their own lens.";
        public const string FirstFovLabel = "First-person field of view";
        public const string FirstFovHint = "How wide the first-person view sees, in degrees (the game's camera is 50; about 90 gives a wide view, good for aiming). Photo mode and PZA's Camera keep their own lens.";
        public const string ShoulderHeightLabel = "Over-the-shoulder height";
        public const string ShoulderHeightHint = "Vertical height of the over-the-shoulder camera above your character's feet (default 15, about 1.5 m). Raise it for a higher view or wider FOV.";
        public const string InvertLabel = "Invert look up / down";
        public const string InvertHint = "Pushing the mouse or stick forward looks down.";
        public const string SideLabel = "Shoulder";
        public const string SideHint = "Which shoulder the over-the-shoulder camera looks over.";
        public const string WindowLabel = "Show the window";
        public const string WindowHint = "The PZA's WASD & Controller Support window and its tiny button.";
        public const string TinyLabel = "Start as the tiny button";
        public const string TinyHint = "Only the tiny WASD button; its + opens the window.";
        public const string PlaceLabel = "Window place";
        public const string PlaceHint = "Saved when you drag it.";
        public const string TinyPlaceLabel = "Tiny button place";
        public const string OpacityLabel = "Tiny button opacity";
        public const string OpacityHint = "The mouse wheel over the tiny button changes it.";
        public const string StartOnLabel = "On at start";
        public const string StartOnHint = "WASD & Controller is on when a game starts. V turns it on and off at any time.";
        public const string SteerLabel = "Steer distance";
        public const string SteerHint = "How far ahead of the character each step points (10 is about a metre). Longer helps on stairs and round corners; the window's CONTROL tab has Short / Normal / Long / Longer.";
        public const string StopLabel = "Stop on release";
        public const string StopHint = "Letting go of W A S D stops the character at once.";
        public const string FollowLabel = "Camera follows";
        public const string FollowHint = "The camera stays on the character you steer while WASD & Controller is on.";
        public const string PromptsLabel = "Controller prompts";
        public const string PromptsHint = "A line at the top of the screen with what the controller's buttons do right now. Shows while you use the controller.";
        public const string LedgesLabel = "Ledges";
        public const string JumpLabel = "Jump";
        public const string JumpHint = "On: Shift+U (or the controller's bottom button with nothing under the crosshair) jumps; pushing a direction hops forward over level or gently sloped ground. Off: no jump at all, and the bottom button only does the safe action, as before.";
        public const string LedgesHint = "At the edge of a drop: Stop keeps your character 75 cm short of it (as before). Fall lets them walk to the edge, and pushing on tumbles them over it; a fall of more than 3 m hurts the legs, more the higher it is (a high fall breaks them). Only the character you steer ever falls.";
        public const string DialTimeLabel = "Time under a dial";
        public const string DialTimeHint = "Controller dials: Slow runs time at a quarter while any dial is open (no other mod needed; with PZA's SlowMo installed it goes through SlowMo so the two stay in step); Pause stops time; Normal leaves it running. Your own pause or speed change always wins.";
        public const string ManualCombatLabel = "Manual combat (ManualCombat mod)";
        public const string ManualCombatHint = "On: ManualCombat runs while WASD is on, with full controller support in fights (RT attack, LT block, B dodge, R3 target, LT+B leave, LT+X swap). Off: turns ManualCombat off cleanly (for vanilla combat or CATS) and remembers it across restarts.";
    }

    static readonly Dictionary<string, string> EnTable = new(StringComparer.Ordinal)
    {
        ["setting.starton.label"] = En.StartOnLabel,
        ["setting.starton.hint"] = En.StartOnHint,
        ["setting.steer.label"] = En.SteerLabel,
        ["setting.steer.hint"] = En.SteerHint,
        ["setting.stop.label"] = En.StopLabel,
        ["setting.stop.hint"] = En.StopHint,
        ["setting.follow.label"] = En.FollowLabel,
        ["setting.follow.hint"] = En.FollowHint,
        ["setting.jump.label"] = En.JumpLabel,
        ["setting.jump.hint"] = En.JumpHint,
        ["setting.fov.label"] = En.FovLabel,
        ["setting.fov.hint"] = En.FovHint,
        ["setting.firstfov.label"] = En.FirstFovLabel,
        ["setting.firstfov.hint"] = En.FirstFovHint,
        ["setting.shoulder_height.label"] = En.ShoulderHeightLabel,
        ["setting.shoulder_height.hint"] = En.ShoulderHeightHint,
        ["setting.ledges.label"] = En.LedgesLabel,
        ["setting.ledges.hint"] = En.LedgesHint,
        ["win.ledges"] = "At a ledge",
        ["win.ledges_stop"] = "STOP",
        ["win.ledges_fall"] = "FALL",
        ["win.ledges_off"] = "UNAVAILABLE",
        ["log.ledge_patch_on"] = "ledges: RagdollSystem.Wanted postfix on (a steered character can tumble off a ledge with Ledges on Fall)",
        ["log.ledge_patch_missing"] = "ledges: RagdollSystem.Wanted(CharacterCold) not found or changed in this Meitou; Ledges stays on Stop",
        ["log.ledge_patch_failed"] = "ledges: the Wanted patch failed ({0}); Ledges stays on Stop",
        ["log.ledge_start"] = "WASD Control: ledge: character {0} tumbles at height {1:0.0} m, throw {2:0.0} m/s.",
        ["log.ledge_refused"] = "WASD Control: ledge: tumble for character {0} refused ({1}).",
        ["log.unstick_back"] = "WASD Control: stuck where no step works: walking back {0:0.0} m to the last spot it could walk from.",
        ["notice.talk_choice"] = "Reply {0} of {1}: {2}  (A says it)",
        ["log.jumpover_on"] = "jump: AnimationSystem.TechniqueOf prefix on (a playing jump shows over leftover crossbow poses and stumbles)",
        ["log.jumpover_missing"] = "jump: AnimationSystem.TechniqueOf not found or changed in this Meitou: after a fight a jump may not show",
        ["log.jumpover_failed"] = "jump: the TechniqueOf patch failed ({0}): after a fight a jump may not show",
        ["log.stumble_patch_on"] = "jump: CombatSystem.ModMove postfix on (a stumble whose stun is over no longer blocks the jump)",
        ["log.stumble_patch_missing"] = "jump: CombatSystem.ModMove not found or changed in this Meitou: a character stumbled in a fight may not jump until stumbled again",
        ["log.stumble_patch_failed"] = "jump: the ModMove patch failed ({0}): a character stumbled in a fight may not jump until stumbled again",
        ["log.jump_combat_hold"] = "WASD Control: jump: Meitou's combat holds character {0} ({1}) at tick {2}: its own move plays, no mod move can.",
        ["log.jump_dive"] = "WASD Control: jump: R3 dive at {0:0.0} m/s (a tumble thrown forward).",
        ["log.jump_leap"] = "WASD Control: jump: leaps off a {0:0.0} m drop at {1:0.0} m/s (a tumble with the leap's throw).",
        ["log.leap_start"] = "WASD Control: leap: character {0} leaps {1:0.0} m to ground {2:0.0} m up.",
        ["log.leap_refused"] = "WASD Control: leap: refused for character {0} ({1}); it jumps in place.",
        ["log.leap_plan"] = "WASD Control: leap: no leap ({0}) past {1:0.0} m.",
        ["log.jump_ask"] = "WASD Control: jump: asked {0} at {1:0.0} m/s, slope {2:0} degrees ({3}).",
        ["log.jump_start"] = "WASD Control: jump: character {0} plays {1} ({2} ticks).",
        ["log.jump_refused"] = "WASD Control: jump: refused for character {0} ({1}).",
        ["log.jump_no_data"] = "WASD Control: jump: no jump techniques in the game data (the data mod with the clips is not loaded); the jump stays off.",
        ["log.jump_no_api"] = "WASD Control: jump: this Meitou has no combat-move source (Meitou 0.21.0 or newer has it); the jump stays off.",
        ["log.ledge_end"] = "WASD Control: ledge: tumble ended ({0}), drop {1:0.0} m, fastest fall {3:0.0} m/s, fell {4:0.0} m, hurt: {2}.",
        ["log.ledge_nudge"] = "WASD Control: ledge: the body stopped {1:0.0} m above the ground; nudge {0} so it falls on.",
        ["log.ledge_wait_reset"] = "WASD Control: ledge: the wait for the next tumble ran {0} ticks ahead of the world's clock (a new world or a jump back in time); cleared.",
        ["log.ledge_wait_long"] = "WASD Control: ledge: at a lip but waiting for {0:0.0} s (tumbling: {1}, ask wait {2} ticks).",
        ["log.ledge_ready_cleared"] = "WASD Control: ledge: a new world was built mid-tumble (a load or a new game); the mark is cleared and the character gets up.",
        ["setting.prompts.label"] = En.PromptsLabel,
        ["setting.prompts.hint"] = En.PromptsHint,
        ["prompt.sep"] = "   ·   ",
        ["prompt.a_verb"] = "A: {0}",
        ["prompt.jump_verb"] = "jump",
        ["prompt.a_hold"] = "Hold LB + RB: orders",
        ["prompt.rt"] = "RT: interact (hold: menu)",
        ["prompt.lt"] = "LT: highlight",
        ["prompt.lb"] = "Hold LB: screens",
        ["prompt.rb"] = "Hold RB: squad",
        ["prompt.fight_rt"] = "RT: attack",
        ["prompt.fight_lt"] = "LT: block",
        ["prompt.fight_b"] = "B: dodge",
        ["prompt.fight_r3"] = "R3: target",
        ["prompt.fight_leave"] = "LT+B: leave",
        ["prompt.fight_swap"] = "LT+X: swap",
        ["win.manual_combat"] = "Manual combat",
        ["log.manual_combat_linked"] = "WASD Control: ManualCombat linked: persisted ON/OFF toggle and controller fight layer on (RT attack, LT block, B dodge, R3 target, LT+B leave, LT+X swap).",
        ["log.manual_combat_toggle"] = "WASD Control: ManualCombat {0} ({1}).",
        ["prompt.views"] = "Y / X: views",
        ["prompt.win_a"] = "A: click",
        ["prompt.win_b"] = "B: close",
        ["prompt.win_stick"] = "Right stick: pointer",
        ["prompt.build_a"] = "A: place",
        ["prompt.build_rotate"] = "LB / RB: rotate",
        ["prompt.build_height"] = "D-pad up / down: height",
        ["prompt.build_slope"] = "D-pad left / right: slope",
        ["prompt.build_floor"] = "LT + D-pad up / down: floor",
        ["prompt.build_undo"] = "X: undo",
        ["prompt.build_confirm"] = "Y: confirm",
        ["prompt.build_b"] = "B: drop building",
        ["prompt.menu_choose"] = "D-pad / right stick: choose",
        ["prompt.menu_pick"] = "Let go of RT: pick",
        ["prompt.menu_close"] = "B: close",
        ["setting.dialtime.label"] = En.DialTimeLabel,
        ["setting.dialtime.hint"] = En.DialTimeHint,
        ["debug.toggle"] = "WASD & Controller: on / off (V)",
        ["log.loaded"] = "PZA's WASD & Controller Support {0} loaded: V turns it on and off; W A S D steer the first selected character, G takes control of your character under the pointer, Z over-the-shoulder view, N first person.",
        ["log.missing"] = "PZA's WASD & Controller Support stays OFF: the game lacks {0} (built for Meitou 0.20.1).",
        ["log.patch"] = "{0}",
        ["log.patch_failed"] = "PZA's WASD & Controller Support stays OFF: patching failed ({0}).",
        ["log.on"] = "WASD Control ON ({0}): W A S D steer the first selected character, the camera follows it.",
        ["log.off"] = "WASD Control OFF ({0}): W A S D move the camera again.",
        ["log.controlled"] = "WASD Control: steering character {0}.",
        ["log.counters"] = "WASD Control [{0}] {1}",
        ["notice.on"] = "PZA's WASD & Controller Support: ON (V turns it off)",
        ["notice.off"] = "PZA's WASD & Controller Support: OFF",
        ["notice.view.shoulder"] = "PZA's WASD & Controller Support: over-the-shoulder view (Z)",
        ["notice.view.first"] = "PZA's WASD & Controller Support: first person (N)",
        ["notice.view.normal"] = "PZA's WASD & Controller Support: normal view",
        ["log.view"] = "WASD Control: {0}.",
        ["log.pad_found"] = "WASD Control: game controller found: {0} (slot {1}).",
        ["log.pad_lost"] = "WASD Control: game controller {0} disconnected.",
        ["log.pad_ready"] = "WASD Control: game controller {0} read idle: in use.",
        ["log.pad_noise"] = "WASD Control: game controller {0} (slot {1}) presses buttons by itself (more than 12 a second): ignored. A Switch Pro controller does this on PC without Steam Input: start Meitou through Steam.",
        ["log.pad_never_idle"] = "WASD Control: game controller {0} (slot {1}) never read idle (a button or stick always on): ignored until unplugged.",
        ["log.pad_steam_ignored"] = "WASD Control: game controller {0} (slot {1}) is taken over by Steam Input: its virtual pad is used instead.",
        ["debug.window"] = "PZA's WASD & Controller Support: window",
        ["log.ui_off"] = "WASD Control: the window could not start (the game lacks what PZA's Meitou UI needs); keys and the controller still work.",
        ["log.key_changed"] = "WASD Control: {0} is now on {1}.",
        ["win.title"] = "PZA'S WASD & CONTROLLER SUPPORT",
        ["tab.control"] = "CONTROL",
        ["tab.keys"] = "KEYS",
        ["tab.help"] = "HELP",
        ["tab.pad"] = "CONTROLLER",
        ["win.state_on"] = "WASD & Controller is ON ({0} turns it off)",
        ["win.state_off"] = "WASD & Controller is OFF ({0} turns it on)",
        ["win.on"] = "On",
        ["win.off"] = "Off",
        ["win.camera"] = "Camera",
        ["win.view_normal"] = "Normal",
        ["win.view_shoulder"] = "Shoulder",
        ["win.view_first"] = "First person",
        ["win.look"] = "Look speed (mouse, right stick): {0}",
        ["win.fov"] = "Over-the-shoulder field of view: {0} degrees",
        ["win.fov_first"] = "First-person field of view: {0} degrees",
        ["win.shoulder_height"] = "Over-the-shoulder height: {0}",
        ["win.less"] = "-",
        ["win.more"] = "+",
        ["win.normal"] = "Normal",
        ["win.invert"] = "Invert look up / down",
        ["win.side"] = "Over-the-shoulder camera: which shoulder",
        ["win.left"] = "Left",
        ["win.right"] = "Right",
        ["win.steer"] = "Steer distance: {0} (10 is about a metre)",
        ["win.steer_short"] = "Short",
        ["win.steer_normal"] = "Normal",
        ["win.steer_long"] = "Long",
        ["win.steer_longer"] = "Longer",
        ["win.dial_time"] = "Time while a controller dial is open",
        ["win.dial_slow"] = "Slow",
        ["win.dial_pause"] = "Pause",
        ["keys.intro"] = "Click a key, then press a letter. Keys the game already uses are refused. Esc gives up.",
        ["keys.tip"] = "Click, then press the new letter",
        ["keys.press"] = "Press a letter (Esc gives up)...",
        ["keys.waiting"] = "press a key...",
        ["keys.cancelled"] = "No change.",
        ["keys.changed"] = "{0}: now {1}.",
        ["keys.refused_move"] = "{0} moves the character: pick another letter.",
        ["keys.refused_ours"] = "{0} is already {1}: pick another letter.",
        ["keys.refused_game"] = "{0} is the game's {1} key: pick another letter.",
        ["key.toggle"] = "On / off",
        ["key.shoulder"] = "Over the shoulder",
        ["key.first"] = "First person",
        ["key.take"] = "Take control (point at your character)",
        ["tiny.on"] = "WASD ON",
        ["tiny.off"] = "WASD OFF",
        ["help.1"] = "WASD & Controller steers the first selected character with W A S D, relative to the camera, and the camera follows them. Let go and they stop. Each step points a few metres ahead along the ground: it climbs stairs and slopes, slides along a wall, column or corner that blocks the way, and stops short of a ledge. The CONTROL tab's Steer distance sets how far ahead (Short 3 m, Normal 4 m, Long 6 m, Longer 8 m; any value on Settings > Mods).",
        ["help.2"] = "Views: over the shoulder and first person. In a view the mouse looks around, the cursor in the middle is your crosshair: right-click a person for their menu, an object for its action; left-click one of your people to see their info. Hold Alt for a free cursor. Bare ground does nothing: walk with W A S D. While you carry someone the views move out of the way: over the shoulder looks from higher and further back, first person from a little further forward. The CONTROL tab's two sliders (also on Settings > Mods): Look speed, 0.1 to 2 (1 is normal; - and + step by 0.05), and the field of view, 30 to 110 degrees (the game's camera is 50): in first person it sets the first-person view's, otherwise the over-the-shoulder view's (both are on Settings > Mods; about 90 in first person gives a wide view for aiming). Photo mode keeps its own lens, and PZA's Camera's 0.5x lens comes back when you leave the view.",
        ["help.3"] = "Controller: any pad Windows or Steam knows. A Switch Pro controller needs Meitou started through Steam (Steam Input). A pad that presses buttons by itself is ignored, with a line in the log.",
        ["help.4"] = "Controller dials: hold RB for the squad dial, LB for the screens dial, LB and RB together for the orders dial (Stances: Kenshi's eight orders, lit when on; Tasks: stop, medic, rescue, prospect, put down, clear jobs, rest, select all; Weapons: auto, hip weapon, fists; the D-pad left / right turns its page). Aim with the right stick, let go to pick; B cancels, and letting go without aiming does nothing. A slice that doesn't apply now is dimmed and stays in its place. If a pick changes nothing in the game, a note says so. While a dial is open time runs at a quarter (no other mod needed; with PZA's SlowMo installed, through SlowMo); the main tab's \"Time while a controller dial is open\" picks Slow, Pause or Normal. Your own pause or speed change always wins. While you use the controller, a line at the top of the screen says what its buttons do right now (setting Controller prompts). Tap A on a person, a door or one of your containers: the safe extra option named under the crosshair (talk, trade, first aid, pick up, loot; open, close, lock a door; your own chest's inventory), never an attack, a lockpick or a theft: those stay on hold RT.",
        ["help.6"] = "Ledges (main tab, also Settings > Mods): STOP keeps your character 75 cm short of a drop. FALL lets them walk right to the edge; push on and they tumble over it as a limp body, carried by their run. Only a drop of 1.5 m or more tumbles them, at a ledge or the top of a cliff; steps, stairs and curbs never do. A wall, parapet or railing at the edge stops them: they just stand there. A drop of up to 3 m does nothing; past that each leg takes blunt damage by how fast the body hit, as a fall from that height: 13.5 for every metre past 3 m (about 40 a leg from 6 m, 80 from 9 m, 100 from 10.4 m, at most 150 from 14 m). Rolling down a slope after landing adds nothing. Higher falls hurt the chest and stomach (from 8 m) and the head (from 10 m) as well, and can kill: about 33 m for a 100-HP chest; the Rebirth bridge kills anyone. Jumping toward a drop of 1.5 m or more (within 2.5 m) with FALL on leaps off it: a limp body thrown in an arc, as a fall. A high fall breaks the legs and they crawl until healed. A fall never cuts a leg off; a high one can kill. Only the character you steer ever falls, never anyone else; one fall at a time, 3 s between falls; a fall ends once the body lies on the ground (a body stuck in the air is nudged down, and every fall ends within 20 s), then they get up the game's own way. While they tumble the game counts them as down (enemies may leave them be, a drawn weapon is put away). Off by default. If a Meitou update changes what this needs, FALL shows unavailable and steps stop at ledges as before. Jump (Settings > Mods): ON jumps with Shift+U or the bottom button on nothing; OFF turns the jump off entirely (its animations never play).",
        ["help.5"] = "The Keys tab changes the mod's keys. Shift+U jumps; Shift+F orders the rest of your active squad to follow you (press again to stop following). X is Meitou's own run speed, Numpad 4 its sneak. Recommended with it: PZA's SlowMo for Meitou (its slow motion, kill cam and window; the D-pad and the dials then go through it). PZA's WASD & Controller Support is by PZA, inspired by Direct Control for Kenshi (smokefoolius).",
        ["pad.1"] = "Left stick: move (relative to the camera); light push walk, medium jog, full run",
        ["pad.2"] = "Left stick click (L3): sneak on / off. Right stick click (R3) during a jump: dive (a ragdoll, GTA style)",
        ["pad.3"] = "Right stick: look around / turn the camera",
        ["pad.21"] = "Right stick left / right with PZA's Director's Kit on a locked camera (Isometric, Diorama, Side view): flick to the next quadrant",
        ["pad.4"] = "Right stick click (R3): focus on the selected character and the camera behind you (as F); hold to look back; during a jump: dive",
        ["pad.5"] = "Bottom button (Xbox A, Switch B): in a window: click (the right stick moves the pointer); picks in a menu; in the world: the safe option named under the crosshair; with nothing under the crosshair: jump (Shift+U on the keyboard)",
        ["pad.6"] = "Right button (Xbox B, Switch A): select / inspect; closes a window; cancels a dial",
        ["pad.7"] = "Top button (Xbox Y, Switch X): over-the-shoulder view",
        ["pad.8"] = "Left button (Xbox X, Switch Y): first-person view",
        ["pad.9"] = "LB / L: tap previous squad member; hold for the screens dial (map, inventory, character, squads, build, crafting, tech, faction, hide UI; right stick aims, let go to open, B cancels); with the UI hidden LB brings it back",
        ["pad.10"] = "RB / R: tap next squad member; hold for the squad wheel (right stick aims, let go to take control; top: everyone; bottom: squad pace; left: follow me; B cancels)",
        ["pad.19"] = "LB + RB together (L + R): hold for the orders dial (stances lit when on; D-pad left / right: the tasks and weapons pages; right stick aims, let go to pick, B cancels)",
        ["pad.20"] = "In build mode: A places at the pointer (right stick); LB / RB rotate; D-pad up / down height, left / right slope; LT + D-pad up / down the floor; X undo; Y confirm; B drops the held building (with none held it closes build mode)",
        ["pad.11"] = "LT / ZL (hold): highlight; with RT / ZR: loot / steal; with the right button: stop",
        ["pad.12"] = "RT / ZR: interact (a right click: attacks a hostile, talks to a friend, loots); hold for the menu, aim, let go to pick",
        ["pad.13"] = "LT + right button / ZL + A (Switch): stop",
        ["pad.14"] = "D-pad up / down: zoom in / out; in a conversation, choose a reply (bottom button says it, works with Dialogue Unframed too)",
        ["pad.15"] = "D-pad left / right: game speed slower / faster; left at normal speed turns slow motion on (PZA's SlowMo's speed with it installed, else 0.30x), right turns it off",
        ["notice.slow_on"] = "SlowMo ON {0}x",
        ["notice.slow_off"] = "SlowMo OFF",
        ["pad.16"] = "Menu / +: pause; hold 1 s: the game menu (as Esc)",
        ["pad.17"] = "View / -: WASD & Controller on / off",
        ["wheel.all"] = "EVERYONE",
        ["wheel.all_count"] = "all {0}",
        ["wheel.everyone"] = "The whole squad",
        ["notice.attack_hold"] = "Not hostile: hold A to attack anyway.",
        ["bark.follow_me"] = "Follow me!",
        ["bark.stop_following"] = "Stop following!",
        ["screen.put_down"] = "PUT DOWN",
        ["screen.clear_jobs"] = "CLEAR JOBS",
        ["log.jobs_cleared"] = "WASD Control: screens dial: {0} job(s) cleared from the controlled character.",
        ["screen.inventory"] = "INVENTORY",
        ["screen.character"] = "CHARACTER",
        ["screen.map"] = "MAP",
        ["screen.squads"] = "SQUADS",
        ["screen.tech"] = "TECH",
        ["screen.crafting"] = "CRAFTING",
        ["screen.build"] = "BUILD",
        ["screen.faction"] = "FACTION",
        ["orders.weapons"] = "WEAPONS",
        ["weapon.auto"] = "AUTO",
        ["weapon.hip"] = "HIP WEAPON",
        ["weapon.fists"] = "FISTS",
        ["pad.22"] = "In the title menu and the save / load / Esc menus: D-pad or left stick move between the buttons and boxes (up / down move the save list), the bottom button presses; on a name box it opens the controller keyboard (D-pad moves, bottom button types, X deletes, Y space, LB capitals, CLEAR empties the box, B closes); the right stick still moves the pointer; in a PZA window (Cat Crawlers' table, the SlowMo or Director's Kit window) the D-pad picks its buttons and answers and the bottom button presses, while the sticks stay yours",
        ["log.new_world"] = "WASD Control: a new world: the last world's steering, waits, holds and view are dropped.",
        ["debug.shoulder"] = "PZA's WASD: shoulder view on / off",
        ["debug.shoulder_now"] = "PZA's WASD: view {0}",
        ["log.api_steering_held"] = "WASD Control: {0} holds the steering (WasdApi.HoldSteering): no move or stop orders while it lasts.",
        ["log.api_normal_view"] = "WASD Control: back to the normal view for {0} (WasdApi.SetNormalView).",
        ["keyboard.caps"] = "CAPS",
        ["keyboard.caps_on"] = "CAPS ON",
        ["keyboard.space"] = "SPACE",
        ["keyboard.delete"] = "DELETE",
        ["keyboard.done"] = "DONE",
        ["keyboard.clear"] = "CLEAR",
        ["keyboard.hint"] = "A type   X delete (hold: more)   Y space   LB capitals   B close",
        ["log.menu_nav_on"] = "menus: the pad walks Meitou's menus (focus, lists, typing) and the controller keyboard is on",
        ["log.menu_nav_missing"] = "menus: this Meitou lacks {0}; the pad keeps the pointer in menus for that part",
        ["log.weapon_patch_on"] = "weapons: Fighter.Weapon postfix on (the Orders dial's Weapons page picks hip weapon or fists)",
        ["log.weapon_patch_missing"] = "weapons: Fighter.Weapon not found or changed in this Meitou: no manual weapon switching",
        ["log.weapon_patch_failed"] = "weapons: the Fighter.Weapon patch failed ({0}): no manual weapon switching",
        ["screen.hide_ui"] = "HIDE UI",   // menus, windows and conversations still show while it is hidden (UiPeek)
        ["screens.title"] = "SCREENS",
        ["screens.hint"] = "Aim, let go of LB",
        ["dial.cancel"] = "B: cancel",
        ["dial.cancel_pages"] = "B: cancel · LB / RB: page",
        ["dial.cancel_pages_dpad"] = "B: cancel · D-pad ◀ ▶: page",
        ["dial.hint"] = "Aim, let go of {0}",
        ["dial.on"] = "on",
        ["dial.off"] = "off",
        ["dial.sub_turn_on"] = "turn on",
        ["dial.sub_turn_off"] = "turn off",
        ["dial.sub_not_now"] = "not now",
        ["dial.not_now"] = "{0}: not now.",
        ["dial.failed"] = "{0} didn't take.",
        ["log.close_stuck"] = "WASD Control: B: the game's Esc left the screens as they were ({1}); closed '{0}' directly.",
        ["log.lens_on"] = "WASD Control: over-the-shoulder lens {0:0} degrees (the game's was {1:0.#}).",
        ["log.lens_back"] = "WASD Control: lens handed back ({0}): {1:0.#} degrees.",
        ["log.lens_elsewhere"] = "WASD Control: the lens was changed by something else ({0}); left as it is ({1:0.#} degrees).",
        ["log.dial_failed"] = "WASD Control: dial pick {0} changed nothing in the game (dial.<slice>.failed).",
        ["pad.name.LeftBumper"] = "LB",
        ["pad.name.RightBumper"] = "RB",
        ["pad.name.South"] = "A",
        ["orders.title"] = "ORDERS",
        ["orders.stances"] = "STANCES",
        ["orders.tasks"] = "TASKS",
        ["stance.hold"] = "HOLD",
        ["stance.block"] = "BLOCK",
        ["stance.passive"] = "PASSIVE",
        ["stance.ranged"] = "RANGED",
        ["stance.jobs"] = "JOBS",
        ["stance.taunt"] = "TAUNT",
        ["stance.sneak"] = "SNEAK",
        ["stance.chase"] = "CHASE",
        ["task.stop"] = "STOP",
        ["task.medic"] = "MEDIC",
        ["task.rescue"] = "RESCUE",
        ["task.prospect"] = "PROSPECT",
        ["task.rest"] = "REST",
        ["task.select_all"] = "SELECT ALL",
        ["target.line"] = "{0}: A {1} · hold RT: menu",
        ["target.door"] = "Door",
        ["target.container"] = "Container (not yours)",
        ["target.our_container"] = "Your container",
        ["target.menu_only"] = "{0}: hold RT: menu",
        ["extra.none"] = "{0}: nothing safe for A; hold RT for the menu.",
        ["wheel.follow"] = "FOLLOW ME",
        ["wheel.follow_sub"] = "the rest",
        ["wheel.follow_name"] = "Everyone follows you",
        ["wheel.unfollow_name"] = "Stop following",
        ["log.follow_me"] = "WASD Control: {0} squad member(s) told to follow the controlled character.",
        ["wheel.pace"] = "SQUAD PACE",
        ["wheel.pace_on"] = "on",
        ["wheel.pace_off"] = "off",
        ["wheel.pace_turn_on"] = "Squad pace: turn on",
        ["wheel.pace_turn_off"] = "Squad pace: turn off",
        ["wheel.page"] = "Page {0} / {1}",
        ["wheel.hint"] = "Aim, let go of RB",
        ["wheel.hint_pages"] = "D-pad / LB: page",
        ["pad.18"] = "In a menu: D-pad or right stick choose; holding interact, let go to pick; right button closes",
        ["log.take"] = "WASD Control: control handed to character {0} (G).",
        ["log.exception"] = "WASD Control error in {0}: {1}",
    };

    /// <summary>
    /// The player's language (0.10.2): PZA's Meitou UI's loader reads lang\meitou.{ru,zh,ja}.txt from the mod folder; a key a file lacks shows
    /// in English. Only the ledge texts are translated so far (machine-drafted). Log lines and format strings stay English.
    /// </summary>
    static PZA.MeitouUi.PzaStrings? s_language;

    /// <summary>Picks the language (Meitou's own) and loads its file; the log lines.</summary>
    public static List<string> InitLanguage(string modFolder, string gameLanguage)
    {
        var t = new PZA.MeitouUi.PzaStrings([.. EnTable.Select(p => (p.Key, p.Value))]);
        var log = t.Init(modFolder, gameLanguage, "auto", "lang/meitou.{code}.txt");
        s_language = t.Lang == "en" ? null : t;
        return log;
    }

    /// <summary>Whether a key exists (English is the full table).</summary>
    public static bool Has(string key) => EnTable.ContainsKey(key);

    /// <summary>Every key (the fuzzer checks each string's placeholders).</summary>
    internal static IEnumerable<string> Keys => EnTable.Keys;

    /// <summary>The string for a key in the player's language (English when it has none; the key itself when missing, so a gap shows). Keys
    /// with placeholders and log lines always come from English (format strings are never translated).</summary>
    public static string Get(string key)
    {
        if (!EnTable.TryGetValue(key, out var en)) return key;
        if (s_language is null || key.StartsWith("log.", StringComparison.Ordinal) || en.Contains('{')) return en;
        return s_language.T(key);
    }

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);
}
