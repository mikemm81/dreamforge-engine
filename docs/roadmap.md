# Roadmap

Future milestones for the Unity engine in `Unity_DF_Alternative/`. Kept short; details are worked out when a milestone starts.

## Done

1. `RES` directory reader
2. Level parser and ASCII map
3. Walkable 3D level, untextured
4. Palette and wall/tile texture decode; textured level 1 (walls, floor, ceiling), checked against the game in DOSBox for handedness and tile axes

## Next

- **Wall overlays and decals.** In the game, level 1 shows banners, a dart board and extra windows on its walls, which we don't draw yet. They are most likely the cell records that point at a `RES4` entry (byte 0x08) and sit on one wall face (0x0E) and position (0x0F), drawn as type-6 masked column shapes. Level 1 has six: (2,2), (3,6), (6,1), (9,1), (9,6) and (11,5), using `RES4` 43–45. See [formats/levels.md](formats/levels.md) and [formats/images.md](formats/images.md).
- **Other wall sets.** Only wall set 0's start in `RES4` is known, so levels using other sets fall back to placeholders.
- **Two-row walls.** The layout is built but untested; level 0 is the first level that needs it.
