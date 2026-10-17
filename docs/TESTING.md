# Testing status

## Verified (October 2026, Windows 10 22H2)

- **Unit tests** (`dotnet test`, 99 tests): INI editing, password rules, client-version parsing, the
  component manifest, download hash checking, zip install/upgrade with preserved folders, era presets and
  data-pack ordering, the module list, the player-table list, server package detection, login-protocol replies,
  network adapter classification, Windows argument quoting, the character data layouts and server-script
  parsers the Modifiers page uses (including the map list and key item layout, checked against a real character), the game's COM class registrations that **Fix registry** writes (in a
  scratch per-user registry key), the boot config's window modes and interface size, and server programs
  being safe to ask about once stopped, and reading the character-data converter's report.
- **Per-character Dual Wield in the real server engine**, with LandSandBoat's own test runner (`xi_test.exe`)
  and LocalXI's module and trait rows: a level-30 warrior has no Dual Wield without the setting and Dual Wield I
  with it, loses it when the setting is cleared, and a ninja keeps its own Dual Wield II either way.
- **Character data conversion**, in a throwaway world with a copy of a real character: a new world is marked
  converted without running; loading a save runs the converter (nothing to do); an older layout (columns from
  migrations 069-071 removed, covering accounts_profile, char_jobs and char_exp) is converted on the next Play
  with the character's levels, account and character intact; a failing migration stops the update with the
  backup message and isn't marked done; the real server then starts cleanly.
- **Server shutdown popups** ("No process is associated with this object", one per server program): reproduced
  by checking server status just after each program stopped, as the Play page does; 4 errors before the fix,
  none after.
- **Registry check** against a real official install: the stricter check (install folders plus COM classes)
  accepts it, so working installs aren't flagged.
- **In game** with the October 2026 client: Ashita 4.3.2.1 and xiloader start the game, log in to the local
  server, create a character and load into the world; the character is still there after logging out.
- **The real server, end-to-end** (package `server-1f204df5e2`, built by this repo's workflow in 71 minutes),
  in a fresh portable folder:
  - all five components downloaded and hash-checked (about 500 MB in 1.5 minutes), the game data loaded in
    classic mode in 30 seconds
  - all five programs start; every port listens (TCP 54001, 54002, 54003, 54230, 54231, 51220; UDP 54230)
  - start-up waits for the map server's "ready to work" line (5 to 90 seconds depending on zone loading and
    antivirus scanning); no crashes in the following 25 seconds
  - the classic settings reach the server (`XI_MAIN_*` logged as applied) and all 114 `era/lua` zone overrides
    apply with every zone loaded (they were skipped with `--lazy`, hence classic mode no longer uses it)
  - account creation over the login protocol with TLS 1.3, which Windows 10's own TLS can't do (hence BouncyCastle)
  - Ctrl+C shutdown of all five programs in 1-2 seconds, then an automatic backup and a clean database stop
- **Database end-to-end**, using the real portable MariaDB 12.3.3 and LandSandBoat's real SQL
  (commit `f038f1e`):
  - download and hash check, `mariadb-install-db`, start and clean shutdown
  - all 120 SQL files imported, including 9 triggers, in about 30 seconds
  - account creation with a bcrypt hash that verifies
  - the LAN zone-address update (299 of 300 zones; the disabled zone untouched)
  - export → wipe → import of a character with job, level and mission variables, including clearing stray
    rows and switching the login account
- **Game era switching** against real LandSandBoat data (commit `7b39a67`): a classic import applies the
  era data packs; switching to modern reloads clean game data (abilities, spells, recipes and skill ranks
  change); switching back reproduces the classic data checksum-for-checksum; characters survive both switches,
  with a backup before each. Two stale upstream statements in the `abyssea` pack are skipped and logged.
- **Setup wizard** steps 1-3 on a real install: the game folder was found automatically, the registry check
  passed, the out-of-date client was flagged, and the Radmin VPN address was detected and suggested.
- **Single-file publish**: `LocalXI.exe` (about 63 MB) starts on a PC without the .NET runtime installed.
- **Visual C++ runtime** detection, and the pinned Microsoft installers' hashes and signatures.

## Not yet verified

These need the game client:

- [ ] Ctrl+C shutdown saves a logged-in character (shutdown itself is verified, with nobody online)
- [ ] Classic era in-game: level cap 75, later-expansion areas closed
- [ ] Close the game → automatic backup → server stop
- [ ] Modifiers changes as seen in game (gil, items, job levels, fame, quest log, home points, EXP rate,
  storage sizes and the Mog Locker, learned spells and trusts, GM mode and the New Adventurer icon, sub job,
  Dual Wield, weapon skills, maps). The Judge's outfit has been worn in game.
- [ ] Window modes in game: borderless windowed (0034 = 2) is a borderless window of the chosen size, borderless
  fullscreen (0034 = 3) covers the screen (the client's documented names for 2 and 3 are swapped)
- [ ] Interface size in game: a smaller menu resolution (0037/0038) makes the menus bigger
- [ ] Windows key in game with Ashita's keyboard.windowskeyenabled = 1 (Start menu, Windows+Shift+arrows to
  move the window to another monitor)
- [ ] Host mode: a second PC on Radmin VPN creates an account from Join setup and plays
- [ ] Firewall rules: friends connect after **Allow friends through Windows Firewall**
- [ ] Registry repair on a copied install (install folders and COM classes), then the game starts
- [ ] Visual C++ runtime install on a clean Windows install

## Manual end-to-end checklist

1. Update FINAL FANTASY XI through PlayOnline. This needs one sign-in to Square Enix's official servers first
   (see the README's "You bring the game").
2. Build or download `LocalXI.exe` (the server package is already published and pinned).
3. Fresh run → Solo → finish setup → **Play** → create a character → walk into another zone → close the game.
   Expect: the Logs page shows all five programs starting, the game reaches the character screen, and on close
   a new `auto-*.xisave` appears in Backups.
4. **Saves → Export**, then on a second PC (or after **Run setup again** in a new portable folder) **Import**.
   Expect the character to be there with the same items and position.
5. **Network → Host for friends** → pick the Radmin address → allow firewall → **Play**. On a second PC:
   **Join a friend** → host's address → Test connection → finish setup → **Play**.

Tip: to test without touching your real data, drop a `portable.txt` next to the exe.
