# Stratum

An open-world voxel sandbox for Windows and Linux by Rushing Technologies. Endless generated land: meadows, oak and
birch woods, pine forests, frostlands, dunes, peaks, rivers and seas, with caves and ore under it. Dig, gather, craft
tools at a workbench, smelt at a furnace, and build. Survival and Creative modes.

Everything is made in code: terrain, textures (painted at 64 px per block at launch), icons, sound and music. There are
no imported art files.

## Controls

| | |
|---|---|
| WASD | walk (Shift sprints, Ctrl or C sneaks) |
| Space | jump; double-tap to fly in Creative |
| Mouse | look; left button digs, right button places or uses |
| Middle button | pick the block you're looking at |
| 1-9 / wheel | hotbar |
| E or Tab | backpack and crafting |
| Esc | pause |
| F1 / F2 / F3 / F11 | hide HUD / screenshot / debug info / fullscreen |

## Layout

- `Assets/Scripts/Core` (no Unity references, `Stratum.Core.asmdef`): blocks, noise, chunks, world generation, the
  mesher (light and ambient occlusion), items, recipes, inventory, mining.
- `Assets/Scripts/Game`: `App` (GameRoot boots in the empty `Main` scene, sky, sound, settings, saving),
  `World/VoxelWorld` (chunk streaming on worker threads, saving edited chunks), `Player`, `Art` (textures, icons),
  `UI` (HUD, backpack, menus).
- `Assets/Resources/Shaders`: blocks, water, sky, clouds, overlay.
- Saves: `<persistentDataPath>/worlds/<name>/world.json` plus `chunks/x_z.bin` for chunks the player changed.
  Linux: `~/.config/unity3d/Rushing Technologies/Stratum/`. Windows: `%USERPROFILE%\AppData\LocalLow\Rushing Technologies\Stratum\`.

## Building

Unity 6000.0.85f1 (URP 17). Menu **Stratum > Set Up Project**, then **Build Linux** or **Build Windows** (Windows needs
the Windows Build Support module). From the command line, with the Editor open:

```bash
unity command --project-path . script eval 'Stratum.EditorTools.Build.Linux(); return 0;'
```

Output goes to `Builds/Linux/Stratum.x86_64` or `Builds/Windows/Stratum.exe`.

## Checking a build

`-smoketest <folder>` plays a short tour of a fresh world (title, spawn, backpack, a view from the air, dawn, night,
a torch-lit cave), saves a PNG of each stop and `smoke.txt` with frame rates, then quits. It works without a
screen too:

```bash
xvfb-run -a -s "-screen 0 1600x900x24" Builds/Linux/Stratum.x86_64 -force-glcore -screen-fullscreen 0 \
  -screen-width 1600 -screen-height 900 -smoketest /tmp/smoke
```

`-screen-fullscreen 0` starts the game in a window.
