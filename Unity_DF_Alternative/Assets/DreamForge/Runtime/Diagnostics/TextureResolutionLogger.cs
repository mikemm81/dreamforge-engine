// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DreamForge.Data;
using DreamForge.Formats;
using UnityEngine;

namespace DreamForge.Diagnostics
{
    /// <summary>
    /// On Play, resolves every face of every wall type used by every real level (wall set from INIT) and the
    /// floor/ceiling tiles, and writes one line per level in the same format as
    /// tools/level_dump.py GAMEDIR textures, for diffing. Output goes to the project's Temp folder (git-ignored).
    /// </summary>
    public sealed class TextureResolutionLogger : MonoBehaviour
    {
        [Tooltip("Game key in data-path.txt. Only menzo has been tested.")]
        [SerializeField] string game = "menzo";
        [Tooltip("Optional local folder that replaces data-path.txt. Leave empty in anything you commit.")]
        [SerializeField] string localFolderOverride = "";

        void Start()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TEXRES] {ex.GetType().Name}: {ex.Message}");
            }
        }

        string Need(string file)
        {
            string p = DataPaths.Resolve(game, file, localFolderOverride, out string error);
            if (p == null) throw new FileNotFoundException(error);
            return p;
        }

        void Run()
        {
            var levels = LevelsFile.Open(Need("LEVELS.DAT"));
            var res = ResFile.Open(Need("RES4"));
            var init = GameInit.Open(Need("INIT"));
            foreach (var w in init.Warnings) Debug.LogWarning($"[TEXRES] INIT: {w}");

            var report = new StringBuilder();
            var problems = new StringBuilder();
            int real = 0, skipped = 0, halves = 0, walls = 0, shapes = 0, missing = 0, badTiles = 0;
            var placeholder = levels.Placeholders();
            for (int i = 0; i < levels.Count; i++)
            {
                if (placeholder[i]) { skipped++; continue; }
                var lv = levels.ReadLevel(i, LevelLayout.ForGame(game));
                real++;
                var h = lv.Header;
                var tex = new LevelTextures(res, h, init);
                int rows = LevelTextures.Rows(h);
                var used = Enumerable.Range(0, lv.Width * lv.Height).Select(k => lv.At(k % lv.Width, k / lv.Width))
                                     .Where(c => !c.IsOpen).Select(c => c.WallType).Distinct().OrderBy(t => t).ToList();
                int lw = 0, ls = 0, lm = 0;
                var list = new StringBuilder();
                foreach (int t in used)
                for (int f = 0; f < 4; f++)
                for (int r = 0; r < rows; r++)
                for (int half = 0; half < 2; half++)
                {
                    var ft = tex.Resolve(t, f, r, half);
                    char k = ft.Kind == FaceTextureKind.Wall ? 'W' : ft.Kind == FaceTextureKind.Shape ? 'S' : 'M';
                    if (k == 'W') lw++; else if (k == 'S') ls++; else lm++;
                    list.Append($"{t} {f} {r} {half} {ft.Number} {ft.Entry} {k}\n");
                    if (k == 'M') problems.Append($"\n  level {i} type {t} face {f} row {r} half {half}: {ft.Problem}");
                }
                string Tile(int n)
                {
                    if (n == -1) return "none";   // ceiling -1 = sky (levels.md); level 55 also has floor -1
                    int e = tex.TileEntry(n);
                    if (e < 0) { badTiles++; problems.Append($"\n  level {i}: tile {n} unresolved"); return "?"; }
                    return e.ToString();
                }
                string hash;
                using (var sha = SHA256.Create())
                    hash = string.Concat(sha.ComputeHash(Encoding.ASCII.GetBytes(list.ToString())).Take(8).Select(b => b.ToString("x2")));
                report.Append($"{i,2} set {h.WallSet,2} [{tex.WallSetStart},{tex.WallSetEnd}) rows {rows} types {used.Count,2}: " +
                              $"halves {lw + ls + lm,4} walls {lw,4} shapes {ls,4} missing {lm} | floor {h.FloorTile}->{Tile(h.FloorTile)} " +
                              $"ceiling {h.CeilingTile}->{Tile(h.CeilingTile)} | {hash}\n");
                halves += lw + ls + lm; walls += lw; shapes += ls; missing += lm;
            }

            string outDir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp")), "DreamForgeTextures");
            Directory.CreateDirectory(outDir);
            string file = Path.Combine(outDir, "cs_resolution.txt");
            File.WriteAllText(file, report.ToString());

            string summary = $"[TEXRES] {real} levels ({skipped} identical placeholder copies skipped); INIT tile start {init.TileStart}, " +
                             $"wall-set table [{string.Join(", ", init.WallSetTable)}]\n" +
                             $"  face halves {halves}: walls {walls}, shapes {shapes} (masked walls, drawn later), missing {missing}; unresolved tiles {badTiles}\n" +
                             $"  per-level lines: {file}\n{report}";
            if (missing == 0 && badTiles == 0) Debug.Log(summary);
            else Debug.LogWarning(summary + "problems:" + problems);
        }
    }
}
