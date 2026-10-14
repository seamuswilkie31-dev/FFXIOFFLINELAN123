# Save files (`.xisave`)

A save is a zip file with two entries:

| Entry | Contents |
|---|---|
| `manifest.json` | What's inside and where it came from |
| `player-data.sql` | A `mariadb-dump` of every player table: schema and data, binary columns hex-encoded |

## What counts as player data

The tables in LandSandBoat's `player_data` list (`tools/dbtool.py`), which the launcher reads from the
installed server so it always matches that version. That's about 50 tables, including:

- `accounts` (logins, with bcrypt password hashes)
- `chars` (name, nation, position, and the mission, quest and key item progress blobs)
- `char_vars` (most per-quest state)
- `char_inventory`, `char_storage`, `char_equip` (items, Mog House, equipment)
- `char_jobs`, `char_exp`, `char_stats`, `char_skills`, `char_spells`, `char_merit`, `char_job_points`, ...
- linkshells, the auction house and delivery boxes, conquest and server variables

Everything else in the database is game data that's re-imported from the server package and doesn't belong in
a save.

## manifest.json

```json
{
  "format": 1,
  "createdUtc": "2026-10-13T17:49:56Z",
  "kind": "export",
  "appVersion": "0.1.0",
  "lsbCommit": "f038f1e9872313b835d135b38116574f7bfa633a",
  "clientVersion": "30261006_0",
  "primaryAccount": "seamus",
  "tables": ["accounts", "chars", "char_vars", "..."],
  "characters": [{ "name": "Seamus", "account": "seamus", "job": "RDM", "level": 37 }]
}
```

`kind` is `export` (made by the player) or `backup` (made automatically). `primaryAccount` is the login the
launcher was using when the save was made.

## Importing

1. The game server is stopped (it caches characters in memory) and the database started.
2. The current characters are backed up to `Backups\before-import-*.xisave`.
3. Player tables the save doesn't contain (added by a newer server version) are emptied, so no stray rows
   attach to the imported characters.
4. `player-data.sql` is run: each table is dropped and recreated exactly as saved.
5. The launcher switches its login to `primaryAccount` and sets that account's password to this PC's
   password, so the imported characters show up at the next login.

## Version differences

A save made on an older server version restores the older table layouts. LandSandBoat upgrades player tables
with Python migrations (`tools/migrations`), which the launcher does not run yet, so for now import saves into
the same server version where possible. The manifest's `lsbCommit` records the version. Automating migrations
is on the roadmap.
