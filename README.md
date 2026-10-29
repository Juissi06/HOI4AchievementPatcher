# HOI4AchievementPatcher

Lets Hearts of Iron IV grant achievements with checksum-changing mods, and optionally without Ironman.

## Supported game versions

| HOI4 version | Status |
|--------------|--------|
| 1.19.4.1     | Supported |

Other versions may work: the patch is found by byte pattern, not a fixed offset. Run `status` first; if it says
`unknown game version`, that version is not supported yet and `patch` changes nothing.

## Quick start

1. Download `HOI4AchievementPatcher.exe` from the [latest release](../../releases/latest). It needs nothing else installed.
2. Close Hearts of Iron IV.
3. Double-click the exe. It finds the game in your Steam libraries, patches it and keeps a backup.

Windows SmartScreen may warn about an unknown app: choose "More info", then "Run anyway".

Run it again after every game update.

## Command line

```
HOI4AchievementPatcher [patch|restore|status] [game folder] [--remove-ironman]
```

- `patch` (default): allow achievements with mods. A backup is written next to each changed file as `*.achpatch.bak`.
- `restore`: put the backups back.
- `status`: show what is patched.
- `--remove-ironman`: also allow achievements without Ironman. **Breaks multiplayer**, see below.

The game folder is found automatically in the usual Steam locations and Steam libraries; pass it if yours is elsewhere.

## What it changes

- `hoi4.exe`: the game compares its checksum with the vanilla one at startup and blocks achievements on a
  mismatch. The result of that compare is forced to "match" (3 bytes). The patcher only applies it when the
  pattern loads the vanilla checksum string, so a wrong spot is never patched.
- `common/achievements.txt` (only with `--remove-ironman`): `is_ironman = yes` is commented out in every
  achievement. The Ironman trigger itself is untouched, so events that check Ironman behave as before.

Console commands, difficulty, custom difficulty, start date and game rules still block achievements as in vanilla.

## Multiplayer

The default patch only changes `hoi4.exe`, which does not change the game checksum: you still match players
without the patch (with the same mods, as always).

The multiplayer checksum covers the files in `common/`, so `--remove-ironman` changes your checksum and you will
no longer match other players. Run `restore` and then `patch` to go back.
