// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// INFERRED from data, NOT verified: which way an exit's turn (0x08 high byte, steps of 45 degrees) goes.
        /// Taken as relative to the heading you walk in with (turn 0 keeps it: every turn-0 link, such as the
        /// inn door and the 18/21 edges, then has you facing on into the new level). +1 = clockwise seen from
        /// above. Turns 4 (180 degrees) don't depend on it; turns 2, 6 and 7 (six exits in M) do.
        /// </summary>
        const int ExitTurnSign = 1;

        // Parsed at run time from the user's game data; never serialized.
        [NonSerialized] LevelsFile levelsFile;
        [NonSerialized] ResFile res4;
        [NonSerialized] Palette palette;
        [NonSerialized] GameInit init;
        [NonSerialized] Level parsedLevel;
        [NonSerialized] LevelTextures levelTextures;
        [NonSerialized] LevelTextureCache textureCache;

        LevelMeshes meshes;
        HashSet<Vector2Int> doorCells = new HashSet<Vector2Int>();
        readonly List<Mesh> doorMeshes = new List<Mesh>();
        LevelPlay play;
        int currentLevel = -1;
        Vector2Int playerCell;
        ExitTarget? pendingExit;
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
        /// <summary>Doors, triggers and exits of the current level.</summary>
        public LevelPlay Play => play;
        public FirstPersonController Player => player;
        /// <summary>The level now loaded (exits change it; the serialized start level stays as it is).</summary>
        public int CurrentLevel => currentLevel;
        public Vector2Int PlayerCell => playerCell;

        // Everything is set up in Awake, before the first frame is rendered, so the scene (which holds no
        // camera of its own) has the player's camera from frame one on an ordinary Play.
        void Awake()
        {
            try
            {
                string path = DataPaths.Resolve(game, "LEVELS.DAT", localFolderOverride, out string error);
                if (path == null) { Fail(error); return; }
                levelsFile = LevelsFile.Open(path);
            }
            catch (Exception ex)
            {
                Fail($"Failed to open LEVELS.DAT: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (material == null)
            {
                var shader = Shader.Find("DreamForge/LevelUnlit");
                if (shader == null) { Fail("No material set and shader DreamForge/LevelUnlit not found."); return; }
                material = new Material(shader) { hideFlags = HideFlags.DontSave };
            }

            LoadTextures();
            LoadLevel(levelNumber, spawnCell.x >= 0 ? spawnCell : (Vector2Int?)null, 0f);
        }

        /// <summary>
        /// Loads level <paramref name="number"/> and puts the player in <paramref name="cell"/> (null = the
        /// automatic choice) facing <paramref name="yaw"/> degrees (0 = towards y+1). Returns false if the
        /// level can't be read (the current one stays).
        /// </summary>
        public bool LoadLevel(int number, Vector2Int? cell, float yaw)
        {
            Level next;
            try
            {
                next = levelsFile.ReadLevel(number, LevelLayout.ForGame(game));
                foreach (var w in next.Warnings) Debug.LogWarning($"[WALK] level {number}: {w}");
            }
            catch (Exception ex)
            {
                if (parsedLevel == null) Fail($"Failed to read level {number}: {ex.GetType().Name}: {ex.Message}");
                else Debug.LogError($"[WALK] failed to read level {number}: {ex.GetType().Name}: {ex.Message}");
                return false;
            }

            parsedLevel = next;
            currentLevel = number;
            levelTextures = res4 != null && init != null ? new LevelTextures(res4, parsedLevel.Header, init) : null;
            var ignored = new List<string>();
            doorCells = LevelPlay.DoorCells(parsedLevel, ignored);
            foreach (var w in ignored) Debug.LogWarning($"[WALK] level {number}: {w}");
            play = new LevelPlay(parsedLevel);
            play.ExitTaken += t => pendingExit = t;
            pendingExit = null;
            Build();

            var spawn = cell ?? PickSpawn(parsedLevel);
            if (!InMap(spawn) || !parsedLevel.At(spawn.x, spawn.y).IsOpen)
            {
                var near = NearestOpen(parsedLevel, spawn);
                Debug.LogWarning($"[WALK] level {number}: cell {spawn} is not an open cell; using {near}.");
                spawn = near;
            }
            if (player == null) CreatePlayer(spawn, yaw);
            else player.Teleport(CellCentre(spawn) + Vector3.up * 0.05f, yaw);
            playerCell = spawn;   // arriving doesn't fire the cell's floor trigger or exit
            LogSummary(spawn);
            return true;
        }

        /// <summary>RES4 + COLORS + INIT. If any is missing the level still loads, in debug colours.</summary>
        void LoadTextures()
        {
            res4 = null;
            palette = null;
            init = null;
            try
            {
                string resPath = DataPaths.Resolve(game, "RES4", localFolderOverride, out string e1);
                string colorsPath = DataPaths.Resolve(game, "COLORS", localFolderOverride, out string e2);
                string initPath = DataPaths.Resolve(game, "INIT", localFolderOverride, out string e3);
                if (resPath == null || colorsPath == null || initPath == null)
                    Debug.LogWarning($"[WALK] no textures, using debug colours: {e1 ?? e2 ?? e3}");
                else
                {
                    res4 = ResFile.Open(resPath);
                    var colors = ColorsFile.Open(colorsPath);
                    foreach (var w in colors.Warnings) Debug.LogWarning($"[WALK] COLORS: {w}");
                    palette = colors.Get(Palette3D);
                    init = GameInit.Open(initPath);
                    foreach (var w in init.Warnings) Debug.LogWarning($"[WALK] INIT: {w}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WALK] no textures, using debug colours: {ex.GetType().Name}: {ex.Message}");
                res4 = null;
                palette = null;
                init = null;
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
            if (kb != null && kb.eKey.wasPressedThisFrame && player.InputEnabled)
                UseFacing();
        }

        void OnDestroy()
        {
            meshes?.Destroy();
            foreach (var m in doorMeshes) if (m != null) Destroy(m);
            textureCache?.Dispose();
        }

        // ---- geometry ----

        void Build()
        {
            meshes?.Destroy();
            foreach (var m in doorMeshes) if (m != null) Destroy(m);
            doorMeshes.Clear();
            if (root != null)
            {
                root.SetActive(false);   // Destroy waits for the end of the frame; hide its renderers and colliders now
                Destroy(root);
            }

            var settings = new LevelGeometrySettings
                { CellSize = cellSize, RowHeight = rowHeight, TintVanishingWalls = tintVanishingWalls };
            meshes = LevelGeometryBuilder.Build(parsedLevel, settings, textured ? levelTextures : null, doorCells);
            builtCell = cellSize;
            builtRow = rowHeight;
            builtTint = tintVanishingWalls;
            builtTextured = textured;

            root = new GameObject($"Level {currentLevel} (generated)");
            root.transform.SetParent(transform, false);
            Part("Floor", meshes.Floor, collide: true);
            Part("Ceiling", meshes.Ceiling, collide: false);
            Part("Walls", meshes.Walls, collide: true);
            Part("WallTops", meshes.WallTops, collide: false);   // only visible from above
            BuildDoors(settings);
            PlaceTopDownCamera();

            if (IsTextured)
            {
                foreach (var w in levelTextures.Warnings) Debug.LogWarning($"[WALK] level {currentLevel}: {w}");
                foreach (var w in textureCache.Warnings.Distinct()) Debug.LogWarning($"[WALK] level {currentLevel}: {w}");
                textureCache.Warnings.Clear();
            }
        }

        /// <summary>One moving piece per door cell. A rebuild keeps each door's open/closed state.</summary>
        void BuildDoors(LevelGeometrySettings settings)
        {
            var was = new Dictionary<Vector2Int, bool>();
            foreach (var kv in play.Doors) if (kv.Value != null) was[kv.Key] = kv.Value.WantsOpen;
            foreach (var c in doorCells)
            {
                var part = LevelGeometryBuilder.BuildDoor(parsedLevel, settings, textured ? levelTextures : null, c.x, c.y, doorCells);
                doorMeshes.Add(part.Mesh);
                var go = new GameObject($"Door ({c.x}, {c.y})");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = part.Keys.Select(textureCache.Material).ToArray();
                var door = go.AddComponent<Door>();
                door.Init(c, cellSize, meshes.WallHeight);
                var cc = c;
                door.Occupied = () => playerCell == cc;
                if (was.TryGetValue(c, out bool open) && open) { door.SetOpen(true); door.Finish(); }
                play.AddDoor(door);
            }
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

        void CreatePlayer(Vector2Int cell, float yaw)
        {
            var go = new GameObject("Player");
            go.AddComponent<CharacterController>();
            player = go.AddComponent<FirstPersonController>();
            ConfigurePlayer();
            player.Eye.clearFlags = CameraClearFlags.SolidColor;
            player.Eye.backgroundColor = DebugPalette.Background;
            player.Teleport(CellCentre(cell) + Vector3.up * 0.05f, yaw);   // yaw 0 = facing +Z = towards y+1
            player.Moved += OnPlayerMoved;
            player.Hit += OnPlayerHit;
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
            // Looking straight down with +Z (y+1) at the top of the screen and x to the right: the true view
            // from above, which is the ASCII map (y going down) flipped vertically.
            topDown.transform.SetPositionAndRotation(new Vector3(w / 2f, top * 2f + 5f, h / 2f),
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

        // ---- play ----

        void OnPlayerMoved()
        {
            var c = WorldToCell(player.transform.position);
            if (c != playerCell)
            {
                var from = playerCell;
                playerCell = c;
                play.LeaveCell(from);
                play.EnterCell(c);
            }
            if (pendingExit.HasValue)
            {
                var t = pendingExit.Value;
                pendingExit = null;
                TakeExit(t);
            }
        }

        void OnPlayerHit(ControllerColliderHit h)
        {
            if (Mathf.Abs(h.normal.y) > 0.5f) return;   // floor or ceiling
            var c = WorldToCell(h.point - h.normal * 0.05f);
            if (c != playerCell) play.Bump(c);
        }

        void TakeExit(ExitTarget t)
        {
            if (t.Level == LevelPlay.WorldMapLevel)
            {
                Debug.Log("[PLAY] exit to the world map (level 99): not implemented; staying here");
                return;
            }
            if (t.Level < 0 || t.Level >= levelsFile.Count)
            {
                Debug.LogWarning($"[PLAY] exit to level {t.Level}: LEVELS.DAT has {levelsFile.Count} levels; staying here");
                return;
            }
            float yaw = player.transform.eulerAngles.y + ExitTurnSign * t.Turn * 45f;
            LoadLevel(t.Level, t.Cell, yaw);
        }

        /// <summary>Uses the wall face the player looks at, if it is within reach (E).</summary>
        public bool UseFacing()
        {
            var eye = player.Eye.transform;
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, cellSize * 1.25f)) return false;
            if (Mathf.Abs(hit.normal.y) > 0.5f) return false;
            var c = WorldToCell(hit.point - hit.normal * 0.05f);
            int face = LevelTextures.FaceTowards(Mathf.RoundToInt(hit.normal.x), Mathf.RoundToInt(hit.normal.z));
            bool used = play.Use(c, face);
            if (!used) Debug.Log($"[PLAY] nothing to use on wall ({c.x}, {c.y}) face {face}");
            return used;
        }

        // ---- helpers ----

        public Vector2Int WorldToCell(Vector3 p) =>
            new Vector2Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));

        static Vector2Int NearestOpen(Level lv, Vector2Int c)
        {
            var best = PickSpawn(lv);
            int bestD = int.MaxValue;
            for (int y = 0; y < lv.Height; y++)
            for (int x = 0; x < lv.Width; x++)
            {
                if (!lv.At(x, y).IsOpen) continue;
                int d = (x - c.x) * (x - c.x) + (y - c.y) * (y - c.y);
                if (d < bestD) { bestD = d; best = new Vector2Int(x, y); }
            }
            return best;
        }

        Vector3 CellCentre(Vector2Int c) => LevelGeometryBuilder.GridToWorld(c.x + 0.5f, c.y + 0.5f, cellSize);

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
            sb.Append($"[WALK] level {currentLevel}: {parsedLevel.Width}x{parsedLevel.Height}, cell {cellSize} m, " +
                      $"row height {rowHeight} m x {meshes.Rows} row(s) = walls {meshes.WallHeight} m\n");
            sb.Append($"  open cells {meshes.OpenCells} (floor + ceiling quads), wall cells {meshes.WallCells}, " +
                      $"wall faces {meshes.WallFaces} (of them map edge {meshes.EdgeFaces}), vanishing walls {meshes.VanishingCells}\n");
            if (IsTextured)
            {
                string ceil = h.CeilingTile == -1 ? "sky (none drawn)" : $"tile {h.CeilingTile} -> RES4 {levelTextures.TileEntry(h.CeilingTile)}";
                sb.Append($"  textured: wall set {h.WallSet}, palette {Palette3D}; floor tile {h.FloorTile} -> RES4 " +
                          $"{levelTextures.TileEntry(h.FloorTile)}, ceiling {ceil} (tile axes match DOSBox; mirror/180 unknown); " +
                          $"wall textures {meshes.Walls.Keys.Count(k => k >= 0)} (masked {meshes.Walls.Keys.Count(textureCache.IsMasked)}, " +
                          $"{meshes.MaskedWallCells} masked wall cells get floor + ceiling); " +
                          $"placeholder quads {meshes.PlaceholderQuads}\n");
            }
            else
                sb.Append($"  debug colours; ceiling: {(h.CeilingTile == -1 ? "sky (grey)" : "tile " + h.CeilingTile)}\n");
            sb.Append($"  spawn cell ({spawn.x}, {spawn.y}); doors {meshes.DoorCells}\n");
            sb.Append("  controls: click to capture the mouse, WASD/arrows, Shift to run, E use (wall switches), Tab top-down view, Esc release\n");
            sb.Append("  wall-type legend (map char, index, debug RGB, cells):");
            foreach (var kv in meshes.WallTypeCells)
                sb.Append($"\n    {LevelReport.WallTypeChars[kv.Key]}  {kv.Key,2}  #{DebugPalette.Hex(DebugPalette.WallType(kv.Key))}  {kv.Value}");
            Debug.Log(sb.ToString());
        }
    }
}
