# Pocket Rogues Editor

A save editor for **Pocket Rogues** (Steam, Windows): gold, skill points, attributes, skill
levels and gear of every hero — with a backup before every write and undo for every edit.
Works while the game is closed. One portable `.exe`, nothing to install.

Русское описание — в [README.ru.md](README.ru.md).

For changes on the fly, while playing, there is a companion mod:
[Pocket Rogues Mod Menu](https://github.com/Xronon/PocketRoguesModMenu).

**For single-player only.** Do not use it on a co-op game: that is someone else's game too.

## What it does

- **Numbers:** gold; skill points and the four attributes of every hero (up to 50, the game's
  own limit); the levels of all 54 skills (up to 25).
- **Gear** of a hero with a run in progress — back in the fortress, standing in the dungeon or
  in the Camp:
  - quality (common → unusual → ancient → epic → legendary);
  - effect tiers, adding and removing effects — only those the game itself can roll on that
    item, and never more than the game keeps (quality + built-in effects);
  - rings by their own rules: points = quality + 2, one effect of each kind;
  - removing curses;
  - the game's own description under every effect, with the number of the chosen tier.
- **Add, replace, delete items:** any item of the game into the bag (gear and rings come
  legendary, effects rolled as on a dropped item), replace a worn one (the old one goes to the
  bag), delete from the bag. The bag holds as many items as the Warehouse allows.
- **Past edits:** every edit is listed and can be undone; an undo can be undone too.
- **Language:** follows the game — Russian if the game is in Russian, English otherwise; switch
  by hand with "EN ▾" in the top right corner. Names of heroes, skills, items and effects come
  from the game's own translation.

## How to use

1. Download the archive from Releases and extract it into any folder of your own (the program
   keeps its backups and edit log next to itself).
2. Close the game completely. While it is running the program does not write: on exit the game
   would save its own values over the edit.
3. Start `PocketRoguesEditor.exe`, change what you need, press **Write to game**. The program
   shows what will change and asks first.
4. Start the game and check.

The full manual is in [How to use.txt](How%20to%20use.txt) (also in the archive).

Needs Windows 10 or 11 (the .NET Framework 4 it runs on is part of Windows).

## How it keeps your save safe

- It touches only the game's own data in the registry
  (`HKCU\Software\EtherGaming\Pocket Rogues`) and never the game's files.
- Before every write it exports that whole registry key to a `.reg` file in `Backups\` (the latest
  30 are kept). Double-clicking such a file restores everything as it was at that moment, even
  without this program.
- Every edit also gets a small undo file, listed in **Past edits…**.
- Right before writing it checks that the game has not changed its data since the window was
  opened; if it has, nothing is written and the window shows the fresh values.
- Only the values you changed are written; everything else in a gear record stays byte for byte.

## Good to know

- Once an hour the game sends a few totals to its developer's server: gold, crystals, bought
  crystals, the sums of skill points and attributes, number of runs, depth record and guild
  level. The developers can see those numbers, and they can ban an account and remove items.
  Large edits stand out more. Use at your own risk.
- The program never touches crystals.
- Pressing "Load" in the game (a local or a cloud save) brings back that save's numbers, and the
  edit is gone.
- Health cannot be raised by editing: on loading the game cuts it to the maximum. Raise
  Endurance instead.
- A hero without a run in progress has no gear in the save at all — the game hands out starting
  gear when a run starts; then it can be edited.
- Artifacts are shown but not edited: their properties are built into the game.
- Some tier 2 and 3 items are not offered: the game saves them under another item's name, and
  after loading they would turn into a different item or vanish.
- The item and effect catalog built into the program comes from game version 1.38.3.1. Items
  added by later updates show as "not in the catalog" and are not edited.

## Building from source

Needs only Windows: the C# compiler of .NET Framework 4 ships with it.

- `build.cmd` builds `bin\PocketRoguesEditor.exe` (the catalog `data\catalog.tsv` is built in).
- `build-test.cmd` builds the self-test, then run `bin-test\SelfTest.exe`. It works on a
  temporary registry key and a temporary folder and never reads or touches the game's data.
- `package.ps1` builds the release archive into `dist\`.

## License

MIT, see [LICENSE](LICENSE). The names and descriptions of heroes, skills, items and effects in
`data\catalog.tsv` and in the program are taken from Pocket Rogues and belong to its developers.
