# Using your own game files

> The engine isn't written yet. This page is the specification for how it finds game data.

The engine ships without game data. You need an installed copy of at least one of the games, for example from GOG. The engine only reads it, and never copies it into this repository or into a build.

## 1. Get the files from the CD image

Some files exist only on the disc image. Extract it once, to a folder **outside this repository**:

```
python3 -I tools/iso_extract.py "/path/to/game.gog" "/path/to/extracted_cd"
```

| Game | Image in the GOG install | Files only on the disc |
|---|---|---|
| *Menzoberranzan* | `game.gog` | `RES9`, `RES10`, `.DFA` cutscenes (`CINE/`, `CGEN/`) |
| *Strahd's Possession* | `game.gog` | `RES9` to `RES12`, `.ANM` cutscenes |
| *Stone Prophet* | `gog.game` | `COLORS`, `INIT`, `LEVELS.DAT`, `NPCS.DAT`, `OBJECTS.DAT`, `LEVINFO`, `TABLES`, `SINTAB`, `RES10`, cutscenes (`CINE/`) |

## 2. Write `data-path.txt`

Plain UTF-8 text, one `key = path` per line. Leave out the games you don't have. Lines starting with `#` are comments, and paths are used as written (no quotes, spaces are fine).

```
# folder with the installed game, and folder with the extracted disc
menzo.install   = /games/Menzoberranzan
menzo.cd        = /games/data/menzo_cd
strahd.install  = /games/Strahds Possession/RLOFT
strahd.cd       = /games/data/strahd_cd/RLOFT
prophet.install = /games/Stone Prophet/PROPHET
prophet.cd      = /games/data/prophet_cd
```

`install` is the folder that holds `CODE.2`; `cd` is the one that holds `RES9`.

Where the engine looks, in this order:

1. the file named by the environment variable `DREAMFORGE_DATA_PATH`
2. `data-path.txt` in Unity's `Application.persistentDataPath`
3. `data-path.txt` at the top of this repository (development only)

The file is personal and listed in `.gitignore`. Don't commit it.

## 3. Rules for the engine

- **Find files ignoring case.** The originals are upper case, but tools and file systems differ.
- **Search `install` first, then `cd`.** *Stone Prophet* has a `RES9` in both places, and they differ.
- **Open everything read-only.** Never write to either folder, including saves: the engine's own saves go in `persistentDataPath`.
- **Load at run time only.** No game data in `Assets/`, `Resources/`, `StreamingAssets/`, addressables or builds, and no importer that creates assets from it.
- **Caches** of decoded data go in `Application.temporaryCachePath`, never in the project folder.
- **Treat the files as untrusted.** Check counts, offsets and sizes before using them.
- **Check before starting:** the files in the table exist, and `LEVELS.DAT` starts with the expected count (*Menzoberranzan* 56, *Strahd* 32, *Stone Prophet* 43). If not, say which game and file is the problem, and carry on with the other games. Other releases may differ, so a wrong count is a warning.
- **Never log file contents**, only names, sizes and offsets.

## Without the engine

The [tools](../tools/README.md) take paths on the command line and don't read `data-path.txt`.
