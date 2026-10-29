# HOI4AchievementPatcher

Lets Hearts of Iron IV grant achievements with checksum-changing mods, and optionally without Ironman.

## Supported game versions

| HOI4 version | Status |
|--------------|--------|
| 1.19.4.1     | Supported |

Other versions may work: the patch is found by byte pattern, not a fixed offset. Run `status` first; if it says
`unknown game version`, that version is not supported yet and `patch` changes nothing.

## Usage

Close the game, then run:

```
HOI4AchievementPatcher [patch|restore|status] [game folder] [--keep-ironman]
```

- `patch` (default): apply the patch. Backups are written next to each changed file as `*.achpatch.bak`.
- `restore`: put the backups back.
- `status`: show what is patched.
- `--keep-ironman`: only allow mods; Ironman stays required. **Use this if you play multiplayer** (see below).

The game folder is found automatically in the usual Steam locations and Steam libraries; pass it if yours is elsewhere.

Game updates replace the patched files, so run the patcher again after each update.

## What it changes

- `hoi4.exe`: the game compares its checksum with the vanilla one at startup and blocks achievements on a
  mismatch. The result of that compare is forced to "match" (3 bytes). The patcher only applies it when the
  pattern loads the vanilla checksum string, so a wrong spot is never patched.
- `common/achievements.txt` (skipped with `--keep-ironman`): `is_ironman = yes` is commented out in every
  achievement. The Ironman trigger itself is untouched, so events that check Ironman behave as before.

Console cheats, difficulty, custom difficulty, start date and game rules still block achievements as in vanilla.

## Multiplayer

The multiplayer checksum covers the files in `common/`, so removing the Ironman requirement changes your checksum
and you will not match other players. Patching only `hoi4.exe` (`--keep-ironman`) does not change the checksum.
