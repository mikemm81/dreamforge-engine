# Tools

Python 3, standard library only. Run them with `python3 -I`. They read your own game files and write only where you tell them to.

**Keep the output out of this repository.** Extracted pictures and dumps are game content.

| Tool | Purpose |
|---|---|
| `iso_extract.py IMAGE OUT [--list]` | Extract an ISO 9660 image, such as the GOG disc images |
| `res_sprites.py RES OUT --colors COLORS [--palette N]` | 2D pictures to indexed PNG |
| `res_textures.py RES4 OUT --colors COLORS [--palette N]` | Walls and floor/ceiling tiles to PNG |
| `res_shapes.py RES OUT --colors COLORS [--palette N] [--canvas-h 128] [--crop]` | Column shapes to RGBA PNG (`--canvas-h 128` for *Strahd*) |
| `res3_groups.py SHAPES OUT --gamedir DIR [--names CSV]` | Sort exported shapes by type, frame and view |
| `overview_sheet.py KIND_DIR OUTPREFIX` | Labelled contact sheets of the groups |
| `dfa_dump.py info\|check\|frames\|raw ...` | `.DFA` cutscenes (*Menzoberranzan*, *Stone Prophet*) |
| `anm_dump.py check\|frames ...` | `.ANM` cutscenes (*Strahd*) |
| `level_dump.py GAMEDIR check\|level N\|savelev FILE` | *Menzoberranzan* levels and saved levels |
| `level_cross.py GAME=DIR ...` | Check `LEVELS.DAT` of any of the games |
| `script_cross.py check\|table GAME=RES9 ...` | Check the dialogue scripts' structure (no text) |

`GAME` is `strahd`, `menzo` or `prophet`. Formats are described in [../docs/formats/](../docs/formats/README.md).
