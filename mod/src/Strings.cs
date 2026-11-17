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
        public const string StickWalkLabel = "Stick push: walk or run";
        public const string StickWalkHint = "Controller: a light push on the left stick walks, a full push runs at the speed you chose (ZR / X). Let go and your speed is back.";
        public const string KeysGroup = "Keys";
        public const string ViewsGroup = "Views";
        public const string WindowGroup = "Window";
        public const string KeyToggleLabel = "On / off key";
        public const string KeyShoulderLabel = "Over-the-shoulder key";
        public const string KeyFirstLabel = "First-person key";
        public const string KeyTakeLabel = "Take-control key";
        public const string KeyHint = "Easier in the window's Keys tab, which refuses keys the game already uses.";
        public const string LookSpeedLabel = "Look speed";
        public const string LookSpeedHint = "How fast the mouse and the right stick turn the camera in the views.";
        public const string InvertLabel = "Invert look up / down";
        public const string InvertHint = "Pushing the mouse or stick forward looks down.";
        public const string SideLabel = "Shoulder";
        public const string SideHint = "Which shoulder the over-the-shoulder camera looks over.";
        public const string WindowLabel = "Show the window";
        public const string WindowHint = "The PZA's WASD Control window and its tiny button.";
        public const string TinyLabel = "Start as the tiny button";
        public const string TinyHint = "Only the tiny WASD button; its + opens the window.";
        public const string PlaceLabel = "Window place";
        public const string PlaceHint = "Saved when you drag it.";
        public const string TinyPlaceLabel = "Tiny button place";
        public const string OpacityLabel = "Tiny button opacity";
        public const string OpacityHint = "The mouse wheel over the tiny button changes it.";
        public const string StartOnLabel = "On at start";
        public const string StartOnHint = "WASD Control is on when a game starts. V turns it on and off at any time.";
        public const string SteerLabel = "Steer distance";
        public const string SteerHint = "How far ahead of the character each step points (10 is about a metre).";
        public const string StopLabel = "Stop on release";
        public const string StopHint = "Letting go of W A S D stops the character at once.";
        public const string FollowLabel = "Camera follows";
        public const string FollowHint = "The camera stays on the character you steer while WASD Control is on.";
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
        ["debug.toggle"] = "WASD Control: on / off (V)",
        ["log.loaded"] = "PZA's WASD Control {0} loaded: V turns it on and off; W A S D steer the first selected character, G takes control of your character under the pointer, Z over-the-shoulder view, N first person.",
        ["log.missing"] = "PZA's WASD Control stays OFF: the game lacks {0} (built for Meitou 0.20.1).",
        ["log.patch"] = "{0}",
        ["log.patch_failed"] = "PZA's WASD Control stays OFF: patching failed ({0}).",
        ["log.on"] = "WASD Control ON ({0}): W A S D steer the first selected character, the camera follows it.",
        ["log.off"] = "WASD Control OFF ({0}): W A S D move the camera again.",
        ["log.controlled"] = "WASD Control: steering character {0}.",
        ["log.counters"] = "WASD Control [{0}] {1}",
        ["notice.on"] = "PZA's WASD Control: ON (V turns it off)",
        ["notice.off"] = "PZA's WASD Control: OFF",
        ["notice.view.shoulder"] = "PZA's WASD Control: over-the-shoulder view (Z)",
        ["notice.view.first"] = "PZA's WASD Control: first person (N)",
        ["notice.view.normal"] = "PZA's WASD Control: normal view",
        ["log.view"] = "WASD Control: {0}.",
        ["log.pad_found"] = "WASD Control: game controller found: {0} (slot {1}).",
        ["log.pad_lost"] = "WASD Control: game controller {0} disconnected.",
        ["log.pad_ready"] = "WASD Control: game controller {0} read idle: in use.",
        ["log.pad_noise"] = "WASD Control: game controller {0} (slot {1}) presses buttons by itself (more than 12 a second): ignored. A Switch Pro controller does this on PC without Steam Input: start Meitou through Steam.",
        ["log.pad_never_idle"] = "WASD Control: game controller {0} (slot {1}) never read idle (a button or stick always on): ignored until unplugged.",
        ["log.pad_steam_ignored"] = "WASD Control: game controller {0} (slot {1}) is taken over by Steam Input: its virtual pad is used instead.",
        ["debug.window"] = "PZA's WASD Control: window",
        ["log.ui_off"] = "WASD Control: the window could not start (the game lacks what PZA's Meitou UI needs); keys and the controller still work.",
        ["log.key_changed"] = "WASD Control: {0} is now on {1}.",
        ["win.title"] = "PZA'S WASD CONTROL",
        ["tab.control"] = "CONTROL",
        ["tab.keys"] = "KEYS",
        ["tab.help"] = "HELP",
        ["tab.pad"] = "CONTROLLER",
        ["win.state_on"] = "WASD Control is ON ({0} turns it off)",
        ["win.state_off"] = "WASD Control is OFF ({0} turns it on)",
        ["win.on"] = "On",
        ["win.off"] = "Off",
        ["win.camera"] = "Camera",
        ["win.view_normal"] = "Normal",
        ["win.view_shoulder"] = "Shoulder",
        ["win.view_first"] = "First person",
        ["win.look"] = "Look speed (mouse and right stick, in a view)",
        ["win.slow"] = "Slow",
        ["win.normal"] = "Normal",
        ["win.fast"] = "Fast",
        ["win.invert"] = "Invert look up / down",
        ["win.side"] = "Over-the-shoulder camera: which shoulder",
        ["win.left"] = "Left",
        ["win.right"] = "Right",
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
        ["win.pad"] = "Controller: left stick move, right stick look, right stick click camera behind you (hold: look back), A interact (hold for the menu), B select, L / R previous / next member, left stick click sneak, ZL hold highlight (ZL + A loots / steals), ZR run speed, ZL + ZR stop, top button over the shoulder, left button first person, D-pad zoom and game speed, + pause, - on / off.",
        ["help.1"] = "WASD Control steers the first selected character with W A S D, relative to the camera, and the camera follows them. Let go and they stop. Stairs, walls and ledges are followed as when you right-click.",
        ["help.2"] = "Views: over the shoulder and first person. In a view the mouse looks around, the cursor in the middle is your crosshair: right-click a person for their menu, an object for its action; left-click one of your people to see their info. Hold Alt for a free cursor. Bare ground does nothing: walk with W A S D.",
        ["help.3"] = "Controller: any pad Windows or Steam knows. A Switch Pro controller needs Meitou started through Steam (Steam Input). A pad that presses buttons by itself is ignored, with a line in the log.",
        ["help.4"] = "The Keys tab changes the mod's keys. X is Meitou's own run speed, Numpad 4 its sneak. PZA's WASD Control is by PZA, inspired by Direct Control for Kenshi (smokefoolius).",
        ["pad.1"] = "Left stick: move (relative to the camera); a light push walks, a full push runs",
        ["pad.2"] = "Left stick click (L3): sneak on / off",
        ["pad.3"] = "Right stick: look around / turn the camera",
        ["pad.4"] = "Right stick click (R3): camera behind you; hold to look back",
        ["pad.5"] = "Bottom button (Xbox A, Switch B): interact; hold for the menu",
        ["pad.6"] = "Right button (Xbox B, Switch A): select / inspect",
        ["pad.7"] = "Top button (Xbox Y, Switch X): over-the-shoulder view",
        ["pad.8"] = "Left button (Xbox X, Switch Y): first-person view",
        ["pad.9"] = "LB / L: previous squad member",
        ["pad.10"] = "RB / R: tap next squad member; hold for the squad wheel (right stick aims, let go to take control; top slice: everyone; B cancels)",
        ["pad.11"] = "LT / ZL (hold): highlight; with interact: loot / steal",
        ["pad.12"] = "RT / ZR (tap): run speed",
        ["pad.13"] = "LT + RT / ZL + ZR together: stop",
        ["pad.14"] = "D-pad up / down: zoom in / out",
        ["pad.15"] = "D-pad left / right: game speed slower / faster",
        ["pad.16"] = "Menu / +: pause",
        ["pad.17"] = "View / -: WASD Control on / off",
        ["wheel.all"] = "EVERYONE",
        ["wheel.all_count"] = "all {0}",
        ["wheel.everyone"] = "The whole squad",
        ["wheel.page"] = "Page {0} / {1}",
        ["wheel.hint"] = "Aim, let go of RB",
        ["wheel.hint_pages"] = "D-pad / LB: page",
        ["pad.18"] = "In a menu: D-pad or right stick choose; holding interact, let go to pick; right button closes",
        ["log.take"] = "WASD Control: control handed to character {0} (G).",
        ["log.exception"] = "WASD Control error in {0}: {1}",
    };

    /// <summary>The string for a key (English for now; the key itself when missing, so a gap shows).</summary>
    public static bool Has(string key) => EnTable.ContainsKey(key);

    public static string Get(string key) => EnTable.TryGetValue(key, out var s) ? s : key;

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);
}
