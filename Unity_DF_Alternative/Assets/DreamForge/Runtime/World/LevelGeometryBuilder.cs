// SPDX-License-Identifier: GPL-3.0-only
// Builds untextured geometry from a parsed level's cell grid (docs/formats/levels.md: wall type 0 = open).
using System.Collections.Generic;
using DreamForge.Formats;
using UnityEngine;
using UnityEngine.Rendering;

namespace DreamForge.World
{
    public struct LevelGeometrySettings
    {
        public float CellSize;
        public float WallHeight;
        public bool TintVanishingWalls;
    }

    public sealed class LevelMeshes
    {
        public Mesh Floor, Ceiling, Walls, WallTops;
        public int OpenCells, WallFaces, EdgeFaces, WallCells;
        /// <summary>Wall type -> number of wall cells with it.</summary>
        public SortedDictionary<int, int> WallTypeCells = new SortedDictionary<int, int>();
        public int VanishingCells;

        public void Destroy()
        {
            foreach (var m in new[] { Floor, Ceiling, Walls, WallTops })
                if (m != null) Object.Destroy(m);
        }
    }

    /// <summary>
    /// Grid x -> world +X, grid y -> world -Z: cell (x, y) covers [x*s, (x+1)*s] x [-(y+1)*s, -y*s].
    /// Seen from above with +Z up, the level looks like the ASCII map (y going down).
    /// </summary>
    public static class LevelGeometryBuilder
    {
        // Neighbour offsets in grid space and the world normal of the face that looks into the open cell.
        static readonly (int dx, int dy)[] Sides = { (0, -1), (0, 1), (-1, 0), (1, 0) };

        public static LevelMeshes Build(Level lv, LevelGeometrySettings s)
        {
            int w = lv.Width, h = lv.Height;
            float c = s.CellSize, top = s.WallHeight;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color Col(Color k) => linear ? k.linear : k;

            var floor = new MeshData();
            var ceiling = new MeshData();
            var walls = new MeshData();
            var tops = new MeshData();
            var result = new LevelMeshes();

            Color floorCol = Col(DebugPalette.Floor);
            Color ceilCol = Col(lv.Header.CeilingTile == -1 ? DebugPalette.SkyCeiling : DebugPalette.Ceiling);

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
                    tops.Quad(new Vector3(x0, top, zN), new Vector3(x1, top, zN), new Vector3(x1, top, zS),
                              new Vector3(x0, top, zS), Vector3.up, Col(WallColour(cell, s)));
                    continue;
                }

                result.OpenCells++;
                floor.Quad(new Vector3(x0, 0, zN), new Vector3(x1, 0, zN), new Vector3(x1, 0, zS),
                           new Vector3(x0, 0, zS), Vector3.up, floorCol);
                ceiling.Quad(new Vector3(x0, top, zN), new Vector3(x1, top, zN), new Vector3(x1, top, zS),
                             new Vector3(x0, top, zS), Vector3.down, ceilCol);

                // A wall face on every side that borders a wall cell or the map edge, facing into this cell.
                foreach (var (dx, dy) in Sides)
                {
                    int nx = x + dx, ny = y + dy;
                    bool inside = nx >= 0 && nx < w && ny >= 0 && ny < h;
                    Color k;
                    if (!inside) { k = DebugPalette.MapEdge; result.EdgeFaces++; }
                    else
                    {
                        var nb = lv.At(nx, ny);
                        if (nb.IsOpen) continue;
                        k = WallColour(nb, s);
                    }
                    result.WallFaces++;

                    // Shared edge between this cell and the neighbour, in world space.
                    Vector3 a, b, normal;
                    if (dy == -1) { a = new Vector3(x0, 0, zN); b = new Vector3(x1, 0, zN); normal = Vector3.back; }
                    else if (dy == 1) { a = new Vector3(x0, 0, zS); b = new Vector3(x1, 0, zS); normal = Vector3.forward; }
                    else if (dx == -1) { a = new Vector3(x0, 0, zN); b = new Vector3(x0, 0, zS); normal = Vector3.right; }
                    else { a = new Vector3(x1, 0, zN); b = new Vector3(x1, 0, zS); normal = Vector3.left; }
                    if (dx != 0) k *= DebugPalette.EastWestShade;
                    k.a = 1;
                    walls.Quad(a, b, b + Vector3.up * top, a + Vector3.up * top, normal, Col(k));
                }
            }

            result.Floor = floor.ToMesh($"Level{lv.Index}_Floor");
            result.Ceiling = ceiling.ToMesh($"Level{lv.Index}_Ceiling");
            result.Walls = walls.ToMesh($"Level{lv.Index}_Walls");
            result.WallTops = tops.ToMesh($"Level{lv.Index}_WallTops");
            return result;
        }

        static Color WallColour(Cell cell, LevelGeometrySettings s)
        {
            var k = DebugPalette.WallType(cell.WallType);
            return s.TintVanishingWalls && cell.VanishingWall ? DebugPalette.Vanishing(k) : k;
        }

        sealed class MeshData
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Color> col = new List<Color>();
            readonly List<int> tri = new List<int>();

            /// <summary>Adds a quad whose front side faces <paramref name="normal"/>, whatever the corner order.</summary>
            public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Color k)
            {
                // Unity treats Cross(b - a, c - a) as the front side; flip the corner order if needed.
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) < 0)
                    (p1, p3) = (p3, p1);
                int i = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                for (int k2 = 0; k2 < 4; k2++) { n.Add(normal); col.Add(k); }
                tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
                tri.Add(i); tri.Add(i + 2); tri.Add(i + 3);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetColors(col);
                m.SetTriangles(tri, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
