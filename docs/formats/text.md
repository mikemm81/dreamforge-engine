# Text (`T`)

`u32 count`, `count` × `u32` offsets, then for each entry `u16 length` and `length` bytes. XOR each byte with `0xDC`.

| Game | Entries |
|---|---|
| S | 1,073 |
| M | 1,185 |
| P | 1,655 |

In M, 1,183 entries decode to plain printable text; the other two weren't examined. Other files and scripts refer to texts by entry number, often relative to a base given in `INIT` or set by a [script](scripts.md).
