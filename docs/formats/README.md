# File formats

| Document | Files |
|---|---|
| [resources.md](resources.md) | `RES*`, compression |
| [images.md](images.md) | `COLORS`, sprites, walls, tiles, column shapes |
| [types.md](types.md) | `INIT`, `NPCS.DAT`, `OBJECTS.DAT`, shape numbering |
| [levels.md](levels.md) | `LEVELS.DAT`, saved levels |
| [scripts.md](scripts.md) | dialogue scripts in `RES9`, plot flags |
| [text.md](text.md) | `T` |
| [saves.md](saves.md) | `SAVE.DAT`, character records |
| [cutscenes.md](cutscenes.md) | `.DFA`, `.ANM`, `.ACT` |

## Conventions

- Numbers are little-endian. `u8`, `s16`, `u32` and so on give the sign and size in bits. Offsets and sizes starting with `0x` are hexadecimal.
- "16.16" is a 32-bit fixed-point number with 16 fraction bits.
- **S**, **M** and **P** mean *Strahd's Possession*, *Menzoberranzan* and *Stone Prophet*.
- Bytes that aren't listed have an unknown meaning or aren't used. *(likely)* marks an inference.
- The formats were derived from the GOG releases. Parsers were checked by using every byte of the shipped files, except where a document says otherwise. Nothing has been checked in the running games.
