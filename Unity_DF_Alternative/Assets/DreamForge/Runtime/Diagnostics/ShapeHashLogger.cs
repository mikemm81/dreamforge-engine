// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DreamForge.Data;
using DreamForge.Formats;
using UnityEngine;

namespace DreamForge.Diagnostics
{
    /// <summary>
    /// On Play, decodes every column shape (EH type 6) in the chosen RES files and writes one line per shape
    /// in the same format as tools/res_shapes.py --hash: "entry WxH sha256(RGBA) sha256(mask,index)".
    /// Output goes to the project's Temp folder (git-ignored).
    /// </summary>
    public sealed class ShapeHashLogger : MonoBehaviour
    {
        [Tooltip("Game key in data-path.txt. Only menzo has been tested.")]
        [SerializeField] string game = "menzo";
        [SerializeField] string[] fileNames = { "RES4", "RES3" };
        [SerializeField, Min(0)] int palette = 0;
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
                Debug.LogError($"[SHAPES] {ex.GetType().Name}: {ex.Message}");
            }
        }

        void Run()
        {
            string colorsPath = DataPaths.Resolve(game, "COLORS", localFolderOverride, out string error);
            if (colorsPath == null) { Debug.LogError($"[SHAPES] {error}"); return; }
            var pal = ColorsFile.Open(colorsPath).Get(palette);
            string outDir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp")), "DreamForgeTextures");
            Directory.CreateDirectory(outDir);

            foreach (string name in fileNames)
            {
                string path = DataPaths.Resolve(game, name, localFolderOverride, out error);
                if (path == null) { Debug.LogError($"[SHAPES] {error}"); continue; }
                var res = ResFile.Open(path);
                var lines = new StringBuilder();
                int done = 0, stubs = 0, bad = 0;
                using (var sha = SHA256.Create())
                {
                    foreach (var e in res.Entries)
                    {
                        if (e.Kind != ResEntryKind.Eh || e.Type != 6) continue;
                        try
                        {
                            var r = ResCompression.Decode(res.ReadPayload(e), e.Compression, (int)e.DecodedSize, out byte[] data);
                            if (!r.Ok) throw new InvalidDataException(r.Message);
                            if (ColumnShape.IsStub(data)) { stubs++; continue; }
                            var s = ColumnShape.Decode(data);
                            if (s.DrawnPixels == 0) { stubs++; continue; }
                            lines.Append($"{e.Index} {s.Width}x{s.Height} {Hex(sha.ComputeHash(s.ToRgba(pal)))} {Hex(sha.ComputeHash(s.MaskAndIndex()))}\n");
                            done++;
                        }
                        catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
                        {
                            bad++;
                            lines.Append($"entry {e.Index}: {ex.Message}\n");
                        }
                    }
                }
                string file = Path.Combine(outDir, $"cs_shapes_{name}.txt");
                File.WriteAllText(file, lines.ToString());
                string msg = $"[SHAPES] {name}: {done} shapes, {stubs} stubs skipped, {bad} errors -> {file}";
                if (bad == 0) Debug.Log(msg); else Debug.LogWarning(msg);
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
