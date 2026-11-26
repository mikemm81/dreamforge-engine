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
    /// On Play, decodes every wall and floor/ceiling texture (EH type 5) in a RES file with one COLORS
    /// palette. Writes a hash list (same format as tools/res_textures.py --hash) and PNGs of chosen
    /// entries to the project's Temp folder, which is git-ignored. Nothing is written under Assets.
    /// </summary>
    public sealed class TextureDumpLogger : MonoBehaviour
    {
        [Tooltip("Game key in data-path.txt. Only menzo has been tested.")]
        [SerializeField] string game = "menzo";
        [SerializeField] string fileName = "RES4";
        [Tooltip("COLORS palette number. M: 0 for 3D art (docs/formats/images.md).")]
        [SerializeField, Min(0)] int palette = 0;
        [Tooltip("Entries to also write as PNG.")]
        [SerializeField] int[] pngEntries = { 0, 517, 975, 976, 1060 };
        [Tooltip("Optional local folder that replaces data-path.txt. Leave empty in anything you commit.")]
        [SerializeField] string localFolderOverride = "";

        const string OutFolderName = "DreamForgeTextures";

        void Start()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TEX] {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>The project's Temp folder in the Editor (git-ignored); the temporary cache in a build.</summary>
        static string OutputFolder()
        {
            string root = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp"))
                : Application.temporaryCachePath;
            return Path.Combine(root, OutFolderName);
        }

        void Run()
        {
            string resPath = DataPaths.Resolve(game, fileName, localFolderOverride, out string error);
            if (resPath == null) { Debug.LogError($"[TEX] {error}"); return; }
            string colorsPath = DataPaths.Resolve(game, "COLORS", localFolderOverride, out error);
            if (colorsPath == null) { Debug.LogError($"[TEX] {error}"); return; }

            var colors = ColorsFile.Open(colorsPath);
            foreach (var w in colors.Warnings) Debug.LogWarning($"[TEX] COLORS: {w}");
            var pal = colors.Get(palette);
            var res = ResFile.Open(resPath);

            string outDir = OutputFolder();
            Directory.CreateDirectory(outDir);

            var hashes = new StringBuilder();
            int walls = 0, tiles = 0, failed = 0;
            var pngLines = new StringBuilder();
            using (var sha = SHA256.Create())
            {
                foreach (var e in res.Entries.Where(WallTexture.IsTexture))
                {
                    WallTexture tex;
                    try
                    {
                        tex = WallTexture.Decode(res, e);
                    }
                    catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
                    {
                        failed++;
                        hashes.Append($"entry {e.Index}: {ex.Message}\n");
                        Debug.LogWarning($"[TEX] entry {e.Index}: {ex.Message}");
                        continue;
                    }
                    if (tex.Kind == WallTextureKind.Wall) walls++; else tiles++;

                    var img = tex.Image;
                    byte[] rgba = img.ToRgba(pal);
                    string kind = tex.Kind == WallTextureKind.Wall ? "wall" : "flat";   // names as in res_textures.py
                    hashes.Append($"{e.Index} {kind} {img.Width}x{img.Height} {Hex(sha.ComputeHash(img.Pixels))} {Hex(sha.ComputeHash(rgba))}\n");

                    if (pngEntries != null && Array.IndexOf(pngEntries, e.Index) >= 0)
                    {
                        string png = Path.Combine(outDir, $"{e.Index:D4}_{kind}_{img.Width}x{img.Height}.png");
                        File.WriteAllBytes(png, EncodePng(img.Width, img.Height, rgba));
                        pngLines.Append($"\n  {Path.GetFileName(png)}  (stated {e.UnpackedSize}, decoded {e.DecodedSize}, comp {e.Compression})");
                    }
                }
            }

            string hashFile = Path.Combine(outDir, "cs_hashes.txt");
            File.WriteAllText(hashFile, hashes.ToString());

            var missing = (pngEntries ?? new int[0]).Where(i => i < 0 || i >= res.Entries.Count || !WallTexture.IsTexture(res.Entries[i])).ToArray();
            string msg = $"[TEX] {fileName}: {walls} walls, {tiles} tiles, {failed} failed; COLORS {colors.Count} palettes, using palette {palette}\n" +
                         $"  hashes: {hashFile}\n  PNGs in {outDir}:{pngLines}";
            if (missing.Length > 0) msg += $"\n  not type-5 textures, skipped: {string.Join(", ", missing)}";
            if (failed == 0) Debug.Log(msg); else Debug.LogWarning(msg);
        }

        /// <summary>PNG via a temporary Texture2D. Our images are top row first; Unity textures start at the bottom row.</summary>
        static byte[] EncodePng(int w, int h, byte[] rgbaTopDown)
        {
            var bottomUp = new byte[rgbaTopDown.Length];
            int row = w * 4;
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(rgbaTopDown, y * row, bottomUp, (h - 1 - y) * row, row);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixelData(bottomUp, 0);
                tex.Apply(false);
                return tex.EncodeToPNG();
            }
            finally
            {
                Destroy(tex);
            }
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }
}
