# Tutorial: changing your characters

The **🛠 Modifiers** page edits the characters on your own world: gil and items, job levels, fame, quests,
home points, storage, spells, trusts and GM powers. This guide walks through each tab.

Modifiers only work on a world you run yourself (*Solo* or *Host for friends*). When you join a friend, your
characters live on their PC, so they have to make the changes.

## Before you start

1. **Log out and stop the server.** The game server keeps logged-in characters in memory and saves them when
   they log out, which would overwrite your changes. When you play solo, closing the game also stops the server.
   When you host for friends, the server keeps running for them. If it's running, the Modifiers page shows a
   yellow banner with a **Stop server** button.
2. **Open 🛠 Modifiers** and pick the character from the **Character** list at the top. Press **Refresh** if a
   character you just made isn't listed.
3. **Make your changes.** Each tab has its own save button, and changes are written as soon as you press it.
4. **Press Play** and check the result in game.

### Your safety net

The first time you change something in a session, LocalXI backs up every character first. If the backup fails,
nothing is changed. Backups are named `before-modify-<date>` and kept in `Documents\LocalXI\Backups`.

**To undo:** go to **💾 Saves**, select the `before-modify` backup from the list and press **Load selected**.
Your characters go back to how they were before you started editing (the current state is backed up too, so
even the undo can be undone).

## The EXP rate

The **EXP rate** box at the top sets how fast characters earn experience: ×1 (normal), ×1.5, ×2, ×2.5 or ×3.
Pick one and press **Set**. It applies the next time the server starts.

The EXP rate applies to **everyone on the world**, not just the selected character, because LandSandBoat has
one rate for the whole server.

## Inventory

**Gil:** type an amount (up to 999,999,999) and press **Set gil**.

**Adding an item:**

1. Type part of the item's name in the search box, for example `scorpion harness`, and press **Search** (or
   Enter).
2. Click the item in the results. The columns show the stack size and whether the item is **Rare** (only one
   allowed), **Ex** (can't be traded) or **GM only**.
3. Set the **Quantity** (it's capped at the item's stack size) and press **Add to inventory**.

The item goes into the first free slot of the character's inventory. If the inventory is full, remove something
first or unlock more space on the **Storage** tab.

**Removing an item:** select it in the inventory list and press **Remove selected**. Items the character is
wearing can't be removed; take them off in game first.

## Jobs

Each job has an **Unlocked** box and a **Level** box. Change what you like and press **Save jobs**.

- In classic mode the highest level is 75, and Geomancer and Rune Fencer aren't available (they belong to the
  modern game).
- Changing a job's level resets its EXP towards the next level.
- If the new level is above the character's level cap (normally raised by the limit-break quests), the cap is
  raised so the level sticks.

## Fame

Pick a fame level from 1 to 9 for **San d'Oria**, **Bastok**, **Windurst** and **Norg**, then press **Save fame**.
Modern worlds also list the Abyssea areas and Adoulin.

Jeuno and Selbina/Rabao have no fame of their own: the game works them out from the three nations, and the page
shows what they'll be.

## Quests

1. Choose an area (San d'Oria, Bastok, Windurst, Jeuno, Other Areas, Outlands, Aht Urhgan, Crystal War, Abyssea,
   Adoulin or Coalition).
2. Type in the filter box to find a quest by name.
3. Set each quest to **Not started**, **Accepted** or **Completed**.
4. Press **Save quest changes**. The summary shows how many quests in the area are completed.

This only changes the quest log. Completing a quest here **doesn't hand out its reward**, such as items, key
items, gil or job unlocks. Resetting or completing a quest also clears its in-progress steps, as the game itself
does.

## Home points

**Register every home point** registers all of the home point crystals (122 in this server version), so the
character can warp between any of them from any crystal without touching each one first. **Clear all** removes
them again.

## Storage

The tab lists each storage container and its size. **Unlock all storage (80 slots each)** sets all of these to
80 slots, the most the game shows:

- Gobbie bag (the main inventory)
- Mog Safe and Mog Safe 2
- Mog Locker, with a lease for every area that never runs out
- Mog Satchel, Mog Sack and Mog Case
- all eight Mog Wardrobes

The Mog House's own **Storage** container is sized by the furniture placed in the Mog House, so it isn't changed.

## Magic

**Spells:** tick the kinds of magic to learn:

- White Magic
- Black Magic
- Blue Magic
- Ninjutsu
- Songs
- Summoning
- Geomancy

Then press **Learn ticked spells**. Each line shows how many the character already knows.

It's the same as using every scroll (or, for Blue Magic, learning every spell from monsters). The character
still needs a job and level that can cast a spell before it can be used.

In classic mode the server ignores spells from later expansions: Geomancy, and some Abyssea and Adoulin spells.
They stay learned and appear if the world is switched to Modern in Settings.

**Trusts:** **Learn every trust** adds every trust to the character's Trust magic list. No Trust permit is
needed. Up to three trusts can be called at once; more need the Rhapsody key items from Rhapsodies of Vana'diel
(GMs in GM mode aren't limited).

## GM

A GM character can use LandSandBoat's chat commands. These are typed in the chat box and start with `!`.

1. Choose a **GM level**:
   - **0** is a normal player.
   - **1** gives almost every command.
   - **2** to **5** only add server and developer tools.
2. Tick **GM mode on when logging in** if you want the GM icon and GM name colour straight away. You can also
   switch it on and off in game with `!togglegm`.
3. Untick **Show the New Adventurer "?" icon** to remove the red question mark. The game normally shows it for
   a character's first 240 hours.
4. Press **Save GM settings**.

**The Judge's outfit:** **Add Judge's outfit to inventory** adds the Judge's Helm, Cuirass, Gauntlets, Cuisses
and Greaves, wearable by every job from level 1. It's GM-only gear, so save a GM level first. The rest of the set
(shield, sword, ring, cape, earring, gorget and belt) is on the **Inventory** tab: search for `judge`. The ring
and cape make a character almost impossible to hurt.

### Handy GM commands

| Command | What it does |
|---|---|
| `!togglegm` | GM icon and name colour on or off |
| `!hide` | Become invisible to other players (again to show yourself) |
| `!speed 80` | Run faster; `!speed 0` returns to normal |
| `!godmode` | Switch god mode on or off |
| `!zone 230` | Go to a zone by number: 230 Southern San d'Oria, 234 Bastok Mines, 238 Windurst Waters, 245 Lower Jeuno, 50 Aht Urhgan Whitegate |
| `!goto Name` | Go to another player |
| `!homepoint` | Return to your home point |
| `!pos` | Show where you are |
| `!additem 4096 12` | Add an item by number (here 12 Fire Crystals); the Inventory tab is easier |
| `!givegil 10000` | Give yourself gil |
| `!changejob WAR 75` | Change main job, optionally setting the level |
| `!changesjob THF 37` | Change support job |
| `!setplayerlevel 75` | Set your current job's level |
| `!addallspells` | Learn every spell (the Magic tab does this too) |

LandSandBoat has more than 200 commands. The full list is the `scripts/commands` folder of the server, in
`%LOCALAPPDATA%\LocalXI\runtime\server`. Each file starts with a short description.

## Good to know

- **Friends' characters:** if you host for friends, their characters live on your world, so you can edit them
  too. They need to be logged out, and the server stopped.
- **Saving it all:** a save file from **Saves → Export save** includes everything changed here, so modified
  characters carry over to another PC.
- **Something looks wrong in game?** Load the `before-modify` backup (see *Your safety net*) and tell us what
  happened on the project's [GitHub issues](../../../issues).
