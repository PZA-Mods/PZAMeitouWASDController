# PZA's WASD & Controller Support for Meitou (beta 0.9.9)

Play Meitou like a third-person game: steer your character yourself with **W A S D** or a **game controller** (Xbox, PlayStation,
Switch Pro and others), the camera following, with an over-the-shoulder view and a first-person view. Press **V** (or the pad's View / -
button) to switch it on and off; while it is off, Meitou plays as usual. (In game its window and settings say "PZA's WASD Control".)

**Download:** the mod zip and its source zip are on the [Releases page](https://github.com/PZA-Mods/PZAMeitouWASDController/releases). Building from source: [BUILD.md](BUILD.md).

![PZA's WASD Control: the controller layout](media/controller-layout.png)

## Hotkeys

| Key | What it does |
|---|---|
| **V** | WASD Control on / off |
| **W A S D** (or the arrows) | Move the first selected character, relative to the camera; let go and they stop |
| **Mouse** | Turns the camera (the crosshair stays in the middle); left / right click act on what is under it |
| **Z** | Over-the-shoulder view on / off |
| **N** | First-person view on / off |
| **G** | Point at one of your characters and press G: control moves to them |
| **Alt** (held) | Frees the mouse pointer |
| **X** | Meitou's own run speed (walk, run, full, squad pace) |
| Q / E, middle mouse, wheel | Turn, tilt and zoom the camera as usual |

Ctrl and P are left alone on purpose (Meitou's camera orbit and photo mode). Nothing fires while you type in a text box.

**Game controller** (the picture above): left stick steers (a light push walks, medium jogs, full runs), right stick looks, the face buttons act and select, LB / RB
switch squad members. Hold RB for the squad dial: aim at a face and let go to take control; its slices also give ALL (everyone), PACE (squad
pace on / off) and FOLLOW (the rest of the squad follows you). RT / ZR is left free for other mods. The full layout is on the window's CONTROLLER tab. A Switch Pro controller works best
when Meitou is started through Steam (as a non-Steam game), so Steam Input handles it.

## The window
Starts as a tiny **WASD ON / OFF** button (top right): click to switch, **+** opens the window, drag it, mouse wheel for its opacity. Tabs:
CONTROL (on / off, camera view, look speed, invert, shoulder side), KEYS, CONTROLLER and HELP. Everything is kept in **Settings > Mods >
PZA's WASD Control**.

## Requires
**Meitou 0.20.1 up to 0.21.x.** It refuses to load on other Meitou versions.

## Install
- Unpack `PZAWASDControl.Meitou-0.9.9-beta.zip` into Kenshi's folder (`Kenshi\mods\PZAWASDControl\`), then tick
  **PZAWASDControl** in Meitou's mod list.
- No items or world data; load order doesn't matter. Safe to remove at any time.

## This is a beta
Earlier builds were played a lot (walking, stairs, the run-speed key, the controller; "working great"). **This build (0.9.9: walk / jog / run by the
stick's push, squad pace and FOLLOW ME on the squad dial, right clicks in a view finding people first) passed its offline checks on Meitou
0.20.1 and 0.21.0 but has not been played in game yet.**

Known limits:
- It steers one character at a time (the first selected one); the rest of the squad follows Meitou's own orders, or you (FOLLOW on the dial).
- Manual combat (attacking with a key) is not in yet.
- Texts are in English only for now.

## Planned
Ideas, no dates:
- **Manual combat:** attack, block and dodge with keys or the pad.
- Texts in Russian, Chinese and Japanese, as the other PZA mods.

## Reporting a bug
What happened, how to make it happen again, keyboard or which controller, your Meitou version and mod list, and Meitou's `log.txt` (in your
Meitou folder) copied right after that session. Our lines start with `pza.wasdcontrol`. Post it in its thread on the Meitou Discord or in the Workshop page's Bug reports discussion.

## Licence and source
GNU GPL version 3 or later; the source is attached to the release (`-source.zip`). Made by **PZA**. Unofficial fan-made mod; not affiliated
with Lo-Fi Games.
