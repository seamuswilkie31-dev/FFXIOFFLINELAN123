# Releasing

There are two kinds of release on GitHub:

- **Server packages** (`server-<commit>`, marked pre-release): LandSandBoat built for Windows plus the
  navigation meshes. Made by the *Build server package* workflow.
- **Launcher releases** (`v0.1.0`, ...): `LocalXI.exe`. Made by the *Launcher* workflow when you push a tag.

Each launcher version pins exactly one server package in `components.json`, so everyone on the same launcher
version runs the same server.

## First-time setup

1. Create a GitHub repository (e.g. `LocalXI`) and push this code to it.
2. In `components.json`, set `"repository"` to `your-name/LocalXI`.
3. In the repository's **Settings → Actions → General**, make sure workflows have **Read and write
   permissions** so they can create releases.

## Publishing a server package

1. **Actions → Build server package → Run workflow.** Leave `lsb_ref` as `base` for the latest LandSandBoat,
   or give a tag or commit.
2. Wait for it to finish (about 15-30 minutes; it's a large C++ build).
3. The run's **Summary** shows a table with the release tag and two SHA-256 hashes. In `components.json`, set:
   - `server`: `"version"` to the tag (e.g. `server-f038f1e987`) and `"sha256"` to the server hash
   - `meshes`: the same `"version"` and the meshes hash
4. Commit that change.

The release notes link the exact LandSandBoat commit, which keeps the GPL source requirement satisfied.

## Publishing the launcher

```
git tag v0.1.0
git push origin v0.1.0
```

The workflow runs the tests, builds the single-file `LocalXI.exe` and attaches it to a `v0.1.0` release. Point
friends at **Releases → latest**.

## Updating pinned third-party components

For MariaDB, Ashita, xiloader or the Visual C++ runtime, change the URL, version and `sha256` in
`components.json`. Get the hash by downloading the file and running `Get-FileHash <file>` in PowerShell, or
from the publisher's checksums (MariaDB's download API and GitHub release pages list them).

xiloader's version must match what the server expects: LandSandBoat checks the major and minor version
(`SupportedXiloaderVersion` in `src/login/auth_session.h`).

## Updating an existing server install

When a player presses **Play** on a launcher that pins a newer component, the launcher installs it first. For
a new server it keeps `navmeshes\`, `ximeshes\` and `log\`, backs up the characters (`before-update-*.xisave`),
then re-imports game data without touching player tables (`SchemaImporter.RefreshGameDataAsync`).

**Not automated yet:** running LandSandBoat's Python migrations (`tools/migrations`) after an update that
changes player-table layouts. Until that exists, check the LandSandBoat changelog before bumping the server
version for existing players.
