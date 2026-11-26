// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Text;
using DreamForge.Data;
using DreamForge.Formats;
using DreamForge.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DreamForge.World
{
    /// <summary>
    /// On Play, parses one level from the user's LEVELS.DAT, builds untextured debug geometry with
    /// collision, and spawns a first-person player. Tab switches to a top-down view of the whole map.
    /// Everything is generated at run time; nothing derived from game data is saved in the project.
    /// </summary>
    public sealed class LevelLoader : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Game key in data-path.txt. Only menzo has been tested.")]
        [SerializeField] string game = "menzo";
        [SerializeField, Min(0)] int level = 1;
        [Tooltip("Optional local folder that replaces data-path.txt. Leave empty in anything you commit.")]
        [SerializeField] string localFolderOverride = "";

        [Header("Scale (live: changes rebuild the level)")]
        [SerializeField, Min(0.25f)] float cellSize = 2f;
        [SerializeField, Min(0.25f)] float wallHeight = 2f;

        [Header("Look")]
        [SerializeField] Material material;
        [SerializeField] bool tintVanishingWalls = true;

        [Header("Player")]
        [Tooltip("Cell to start in; (-1, -1) = the open cell nearest the middle with the most open neighbours.")]
        [SerializeField] Vector2Int spawnCell = new Vector2Int(-1, -1);

        public Level Level { get; private set; }

        LevelMeshes meshes;
        GameObject root;
        FirstPersonController player;
        Camera topDown;
        float builtCell, builtHeight;
        bool builtTint;

        void Start()
        {
            try
            {
                string path = DataPaths.Resolve(game, "LEVELS.DAT", localFolderOverride, out string error);
                if (path == null) { Debug.LogError($"[WALK] {error}"); return; }
                Level = LevelsFile.Open(path).ReadLevel(level, LevelLayout.ForGame(game));
                foreach (var w in Level.Warnings) Debug.LogWarning($"[WALK] level {level}: {w}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WALK] Failed to read level {level}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (material == null)
            {
                var shader = Shader.Find("DreamForge/VertexColorUnlit");
                if (shader == null) { Debug.LogError("[WALK] No material set and shader DreamForge/VertexColorUnlit not found."); return; }
                material = new Material(shader);
            }

            Build();
            var spawn = spawnCell.x >= 0 ? spawnCell : PickSpawn(Level);
            if (!InMap(spawn) || !Level.At(spawn.x, spawn.y).IsOpen)
            {
                Debug.LogWarning($"[WALK] spawn cell {spawn} is not an open cell; using the automatic choice.");
                spawn = PickSpawn(Level);
            }
            CreatePlayer(spawn);
            LogSummary(spawn);
        }

        void Update()
        {
            if (Level == null || root == null) return;

            if (!Mathf.Approximately(cellSize, builtCell) || !Mathf.Approximately(wallHeight, builtHeight) ||
                tintVanishingWalls != builtTint)
                Rebuild();

            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame)
                SetTopDown(!topDown.enabled);
        }

        void OnDestroy() => meshes?.Destroy();

        // ---- geometry ----

        void Build()
        {
            meshes?.Destroy();
            if (root != null) Destroy(root);

            meshes = LevelGeometryBuilder.Build(Level, new LevelGeometrySettings
                { CellSize = cellSize, WallHeight = wallHeight, TintVanishingWalls = tintVanishingWalls });
            builtCell = cellSize;
            builtHeight = wallHeight;
            builtTint = tintVanishingWalls;

            root = new GameObject($"Level {level} (generated)");
            root.transform.SetParent(transform, false);
            Part("Floor", meshes.Floor, collide: true);
            Part("Ceiling", meshes.Ceiling, collide: false);
            Part("Walls", meshes.Walls, collide: true);
            Part("WallTops", meshes.WallTops, collide: false);   // only visible from above
            PlaceTopDownCamera();
        }

        void Part(string name, Mesh mesh, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
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
            Debug.Log($"[WALK] rebuilt: cell {cellSize} m, wall height {wallHeight} m");
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
            float height = wallHeight * 0.8f;
            float radius = Mathf.Min(cellSize * 0.25f, height * 0.45f);
            player.Configure(height, radius, wallHeight * 0.6f);
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
            float w = Level.Width * cellSize, h = Level.Height * cellSize;
            float aspect = topDown.aspect > 0 ? topDown.aspect : 16f / 9f;
            topDown.orthographicSize = Mathf.Max(h / 2f, w / 2f / aspect) * 1.05f;
            topDown.nearClipPlane = 0.1f;
            topDown.farClipPlane = wallHeight * 4f + 10f;
            // Looking straight down with +Z at the top of the screen: the same orientation as the ASCII map.
            topDown.transform.SetPositionAndRotation(new Vector3(w / 2f, wallHeight * 2f + 5f, -h / 2f),
                                                     Quaternion.Euler(90f, 0f, 0f));
        }

        void SetTopDown(bool on)
        {
            topDown.enabled = on;
            player.Eye.enabled = !on;
            player.InputEnabled = !on;
            if (on) PlaceTopDownCamera();   // refit to the current window aspect
        }

        // ---- helpers ----

        Vector3 CellCentre(Vector2Int c) => new Vector3((c.x + 0.5f) * cellSize, 0, -(c.y + 0.5f) * cellSize);

        bool InMap(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Level.Width && c.y < Level.Height;

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
            var sb = new StringBuilder();
            sb.Append($"[WALK] level {level}: {Level.Width}x{Level.Height}, cell {cellSize} m, wall height {wallHeight} m\n");
            sb.Append($"  open cells {meshes.OpenCells} (floor + ceiling quads), wall cells {meshes.WallCells}, " +
                      $"wall faces {meshes.WallFaces} (of them map edge {meshes.EdgeFaces}), vanishing walls {meshes.VanishingCells}\n");
            sb.Append($"  ceiling: {(Level.Header.CeilingTile == -1 ? "sky (grey)" : "tile " + Level.Header.CeilingTile)}, " +
                      $"spawn cell ({spawn.x}, {spawn.y})\n");
            sb.Append("  controls: click to capture the mouse, WASD/arrows, Shift to run, Tab top-down view, Esc release\n");
            sb.Append("  wall-type legend (map char, index, debug RGB, cells):");
            foreach (var kv in meshes.WallTypeCells)
                sb.Append($"\n    {LevelReport.WallTypeChars[kv.Key]}  {kv.Key,2}  #{DebugPalette.Hex(DebugPalette.WallType(kv.Key))}  {kv.Value}");
            Debug.Log(sb.ToString());
        }
    }
}
