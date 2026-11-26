// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using System.Text;
using DreamForge.Data;
using DreamForge.Formats;
using DreamForge.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace DreamForge.World
{
    /// <summary>
    /// On Play, parses one level from the user's LEVELS.DAT, builds its geometry with collision (textured
    /// from RES4 and COLORS, or in debug colours), and spawns a first-person player. Tab switches to a
    /// top-down view of the whole map. Everything is generated in memory at run time; nothing derived
    /// from game data is saved in the project.
    /// </summary>
    public sealed class LevelLoader : MonoBehaviour
    {
        /// <summary>COLORS palette for 3D art in M (docs/formats/images.md).</summary>
        const int Palette3D = 0;

        [Header("Data")]
        [Tooltip("Game key in data-path.txt. Only menzo has been tested.")]
        [SerializeField] string game = "menzo";
        [Tooltip("Level number in LEVELS.DAT.")]
        [SerializeField, Min(0), FormerlySerializedAs("level")] int levelNumber = 1;
        [Tooltip("Optional local folder that replaces data-path.txt. Leave empty in anything you commit.")]
        [SerializeField] string localFolderOverride = "";

        [Header("Scale (live: changes rebuild the level)")]
        [SerializeField, Min(0.25f)] float cellSize = 2f;
        [Tooltip("Height of one row of wall texture. 1.5 x cell size gives square texels (a face is 128 x 192 texels).")]
        [SerializeField, Min(0.25f)] float rowHeight = 3f;

        [Header("Look (live)")]
        [Tooltip("Real wall, floor and ceiling textures; off = the debug colours by wall type.")]
        [SerializeField] bool textured = true;
        [Tooltip("Base material; its shader must be DreamForge/LevelUnlit (texture x vertex colour).")]
        [SerializeField] Material material;
        [SerializeField] bool tintVanishingWalls = true;

        [Header("Player")]
        [Tooltip("Cell to start in; (-1, -1) = the open cell nearest the middle with the most open neighbours.")]
        [SerializeField] Vector2Int spawnCell = new Vector2Int(-1, -1);

        // Parsed at run time from the user's game data; never serialized.
        [NonSerialized] Level parsedLevel;
        [NonSerialized] LevelTextures levelTextures;
        [NonSerialized] LevelTextureCache textureCache;

        LevelMeshes meshes;
        GameObject root;
        FirstPersonController player;
        Camera topDown;
        float builtCell, builtRow;
        bool builtTint, builtTextured;

        /// <summary>For diagnostics (FacePixelCheck): the level and how it was built.</summary>
        public Level ParsedLevel => parsedLevel;
        public LevelTextures Textures => levelTextures;
        public LevelTextureCache TextureCache => textureCache;
        public LevelMeshes Meshes => meshes;
        public float CellSize => builtCell;
        public float RowHeight => builtRow;
        public bool IsTextured => builtTextured && levelTextures != null;

        // Everything is set up in Awake, before the first frame is rendered, so the scene (which holds no
        // camera of its own) has the player's camera from frame one on an ordinary Play.
        void Awake()
        {
            try
            {
                string path = DataPaths.Resolve(game, "LEVELS.DAT", localFolderOverride, out string error);
                if (path == null) { Fail(error); return; }
                parsedLevel = LevelsFile.Open(path).ReadLevel(levelNumber, LevelLayout.ForGame(game));
                foreach (var w in parsedLevel.Warnings) Debug.LogWarning($"[WALK] level {levelNumber}: {w}");
            }
            catch (Exception ex)
            {
                Fail($"Failed to read level {levelNumber}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (material == null)
            {
                var shader = Shader.Find("DreamForge/LevelUnlit");
                if (shader == null) { Fail("No material set and shader DreamForge/LevelUnlit not found."); return; }
                material = new Material(shader) { hideFlags = HideFlags.DontSave };
            }

            LoadTextures();
            Build();
            var spawn = spawnCell.x >= 0 ? spawnCell : PickSpawn(parsedLevel);
            if (!InMap(spawn) || !parsedLevel.At(spawn.x, spawn.y).IsOpen)
            {
                Debug.LogWarning($"[WALK] spawn cell {spawn} is not an open cell; using the automatic choice.");
                spawn = PickSpawn(parsedLevel);
            }
            CreatePlayer(spawn);
            LogSummary(spawn);
        }

        /// <summary>RES4 + COLORS. If either is missing the level still loads, in debug colours.</summary>
        void LoadTextures()
        {
            ResFile res4 = null;
            Palette palette = null;
            try
            {
                string resPath = DataPaths.Resolve(game, "RES4", localFolderOverride, out string e1);
                string colorsPath = DataPaths.Resolve(game, "COLORS", localFolderOverride, out string e2);
                if (resPath == null || colorsPath == null)
                    Debug.LogWarning($"[WALK] no textures, using debug colours: {e1 ?? e2}");
                else
                {
                    res4 = ResFile.Open(resPath);
                    var colors = ColorsFile.Open(colorsPath);
                    foreach (var w in colors.Warnings) Debug.LogWarning($"[WALK] COLORS: {w}");
                    palette = colors.Get(Palette3D);
                    levelTextures = new LevelTextures(res4, parsedLevel.Header);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WALK] no textures, using debug colours: {ex.GetType().Name}: {ex.Message}");
                res4 = null;
                palette = null;
                levelTextures = null;
            }
            textureCache = new LevelTextureCache(res4, palette, material);
        }

        void Update()
        {
            if (parsedLevel == null || root == null) return;

            if (!Mathf.Approximately(cellSize, builtCell) || !Mathf.Approximately(rowHeight, builtRow) ||
                tintVanishingWalls != builtTint || textured != builtTextured)
                Rebuild();

            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame)
                SetTopDown(!topDown.enabled);
        }

        void OnDestroy()
        {
            meshes?.Destroy();
            textureCache?.Dispose();
        }

        // ---- geometry ----

        void Build()
        {
            meshes?.Destroy();
            if (root != null) Destroy(root);

            var settings = new LevelGeometrySettings
                { CellSize = cellSize, RowHeight = rowHeight, TintVanishingWalls = tintVanishingWalls };
            meshes = LevelGeometryBuilder.Build(parsedLevel, settings, textured ? levelTextures : null);
            builtCell = cellSize;
            builtRow = rowHeight;
            builtTint = tintVanishingWalls;
            builtTextured = textured;

            root = new GameObject($"Level {levelNumber} (generated)");
            root.transform.SetParent(transform, false);
            Part("Floor", meshes.Floor, collide: true);
            Part("Ceiling", meshes.Ceiling, collide: false);
            Part("Walls", meshes.Walls, collide: true);
            Part("WallTops", meshes.WallTops, collide: false);   // only visible from above
            PlaceTopDownCamera();

            if (IsTextured)
                foreach (var w in levelTextures.Warnings) Debug.LogWarning($"[WALK] level {levelNumber}: {w}");
        }

        void Part(string name, MeshPart part, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = part.Keys.Select(textureCache.Material).ToArray();
            if (collide && part.Mesh.vertexCount > 0) go.AddComponent<MeshCollider>().sharedMesh = part.Mesh;
        }

        void Rebuild()
        {
            // Keep the player at the same place in grid units.
            Vector3 p = player.transform.position;
            float oldCell = builtCell;
            Build();
            ConfigurePlayer();
            player.Teleport(new Vector3(p.x / oldCell * cellSize, 0.05f, p.z / oldCell * cellSize),
                            player.transform.eulerAngles.y);
            Debug.Log($"[WALK] rebuilt: {(IsTextured ? "textured" : "debug colours")}, cell {cellSize} m, " +
                      $"row height {rowHeight} m x {meshes.Rows} row(s)");
        }

        // ---- player and cameras ----

        void CreatePlayer(Vector2Int cell)
        {
            var go = new GameObject("Player");
            go.AddComponent<CharacterController>();
            player = go.AddComponent<FirstPersonController>();
            ConfigurePlayer();
            player.Eye.clearFlags = CameraClearFlags.SolidColor;
            player.Eye.backgroundColor = DebugPalette.Background;
            player.Teleport(CellCentre(cell) + Vector3.up * 0.05f, 0f);   // yaw 0 = facing +Z = up the map
        }

        void ConfigurePlayer()
        {
            // Sized from one row so two-row levels don't make a giant player. The eye height is a guess.
            float height = rowHeight * 0.8f;
            float radius = Mathf.Min(cellSize * 0.25f, height * 0.45f);
            player.Configure(height, radius, rowHeight * 0.6f);
        }

        void PlaceTopDownCamera()
        {
            if (topDown == null)
            {
                var go = new GameObject("TopDownCamera");
                go.transform.SetParent(transform, false);
                topDown = go.AddComponent<Camera>();
                topDown.orthographic = true;
                topDown.clearFlags = CameraClearFlags.SolidColor;
                topDown.backgroundColor = DebugPalette.Background;
                topDown.enabled = false;
            }
            float w = parsedLevel.Width * cellSize, h = parsedLevel.Height * cellSize, top = meshes.WallHeight;
            float aspect = topDown.aspect > 0 ? topDown.aspect : 16f / 9f;
            topDown.orthographicSize = Mathf.Max(h / 2f, w / 2f / aspect) * 1.05f;
            topDown.nearClipPlane = 0.1f;
            topDown.farClipPlane = top * 4f + 10f;
            // Looking straight down with +Z at the top of the screen: the same orientation as the ASCII map.
            topDown.transform.SetPositionAndRotation(new Vector3(w / 2f, top * 2f + 5f, -h / 2f),
                                                     Quaternion.Euler(90f, 0f, 0f));
        }

        void SetTopDown(bool on)
        {
            topDown.enabled = on;
            player.Eye.enabled = !on;
            player.InputEnabled = !on;
            if (on) PlaceTopDownCamera();   // refit to the current window aspect
        }

        /// <summary>Logs the error and shows a plain camera, so a failed load never leaves the Game view camera-less.</summary>
        void Fail(string message)
        {
            Debug.LogError($"[WALK] {message}");
            var cam = new GameObject("FallbackCamera").AddComponent<Camera>();
            cam.transform.SetParent(transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = DebugPalette.Background;
        }

        // ---- helpers ----

        Vector3 CellCentre(Vector2Int c) => new Vector3((c.x + 0.5f) * cellSize, 0, -(c.y + 0.5f) * cellSize);

        bool InMap(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < parsedLevel.Width && c.y < parsedLevel.Height;

        /// <summary>The open cell with the most open neighbours, nearest the middle of the map on ties.</summary>
        public static Vector2Int PickSpawn(Level lv)
        {
            var best = new Vector2Int(-1, -1);
            int bestOpen = -1;
            float bestDist = float.MaxValue;
            float cx = (lv.Width - 1) / 2f, cy = (lv.Height - 1) / 2f;
            for (int y = 0; y < lv.Height; y++)
            for (int x = 0; x < lv.Width; x++)
            {
                if (!lv.At(x, y).IsOpen) continue;
                int open = 0;
                if (x > 0 && lv.At(x - 1, y).IsOpen) open++;
                if (x + 1 < lv.Width && lv.At(x + 1, y).IsOpen) open++;
                if (y > 0 && lv.At(x, y - 1).IsOpen) open++;
                if (y + 1 < lv.Height && lv.At(x, y + 1).IsOpen) open++;
                float d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (open > bestOpen || (open == bestOpen && d < bestDist))
                {
                    best = new Vector2Int(x, y); bestOpen = open; bestDist = d;
                }
            }
            return best;
        }

        void LogSummary(Vector2Int spawn)
        {
            var h = parsedLevel.Header;
            var sb = new StringBuilder();
            sb.Append($"[WALK] level {levelNumber}: {parsedLevel.Width}x{parsedLevel.Height}, cell {cellSize} m, " +
                      $"row height {rowHeight} m x {meshes.Rows} row(s) = walls {meshes.WallHeight} m\n");
            sb.Append($"  open cells {meshes.OpenCells} (floor + ceiling quads), wall cells {meshes.WallCells}, " +
                      $"wall faces {meshes.WallFaces} (of them map edge {meshes.EdgeFaces}), vanishing walls {meshes.VanishingCells}\n");
            if (IsTextured)
            {
                string ceil = h.CeilingTile == -1 ? "sky (none drawn)" : $"tile {h.CeilingTile} -> RES4 {levelTextures.TileEntry(h.CeilingTile)}";
                sb.Append($"  textured: wall set {h.WallSet}, palette {Palette3D}; floor tile {h.FloorTile} -> RES4 " +
                          $"{levelTextures.TileEntry(h.FloorTile)}, ceiling {ceil} (tile orientation PROVISIONAL); " +
                          $"{meshes.Walls.Keys.Length} wall textures; placeholder quads {meshes.PlaceholderQuads}\n");
            }
            else
                sb.Append($"  debug colours; ceiling: {(h.CeilingTile == -1 ? "sky (grey)" : "tile " + h.CeilingTile)}\n");
            sb.Append($"  spawn cell ({spawn.x}, {spawn.y})\n");
            sb.Append("  controls: click to capture the mouse, WASD/arrows, Shift to run, Tab top-down view, Esc release\n");
            sb.Append("  wall-type legend (map char, index, debug RGB, cells):");
            foreach (var kv in meshes.WallTypeCells)
                sb.Append($"\n    {LevelReport.WallTypeChars[kv.Key]}  {kv.Key,2}  #{DebugPalette.Hex(DebugPalette.WallType(kv.Key))}  {kv.Value}");
            Debug.Log(sb.ToString());
        }
    }
}
