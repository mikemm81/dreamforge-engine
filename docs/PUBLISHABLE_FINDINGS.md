# Inside DreamForge's *Menzoberranzan* (1994): reverse-engineering notes

A curated, living summary of what static analysis of the DOS release (GOG edition) has turned up so far.

- **Method:** static analysis only (Ghidra, plus Python parsers run over the shipped data files). **Nothing here has been tested by running the game**; items that need that are marked 🎮.
- **No game content is included.** This document describes formats and behaviour. You need your own copy of the game to use any of it.
- **Public edition.** Story spoilers, an instruction listing and a few quoted strings in the working notes are replaced by descriptions; `F-` numbers are unchanged. Byte-level formats: [`formats/`](formats/README.md).
- **Last updated:** 2026-11-25. See the [changelog](#changelog).

### Status labels
| Label | Meaning |
|---|---|
| ✅ Confirmed | Read from the bytes or code **and** agrees with a second source (data that parses completely, rendered art, another code path) |
| 🔎 From code | Read directly in the disassembly or pseudocode, with no second check |
| 🤔 Likely | Strong inference, not proven |
| 🎮 Needs in-game check | Static reading only; the behaviour has not been observed in the running game |
| 📚 Needs citation | Compares the game with published AD&D material from general knowledge; check the book before quoting |

### Which build
Addresses are linear addresses in this exact file, after conversion to ELF (see [F-02](#f-02-getting-the-executables-into-ghidra)):

| File | Bytes | SHA-256 |
|---|---|---|
| `CODE.1` | 294,203 | `905590c145176f93cef1651eb6b34a2253758aea94e1a621205402f0b7e2bda9` |
| `CODE.2` | 650,385 | `4a4fa725dddea00cb62ff44f44c48999cf6ed4c899b16c21826e8141ec7fda96` |

---

## Part 1. Engine overview

### F-01: What the executables are
✅ Confirmed, except where marked

- `M.EXE` is a small 16-bit real-mode launcher (Borland C++, 1991). It runs `CODE.2` and `CODE.1` in a loop driven by their exit codes (🤔 the meaning of the individual codes is inferred).
- `CODE.1` (🤔) is character generation. `CODE.2` is the game engine.
- Both are **Watcom C/C++32 programs, in LE format, bound to the DOS/4GW Professional extender**.
- `CODE.2` contains about 1,100 functions. Roughly 70 % of its function code is game logic; the rest is the renderer, resource, memory and sound layers and the Watcom runtime (🤔 split by address range).
- Calls pass arguments on the stack and floating point is almost absent, so decompilers cope well.
- Components identified from their strings: DFPLAYER (animation), BUFMGR, DFMEM (handle-based memory manager), and SpacePlayer.

### F-02: Getting the executables into Ghidra
✅ Confirmed

Ghidra 12.2 has no LE loader. Opened directly, it sees only the DOS/4GW stub (about 300 extender functions, no game code).

The workaround is a small converter, `le2elf.py` (not in this repository, see [`NOTICE`](../NOTICE)). It finds the inner LE image, places its two objects at their LE base addresses, applies the fixups and writes a 32-bit ELF, which Ghidra then loads normally.

| | `CODE.1` | `CODE.2` |
|---|---|---|
| Code object | base `0x10000`, size `0x1128d` | base `0x10000`, size `0x42ffe` |
| Data object | base `0x30000`, size `0x109e0` | base `0x60000`, size `0x45430` |
| Entry point | `0x1ccb8` | `0x4caec` |
| Fixups (all 32-bit offsets) | 2,976 | 11,835 |

Both files are layered: DOS/4GW stub, `BW` extender data, inner MZ stub at `0x25d24`, then the LE header at `0x27bdc`.

### F-03: A Watcom/Ghidra gotcha worth knowing
✅ Confirmed (method and result)

Watcom often pushes an *outer* call's arguments before making an *inner* call. Ghidra then credits them to the inner call, so the pseudocode shows wrong argument lists.

The caller's `ADD ESP,n` after each call gives the true count. A script that collects those and locks the function signatures reduced calls disagreeing with their function's own signature from **783 to 36 of 5,932**. The remaining 36 have not all been traced.

### F-04: Is the engine shared with the Ravenloft games?
✅ Answered: **one engine lineage in three generations**. See [F-29](#f-29-one-engine-three-generations).

An earlier version of this entry said that code sharing was unconfirmed. All three games use the same RES container, LZSS variant and text encoding, and `le2elf.py` converts all six `CODE` files.

### F-22: Shared cutscene code between *Menzoberranzan* and *Stone Prophet*
✅ Confirmed

Five cutscene decoder routines (493 bytes of hand-written assembly) are **byte-for-byte identical** in both executables of *Menzoberranzan* and both executables of *Stone Prophet*. They do not appear in *Strahd's Possession*, whose cutscenes use a different format (`.ANM`).

This proves shared library code between those two games. The rest of the engine is compared in [F-29](#f-29-one-engine-three-generations).

### F-29: One engine, three generations
✅ Confirmed for the family and for each format · 🤔 the exact percentages (they depend on the matcher)

*Menzoberranzan* and *Stone Prophet* are the same engine at two dates. *Strahd's Possession* is the same codebase one generation earlier: the odd one out, but not a different engine.

| Counterpart of *Menzo*'s `CODE.2` code (by bytes) | Stone Prophet | Strahd |
|---|---|---|
| byte-identical, addresses masked | 12 % | 3 % |
| also identical or close by decompiler features (score 0.70+) | 42 % | 17 % |
| related or weak match | 31 % | 38 % |
| none found | 14 % | 41 % (an upper bound) |

- **How it was checked.** Relocations were masked exactly, using the executables' own fixup tables. Matching also used compiler-independent features, because Strahd's `CODE.2` was built with different options (no frame pointers), which hides shared source: even inside one game, Strahd's two programs share only 10 % byte-identically, against 72 % for Menzo's two. Functions named by a unique string were held out: 92 of 92 matched ones were right, 24 to 33 % of the Strahd ones were missed. An unrelated Watcom program from the same disc matched 4 %.
- **Weak matches** are the least certain: of six drawn at random and read, five were right.
- **Shared:** resources, text, 2D graphics, script machine, level skeleton, ray-casting method, combat rules, launcher.
- **New after Strahd:** handle-based memory, bigger character records (0x248 → 0x41c bytes), a rewritten renderer (128 → 192 rows), `.DFA` cutscenes.
- **Per game:** spells, items, story events and AI are hard-coded each time.

Strahd's shipped `CODE.2` is dated March 1995, a year after the game, so this compares a rebuild.

### F-34: Carrying one game's analysis into the next
✅ Confirmed (method and checks) · names marked "propagated" are only as good as the match

Names and structures from *Menzoberranzan* were moved to *Stone Prophet* with three checks the matcher itself never used: where 13,618 address references vote for each global (99.3 % agree), whether both functions of a pair touch corresponding globals, and whether carried parameter lists fit argument counts measured separately (all do). Result: 33 % of *Stone Prophet*'s engine code is named, 11 points of it read there rather than carried over.

What had to change is small: the NPC record grew from 46 to 50 bytes (a fifth status timer), three other records from 36 to 44, and the map became a pointer. The character record is identical.

### F-45: The oldest engine, mapped
✅ Confirmed (method and checks) · names marked "likely" are hints only

*Strahd's Possession* shares much less with the later games and was recompiled in 1995, so carrying names over by matching alone would not have been enough. Matches from *Menzoberranzan* and *Stone Prophet* only said where to look; each name was then given from *Strahd*'s own code, and no structure was copied. Result: 58 % of its engine code is named (304 functions). By share of the code: 21 % had a matching name in a table (two thirds of that from weak matches, which were read before use), 8 % has a counterpart that no matcher found, 16 % was matched but unnamed before, and 14 % is new.

- 516 of 521 functions matched to both games agree with the direct match between those two.
- 8 of 8 saves and 118 of 118 saved levels parse to the last byte; the structures then predict, for example, every one of 22,932 stored map cells from its coordinates.
- Same-named functions in *Menzo* and *Strahd* use 70 % of the same record fields; unrelated ones 5 %.
- **Caveat:** names marked "likely" were about 1 in 6 wrong in a small random sample, and 61 functions with only weak matches were left unnamed.

---

## Part 2. File formats

### F-05: `RES*` containers
✅ Confirmed

- `u32 count`, then `count` × `u32` absolute offsets. Entries may be empty.
- Most entries start with `"EH"`, `u8 type`, `s16 header_length`, then the header bytes.

| Type | Content |
|---|---|
| 1 | Sprite set |
| 2 | Sprite set with its 8-byte header at the *end* of the data |
| 3 | XMIDI music |
| 4 | Digital audio |
| 5 | Blob with `u8 compression, u32 size` (walls, floor/ceiling tiles) |
| 6 | Blob with `u8 compression, u16 size` (column shapes) |

Compression codes: 0 raw, 1 RLE (PackBits-style), 2 LZW (present in the code but disabled), 3 LZSS.

**LZSS:** a 4,096-byte ring pre-filled with `0xFE`, start position `0xFEE`. Flags are read least-significant bit first, and 1 means literal. For a match, `offset = b0 | (b1 & 0xF0) << 4` and `length = (b1 & 0x0F) + 3`.

**PackBits-style RLE:** a signed control byte `c`. If `c < 0`, repeat the next byte `1 − c` times; otherwise copy `c + 1` literals.

### F-06: Images and palettes
✅ Confirmed by rendering

- **`COLORS`:** `u8 n` (41 palettes) followed by `n` × 768 bytes of 6-bit VGA RGB. Palette 0 is right for sprites, portraits and 3D art. Some full-screen art needs another palette (which one is still open).
- **Type 1 sprites:** `s16 count, s16 height, u8 width/8, u8 transparent (0xFF = none), u8 compression, u8 flag`, then `count × height × width` linear 8-bit pixels. Large images are stored as many one-pixel-high rows to be stitched together.
- **Walls (type 5):** `u16 192` then 64 × 192 pixels, **column-major**. Floor/ceiling tiles are 64 × 64 (🤔 their row/column order is unconfirmed, as are 2 trailing bytes).
- **Creatures, objects, items and masked wall overlays (type 6):** column shapes in the style of *Wolfenstein 3D*. Header `u16 width, left, right, top, bottom`, a column offset table at `+0x0A`, then per-column commands `(end*2, s16 source, start*2)` ending with 0. Rows are in a 192-high space.
- **Shape numbering in `RES3`:** `shape = kind_base + type_base + frame × views + view`, with the kind bases read from `INIT` (NPC 25, object 1946, item 2726, effects 2976).

### F-07: Game text (`T`): XOR `0xDC`
✅ Confirmed: 1,185 of 1,185 entries decode

`u32 count` (1185), `u32` offsets, then per entry `u16 length` and that many bytes, each XORed with **`0xDC`**. 1,183 decode to plain printable text; the other two were not examined. The file holds dialogue, interface labels and item text.

### F-08: Levels (`LEVELS.DAT`)
✅ Confirmed: all 56 levels parse to the last byte

`u32 count` (56), `u32` offsets. Each level, in order:

| Part | Size |
|---|---|
| `u8 0`, `u32 section[7]` (ignored by the engine) | `0x1D` |
| Level header: size, floor/ceiling, 64 wall types, spawn counts, sky, light, music | `0x824` |
| Map cells | `width × height × 2` |
| For each map column: `s16 n`, then `n` cell records of 16 bytes | varies |
| NPC, object, then item spawns | 11 bytes each |
| 50 trigger lists of 26 bytes | `0x514` |
| Dimensions again, automap, automap notes | varies |

- **Spawn:** `s16 type, s16 x, u16 x fraction, s16 y, u16 y fraction, u8 facing`.
- **Cell records** cover doors, wall switches, locks, floor triggers, spinners, exits and things to examine.
- **Trigger list:** a count and up to five actions `{op, y, x, arg, arg2}`. Operations include open/close/toggle, change wall, fire an effect, spawn a monster, play a cinematic and raise an event.
- The stored section table is out of date in 21 levels; the engine reads sequentially and never uses it.
- 21 of the 56 levels are identical 12 × 12 placeholders, which leaves 35 real ones.

Cross-checks: every one of 6,842 spawns has a valid type and lies inside its map. All 35 lock records name a key item. 191 of the 199 exits outside the placeholders that lead to a normal level land on open floor, and the other 8 link level 44 to itself. Nine more exits lead to "level 99", the world map.

### F-09: Saves
✅ Confirmed: every sample parsed to the last byte (2 `SAVE.DAT`, 2 `LEVnnn.DAT`)

- `SAVE.DAT`: a 50-byte name, a `0x2F2F`-byte state block (starting with the plot flags), clocks, the item on the cursor, the 4-character party, active effects, 94 containers, and the 9-record built-in roster.
- `LEVnnn.DAT`: a level in its current state, in nearly the same order as F-08 but with live monster, object and item arrays.

### F-10: Not decoded yet
- ~~`.DFA` cutscenes~~: decoded, see [F-21](#f-21-dfa-cutscenes).
- *Strahd's Possession*'s `.ANM` cutscenes and *Stone Prophet*'s `.ACT` files.
- `INIT` fields beyond the type tables; `DATA.A00`/`DATA.A01` (multi-volume ARJ archive, not unpacked).
- Type 2 sprites (`RES0`): the loader has been read, but these images have not been exported.

### F-21: `.DFA` cutscenes
✅ Confirmed: all 61 files decode to the last byte, and all 5,192 frames match FFmpeg pixel for pixel

**Prior art:** FFmpeg already decodes this format, under the name "Chronomaster DFA". What follows was derived independently from the game's player code and agrees with it.

- **Header (128 bytes):** `DFIA`, `u8 0`, `u8 1` (planar), `u16 frames, width, height`, `u32 milliseconds per frame`, `u32 first chunk offset`.
- **Chunks:** four-character name, `u32 size`, `u32 type`, data. The player uses only the type.
- **Gotcha:** the frame buffer is **four VGA Mode X planes stored one after another**. A decoder that ignores this still parses every byte perfectly, but shows a 4 × 4 mosaic.

| Type | Name | Purpose |
|---|---|---|
| 0 | EOFR | End of frame |
| 1 | PAL1 | Palette (6-bit RGB) |
| 2 | COPY | Raw frame |
| 3 | TSW1 | LZ on 16-bit words |
| 4, 5 | BDLT, WDLT | Per-line delta with runs (bytes, words) |
| 6 | TDLT | Copy/skip tokens |
| 7 | DSW1 | LZ on 16-bit words with skips |

*Menzoberranzan* uses only types 0, 1, 3, 6 and 7. The decoders for 2, 4 and 5 were checked against FFmpeg with synthetic files instead. *Stone Prophet*'s 36 DFA files (11,074 frames, which add a type 8 "BLCK" clear) match FFmpeg too.

### F-32: Strahd's `.ANM` and Stone Prophet's `.ACT`
✅ Confirmed (13 of 13 `.ANM` files, 4,149 frames, every chunk exact; samples rendered) · prior art for `.ANM` was not searched for

- **ANM:** u16 frames, u16 ticks; per frame a u16 chunk count, then chunks (u16 type, u32 size): 0 palette, 1 clear, 2 RLE 320×400 screen, 3 vertical delta runs, 5 music, 6 sound, 7 caption, 8 raw 320×256 in four VGA planes, 9 speech (4, a loop, is never used). Unrelated to `.DFA`.
- **ACT:** an event track beside a `.DFA`: u16 words, two preload lists, then one list per frame of (−50 caption, −51 music, −52 sound) and an argument, each list ended by −1. It matches the DFA's frame count in 30 of 32 files (one is one short; `LOGOS.ACT` is a copy of `SHIP.ACT`).
- *Menzo* has no such file: its cutscene sounds and captions are in the code.

### F-35: *Stone Prophet*'s palettes, `TABLES` and `LEVINFO`
✅ Confirmed from code, by file size and by rendering (walls)

- **Area palette = wall set + 4**, where the wall set is a byte in the level header. Examine pictures use palette 65 + picture; dialogue scenes use 18 + script number.
- **`TABLES`**: 15 records of 0x12400 bytes, one per wall set: four 0x900-byte shading tables and a 0x10000-byte table (its use was not traced).
- **`LEVINFO`**: 16 records of 0x801 bytes, the wall types for the 16 regions of the 256 × 256 desert level, loaded as you walk.

### F-36: Why *Stone Prophet*'s save block is bigger
✅ Confirmed

The state block grew from 12,079 to 14,060 bytes. 1,800 of the 1,981 extra bytes are the memorised-spell list (4 × 90 entries), whose names grew from 30 to 35 characters; the rest is new game state. A shipped save parses to the last byte.

### F-46: *Strahd*'s records: the same idea, smaller
✅ Confirmed from the save and level code, and by 8 saves and 118 saved levels

- **Character:** 0x248 bytes. The first 0x6A bytes (name, class, hit points, levels, XP, abilities, all 21 item slots) are laid out exactly as in *Menzoberranzan*. After that there are 10 spells per level instead of 18, 26 lasting effects instead of 58, and no extra byte per item.
- **Monsters, objects, items and effects** are single 0x16-byte records with the same limits as later (100 / 450 / 300 / 150). *Menzo* split them in two.
- **SAVE.DAT** is always 9,288 bytes: name, 256 plot flags, position, party, up to 150 effects, game time, 62 containers and 10 built-in companions. (The fixed size holds when no effects are active, as in all samples.)
- A level header lists three monster types that **come back** somewhere else when killed (🎮).

---

## Part 3. How the world is wired together

### F-11: The script system
✅ Confirmed. The full opcode list is now in [F-19](#f-19-all-42-script-opcodes).

- A bytecode interpreter (`0x29b18`) runs 16-bit words: **42 opcodes, numbered 2–44 with no case for 4**. They switch on the word at an instruction pointer into a buffer loaded from `RES9` (CD).
- Plot state is a bit array of **256 flags** (an earlier version of this document said 416; see [F-25](#f-25-256-plot-flags-and-what-they-are-for)). Scripts and hard-coded C both read and write it.
- Dialogue is a list of choices, each with a jump target, a speaker and four (kind, value) variants.

### F-12: Levels never name a script
✅ Confirmed for the talking path

Talking to an NPC runs **script number = NPC type − `0x31`**. Drizzt is NPC type `0x35`, so the script is 4, and the code independently calls script 4 in one other place. A second built-in character checked by name leads to the matching script as well.

- 30 of the 32 placed NPC types that can be talked to have a script. The two without are types 28 and 29. 🎮 What happens when you talk to them has not been observed.
- Which `RES9` entry is script 0 (418) was found by fitting the data, not read from a single constant.
- NPCs appear or disappear through a **hard-coded** table of NPC type → plot flag, applied when a level loads. Trigger lists *can* set plot flags, but no shipped level does.

### F-19: All 42 script opcodes
✅ Confirmed for the 35 opcodes the game uses (effects of three are 🤔 likely) · 🔎 from code for the 7 it never uses

All 31 scripts disassemble to exactly their stored length. All 1,021 jump targets land on an instruction, and every text, item, flag, NPC and character reference is valid.

| Group | Opcodes |
|---|---|
| Talking | 2 NPC speaks · 42 party remark (optionally once) · 43 pop-up line · 25 / 39 text / speech base |
| Choices | 14 open the list · 19 add · 20 remove · 21 leave after this branch · *3 retarget, 26 add first (unused)* |
| Flow | 7 end branch · 18 call · 24 jump · 27 end script · 15 timer · 44 🤔 inside-call marker |
| Plot flags | 9 / 13 jump if set / not set · 10 set · *11 clear, 12 toggle (unused)* |
| Local flags | 30 / 31 set / clear · 33 / 34 jump if set / not set · *32 toggle (unused)* |
| Items | 6 give · 8 jump if carried · 40 take · 41 drop |
| People | 5 a built-in character joins · 22 an NPC leaves the level |
| Pictures, sound | 16 draw · 17 show · 23 picture base · 35 random picture · 38 🤔 clipped draw · 29 🤔 music · 36 cinematic · *28 sound, 37 modified draw (unused)* |

### F-23: Who says the line depends on who is in your party
✅ Confirmed

Each dialogue choice holds up to four (test, text) pairs. The first pair that some able party member satisfies is used, and **that member speaks it**. Tests cover alignment, race, class, sex, and stats of 17 or more or below 10. Text also adapts to party size, through a `[plural|singular]` notation.

Race and alignment numbers follow the game's own lists (race: human, half-elf, halfling, gnome, dwarf, elf, centaur, kenku, drow). Drizzt comes out as drow and chaotic good, as expected.

### F-24: Hard-coded party remarks
✅ Confirmed by decoding the routine to the last byte · 🔎 not run

Besides the scripts, the characters comment on events through a hard-coded switch at `0x34bf0` (1,712 bytes, 65-entry jump table, 44 handlers).

- One global holds a pending event id. Level triggers, item pick-ups, level changes, locks, cutscenes and scripts set it; the main loop handles it after drawing a frame. There is **one slot**, so a later event replaces an earlier one.
- A handler may test a plot flag or a carried item, then passes an id and up to four (test, text) pairs to one routine (`0x349f4`). The tests are the same as in dialogue ([F-23](#f-23-who-says-the-line-depends-on-who-is-in-your-party)).
- Flag 128 + id marks a remark as already said; id −1 means it may repeat.
- The texts form one 65-entry block in `T`, starting at 948, which ends exactly where the next block named by `INIT` begins.
- Two real saves agree: bit 129 is set after the opening remark (id 1).

### F-25: 256 plot flags, and what they are for
✅ Confirmed for the layout · meanings: 65 ✅, 10 🤔, 10 dead (of 85 used)

The flag array at `0x68f84` is 8 dwords, not 13: a new game clears exactly 8, and the next five are counters for True Seeing, Detect Enemy, Detect Evil, Detect Magic and Faerie Fire. Flags 0–127 are story state and 128–255 are "remark said" bits. No level trigger in the shipped data sets a flag; scripts and C code do all of it.

Every access was found by scanning the machine code and all 31 scripts, then cross-checked: each used flag has a meaning, and each flag called dead has no setter anywhere. "🤔" flags are set but never read, so their meaning comes from one side only.

### F-30: One script language, three dialects
✅ Confirmed

All scripts of all three games disassemble to their exact length (38, 31 and 48), with every one of 3,822 jump targets on an instruction. The wrong game's table fails, except that Menzo's layouts are a subset of Stone Prophet's.

- *Strahd* implements opcodes 0–41, *Menzo* 2, 3 and 5–44, *Stone Prophet* 2–55. **Strahd and Menzo both have 42, but not the same 42.**
- 36 opcodes are the same in all three. Menzo added 42–44, Stone Prophet 45–55; Strahd's 0, 1 and 4 were dropped.
- Watch out: in *Stone Prophet* the speaker tests above 24 are shifted by five, so a script can't be moved over unchanged.

### F-31: One level skeleton
✅ Confirmed: 32, 56 and 43 levels parse to the last byte

Same order of parts in all three: an ignored 29-byte prefix, header, map, per-column records, 11-byte spawns, 50 trigger lists of 26 bytes, size, automap, notes. *Menzo* and *Stone Prophet* have identical sizes; *Strahd* has a smaller header (0x2a4 against 0x824) and 10-byte records (against 16). What some record kinds mean differs between games.

### F-37: New things on the map in *Stone Prophet*
✅ Confirmed in code · 🎮 not seen in game

Map record kinds gained forced movement (7) and pits (8); exits moved from 8 to 9, and examine pictures from 10 to 11. A map flag marks hazard cells that hurt, or cause a lasting status, depending on the level.

### F-49: *Strahd*'s story machinery
✅ Confirmed for format and counts · nothing observed in play

- 38 of 38 scripts, and the whole hard-coded remark routine (`0x2ce68`: 2,022 of 2,022 instructions, 92 events, 128 of 128 paths) decode. The routine is an optimised decision tree, so it was executed symbolically once per event instead of being read from a table.
- 256 plot flags: 88 story flags in use (85 ✅, 1 set but never read, 2 dead), 76 "already said" bits at 128 + id, as in *Menzoberranzan*.
- **Checked with saves:** every flag set in 8 saves has a known setter, and "companion joined" flags match the party in 80 of 80 cases. The first version of the flag list failed that test by one flag, which led to a remark said outside the routine.
- Flags 4–13 are "joined" (built-in character + 4), 15–24 "dropped while dead".

### F-50: An earlier build's scripts are on the CD
✅ Confirmed in data · 🤔 that it is a leftover (no code was found that opens `RES12`)

`RES12` on the *Strahd* CD contains 37 of the game's 38 scripts from some days earlier (files dated 4 and 10 March 1994). They use the same language and the same text numbers, but have no speech, and the instruction that adds a companion carries **the whole character sheet** instead of a number. All 37 parse exactly with a 141-word record.

- About nine tenths of the early story steps survive; the shipped set adds guards against receiving things twice and topics that appear only after you have heard of them.
- The sheets show work in progress: missing ability scores, equipment copied from another character, classes and sexes that changed later.

---

## Part 4. Fun discoveries

### F-13: It really is AD&D 2nd Edition under the hood
🔎 From code / 📚 comparison with the books needs citation

- To-hit is a d20 roll against `THAC0 − AC`, adjusted for strength, encumbrance and effects. A total of 20 or more always hits and 1 always misses.
- Exceptional strength (18/xx) is stored and used.
- Experience is split among conscious party members, then between each character's classes.
- Monsters carry separate immunity and resistance flags for blunt, edged, +1/+2 and +3-or-better weapons. A magic weapon counts as its own type, so blunt/edged immunity does not apply to it.
- The experience table and weapon dice match the published rules wherever compared (long sword 1d8 / 1d12 vs large).
- 🤔 Spell durations use multiples of 1,080 and 10,800 ticks, which at about 18 ticks per second would be the rules' 1-minute rounds and 10-minute turns. Not measured.

### F-14: A possible bug: do magic weapons hit *less* often?
🔎 From code · **🎮 Needs in-game check. Do not present as established.**

In the melee to-hit function (`0x1ee40`), the weapon's magic bonus, less any temporary enchantment, is **added to the number you need to roll**. AD&D would subtract both.

On this reading a +2 sword needs a roll 2 higher than a plain one.

Why it is only "possible":
- It has not been observed in the running game.
- Only this function has been checked; another code path could compensate.
- It covers melee to-hit only. Whether the bonus affects damage was not examined here.

**Suggested test:** same character, same enemy, a plain and a +2 weapon, and a few hundred swings each.

**Probable reason:** see [F-47](#f-47-why-the-weapon-plus-is-backwards).

### F-15: A typo in the cleric experience table
✅ Confirmed in the data · 🎮 effect on play not observed

The experience table (`0x68644`, 6 classes × 20 levels, in thousands) has `2055` at cleric index 17:

`… 1575, 1800, 2025, `**`2055`**`, 2475, 2700`

Neighbouring steps are all 225, so `2250` is the value that fits. Taken literally, a cleric would reach that level about 195,000 XP too early.

### F-16: Drizzt is special-cased by name
✅ Confirmed in code · 🎮 in-game behaviour not observed

When a character takes damage (`0x1f4a4`) and drops to 0 HP or below, the code **compares the character's name with "DRIZZT"**. If it matches, the character is not marked dead but leaves the party, with four specific items, and two plot flags are changed.

### F-17: Drizzt as the Rosetta Stone for the character record
✅ Confirmed internally · 📚 published stats need a citation

The game ships built-in characters in the data section as complete 1,052-byte (`0x41C`) records. Drizzt's made it possible to check the decoded layout:

| Field | Offset | Drizzt's value |
|---|---|---|
| Name | `+0x00` | `DRIZZT` |
| Level (first class) | `+0x20` | 15 |
| Experience (first class) | `+0x26` | 2,250,000 |
| STR, DEX, CON, INT, WIS, CHA (base; each followed by the current value) | `+0x32` … `+0x3D` | 13, 20, 15, 17, 17, 14 |

Those six scores match his published AD&D statistics in the standard order, which fixes the field order.

Other anchors that agree: the umber hulk's record (8 hit dice, AC 2, THAC0 11, 3d4 damage, 4,000 XP), and a shipped quick-start save whose title names the races and classes of two characters, which is what it decodes to.

### F-18: A disabled graphics option
✅ Confirmed

The "Sprite Dithering" setting exists in the detail menu, but the renderer never reads its flag. Only the menu's drawing and click handlers do.

### F-20: Two companions' plot flags are swapped
✅ Confirmed in code and data · **🎮 Needs in-game check** for what the player sees

Each recruitable companion has a "joined" plot flag (0–9) and a "died" flag (joined + 10). The recruit scripts set the joined flag, and a table in the code says which one to clear when a companion leaves.

| Companions | Set by their script | Cleared by the code |
|---|---|---|
| six of them | 0, 1, 2, 3, 8, 9 | the same |
| **fourth** | **4** | **5** |
| **fifth** | **5** | **4** |

So when one of those two leaves, the code clears the *other* one's flag. The flags also decide whether the companion's NPC appears on the map, so this could hide or bring back the wrong NPC.

**Suggested test:** recruit the companions with flags 4 and 5, dismiss one, and revisit the places where they were found.

### F-26: A quest potion sets the wrong flag
🔎 From code · **🎮 Needs in-game check** for what the player sees

Flag 60 means "carrying the potion" and 61 means "it was drunk": dropping or throwing the potion clears 60, and level changes test both. But picking the potion up sets **61**, and nothing anywhere sets 60. (The similar code for another quest item sets and clears the same flag.) As a result one remark cannot play, and code that would put the potion back where it was found never runs.

**Suggested test:** pick the potion up on level 9, leave without drinking it, and come back. The code says it is removed and not put back.

### F-27: Two remarks share one "already said" flag
✅ Confirmed in machine code · 🎮 not seen in game

A line in one story scene and the remark for picking up one kind of potion both use id `0x28`, so whichever plays first silences the other.

### F-28: Cut content the flags reveal
✅ Confirmed that nothing reaches it · 🤔 that it was planned content

- Two dialogue branches, one of them long, are in the scripts, but nothing sets their flags (89 and 33).
- Two flag pairs (6/16 and 7/17) are tested like companions' joined/died flags, for two characters who can't be recruited.
- Six remark handlers are never triggered, and one event is triggered from seven places but has no handler.
- A set of replies from NPCs is behind a check that always fails (it is leftover *Strahd* code, see [F-48](#f-48-leftovers-from-strahd-in-the-later-games)), and one class-specific hint is never called.

### F-33: Bugs with a family history
✅ Confirmed in data and machine code · **🎮 Needs in-game check** for the weapon one

- The cleric XP typo ([F-15](#f-15-a-typo-in-the-cleric-experience-table)) is in **all three games**; the 470-byte table block is byte-identical.
- The weapon bonus that is *added* to the number needed to hit ([F-14](#f-14-a-possible-bug-do-magic-weapons-hit-less-often)) is in all three to-hit routines (Strahd `0x1c80c`, Menzo `0x1ee40`, Stone Prophet `0x20b10`).

Inherited mistakes are good evidence of common source. They don't show that the weapon code misbehaves in play.

### F-38: *Stone Prophet* has a hidden difficulty level
✅ Confirmed in code · 🎮 not measured in play

It isn't a menu option. When a new party is loaded, its average level sets a value from 0 to 3 (below 7, 7–8, 9, 10 and up) that changes monsters' hit points, to-hit, damage and attack pauses, and some monsters' XP. The shipped quick-start save (average level 4.5) stores 0, as predicted. *Menzoberranzan* has nothing like it.

### F-39: Fixed in the sequel: the swapped companion flags
✅ Confirmed in code

The companion-flag mix-up in *Menzoberranzan* ([F-20](#f-20-two-companions-plot-flags-are-swapped)) is gone in *Stone Prophet*: there is no table any more, the companion's number is the flag number. The reversed weapon bonus ([F-33](#f-33-bugs-with-a-family-history)) is still there.

### F-40: *Stone Prophet*'s story machinery
✅ Confirmed for format and counts · nothing observed in play

- 48 of 48 dialogue scripts and the whole hard-coded remark routine (`0x395d4`, 1,559 of 1,559 bytes, 107 event ids, 45 with handlers) decode completely.
- 256 plot flags again: 0–149 story (140 used: 126 ✅, 9 set but never read, 5 dead), 150–207 "already said" bits, the rest unused. Every access in code, scripts and triggers was listed and checked against a meaning table; planted errors are caught.
- The "already said" ids are shared by party remarks and companion lines with no clash, unlike *Menzoberranzan* ([F-27](#f-27-two-remarks-share-one-already-said-flag)).
- One flag isn't stored but computed (can the party talk with animals), and one is rewritten from the clock before every conversation (night).
- New compared with *Menzoberranzan*: a **hidden counter** kept by the scripts, and conversations in which **several characters' scripts are chained** in one screen in a fixed order.

### F-41: Two good deeds are credited to the wrong quest
✅ Confirmed in data and machine code · **🎮 Needs in-game check**

*Stone Prophet* keeps ten "good deed" flags. Two scripts use the wrong number: one quest is recorded as another quest's deed, and that other quest's script records a third, which has no text. The flag for the first quest is tested but never set. Three independent sources agree on the intended numbering (the ten replies that list deeds, the pairs used by the test, and the six other setters).

**Suggested test:** finish only the first of these quests, then see which deed is credited. The code says the wrong one is named.

### F-42: Broken-promise checks that test too little
🔎 From code · **🎮 Needs in-game check**

Three of the five "broken promise" tests check only that you *met* someone, not that you agreed to anything. Penalties are never taken back, and a small positive score is answered with the text meant for a negative one.

### F-43: Flags *Stone Prophet* tests but never sets
✅ Confirmed that nothing sets them · 🤔 intent

Five: 18, 58, 70, 85 and 89. They leave behind a conversation that always starts over, an answer that can never be given, one companion missing from a table (it covers 8 of 9), and an unreachable remark. Another remark is event number 0, which the main loop treats as "none", so it can't play although a floor trigger asks for it. Level 42 contains one of every talking character and no exit leads to it (🤔 a test level).

### F-44: A timed event that can be skipped
🔎 From code · **🎮 Needs in-game check**

One countdown in *Stone Prophet* compares elapsed minutes with exactly 5, but only the main loop updates it. Resting advances the clock in its own loop, so (if resting is allowed at that point) a long rest jumps over 5 and the event never comes.

### F-47: Why the weapon plus is backwards
✅ Confirmed in code and data of all three games · 🤔 the explanation · **🎮 effect on play not observed**

All three games *add* a weapon's plus to the number needed to hit ([F-14](#f-14-a-possible-bug-do-magic-weapons-hit-less-often)). *Strahd* shows a likely reason: its damage routine (`0x1c910`) asks whether the plus is `>= -2`, so it was written for pluses stored as **negative** numbers (a +3 weapon as −3, the way THAC0 and AC improve downwards). The shipped item file stores +1 to +3. With negative numbers both routines make sense; with the shipped ones they are reversed.

In *Strahd* the same mistake has a second effect: for monsters that should need a +3 weapon (three types), **any magic melee weapon does 1 point of damage**, because every stored plus passes the test (one special rod is exempt). Weapons that fail an immunity also do 1. Thrown weapons don't go through this routine. *Menzoberranzan* repaired that test (`plus < 3`) but kept the to-hit line.

**Suggested test (*Strahd*):** hit one of those monsters (banshees are one type) with a +2 weapon and watch the damage numbers; then throw a weapon at it.

### F-48: Leftovers from *Strahd* in the later games
✅ Confirmed in code

- The unused NPC replies in *Menzoberranzan* ([F-28](#f-28-cut-content-the-flags-reveal)) are *Strahd* code: the same five NPC numbers and the same five remark numbers. In *Strahd* it works.
- The main function of **all three** games reads four environment variables with meaningless names (`ALSIU`, `KHSSDKJ`, `CANLW`, `SEPVON`): start level, two map coordinates and facing. Without them *Strahd* uses the quick-start position. A developer shortcut; 🎮 not tried.

### F-51: More wrong numbers: four flag slips in *Strahd*
✅ Confirmed in machine code · **🎮 Needs in-game check** for what the player sees

- A check for four collectable parts tests parts 1, 2, 3 and then 3 again, so **three of the four** are enough.
- One companion's "has left" flag is written as 33 and read as 27, so one conversation can't happen.
- One floor action marks the wrong remark as said (0x9D instead of 0xBB).
- One dialogue line depends on a flag that is cleared before it could be used.

With *Menzoberranzan*'s potion ([F-26](#f-26-a-quest-potion-sets-the-wrong-flag)) and *Stone Prophet*'s deeds ([F-41](#f-41-two-good-deeds-are-credited-to-the-wrong-quest)), every game of the three has at least one: flags were plain numbers typed by hand.

### F-52: Remarks that lost their handlers
✅ Confirmed that nothing handles them · 🤔 which text belongs to which

Six events fired by *Strahd*'s floor triggers (30, 34, 82, 83, 94, 99) have no handler, and five handlers are never fired. The texts still ship. Compare [F-28](#f-28-cut-content-the-flags-reveal) and [F-43](#f-43-flags-stone-prophet-tests-but-never-sets).

---

## Open questions (good places to help)

| # | Question | Needs |
|---|---|---|
| Q1 | Do +N weapons really hit less often (F-14)? | 🎮 |
| Q2 | What happens at the cleric level with the 2055 XP entry (F-15)? | 🎮 |
| Q3 | What does the player see when Drizzt reaches 0 HP (F-16)? | 🎮 |
| Q4 | What do NPC types 28 and 29 do when clicked (F-12)? | 🎮 |
| ~~Q5~~ | ~~Decode the `DFIA` cutscene codec~~ | Done: F-21 |
| ~~Q6~~ | ~~Name all 42 script opcodes~~ | Done: F-19 |
| ~~Q7~~ | ~~Is the *3D engine* code shared with the Ravenloft games?~~ | Done: F-29 |
| Q8 | Verify the AD&D comparisons against the rulebooks (F-13, F-17) | 📚 |
| Q9 | Which palette does each full-screen image use (F-06)? | Analysis |
| Q10 | What happens when the companion with flag 4 or 5 leaves the party (F-20)? | 🎮 |
| Q11 | Can you lose the quest potion by leaving its level (F-26)? | 🎮 |
| Q12 | Does the potion remark silence the scene's line (F-27)? | 🎮 |
| Q13 | Which code sets flag 128 in the shipped quick-start save (F-25)? Lead: in *Strahd* it is the opening remark, id 0 (F-49); *Menzo* has no remark 0 | Analysis |
| Q14 | Which deed is credited after the first quest (F-41)? | 🎮 |
| Q15 | Are you penalised for people you only met (F-42)? | 🎮 |
| Q16 | Can you rest during the countdown, and does it skip the event (F-44)? | 🎮 |
| Q17 | Do magic melee weapons do 1 damage to banshees in *Strahd* (F-47)? | 🎮 |
| Q18 | Do the start-position environment variables work (F-48)? | 🎮 |
| Q19 | Do three of the four parts suffice (F-51)? | 🎮 |

---

## Maintaining this document
- No spoilers, instruction or pseudocode listings, quoted game text, or personal save data.
- Add new findings at the end of the matching part with the **next free `F-` number**, and never renumber or reuse numbers, so outside links stay valid. Copy this block:

```
### F-NN: Short title
STATUS (✅ / 🔎 / 🤔 / 🎮 / 📚, with a note if mixed)

What it is, in a sentence or two. Address or file offset if relevant.
How it was checked, and what was *not* checked.
```

- When a 🎮 or 📚 item is settled, update its status line, close its `Q` row and log it below.
- If a finding turns out wrong, keep its number, strike the title through and say why.

**Next free number: F-53**

### Changelog
| Date | Change |
|---|---|
| 2026-11-24 | First version: F-01 to F-18 |
| 2026-11-24 | F-19 to F-23 added (opcodes, companion flags, DFA, shared code, dialogue speakers). Q5 and Q6 closed, Q10 added; F-04, F-10 and F-11 updated |
| 2026-11-24 | F-24 to F-28 added (remarks, plot flags, jump potion, shared remark flag, cut content). F-11 corrected: 256 flags, not 416. Q11 to Q13 added |
| 2026-11-24 | F-29 to F-33 added (cross-game comparison, script dialects, levels, ANM/ACT, inherited bugs). F-04 answered, F-22 updated, Q7 closed |
| 2026-11-24 | F-34 to F-39 added (*Stone Prophet*: propagation, palettes, save block, map records, difficulty, companion flags) |
| 2026-11-24 | F-40 to F-44 added (*Stone Prophet*: story machinery, deeds, dead flags, countdown). Q14 to Q16 added |
| 2026-11-24 | F-45 to F-48 added (*Strahd*: names and records, the negative weapon plus, leftovers). F-14 and F-28 point to them. Q17 and Q18 added |
| 2026-11-24 | F-49 to F-52 added (*Strahd*: story machinery, early script set, flag slips, lost handlers). Q13 lead, Q19 added |
| 2026-11-25 | Public edition: spoilers, an instruction listing and quoted strings replaced by descriptions |
