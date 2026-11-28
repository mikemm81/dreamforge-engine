// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;

namespace DreamForge.World
{
    /// <summary>
    /// The moving part of a door cell: its faces and a box collider filling the cell. Opening slides it up
    /// out of the way; it blocks until fully open and again as soon as it starts to close.
    /// </summary>
    public sealed class Door : MonoBehaviour
    {
        /// <summary>
        /// INFERRED from data, not in docs: every door record in M starts at position 8 (byte 0x06) on a wall
        /// cell with the blocking bit set, so 8 is taken as closed and 0 as open.
        /// </summary>
        public const int ClosedPosition = 8;

        /// <summary>
        /// NOT FROM DATA: how the game animates a door is unknown (speed byte 0x07 is 0 in every M door).
        /// A placeholder: the door slides up by its full height over this many seconds.
        /// </summary>
        public const float SlideSeconds = 0.6f;

        public Vector2Int Cell { get; private set; }
        /// <summary>Where the door is heading: true = open.</summary>
        public bool WantsOpen { get; private set; }
        /// <summary>0 = closed, 1 = fully open.</summary>
        public float Openness { get; private set; }
        public bool IsFullyOpen => Openness >= 1f;
        /// <summary>Set by the level: true while the player stands in the door's cell (a door won't close on them).</summary>
        public System.Func<bool> Occupied;

        float height;
        BoxCollider box;

        public void Init(Vector2Int cell, float cellSize, float wallHeight)
        {
            Cell = cell;
            height = wallHeight;
            box = gameObject.AddComponent<BoxCollider>();
            box.center = LevelGeometryBuilder.GridToWorld(cell.x + 0.5f, cell.y + 0.5f, cellSize, wallHeight / 2f);
            box.size = new Vector3(cellSize, wallHeight, cellSize);
            Apply();
        }

        public void SetOpen(bool open) => WantsOpen = open;

        /// <summary>Jumps to the end of the current movement (for tests and level setup).</summary>
        public void Finish()
        {
            if (!WantsOpen && Occupied != null && Occupied()) return;
            Openness = WantsOpen ? 1f : 0f;
            Apply();
        }

        void Update()
        {
            float target = WantsOpen ? 1f : 0f;
            if (Mathf.Approximately(Openness, target)) return;
            if (!WantsOpen && Occupied != null && Occupied()) return;   // wait until the cell is clear
            Openness = Mathf.MoveTowards(Openness, target, Time.deltaTime / SlideSeconds);
            Apply();
        }

        void Apply()
        {
            transform.localPosition = Vector3.up * (Openness * height);
            // Block unless fully open; a closing door blocks at once.
            box.enabled = !(WantsOpen && IsFullyOpen);
            // The box moves with the door; keep it in the cell.
            box.center = new Vector3(box.center.x, height / 2f - transform.localPosition.y, box.center.z);
        }
    }
}
