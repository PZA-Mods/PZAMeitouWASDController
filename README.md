# PZA's WASD & Controller Support for Meitou (beta 0.9.21)

Play Meitou like a third-person game: steer your character yourself with **W A S D** or a **game controller** (Xbox, PlayStation,
Switch Pro and others), the camera following, with an over-the-shoulder view and a first-person view. Press **V** (or the pad's View / -
button) to switch it on and off; while it is off, Meitou plays as usual.

**Steam Workshop:** https://steamcommunity.com/sharedfiles/filedetails/?id=3841676799

**Download:** the mod zip and its source zip are on the [Releases page](https://github.com/PZA-Mods/PZAMeitouWASDController/releases). Building from source: [BUILD.md](BUILD.md).

![PZA's WASD & Controller Support: the controller layout](media/controller-layout.png)

## Keyboard and mouse

| Key | What it does |
|---|---|
| **V** | WASD Control on / off |
| **W A S D** (or the arrows) | Move the first selected character, relative to the camera; let go and they stop. Stairs, walls and ledges are followed as with a right click |
| **Z** | Over-the-shoulder view on / off |
| **N** | First-person view on / off (look all the way round; your character turns with you) |
| **Mouse** | In a view: turns the camera; a small cross marks the middle, and left / right click act on what it is on (looking past your own character) |
| **G** | Point at one of your characters and press G: control moves to them |
| **Alt** (held) | Frees the mouse pointer |
| **X** | Meitou's own run speed (walk, jog, run, squad pace) |
| Q / E, middle mouse, wheel | Turn, tilt and zoom the camera as usual |

Ctrl and P are left alone on purpose (Meitou's camera orbit and photo mode). Nothing fires while you type in a text box. Trade, inventory
and loot windows free the pointer by themselves.

## Game controller

- **Left stick:** move. A light push walks, a medium push jogs, a full push runs (with squad pace on, the full push keeps the squad together).
- **Right stick:** look around, up and down too.
- **RT / ZR:** interact: a tap does what a right click does (attacks a hostile, talks to a friend, loots). **Hold ZR** for the full menu
  (stealth knockout and the other attacks too), aim with the stick, let go to pick.
- **A** (bottom button): in a trade, inventory or loot window, the click (the right stick moves the pointer there); free otherwise.
- **B** (right button): select / inspect; closes a trade, inventory or loot window.
- **Y / X:** over-the-shoulder view / first-person view.
- **LB / RB:** previous / next squad member.
  - **Hold LB** for the **screens dial:** inventory, character, map, squads, tech, crafting, build, faction, and PUT DOWN while you carry
    someone; aim and let go to open.
  - **Hold RB** for the **squad dial:** your squad's faces in a circle; aim and let go to take control. Top: **everyone**. Bottom: **squad
    pace** on / off. Left: **follow me** (your character calls "Follow me!" and the rest of the squad follows you; choose it again and they
    stop). Big squads get pages (D-pad or LB).
- **LT / ZL (hold):** highlight; with ZR: loot / steal. **LT + B:** stop.
- **L3:** sneak. **R3:** camera behind you; hold to look back.
- **D-pad:** zoom (normal view), game speed. **Menu / +:** pause; hold for the game menu. **View / -:** on / off.

The full layout is in the picture above and on the window's KEYS tab. A Switch Pro controller works best when Meitou is started through
Steam (as a non-Steam game), so Steam Input handles it.

## The window
Starts as a tiny **WASD ON / OFF** button (top right): click to switch, **+** opens the window, drag it, mouse wheel for its opacity. Tabs:
CONTROL (on / off, camera view, look speed, invert, shoulder side), KEYS (change the keys; the controller list) and HELP. Everything is also
on **Settings > Mods** under the mod's row.

## Requires
**Meitou 0.20.1 up to 0.21.x.** It refuses to load on other Meitou versions.

## Install
- **Workshop:** subscribe, then tick **PZAWASDControlMeitou** in Meitou's mod list.
- **By hand (GitHub):** unpack `PZAWASDControl.Meitou-0.9.21-beta.zip` into Kenshi's folder (`Kenshi\mods\PZAWASDControl\`), then tick
  **PZAWASDControl** in Meitou's mod list.
- No items or world data; load order doesn't matter. Safe to remove at any time.

## This is a beta
The basics (walking, stairs, the views, the controller) were played a lot. **This build (0.9.21) passed its offline checks on Meitou 0.20.1
and 0.21.0; its newest parts are not played in game yet:** ZR as interact (its tap attacks a hostile), the controller pointer and A in trade /
inventory / loot windows, the screens dial with PUT DOWN, follow me on / off, squad pace with the stick.

Known limits:
- Removing one job from a character's job list has no controller shortcut yet (mouse or keyboard).
- A right click on your own squad member in a view usually opens nothing (Meitou has no action for them there).
- The squad dial shows the squad bar's members, at most 30.
- Follow me's on / off only knows the follows it started itself.
- With Settings' Hardware mouse on there is no crosshair (the look still works).
- It steers one character at a time (the first selected one); the rest follow Meitou's own orders, or you (follow me).
- No manual combat (attack / block / dodge with keys); ZR gives the game's own attack orders.
- D-pad zoom works in the normal view only.
- Controller buttons cannot be rebound yet (keyboard keys can).
- Texts are in English only for now.

## Planned
Ideas, no dates: jobs on the controller (remove one from a dial), a new use for A, actions for your own squad members in a view, mouse-wheel
steps into the views, vibration when your character is hit, texts in Russian, Chinese and Japanese.

## Reporting a bug
What happened, how to make it happen again, keyboard or which controller, your Meitou version and mod list, and Meitou's `log.txt` (in your
Meitou folder) copied right after that session. Our lines start with `pza.wasdcontrol`. Post it in its thread on the Meitou Discord or in the
Workshop page's Bug reports discussion.

## Licence and source
GNU GPL version 3 or later; the source is attached to the release (`-source.zip`). Made by **PZA**. Inspired by Direct Control for Kenshi
(smokefoolius); no code shared. Unofficial fan-made mod; not affiliated with Lo-Fi Games.
