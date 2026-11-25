# Dialogue scripts

Scripts are the `RES9` entries without an `"EH"` header: `u16 count`, then `count` × `u16` words, then zero padding.

| | S | M | P |
|---|---|---|---|
| Scripts | 38 | 31 | 48 |
| `RES9` entry of script 0 | 517 | 418 | 566 |
| Script of a talking NPC | type − 0x22 | type − 0x31 | type − 0x31 |
| Opcodes | 0–41 | 2, 3, 5–44 | 2–55 |

## How a script runs

1. Run from word 0 until opcode 14 (open choices) or 27 (end).
2. Show up to four choices. The one picked runs from its target until opcode 7.
3. Opcode 21 makes the conversation end after the current branch.
4. Finally, run on to opcode 27.

Five repeating timers (opcode 15) run while the script waits, for the talking animation. Jump targets are word positions; `0xFFFF` means none.

## Opcodes

| Op | Operands | Meaning | Games |
|---|---|---|---|
| 0 | n, n words | set all choice texts | S |
| 1 | n, n words | set all choice targets | S |
| 2 | speech, text | NPC speaks. S: speech, then pairs up to the end test | all |
| 3 | target, choice | change a choice's target | all |
| 4 | text, choice | change a choice's text (P: no operands, does nothing) | S |
| 5 | character | a built-in character joins | all |
| 6 | item | give an item | all |
| 7 | – | end of branch | all |
| 8 | item, target | jump if the party has the item | all |
| 9, 13 | flag, target | jump if a plot flag is set, not set | all |
| 10, 11, 12 | flag | set, clear, toggle a plot flag | all |
| 14 | – | open the choices | all |
| 15 | timer, target, ticks | repeating timer, target `0xFFFF` = off | all |
| 16 | picture, y, x | draw | all |
| 17 | – | show what was drawn | all |
| 18 | target | call: run until a branch ends, then return | all |
| 19, 26 | target, id, pairs. S: text, target | add a choice at the end, at the front | all |
| 20 | id | remove a choice | all |
| 21 | – | leave after this branch | all |
| 22 | NPC type | NPC leaves the level | all |
| 23, 25, 39 | base | picture, text, speech base | all |
| 24 | target | jump | all |
| 27 | – | end | all |
| 28, 29 | number | sound, music | all |
| 30, 31, 32 | flag | set, clear, toggle a local flag | all |
| 33, 34 | flag, target | jump if a local flag is set, not set | all |
| 35 | picture, count, y, x | draw one of several at random | all |
| 36 | number | cutscene | all |
| 37, 38 | picture, y, x | draw modified *(likely mirrored)*, draw clipped *(likely)* | all |
| 40, 41 | item | take an item, drop an item | all |
| 42 | flag, pairs | party remark, once unless the flag is `0xFFFF` | M, P |
| 43 | speech, speaker, text | pop-up line | M, P |
| 44 | – | marks "inside a call" *(likely)* | M, P |
| 45 | n, n words | fill a table of pictures | P |
| 46 | item, slot, target | jump if an item is in a slot | P |
| 47 | item, count, target | jump if the party has exactly that many | P |
| 48, 49 | number | set a numbered flag once and lower, raise a counter | P |
| 50 | character | a built-in character leaves | P |
| 51, 52, 53 | target | jump if the counter is above 4, if any of a block of flags is set, on five flag pairs | P |
| 54 | number, target | jump if a variable has that value | P |
| 55 | n, n words | preload pictures | P |

## Who speaks

A choice or remark has up to four (test, text) pairs, ending with the test 0x21 (S, M) or 0x26 (P). The first pair that some able party member satisfies is used, and that member says the line. In S, the pairs belong to the NPC's line instead and test the current speaker.

| Test | Meaning |
|---|---|
| 0–5 | alignment: lawful good, neutral good, chaotic good, lawful neutral, true neutral, chaotic neutral |
| 6–17 | ability 17 or more, ability below 10; in the order charisma, strength, intelligence, dexterity, constitution, wisdom |
| 18–24 | human, elf, half-elf, dwarf, gnome, halfling, any of elf/dwarf/gnome/halfling |
| 25–30 | fighter, ranger, paladin, mage, cleric, thief |
| 31, 32 | male, female |
| 33 | anyone, and end of the list |

In P, 25–28 are four more races, 29 is unused, and the rest moves up by five, so "anyone" is 38.

Text can contain `[plural|singular]`. The first form is used unless only one member is able.

## Plot flags

256 bits. Story flags come first, and later bits record that a remark has been said: `128 + id` in S and M, `150 + id` in P.

Scripts, the program and (in the format) trigger lists can set flags. Remarks outside conversations, which NPCs are hidden, and similar rules are in the program, not in the data.
