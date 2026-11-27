// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using DreamForge.Data;
using DreamForge.Formats;
using DreamForge.World;
using UnityEngine;

namespace DreamForge.Diagnostics
{
    /// <summary>
    /// After the LevelLoader has built a textured level, renders named wall faces square-on with an
    /// orthographic camera at one pixel per texel and compares them pixel for pixel with the decoded
    /// textures (decoded again here, independently of the textures the level uploaded). Also checks one
    /// floor cell against its tile in the provisional orientation. Writes the renders to the project's
    /// Temp folder (git-ignored), never under Assets.
    /// </summary>
    public sealed class FacePixelCheck : MonoBehaviour
    {
        [Serializable]
        public struct Face
        {
            [Tooltip("Level the face belongs to; only faces of the loaded level are checked.")]
            public int level;
            public string name;
            [Tooltip("The wall cell.")] public Vector2Int wall;
            [Tooltip("The open cell it is seen from (a neighbour).")] public Vector2Int from;
            [Tooltip("Expected texture numbers, lower row: viewer's left, then right.")]
            public int expectLeft, expectRight;
            [Tooltip("Expected texture numbers, upper row (two-row levels); -1 = not checked.")]
            public int expectUpperLeft, expectUpperRight;
        }

        [Serializable]
        public struct Floor
        {
            public int level;
            [Tooltip("Open cell whose floor is compared with the floor tile.")] public Vector2Int cell;
        }

        [SerializeField] LevelLoader loader;
        [SerializeField] string game = "menzo";
        [Tooltip("Optional local folder that replaces data-path.txt. Leave empty in anything you commit.")]
        [SerializeField] string localFolderOverride = "";
        [SerializeField] Face[] faces =
        {
            new Face { level = 1, name = "door, from inside",       wall = new Vector2Int(6, 6), from = new Vector2Int(6, 5), expectLeft = 10, expectRight = 11, expectUpperLeft = -1, expectUpperRight = -1 },
            new Face { level = 1, name = "door, from outside",      wall = new Vector2Int(6, 6), from = new Vector2Int(6, 7), expectLeft = 10, expectRight = 11, expectUpperLeft = -1, expectUpperRight = -1 },
            new Face { level = 1, name = "fireplace",               wall = new Vector2Int(1, 4), from = new Vector2Int(2, 4), expectLeft = 32, expectRight = 33, expectUpperLeft = -1, expectUpperRight = -1 },
            new Face { level = 1, name = "shelves left",            wall = new Vector2Int(4, 0), from = new Vector2Int(4, 1), expectLeft = 30, expectRight = 31, expectUpperLeft = -1, expectUpperRight = -1 },
            new Face { level = 1, name = "shelves right",           wall = new Vector2Int(5, 0), from = new Vector2Int(5, 1), expectLeft = 30, expectRight = 31, expectUpperLeft = -1, expectUpperRight = -1 },
            new Face { level = 1, name = "cell (5,6) from inside",  wall = new Vector2Int(5, 6), from = new Vector2Int(5, 5), expectLeft = 19, expectRight = 15, expectUpperLeft = -1, expectUpperRight = -1 },
            new Face { level = 1, name = "cell (5,6) from outside", wall = new Vector2Int(5, 6), from = new Vector2Int(5, 7), expectLeft = 16, expectRight = 19, expectUpperLeft = -1, expectUpperRight = -1 },
            new Face { level = 0, name = "two-row walls (1,0)",     wall = new Vector2Int(1, 0), from = new Vector2Int(1, 1), expectLeft = 74, expectRight = 75, expectUpperLeft = 73, expectUpperRight = 76 },
            new Face { level = 0, name = "masked two-row (2,2)",    wall = new Vector2Int(2, 2), from = new Vector2Int(1, 2), expectLeft = 55, expectRight = 54, expectUpperLeft = 61, expectUpperRight = 60 },
            new Face { level = 0, name = "heavily masked (65,62)",  wall = new Vector2Int(65, 62), from = new Vector2Int(64, 62), expectLeft = 63, expectRight = 64, expectUpperLeft = 60, expectUpperRight = 61 },
        };
        [SerializeField] Floor[] floors =
        {
            new Floor { level = 1, cell = new Vector2Int(6, 3) },
            new Floor { level = 0, cell = new Vector2Int(1, 1) },
        };

        const int FaceWidth = 128, RowTexels = WallTexture.WallHeight, TileTexels = WallTexture.TileSide;

        /// <summary>
        /// Clear colour for face checks. The camera's far plane sits just behind the face, so a cut-out
        /// (transparent) pixel of a masked wall must come out exactly this colour.
        /// </summary>
        static readonly byte[] Clear = { 255, 0, 255, 255 };

        IEnumerator Start()
        {
            if (loader == null) loader = GetComponent<LevelLoader>();
            yield return null;   // the loader builds in Awake; one frame for everything to settle
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CHECK] {ex.GetType().Name}: {ex.Message}");
            }
        }

        void Run()
        {
            if (loader == null || loader.ParsedLevel == null) { Debug.LogWarning("[CHECK] no loaded level"); return; }
            var lv = loader.ParsedLevel;
            var levelFaces = faces.Where(f => f.level == lv.Index).ToArray();
            var levelFloors = floors.Where(f => f.level == lv.Index).ToArray();
            if (levelFaces.Length + levelFloors.Length == 0) { Debug.Log($"[CHECK] skipped: no named faces for level {lv.Index}"); return; }
            if (!loader.IsTextured) { Debug.LogWarning("[CHECK] skipped: the level is not textured"); return; }
            float cell = loader.CellSize, row = loader.RowHeight;
            if (!Mathf.Approximately(row, cell * RowTexels / FaceWidth))
            {
                Debug.LogWarning($"[CHECK] skipped: needs square texels (row height = 1.5 x cell); have cell {cell}, row {row}");
                return;
            }

            string resPath = DataPaths.Resolve(game, "RES4", localFolderOverride, out string error);
            string colorsPath = resPath == null ? null : DataPaths.Resolve(game, "COLORS", localFolderOverride, out error);
            if (colorsPath == null) { Debug.LogError($"[CHECK] {error}"); return; }
            var res = ResFile.Open(resPath);
            var pal = ColorsFile.Open(colorsPath).Get(0);
            string outDir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp")), "DreamForgeShots", "facecheck");
            Directory.CreateDirectory(outDir);

            int rows = LevelTextures.Rows(lv.Header), pass = 0, fail = 0;
            var sb = new StringBuilder($"[CHECK] level {lv.Index}: named wall faces rendered at 1 pixel per texel vs decoded textures\n");
            foreach (var f in levelFaces)
            {
                int dx = f.wall.x - f.from.x, dy = f.wall.y - f.from.y;
                if (Math.Abs(dx) + Math.Abs(dy) != 1 || !lv.At(f.from.x, f.from.y).IsOpen || lv.At(f.wall.x, f.wall.y).IsOpen)
                {
                    sb.Append($"  FAIL {f.name}: ({f.from.x},{f.from.y}) -> ({f.wall.x},{f.wall.y}) is not open -> wall neighbours\n");
                    fail++;
                    continue;
                }
                int type = lv.At(f.wall.x, f.wall.y).WallType;
                int face = LevelTextures.FaceTowards(-dx, -dy);
                var wf = lv.Header.WallTypes[type].Faces[face];
                int nl = LevelTextures.Number(wf, 0, 0), nr = LevelTextures.Number(wf, 0, 1);
                int ul = LevelTextures.Number(wf, 1, 0), ur = LevelTextures.Number(wf, 1, 1);

                // Expected: the face's textures side by side, upper row above lower, top row first.
                // Walls are decoded with WallTexture, masked walls with ColumnShape (both checked against
                // the Python tools); cut-out pixels are expected to show the clear colour.
                int w = FaceWidth, h = RowTexels * rows;
                var expected = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++) Buffer.BlockCopy(Clear, 0, expected, i * 4, 4);
                bool resolved = true;
                int maskedHalves = 0, cutOut = 0;
                for (int r = 0; r < rows; r++)
                for (int half = 0; half < 2; half++)
                {
                    int entry = loader.Textures.WallEntry(type, face, r, half);
                    if (entry < 0) { resolved = false; continue; }
                    byte[] rgba = HalfImage(res, entry, pal, out bool masked);
                    if (masked) maskedHalves++;
                    int y0 = (rows - 1 - r) * RowTexels;
                    for (int y = 0; y < RowTexels; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        int s = (y * 64 + x) * 4;
                        if (rgba[s + 3] == 0) { cutOut++; continue; }
                        Buffer.BlockCopy(rgba, s, expected, ((y0 + y) * w + half * 64 + x) * 4, 4);
                    }
                }

                Vector3 forward = LevelGeometryBuilder.GridDir(dx, dy);
                Vector3 centre = LevelGeometryBuilder.GridToWorld(f.from.x + 0.5f + dx * 0.5f, f.from.y + 0.5f + dy * 0.5f,
                                                                  cell, rows * row / 2f);
                // Far plane just behind the face: whatever shows through a cut-out pixel is the clear colour.
                float dist = cell * 0.4f;
                byte[] got = Render(centre - forward * dist, Quaternion.LookRotation(forward, Vector3.up),
                                    w, h, row * rows / 2f, dist + 0.02f);
                int diff = Compare(expected, got, out int maxDelta, out int firstAt);
                bool upperChecked = rows == 2 && f.expectUpperLeft >= 0;
                bool numbersOk = nl == f.expectLeft && nr == f.expectRight &&
                                 (!upperChecked || (ul == f.expectUpperLeft && ur == f.expectUpperRight));
                bool ok = numbersOk && resolved && diff == 0;
                if (ok) pass++; else fail++;
                string file = $"{Slug(f.name)}.png";
                File.WriteAllBytes(Path.Combine(outDir, file), SideBySide(got, expected, w, h));
                string numbers = $"lower {nl}|{nr} (expected {f.expectLeft}|{f.expectRight})" +
                                 (upperChecked ? $", upper {ul}|{ur} (expected {f.expectUpperLeft}|{f.expectUpperRight})" : "");
                string mask = maskedHalves > 0 ? $", {maskedHalves} masked half(s) with {cutOut} cut-out pixels" : "";
                sb.Append($"  {(ok ? "PASS" : "FAIL")} {f.name}: wall ({f.wall.x},{f.wall.y}) type {type} face {face}, " +
                          $"{numbers}, {w}x{h} px{mask}, " +
                          (diff == 0 ? "pixel-identical" : $"{diff} pixels differ (max channel delta {maxDelta}, first at x={firstAt % w} y={firstAt / w})") +
                          $"  [{file}: render | expected]\n");
            }

            // Floor: straight down with +Z (towards y+1) at the top of the image and +X on the right. The tile's
            // top row lies towards y-1, so from here it appears flipped vertically.
            foreach (var fl in levelFloors)
            {
                var floorCell = fl.cell;
                int entry = loader.Textures.TileEntry(lv.Header.FloorTile);
                byte[] tile = WallTexture.Decode(res, res.Entries[entry]).Image.ToRgba(pal);
                var expected = new byte[tile.Length];
                for (int y = 0; y < TileTexels; y++)
                    Buffer.BlockCopy(tile, y * TileTexels * 4, expected, (TileTexels - 1 - y) * TileTexels * 4, TileTexels * 4);
                var c = LevelGeometryBuilder.GridToWorld(floorCell.x + 0.5f, floorCell.y + 0.5f, cell, row * 0.5f);
                byte[] got = Render(c, Quaternion.Euler(90f, 0f, 0f), TileTexels, TileTexels, cell / 2f, row);
                int diff = Compare(expected, got, out int maxDelta, out int firstAt);
                if (diff == 0) pass++; else fail++;
                File.WriteAllBytes(Path.Combine(outDir, $"floor_level{lv.Index}.png"), SideBySide(got, expected, TileTexels, TileTexels));
                sb.Append($"  {(diff == 0 ? "PASS" : "FAIL")} floor cell ({floorCell.x},{floorCell.y}): tile {lv.Header.FloorTile} (RES4 {entry}), " +
                          (diff == 0 ? "pixel-identical" : $"{diff} pixels differ (max delta {maxDelta})") +
                          $" to the tile laid top row towards y-1 (axes match DOSBox; mirror/180 degrees unknown; checks our UVs)  [floor_level{lv.Index}.png]\n");
            }

            sb.Append($"  {pass} passed, {fail} failed; images in {outDir}");
            if (fail == 0) Debug.Log(sb.ToString()); else Debug.LogWarning(sb.ToString());
        }

        /// <summary>
        /// One 64 x 192 half-row image (RGBA, top row first), decoded here independently of the level's
        /// texture cache: a wall texture, or a column shape from its canvas's top-left (alpha 0 = cut out).
        /// </summary>
        static byte[] HalfImage(ResFile res, int entry, Palette pal, out bool masked)
        {
            var e = res.Entries[entry];
            masked = e.Kind == ResEntryKind.Eh && e.Type == 6;
            if (!masked) return WallTexture.Decode(res, e).Image.ToRgba(pal);
            var r = ResCompression.Decode(res.ReadPayload(e), e.Compression, (int)e.DecodedSize, out byte[] data);
            if (!r.Ok) throw new InvalidDataException($"entry {entry}: {r.Message}");
            var o = new byte[64 * RowTexels * 4];
            if (ColumnShape.IsStub(data)) return o;
            var s = ColumnShape.Decode(data);
            byte[] src = s.ToRgba(pal);
            for (int y = 0; y < Math.Min(RowTexels, s.Height); y++)
                Buffer.BlockCopy(src, y * s.Width * 4, o, y * 64 * 4, Math.Min(64, s.Width) * 4);
            return o;
        }

        /// <summary>Renders the level with an orthographic camera; returns RGBA, top row first.</summary>
        static byte[] Render(Vector3 pos, Quaternion rot, int w, int h, float orthoSize, float far)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var go = new GameObject("FacePixelCheck camera") { hideFlags = HideFlags.DontSave };
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            var prev = RenderTexture.active;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = orthoSize;
                cam.aspect = w / (float)h;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = far;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color32(Clear[0], Clear[1], Clear[2], Clear[3]);
                cam.transform.SetPositionAndRotation(pos, rot);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                tex.Apply(false);
                byte[] bottomUp = tex.GetRawTextureData();
                var topDown = new byte[bottomUp.Length];
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(bottomUp, y * w * 4, topDown, (h - 1 - y) * w * 4, w * 4);
                return topDown;
            }
            finally
            {
                RenderTexture.active = prev;
                rt.Release();
                Destroy(rt);
                Destroy(tex);
                Destroy(go);
            }
        }

        static int Compare(byte[] a, byte[] b, out int maxDelta, out int first)
        {
            int n = 0;
            maxDelta = 0;
            first = -1;
            for (int i = 0; i < a.Length / 4; i++)
            {
                int d = 0;
                for (int k = 0; k < 3; k++) d = Math.Max(d, Math.Abs(a[i * 4 + k] - b[i * 4 + k]));
                if (d == 0) continue;
                if (n++ == 0) first = i;
                maxDelta = Math.Max(maxDelta, d);
            }
            return n;
        }

        /// <summary>Render and expected next to each other, with a 4-pixel gap, as PNG.</summary>
        static byte[] SideBySide(byte[] left, byte[] right, int w, int h)
        {
            int W = w * 2 + 4;
            var o = new byte[W * h * 4];
            for (int y = 0; y < h; y++)
            {
                Buffer.BlockCopy(left, y * w * 4, o, y * W * 4, w * 4);
                Buffer.BlockCopy(right, y * w * 4, o, (y * W + w + 4) * 4, w * 4);
            }
            return TextureDumpLogger.EncodePng(W, h, o);
        }

        static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.ToString().Trim('_');
        }
    }
}
