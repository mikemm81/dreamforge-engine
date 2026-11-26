# DreamForge engine

A planned open-source reimplementation of the engine behind three DreamForge role-playing games, and documentation of their file formats:

- *Menzoberranzan* (1994)
- *Ravenloft: Strahd's Possession* (1994)
- *Ravenloft: Stone Prophet* (1995)

**You need your own copy of a game.** This repository has no game files, artwork, sound, text, level data or original program code, and it never will. The engine reads the files from your own installation when it runs: see [docs/data-setup.md](docs/data-setup.md).

## Status

| Part | State |
|---|---|
| [docs/](docs/) | Findings and format specifications. Checked against the shipped data; nothing has been checked in the running games yet |
| [tools/](tools/) | Python programs that read your own game files |
| [Unity_DF_Alternative/](Unity_DF_Alternative/) | Unity project. The engine goes here |

## Clean-room notice

- The documentation describes file formats and observable behaviour in original words. It comes from static analysis of the released programs and data.
- The tools and the engine are original code. No disassembly or decompiler output is copied or translated into this repository.
- Implement from the documents in `docs/`, not from the original programs. **If you have decompiler output of these games, don't paste, port or paraphrase it into a contribution.**
- This is a set of working rules. It isn't a claim of a formal two-team clean-room process: the person who analysed the formats also wrote the tools.

## What never goes in

Game files, anything extracted or converted from them (pictures, sound, text, levels, scripts), decompiler output and analysis databases, saved games, and story spoilers. [.gitignore](.gitignore) blocks the usual cases, but `git add -f` gets past it, so look at what you stage.

On Windows and macOS those rules ignore case. Don't name source folders `Game`, `Story`, `Saves` or `Decomp`: they would be left out without a warning.

## Licence and credits

[GPL-3.0](LICENSE), for the code and documents in this repository. Third-party credits are in [NOTICE](NOTICE).

This is an unofficial project, not affiliated with or endorsed by the games' developers, publishers or rights holders. Product names and trademarks belong to their owners and are used only to say which games the project works with.
