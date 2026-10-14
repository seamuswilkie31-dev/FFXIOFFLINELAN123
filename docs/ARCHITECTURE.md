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

Consoles are hidden and output goes to `logs\` and the Logs page. Stopping sends Ctrl+C to each program (so
the map server saves characters), waits, and only then force-closes, in reverse order.

`xi_map` runs with `--lazy` by default: zones load when someone enters them, which makes start-up much faster
for a handful of players.

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

## Starting the game

`GameLauncher` writes `runtime\ashita\config\boot\localxi.ini`, based on Ashita's own private-server example.
It sets `.\bootloader\xiloader.exe --server <address> --user <u> --pass <p> --hide` as the boot command, plus the
game's window size, mode and screenshot folder (Ashita applies these instead of the registry). Then it runs
`Ashita-cli.exe localxi.ini`. The game runs inside the `xiloader` process; the launcher watches for that process
to exit, then waits for the server to save the character, backs up and stops the server (unless friends are
still online in Host mode).

The game and xiloader find their files through `HKLM\SOFTWARE\PlayOnlineUS\InstallFolder` (32-bit view):
`0001` is the game folder and `1000` the PlayOnline Viewer folder. **Fix registry** writes these.

## Administrator rights

The launcher runs as a normal user. Registry repair and firewall rules start a second copy of `LocalXI.exe`
with `--elevated <resultFile> <task> ...` behind a UAC prompt; it does that one task, writes the result and
exits. Visual C++ runtime installers are run elevated directly.

## Saves

See [SAVE-FORMAT.md](SAVE-FORMAT.md).
