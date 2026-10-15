# How LocalXI works

LocalXI glues together existing open-source pieces. It never modifies them; it downloads pinned builds,
configures them from the outside, and starts and stops them in the right order.

## Folders

```
%LOCALAPPDATA%\LocalXI\
  config.json             launcher settings (mode, game path, ports, generated DB password, login)
  mariadb-client.cnf      credentials for MariaDB's command-line tools (keeps the password off command lines)
  downloads\              verified downloads, kept so reinstalling doesn't download again
  runtime\.components\    one marker per installed component (version + hash)
  runtime\mariadb\        portable MariaDB
  runtime\server\         LandSandBoat build (+ navmeshes\, ximeshes\, log\)
  runtime\ashita\         Ashita v4, with bootloader\xiloader.exe added
  database\               MariaDB data folder: the live world
  logs\                   one log per program

Documents\LocalXI\
  Saves\                  exported .xisave files
  Backups\                automatic backups (auto-*, before-import-*, manual-*)
  Screenshots\            the game's own screenshot folder
```

`portable.txt` next to `LocalXI.exe` moves all of this to `LocalXI-data\` beside the exe.

## Components (`components.json`)

Every download is pinned to a version and SHA-256 hash and checked before use. Kinds:

- **zip**: replaces its folder on upgrade, carrying over `preserve` subfolders (logs, meshes, Ashita config).
- **zip-overlay**: extracted on top of another component's folder (meshes into the server folder).
- **file**: one file copied in (xiloader into Ashita's `bootloader\`).
- **installer**: a Microsoft installer run elevated only when its `detect` check fails (Visual C++ runtime).

Components have a role: `client` components are needed on every PC; `server` components only on PCs that
run a server (Solo and Host). Join mode needs about 20 MB.

The LandSandBoat server and meshes are built by `.github/workflows/build-server.yml`, because LandSandBoat
doesn't publish Windows binaries. See [RELEASING.md](RELEASING.md).

## The database

MariaDB runs from the runtime folder as an ordinary process, not a Windows service. On first run
`mariadb-install-db` creates `database\` with a random root password. The launcher then sets
`bind-address=127.0.0.1` (only this PC can reach it, even when hosting) and LandSandBoat's expected
`utf8mb4_general_ci` collation, and picks a free port starting at 3307 so it never collides with another MySQL.

Game data is loaded the way LandSandBoat's `tools/dbtool.py` does it: every `sql/*.sql` file in alphabetical
order with `triggers.sql` last, each piped through the `mariadb` command-line client. The client is needed
rather than a driver because the files use client features such as `DELIMITER`. A `localxi_meta` table records
that the import finished.

## Starting the server

`LsbServer` starts LandSandBoat's programs in the order of its docker-compose file, waiting for each to listen
before starting the next:

| Program | Role | Listens on |
|---|---|---|
| `xi_world` | message broker the others connect to | TCP 54003 |
| `xi_connect` | login / lobby | TCP 54001, 54230, 54231 |
| `xi_search` | search, auction house | TCP 54002 |
| `xi_profile` | PlayOnline profile (if present) | TCP 51220 |
| `xi_map` | the game world | UDP 54230 |

Settings are passed as environment variables, which LandSandBoat applies over its `settings/*.lua`
(`XI_<FILE>_<SETTING>`, e.g. `XI_NETWORK_SQL_PASSWORD`), so the launcher never edits the server's files.
Note that LandSandBoat turns all-digit values into numbers, which is why generated passwords always start with
a letter.

A program counts as started once its ports are open **and** it has logged LandSandBoat's
"The <name>-server is ready to work" line. Ports open long before that: the map server spends anywhere from a
few seconds to over a minute loading scripts, items and zones. If a future version stops printing the line,
open ports plus a grace period (20 s, or 4 minutes for the map server) is enough to carry on.

Consoles are hidden and output goes to `logs\` and the Logs page. Stopping sends Ctrl+C to each program (so
the map server saves characters), waits, and only then force-closes, in reverse order. A fully started server
stops cleanly in a second or two; a map server still starting up ignores Ctrl+C and is force-closed after 20 s.

In **modern** mode `xi_map` runs with `--lazy` (zones load when someone enters them), which starts in seconds.
**Classic** mode loads every zone at start-up: many `era/lua` overrides target zone scripts (notorious monster
respawns, shop stock, fame checks), and with lazy zones those scripts don't exist yet when the overrides are
applied, so 114 of them were silently skipped. Loading every zone takes the map server to about 2.8 GB of RAM.

## Game era

`EraPresets` maps the launcher's era choice onto LandSandBoat's own era support (its `modules/README.md`):

| | Classic (default) | Modern |
|---|---|---|
| `XI_MAIN_MAX_LEVEL` | 75 | LandSandBoat default (99) |
| `XI_MAIN_RESTRICT_CONTENT` | 1: content tagged for disabled expansions isn't loaded | default |
| Expansions (`XI_MAIN_ENABLE_*`) | ROTZ, COP, TOAU, WOTG, ACP, AMK, ASA on; ABYSSEA, VOIDWATCH, SOA, ROV, TVR off | all on |
| Later systems | Mog Garden, Mog House 2F, Records of Eminence, Voidwalker, daily tally off | on |
| `modules/init.txt` | `era/lua` | `era/lua` (its overrides only apply to disabled expansions, so they do nothing here) |
| `era/sql` data packs | abyssea, voidwatch, soa, rov, tvr (whichever exist) | none |

The settings travel as environment variables at server start, so switching is instant for them. The data packs
edit game tables in place, so the launcher records the era in `localxi_meta`. When it changes, the next
Play backs up characters (`before-era-change-*`), re-imports game data (player tables untouched) and applies
the packs for the new era. The packs only touch game-data tables (abilities, spells, drops, recipes, zones...).

The packs are optional extras that can lag behind LandSandBoat's schema, so they're applied with
`mariadb --force`: a stale statement is skipped and reported on the Logs page instead of failing the import.
(As of October 2026, two statements in the `abyssea` pack reference columns that no longer exist.) The main
game data is still imported strictly.

## Solo vs Host vs Join

The address clients use to reach a zone comes from the `zone_settings.zoneip` column, and `xi_map` only serves
zones whose address matches its `--ip` argument. Before each start the launcher sets both:

| Mode | zone address / `--ip` | login server binds to | game connects to |
|---|---|---|---|
| Solo | `127.0.0.1` | `127.0.0.1` | `127.0.0.1` |
| Host | chosen LAN/VPN address | `0.0.0.0` | the same chosen address |
| Join | (no local server) | - | the friend's address |

In Host mode the host's own game also connects through the chosen address, so the login and zone connections
come from the same IP, which LandSandBoat checks.

Firewall rules (Host only, added elevated on request) allow the five server programs, and only from
`LocalSubnet` plus the Radmin (26/8), Hamachi (25/8) and Tailscale (100.64/10) ranges, so even a forwarded
router port doesn't expose the server to the whole internet.

## Accounts

- **On a local server**, accounts are written straight to the `accounts` table, mirroring LandSandBoat's
  `auth_session.cpp`: ids start at 1000 and passwords are bcrypt hashes.
- **On a friend's server**, `LoginClient` speaks xiloader's protocol on TCP 54231: a TLS connection (the server
  uses a self-signed certificate), one JSON request (`{"command": 32, "username", "password", "version": [2,2,0], ...}`)
  and one JSON reply (`{"result": 3}` means created). This only works while the host allows account creation,
  which is LandSandBoat's default.
- The login server accepts **TLS 1.3 only** (`tlsv13_server` in `src/login/handler.h`), which Windows 10's built-in
  TLS can't do as a client. Like xiloader bringing its own mbedTLS, `LoginClient` uses BouncyCastle's managed TLS
  rather than `SslStream`, so it works on every Windows version.

## Starting the game

`GameLauncher` writes `runtime\ashita\config\boot\localxi.ini`, based on Ashita's own private-server example.
It sets `.\bootloader\xiloader.exe --server <address> --user <u> --pass <p> --hide` as the boot command, plus the
game's window size, mode and screenshot folder (Ashita applies these instead of the registry). Then it runs
`Ashita-cli.exe localxi.ini`. The game runs inside the `xiloader` process; the launcher watches for that process
to exit, then waits for the server to save the character, backs up and stops the server (unless friends are
still online in Host mode).

Windows finds the game through two kinds of registry entries, both written by Square Enix's installer:

- **COM classes.** xiloader creates PlayOnline (`POLCoreCom`, in `viewer\com\polcore.dll`) and the game
  (`FFXiEntry`, in `FFXi.dll`, which loads `FFXiMain.dll`) with `CoCreateInstance`, so Windows looks the DLLs up
  under `HKLM\SOFTWARE\Classes\CLSID` (32-bit view). Machine-wide entries are what count, because COM ignores
  per-user registrations in elevated processes and the game runs elevated.
- **Install folders.** xiloader hands PlayOnline the folder in `HKLM\SOFTWARE\PlayOnlineUS\InstallFolder`
  value `1000` (PlayOnline Viewer), and the game reads its data from value `0001` (FINAL FANTASY XI).

A folder copied from another PC has neither. `PlayOnlineRegistry` checks both against the chosen folders, and
**Fix registry** writes both. It writes the ten classes the official US installer registers for files in those
folders (game, PlayOnline core, app, contents and movie source), but only those whose DLL exists and isn't
already registered there, so a working install is left alone. When it re-points a class, it also drops any
Windows Installer descriptor (the `InprocServer32` value), which COM would otherwise follow back to the old
install.

## Administrator rights

The launcher runs as a normal user. Registry repair and firewall rules start a second copy of `LocalXI.exe`
with `--elevated <resultFile> <task> ...` behind a UAC prompt; it does that one task, writes the result and
exits. Visual C++ runtime installers are run elevated directly.

`Ashita-cli.exe` declares `requireAdministrator` in its manifest (it injects into the game), so it is started
through the shell, which shows the UAC prompt on every Play; starting it directly fails with "requires
elevation". The game therefore runs elevated. The launcher only watches for its `xiloader` process to close,
which works without inspecting it.

## Saves

See [SAVE-FORMAT.md](SAVE-FORMAT.md).
