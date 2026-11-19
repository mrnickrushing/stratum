# Stratum

An open-world voxel sandbox for Windows and Linux by Rushing Technologies. Endless generated land: meadows, oak and
birch woods, pine forests, frostlands, dunes, peaks, rivers and seas, with caves and ore under it. Dig, gather, craft
tools at a workbench, smelt at a furnace, and build. Survival and Creative modes, alone or together on a server.

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
| T or Enter / | chat on a server (`/` starts a command; `/list` shows who's on) |
| Esc | pause |
| F1 / F2 / F3 / F11 | hide HUD / screenshot / debug info / fullscreen |

## Layout

- `Assets/Scripts/Core` (no Unity references, `Stratum.Core.asmdef`): blocks, noise, chunks, world generation, the
  mesher (light and ambient occlusion), items, recipes, inventory, mining.
- `Assets/Scripts/Game`: `App` (GameRoot boots in the empty `Main` scene, sky, sound, settings, saving),
  `World/VoxelWorld` (chunk streaming on worker threads, saving edited chunks), `Player`, `Art` (textures, icons),
  `UI` (HUD, backpack, menus).
- `Assets/Scripts/Game/Net`: the connection to a server (`NetClient`) and other players' figures (`Avatars`);
  `App/GameRoot.Net.cs` plays a server's world, `UI/Hud.Chat.cs` is chat.
- `Assets/Scripts/Core/Net.cs`: the multiplayer protocol, shared by the game and the server. `Core/Water.cs`: flowing
  water, run by whoever owns the world.
- `Server/`: the dedicated server (.NET 8 console app; it compiles the game's Core code).
- `Assets/Resources/Shaders`: blocks, water, sky, clouds, overlay, other players.
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

## Multiplayer

Main menu > **Multiplayer**: a name, the server's address (`host` or `host:port`, default port 25600) and its
password if it has one. The server owns the world: it makes the land, keeps every change and checks each edit (in
reach, agreeing on what was there). Each copy of the game keeps a secret key, and the server knows the player by its
hash, keeping their place, health and backpack between visits. Chat with T.

### Running a server

```bash
cd Server && dotnet run -c Release                     # .NET 8 SDK; listens on 25600, world in ./world
dotnet run -c Release -- --selftest                    # two scripted players check joining, edits, chat, saving
docker build -f Server/Dockerfile -t stratum-server .   # from the repository root
docker run -p 25600:25600 -v stratum-world:/data stratum-server
```

Settings are environment variables (or `--flag value`): `STRATUM_PORT`, `STRATUM_DATA` (world folder; `/data` in
Docker), `STRATUM_SEED` (a new world only; text works like the New World box), `STRATUM_NAME`, `STRATUM_MOTD`,
`STRATUM_MAX_PLAYERS` (10), `STRATUM_PASSWORD`, `STRATUM_CREATIVE` (1), `STRATUM_VIEW` (most chunks seen each way,
10). The world saves every minute and on SIGTERM.

**Railway:** a service from this repository built from `Server/Dockerfile` (the service's Dockerfile path), a volume
mounted at `/data`, and a TCP proxy to port 25600. Players join at the proxy's
`host:port`.

### Testing two players

`-mptest <folder> -connect host:port -name X -mprole host|guest`: two copies of the game join one server; the host
builds a sunstone pillar by the spawn, the guest finds it and breaks its top block, and each takes screenshots of the
other and logs what it saw to `smoke.txt`. `-connect host:port -name X` alone joins straight from the command line.
