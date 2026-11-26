// SPDX-License-Identifier: GPL-3.0-only
// Builds level geometry from a parsed level's cell grid (docs/formats/levels.md: wall type 0 = open),
// either in debug colours or textured from the wall-type table (see Formats/LevelTextures).
using System.Collections.Generic;
using DreamForge.Formats;
using UnityEngine;
using UnityEngine.Rendering;

namespace DreamForge.World
{
    public struct LevelGeometrySettings
    {
        public float CellSize;
        /// <summary>Height of one row of wall texture. 1.5 x CellSize gives square texels (a face is 128 x 192).</summary>
        public float RowHeight;
        public bool TintVanishingWalls;
    }

    /// <summary>A mesh with one submesh per texture key (see LevelTextureCache).</summary>
    public sealed class MeshPart
    {
        public Mesh Mesh;
        public int[] Keys;
    }

    public sealed class LevelMeshes
    {
        public MeshPart Floor, Ceiling, Walls, WallTops;
        public int Rows;
        public float WallHeight;
        public int OpenCells, WallFaces, EdgeFaces, WallCells, PlaceholderQuads;
        /// <summary>Wall type -> number of wall cells with it.</summary>
        public SortedDictionary<int, int> WallTypeCells = new SortedDictionary<int, int>();
        public int VanishingCells;

        public void Destroy()
        {
            foreach (var p in new[] { Floor, Ceiling, Walls, WallTops })
                if (p?.Mesh != null) Object.Destroy(p.Mesh);
        }
    }

    /// <summary>
    /// Grid x -> world +X, grid y -> world -Z: cell (x, y) covers [x*s, (x+1)*s] x [-(y+1)*s, -y*s].
    /// Seen from above with +Z up, the level looks like the ASCII map (y going down).
    /// </summary>
    public static class LevelGeometryBuilder
    {
        // Neighbour offsets in grid space, from the open cell towards the wall.
        static readonly (int dx, int dy)[] Sides = { (0, -1), (0, 1), (-1, 0), (1, 0) };

        /// <param name="textures">null = debug colours; otherwise the walls, floor and ceiling are textured.</param>
        public static LevelMeshes Build(Level lv, LevelGeometrySettings s, LevelTextures textures)
        {
            int w = lv.Width, h = lv.Height;
            float c = s.CellSize;
            int rows = LevelTextures.Rows(lv.Header);
            float top = rows * s.RowHeight;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color Col(Color k) => linear ? k.linear : k;
            bool textured = textures != null;

            var floor = new MeshData();
            var ceiling = new MeshData();
            var walls = new MeshData();
            var tops = new MeshData();
            var result = new LevelMeshes { Rows = rows, WallHeight = top };

            bool sky = lv.Header.CeilingTile == -1;
            int floorKey = textured ? textures.TileEntry(lv.Header.FloorTile) : LevelTextureCache.White;
            int ceilKey = textured && !sky ? textures.TileEntry(lv.Header.CeilingTile) : LevelTextureCache.White;
            Color floorCol = textured ? Color.white : Col(DebugPalette.Floor);
            Color ceilCol = textured ? Color.white : Col(sky ? DebugPalette.SkyCeiling : DebugPalette.Ceiling);
            if (floorKey == LevelTextures.Placeholder) result.PlaceholderQuads++;
            if (ceilKey == LevelTextures.Placeholder) result.PlaceholderQuads++;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float x0 = x * c, x1 = (x + 1) * c, zN = -y * c, zS = -(y + 1) * c;
                var cell = lv.At(x, y);

                if (!cell.IsOpen)
                {
                    result.WallCells++;
                    result.WallTypeCells.TryGetValue(cell.WallType, out int n);
                    result.WallTypeCells[cell.WallType] = n + 1;
                    if (cell.VanishingWall) result.VanishingCells++;
                    tops.Quad(LevelTextureCache.White, Vector3.up, Col(WallColour(cell, s)),
                              V(x0, top, zN), V(x1, top, zN), V(x1, top, zS), V(x0, top, zS));
                    continue;
                }

                result.OpenCells++;
                // Tiles (PROVISIONAL orientation, unconfirmed): laid on the map like an image, top row towards
                // y-1, left column towards x-1. The ceiling uses the same world mapping.
                floor.Quad(floorKey, Vector3.up, floorCol,
                           V(x0, 0, zS), V(x1, 0, zS), V(x1, 0, zN), V(x0, 0, zN));
                if (!(textured && sky))
                    ceiling.Quad(ceilKey, Vector3.down, ceilCol,
                                 V(x0, top, zS), V(x1, top, zS), V(x1, top, zN), V(x0, top, zN));

                // A wall face on every side that borders a wall cell or the map edge, facing into this cell.
                foreach (var (dx, dy) in Sides)
                {
                    int nx = x + dx, ny = y + dy;
                    bool inside = nx >= 0 && nx < w && ny >= 0 && ny < h;
                    Cell nb = inside ? lv.At(nx, ny) : default;
                    if (inside && nb.IsOpen) continue;
                    result.WallFaces++;
                    if (!inside) result.EdgeFaces++;

                    // The shared edge, ordered from the viewer's left to right as seen from this cell.
                    Vector3 normal = new Vector3(-dx, 0, dy);                 // points back into this cell
                    Vector3 forward = -normal;
                    Vector3 right = Vector3.Cross(Vector3.up, forward);
                    Vector3 mid = new Vector3((x0 + x1) / 2f + dx * c / 2f, 0, (zN + zS) / 2f - dy * c / 2f);
                    Vector3 a = mid - right * (c / 2f), b = mid + right * (c / 2f);

                    if (textured && inside)
                    {
                        // The wall cell's face that looks back at us; split into halves (left, right) and rows.
                        int face = LevelTextures.FaceTowards(-dx, -dy);
                        for (int row = 0; row < rows; row++)
                        for (int half = 0; half < 2; half++)
                        {
                            int key = textures.WallEntry(nb.WallType, face, row, half);
                            if (key == LevelTextures.Placeholder) result.PlaceholderQuads++;
                            Vector3 l = Vector3.Lerp(a, b, half / 2f), r = Vector3.Lerp(a, b, (half + 1) / 2f);
                            Vector3 lo = Vector3.up * (row * s.RowHeight), hi = Vector3.up * ((row + 1) * s.RowHeight);
                            walls.Quad(key, normal, Color.white, l + lo, r + lo, r + hi, l + hi);
                        }
                        continue;
                    }

                    Color k = inside ? WallColour(nb, s) : DebugPalette.MapEdge;
                    if (dx != 0) k *= DebugPalette.EastWestShade;
                    k.a = 1;
                    walls.Quad(LevelTextureCache.White, normal, Col(k), a, b, b + Vector3.up * top, a + Vector3.up * top);
                }
            }

            result.Floor = floor.ToPart($"Level{lv.Index}_Floor");
            result.Ceiling = ceiling.ToPart($"Level{lv.Index}_Ceiling");
            result.Walls = walls.ToPart($"Level{lv.Index}_Walls");
            result.WallTops = tops.ToPart($"Level{lv.Index}_WallTops");
            return result;
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static Color WallColour(Cell cell, LevelGeometrySettings s)
        {
            var k = DebugPalette.WallType(cell.WallType);
            return s.TintVanishingWalls && cell.VanishingWall ? DebugPalette.Vanishing(k) : k;
        }

        sealed class MeshData
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Color> col = new List<Color>();
            readonly SortedDictionary<int, List<int>> tris = new SortedDictionary<int, List<int>>();

            static readonly Vector2[] Corners = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };

            /// <summary>
            /// Adds a quad with corners bottom-left, bottom-right, top-right, top-left of its texture
            /// (UV (0,0), (1,0), (1,1), (0,1)), whose front side faces <paramref name="normal"/>.
            /// </summary>
            public void Quad(int key, Vector3 normal, Color k, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                int i = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                for (int j = 0; j < 4; j++) { n.Add(normal); col.Add(k); uv.Add(Corners[j]); }
                if (!tris.TryGetValue(key, out var t)) tris[key] = t = new List<int>();
                // Unity treats Cross(b - a, c - a) as the front side; pick the winding that faces the normal.
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) >= 0)
                { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
                else
                { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
            }

            public MeshPart ToPart(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetColors(col);
                var keys = new int[tris.Count];
                m.subMeshCount = tris.Count;
                int s = 0;
                foreach (var kv in tris) { keys[s] = kv.Key; m.SetTriangles(kv.Value, s++); }
                m.RecalculateBounds();
                return new MeshPart { Mesh = m, Keys = keys };
            }
        }
    }
}
