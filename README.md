# LocalXI

**Your own FINAL FANTASY XI private server, on your own PC, in a few clicks.**

LocalXI is a launcher for Windows that sets up and runs a [LandSandBoat](https://github.com/LandSandBoat/server)
server and starts the game through [Ashita](https://www.ashitaxi.com) for you. No compiling, no database setup,
no config files. Play solo, host a world for friends on your home network or over Radmin VPN, or join a
friend's world. Your characters are saved on your PC and can be carried to another PC as a single file.

> **Status: early development.** The launcher, setup wizard, database, accounts and save files work and are
> tested. Playing end-to-end needs the first server package to be published (see
> [docs/RELEASING.md](docs/RELEASING.md)) and has not been tested on real hardware yet. See [docs/TESTING.md](docs/TESTING.md).

## You bring the game

LocalXI contains **no Square Enix files** and never downloads any. You need FINAL FANTASY XI installed
yourself from Square Enix's official installer, and **updated to the current version**. The server follows the
current retail game, so an old install will have problems.

**Updating the game needs one sign-in to Square Enix's official servers.** The official installer stops at
April 2019, and PlayOnline won't offer FINAL FANTASY XI under *Check Files* until the game has connected to the
official servers once. Any of these works:

- an existing or past FFXI account
- a newly bought copy (it includes 30 days of play)
- Square Enix's free trial
- a "Return Home to Vana'diel" campaign, when Square Enix lets former players back in for free

After that one sign-in, **PlayOnline → Check Files → FINAL FANTASY XI** updates the game whenever needed,
even if the subscription has lapsed. This follows LandSandBoat's own
[client setup guide](https://github.com/LandSandBoat/server/wiki/Client-Setup-Windows). LocalXI doesn't support
ways around it.

On Windows 10, also turn on **DirectPlay** (*Turn Windows features on or off → Legacy Components*).

## Getting started

1. Download `LocalXI.exe` from the [latest release](../../releases/latest). It's a single file and needs
   nothing else installed.
2. Run it and follow the four-step setup:
   - **How do you want to play?** Solo, host for friends, or join a friend.
   - **Where is the game installed?** Usually found automatically. If Windows doesn't know where the game is
     (common when it was copied from another PC), press **Fix registry**.
   - **Your login.** A username and password for *your* server. This is not your Square Enix account.
   - **Setup.** Downloads the server, database and loader (a few hundred MB, once), then loads the game data.
3. Press **Play**.

The server starts when you press Play and stops, with an automatic backup, when you close the game.

## Playing with friends

**Same house:** choose *Host for friends* and pick your home network address. Friends choose *Join a
friend* and type that address.

**Over the internet with [Radmin VPN](https://www.radmin-vpn.com):**

1. Everyone installs Radmin VPN. The host creates a network and friends join it.
2. The host chooses *Host for friends*, picks the **Radmin VPN - 26.x.x.x** address, and presses
   **Allow friends through Windows Firewall** on the Network page.
3. Friends choose *Join a friend* and enter the host's 26.x.x.x address. LocalXI creates their account on the
   host's server automatically.

Hamachi, ZeroTier and Tailscale work the same way. More detail: [docs/LAN-PLAY.md](docs/LAN-PLAY.md).

## Taking your characters to another PC

**Saves → Export save** writes one `.xisave` file containing every character on the PC: levels, jobs, items,
Mog House, quests, missions and story progress. Copy it to the other PC (USB stick, cloud drive, Discord) and
use **Saves → Import save** there. Your current characters are backed up automatically before an import, so it
can always be undone.

Automatic backups are kept in `Documents\LocalXI\Backups`; exports go to `Documents\LocalXI\Saves`.

## Troubleshooting

| Problem | Try |
|---|---|
| The game doesn't open | Settings → **Fix registry**. If that's fine, tick *Run the game as administrator*. |
| "Your game files are older than this server expects" | Update the game in PlayOnline (*Check Files → FINAL FANTASY XI*). If FINAL FANTASY XI isn't in that list, the game hasn't signed in to Square Enix's servers yet; see [You bring the game](#you-bring-the-game). |
| Friends can't connect | Network → **Allow friends through Windows Firewall**. Check you're both in the same Radmin network and that the host picked the Radmin address. Friends can use **Test connection**. |
| "Ports are already in use" | Another FFXI server or launcher is running. Close it. |
| Anything else | The **Logs** page shows everything the server and database print. |

## How it works

| Piece | What it does | Where it comes from |
|---|---|---|
| [LandSandBoat](https://github.com/LandSandBoat/server) | The game server | Built for Windows by this repo's GitHub Actions |
| [MariaDB](https://mariadb.org) | Stores the world and your characters | Official portable zip, pinned and hash-checked |
| [Ashita v4](https://www.ashitaxi.com) | Starts the game client | Official release, pinned and hash-checked |
| [xiloader](https://github.com/LandSandBoat/xiloader) | Connects the client to the server | Official release, pinned and hash-checked |
| Visual C++ runtime | Needed by the above | Microsoft, installed only if missing |

Nothing is installed system-wide apart from the Visual C++ runtime, the PlayOnline registry fix and firewall
rules (each only if needed, each behind a Windows permission prompt). Everything else lives in
`%LOCALAPPDATA%\LocalXI`. Put a file named `portable.txt` next to `LocalXI.exe` to keep everything beside it
instead.

Details: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/SAVE-FORMAT.md](docs/SAVE-FORMAT.md).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```
dotnet build LocalXI.slnx
dotnet test tests/LocalXI.Core.Tests
dotnet run --project src/LocalXI.App
```

Single-file release build:

```
dotnet publish src/LocalXI.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

| Folder | Contents |
|---|---|
| `src/LocalXI.Core` | Everything that isn't UI: downloads, database, server processes, accounts, saves, networking |
| `src/LocalXI.App` | The WPF launcher (setup wizard, Play, Saves, Network, Settings, Logs) |
| `tests/LocalXI.Core.Tests` | Unit tests |
| `components.json` | Pinned versions and SHA-256 hashes of everything the launcher downloads |
| `.github/workflows` | Launcher CI/release, and the LandSandBoat Windows build |

Publishing releases (including the first server package): [docs/RELEASING.md](docs/RELEASING.md).

## Legal

LocalXI is free software under the [GPL-3.0](LICENSE), like LandSandBoat, Ashita and xiloader. It is a fan
project, not affiliated with or endorsed by Square Enix. FINAL FANTASY is a registered trademark of Square Enix
Holdings Co., Ltd. Running a private server is against Square Enix's terms of service for the official game;
LocalXI is a non-commercial hobby project and must never be sold or used to charge for play.
