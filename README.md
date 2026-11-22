# Stratum

An open-world voxel sandbox for Windows and Linux by Rushing Technologies. Endless generated land: meadows, oak and
birch woods, pine forests, frostlands, dunes, peaks, rivers and seas, with caves and ore under it. Dig, gather, craft
tools at a workbench, smelt at a furnace, and build. Survival and Creative modes, alone or together on a server.

Everything is made in code: terrain, textures (painted at 64 px per block at launch), icons, sound and music. There are
no imported art files.

Download and install: **https://stratum.rushingtechnologies.com** (Windows installer, Linux one-line install).

0.2 adds the rest of survival: creatures and combat, food and hunger, armour, chests, beds, signs, buckets and flowing
water and lava, farming, XP, enchanting and the anvil, weather, compass/clock/maps, villages with traders, boats,
minecarts and rails, spark power (levers, buttons, plates, wire, torches, lamps, repeaters, doors), and the Ember Deep:
an underworld reached through lit obsidian frames (a strip of the same world at x 56000-64000, 1:8 with the surface;
`Core/Ember.cs`), with Cinder Keeps where the Cinder Colossus waits.

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
- World kinds (`WorldKind`, chosen on New World or per server world, saved with the world): Normal; **Sky Islands**
  (`WorldGen.Sky.cs`: floating islands on a jittered grid, each with its own climate, a domed top and a ragged
  tapering underside; ponds and sealed lava pockets in the big ones; ores through the island stone; no caves, villages
  or bedrock; a sea of clouds drawn under them by `Sky.Islands`; the player's collision is open below y 0, a fall kills
  in survival and leaves the satchel at `Player.LastGround`, creative is caught and put back); and themes that reshape
  the normal land in `WorldGen.Sample`: Frozen Wilds (cold climate, frozen seas, snowy pinewoods), Endless Dunes (hot
  and dry, mostly land), Archipelago (small continents in warm sea; the spawn needs room around it), Giant Peaks
  (mountains everywhere, half as high again), Flatlands (grass at `FlatTop`, no caves). `WorldGen.KindNames/KindHints`
  are the names and blurbs shown in the menus.
- World types added in 0.5 (same `WorldKind` byte, so nothing new on the wire; games before 0.5 are refused from
  them by the Hello features bit `KnowsTypes`): **Towering Wilds** (everything above the sea doubled, mesas and
  spires on top, rivers cut through as gorges, then `WorldGen.Overhangs` bites arches and ledges out of ground above
  y 88 and regrows turf under them; `Column.Lift` raises where bare peaks and snow begin), **Vast Lands** (continent,
  erosion and climate noise sampled at a quarter scale, so regions are four times as wide) and the single-biome
  worlds `OnlyMeadow`..`OnlyPeaks` (all land with rivers, `Column.Only` forces the biome; New World shows them as
  "One Biome" plus a Biome button).
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

## Releasing

1. Bump the version in `Assets/Editor/Build.cs` (and `VERSION` in `site/worker.js`, the sizes/version in
   `site/public/index.html`), build both players, and smoke-test them (below).
2. `Tools/release/package.sh 0.2.0` writes `Builds/release/`: `Stratum-<v>-Setup.exe` (NSIS, 64-bit, per-user, no
   admin; `Tools/release/stratum.nsi`), `Stratum-<v>-Windows.zip`, `Stratum-<v>-Linux.tar.gz` (carries
   `install.sh`), `install.sh` and `SHA256SUMS.txt`.
3. Upload them to the R2 bucket `stratum-downloads` under `v<version>/` (`wrangler r2 object put
   stratum-downloads/v0.2.0/<file> --file Builds/release/<file>`), copy `Tools/release/install.sh` to
   `site/public/`, and `cd site && wrangler deploy` (Worker `stratum-site` on stratum.rushingtechnologies.com:
   static page in `site/public`, `/download/{windows,windows-zip,linux,sums}` streamed from R2).
4. `gh release create v<version>` with the same files, and merge to `main` (Railway redeploys the server).

The Multiplayer screen fills in the default server by itself (`Game/Net/DefaultServer.cs`): built in, and checked at
launch against `site/public/server.json`. If the server's address ever changes, edit that file and redeploy the site;
installed games follow (and keep their remembered logins). Only an address a player types is saved as theirs.

The icon is drawn by `Tools/brand/icon.py` (`Assets/Art/Icon.png`, `Tools/brand/stratum.ico`).

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

Main menu > **Multiplayer**: the server's address (`host` or `host:port`, default port 25600), then **Create Account**
(username, password twice, and the server's invite code if it has one) or **Log In**. Accounts live on the server
(`accounts.json` in the world folder, passwords as salted PBKDF2 hashes); the server keeps each account's place,
health and backpack, so a player can log in from any computer. A login leaves a "remember me" token on that computer
(Forget me removes it), so the password can stay blank next time. Five wrong passwords lock an account for a minute.

The server owns the world: it makes the land, keeps every change and checks each edit (in reach, agreeing on what was
there). Chat with T. **Voice chat:** hold V to talk (Settings > Voice chat: Hold V / Open mic / Off, plus voice volume
and microphone boost). Voices are 16 kHz IMA ADPCM (~8 KB/s while talking), relayed by the server and played from the
speaker's figure (60% placed in the world, so a friend far away is still heard); a speaker's name tag turns green.

**Keeping things and backups (0.5.1):** `keep` as a world's 8th part (or `STRATUM_KEEP=1` for every world) lets players
keep what they carry when they die there; single-player worlds choose it on New World or with `/keep on|off`. The
server copies its whole data folder to `<data>/backups/<date-time>` at start and twice a day and keeps the newest six
(`STRATUM_BACKUPS=0` turns it off); to go back, stop the server and copy a backup's contents over the data folder.
Single-player worlds are copied to `<game data>/backups/<world>/` each time they are opened (newest five kept) and
restored from the Backups button on the world list.

**Extras (0.6):** the main menu's Extras page lists free packs with a USE button (`Game/App/Extras.cs`, `UI/Menus.Extras.cs`).
World packs are a kind, a seed, a way to play and a starting kit; USE makes the world once and reopens it after
that. Tile pictures are `Resources/Extras/<id>.jpg` (800x392). Outfits are colour sets for the avatar; the choice is
sent as one byte after the Hello's features and relayed in `Joined`, so other players see it.

**Crafting and smelting (0.7):** the backpack has a 2x2 crafting grid and a workbench a 3x3 (`UI/Hud.Grid.cs`): one of
each thing a recipe needs, in any squares (recipes have no shapes), and taking the result uses one from every square.
The recipe list stays as the book. A furnace is a container (`Core/Smelting.cs`): slots for the thing, the fuel and
the result, plus two hidden slots (`_burn`, `_cook`) for the fire and the progress, stepped ten times a second by
`Furnaces` in single player and on the server whether or not anyone is near. Coal smelts 8 things, a log or planks
1.5, a stick 0.5; a piece takes 8 seconds. Furnace recipes can no longer be made from the list.

**0.8 (audit fixes):**
- *Logins are sealed* (`Core/Secure.cs`): the server keeps an RSA key in `<data>/server.key`; a game pings on its login
  connection, gets the public key and a one-time number in the Pong, and sends password/token and invite encrypted
  (RSA-OAEP wrapping a fresh AES key, AES-CBC + HMAC for the secrets). The remember-me token comes back hidden with the
  same key. Plain-text logins are refused unless `STRATUM_LEGACY_LOGIN=1`. The game checks the key's fingerprint:
  for our own server against `key` in `site/public/server.json` (the server logs its fingerprint at start; put it
  there and redeploy the site if the key ever changes), for any other server against the one seen on the first visit.
  Only the login is encrypted, not the rest of the session.
- *Crafting patterns* (`Core/Crafting.cs`): tools and building pieces have shapes (may sit anywhere, may be mirrored);
  the rest are any-squares. A self-test lays out every recipe and checks it makes its own thing. Clicking a recipe's
  picture lays it out in the grid.
- *State changes online*: `Blocks.Changes` lets the server accept a door opening, a lever, tilling and slab merging.
- *Backups are used*: `SafeFile.Load` falls back to `.bak` and keeps a damaged file as `.bad` (world list, server
  world/player/accounts files, containers); the server also looks in `<data>/backups`. A world whose manifest can't
  be found anywhere is not started over.
- *Monster light* spreads through open space like the light that is drawn (`MobSim.BlockLight`).
- *CI*: `.github/workflows/server.yml` builds the server and runs the self-test on every push and pull request.
- *Linux installer*: checks the download against `/download/sums` and keeps the old copy until the new one is in place.

**0.9:**
- *Chutes* (`Core/Chutes.cs`): a container block that takes one thing at a time from the chest or furnace above it and
  drops it into the one below (smeltable things into a furnace's top slot, fuel into its fuel slot; under a furnace it
  takes what is done). Chutes carry a hidden `_chute` mark and are stepped every 0.4 s in single player and on the
  server. The block is added last so older worlds' block numbers don't move.
- *Controls* (`App/Keys.cs`, `UI/Menus.Controls.cs`): the movement and everyday keys can be changed; UI size (80 to
  130%, every canvas follows `UIKit.ApplyScale`); sound captions (`Hud.Caption`, with which side the sound came from).
- *Backpack*: the creative palette has a search box; holding the left button with a stack and sweeping over slots
  shares it out (`Hud.Comfort.cs`).
- *Performance* (`App/Perf.cs`): F3 shows the average, the slowest 1% and the worst frame; F4 writes
  `performance-<time>.txt` to the game's data folder with the computer, settings, frame figures, world-entry time and
  memory.
- *First night* self-test: from empty hands to an iron pickaxe using the mining, crafting-grid and furnace rules.
- A chunk job that throws is retried instead of staying queued; a leaving player's chests and edits are written
  with their pack.

**0.10:**
- *Building blocks* (end of `Blocks`, painters under "for houses" in `Art/BlockTextures.cs`): white plaster, timber
  frame, clay roof tiles, polished stone, dark and whitewashed planks, bookshelf, barrel (a container), hearth (gives
  light), lantern, table, stool, red and blue carpet, potted plant, lattice window (joins like a pane), stone pillar.
- **Block numbers are two bytes (since 0.11):** `Blocks.Max = 4096`, 514 in use. Chunk files and messages carry
  two bytes a block; an old one-byte chunk file is told apart by its length and still loads (`Net.Decompress`).
- **Looks** (`Game/App/Looks.cs`): Classic (16 crisp texels a side, the default since 0.13), Stratum (the detailed
  painting), Chunky, Storybook, Pastel, Inked. A look repaints the finished 64 px picture of each block.
- *Interruptions* (regression checks in `GameRoot.DevRegress()`, run in Play mode in a single-player world):
  leaving mid-portal calls the trip off (`AbortPortal`) and saves where the player left from; every stack that is the
  player's but not in a slot (cursor, stations, grid, waiting for room, set aside by a load) is in every save, with no
  limit on this computer and up to `Net.MaxExtraStacks` (215) on a server.

**Worlds:** a server can run several worlds at once (`STRATUM_WORLDS`). The Multiplayer screen asks the server for them
(its ping answers with each world's name, mode, difficulty and who's in it) and shows them beside the login; the pick
is sent with Hello and remembered (`mp_world`). Each world keeps its own land, creatures, chests and players' things,
so creative blocks never reach a survival world; the account is the same everywhere, and a player is in one world at
a time (logging in to another ends the session in the first). Pause > **Change World** saves and returns to the list.
Clients before 0.3.0 don't choose and land in the first world. On the server, `GameServer` is the front door
(listener, accounts, the world list) and each `WorldHost` runs one world on its own thread.

### Running a server

```bash
cd Server && dotnet run -c Release                     # .NET 8 SDK; listens on 25600, world in ./world
dotnet run -c Release -- --selftest                    # two scripted players check joining, edits, chat, saving
docker build -f Server/Dockerfile -t stratum-server .   # from the repository root
docker run -p 25600:25600 -v stratum-world:/data stratum-server
```

Settings are environment variables (or `--flag value`): `STRATUM_PORT`, `STRATUM_DATA` (world folder; `/data` in
Docker), `STRATUM_SEED` (a new world only; text works like the New World box), `STRATUM_NAME`, `STRATUM_MOTD`,
`STRATUM_MAX_PLAYERS` (10), `STRATUM_PASSWORD` (the invite code needed to make an account; logging in never needs it),
`STRATUM_RESET` (`Name1,Name2`: clears those accounts' passwords once, and each owner's next login sets a new one;
their things are kept), `STRATUM_CREATIVE` (1), `STRATUM_DIFFICULTY` (peaceful, easy, normal), `STRATUM_VIEW` (most
chunks seen each way, 10). The world saves every minute and on SIGTERM.

`STRATUM_WORLDS` lists the worlds to choose from, `;` between worlds and `|` between each world's parts:
`id|Name|survival or creative|peaceful, easy or normal|seed|a line about it|kind|keep` (kind: normal, sky, frozen, dunes,
islands, peaks, flat, towering, vast, or `all meadow`, `all oakwood`, `all birch`, `all pine`, `all frost`, `all dunes`,
`all peaks`; a world keeps the kind it was made with, so changing it later needs a new id). Blank parts take the defaults (survival,
normal, `STRATUM_SEED` for `main`, a new seed for the others). The world called `main` is the one in the data folder
itself, so an existing server keeps its world; the others are made in `worlds/<id>`. Unset, the server runs one world
from `STRATUM_CREATIVE` and `STRATUM_DIFFICULTY`. The live server's list:

```
main|Survival|survival|normal||The world we started in. Monsters at night.;newlands|New Lands|survival|easy|newlands|A fresh world to explore together.;peaceful|Peaceful|survival|peaceful||No monsters and no hunger. Just build.;creative|Creative|creative|||Every block, flying, nothing can hurt you.;sky|Sky Islands|survival|easy||Floating islands over a sea of clouds. Build bridges, and don't fall!|sky;frozen|Frozen Wilds|survival|easy||Snow, pine forests and frozen seas.|frozen;dunes|Endless Dunes|survival|easy||Sand as far as you can see, with rivers for oases.|dunes;islands|Archipelago|survival|easy||Warm seas full of little islands. Bring a boat!|islands;peaks|Giant Peaks|survival|normal||Enormous mountains, cliffs and deep valleys.|peaks;towering|Towering Wilds|survival|easy||Huge cliffs, overhangs and arches. Everything is taller.|towering;vast|Vast Lands|survival|easy||The normal world, with every forest, desert and sea four times as wide.|vast;oakwood|All Oakwood|survival|easy||Oak forest everywhere, with rivers.|all oakwood
```

**Railway:** a service from this repository built from `Server/Dockerfile` (the service's Dockerfile path), a volume
mounted at `/data`, and a TCP proxy to port 25600. Players join at the proxy's
`host:port`.

### Testing two players

`-mptest <folder> -connect host:port -name X -password P [-create] [-voicetone] -mprole host|guest`: two copies of the game join one server; the host
builds a sunstone pillar by the spawn, the guest finds it and breaks its top block, and each takes screenshots of the
other and logs what it saw to `smoke.txt` (with `-voicetone`, a made-up voice instead of the microphone, and the voice
frames sent and heard). `-connect host:port -name X -password P [-create]` alone logs in straight from the command line.
