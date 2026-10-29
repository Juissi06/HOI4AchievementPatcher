# HOI4AchievementPatcher

Lets Hearts of Iron IV grant achievements with checksum-changing mods and without Ironman.

```
HOI4AchievementPatcher [patch|restore|status] [game folder] [--keep-ironman]
```

- `hoi4.exe`: the result of the vanilla checksum compare (the mods check) is forced to "match".
  Found by byte pattern, verified to load the 32-character checksum string, so it should survive updates.
- `common/achievements.txt`: `is_ironman = yes` is commented out in every achievement. Skip with `--keep-ironman`.

Console cheats, difficulty, custom difficulty, start date and game rules still block achievements.
Backups are written next to each file as `*.achpatch.bak`; `restore` puts them back.
Game updates replace both files, so run the patcher again after each update.

Verified against HOI4 1.19.4.1.
