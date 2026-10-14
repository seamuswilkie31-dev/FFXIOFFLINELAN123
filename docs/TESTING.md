# Testing status

## Verified (October 2026, Windows 10 22H2)

- **Unit tests** (`dotnet test`, 58 tests): INI editing, password rules, client-version parsing, the
  component manifest, download hash checking, zip install/upgrade with preserved folders, era presets and
  data-pack ordering, the player-table list, server package detection, login-protocol replies, network adapter
  classification, and Windows argument quoting.
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

These need a published server package (see [RELEASING.md](RELEASING.md)) and an updated game client:

- [ ] The *Build server package* workflow produces a working package
- [ ] All five server programs start from the package and reach "listening"
- [ ] Ctrl+C shutdown saves characters (falls back to force-stop after 20 s)
- [ ] Classic era in-game: level cap 75, later-expansion areas closed, `era/lua` modules load without errors
- [ ] Ashita + xiloader start the game with the generated `localxi.ini` (`Ashita-cli.exe localxi.ini`)
- [ ] Create a character, play, close the game → automatic backup → stop
- [ ] Host mode: a second PC on Radmin VPN creates an account from Join setup and plays
- [ ] Firewall rules: friends connect after **Allow friends through Windows Firewall**
- [ ] Registry repair on a copied install
- [ ] Visual C++ runtime install on a clean Windows install

## Manual end-to-end checklist

1. Update FINAL FANTASY XI through PlayOnline. This needs one sign-in to Square Enix's official servers first
   (see the README's "You bring the game").
2. Publish a server package and pin it (RELEASING.md), then build or download `LocalXI.exe`.
3. Fresh run → Solo → finish setup → **Play** → create a character → walk into another zone → close the game.
   Expect: the Logs page shows all five programs starting, the game reaches the character screen, and on close
   a new `auto-*.xisave` appears in Backups.
4. **Saves → Export**, then on a second PC (or after **Run setup again** in a new portable folder) **Import**.
   Expect the character to be there with the same items and position.
5. **Network → Host for friends** → pick the Radmin address → allow firewall → **Play**. On a second PC:
   **Join a friend** → host's address → Test connection → finish setup → **Play**.

Tip: to test without touching your real data, drop a `portable.txt` next to the exe.
