// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using DreamForge.Data;
using DreamForge.Formats;
using UnityEngine;
using UnityEngine.Serialization;

namespace DreamForge.Diagnostics
{
    /// <summary>
    /// On Play, parses one level from LEVELS.DAT and logs it as an ASCII map plus its records.
    /// Logs parsed fields only, never raw file contents.
    /// </summary>
    public sealed class LevelMapLogger : MonoBehaviour
    {
        [Tooltip("Game key in data-path.txt: menzo, strahd or prophet. Only menzo has been tested.")]
        [SerializeField] string game = "menzo";

        [Tooltip("Level number in LEVELS.DAT.")]
        [SerializeField, Min(0), FormerlySerializedAs("level")] int levelNumber = 1;

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
                Debug.LogError($"[LEVEL] Failed to read level {levelNumber}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        void Run()
        {
            string path = DataPaths.Resolve(game, "LEVELS.DAT", localFolderOverride, out string error);
            if (path == null)
            {
                Debug.LogError($"[LEVEL] {error}");
                return;
            }

            var file = LevelsFile.Open(path);
            var lv = file.ReadLevel(levelNumber, LevelLayout.ForGame(game));

            Debug.Log($"[LEVEL] {path}: {file.Count} levels\n{LevelReport.Format(lv)}");

            var kinds = lv.Records.GroupBy(r => r.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}");
            Debug.Log($"[LEVEL] level {levelNumber} record kinds: {string.Join(", ", kinds)}\n  legend: {LevelReport.Legend}");

            if (lv.Warnings.Count == 0)
                Debug.Log($"[LEVEL] level {levelNumber}: parsed cleanly, ends exactly.");
            else
                Debug.LogWarning($"[LEVEL] level {levelNumber}: {lv.Warnings.Count} warning(s):\n  " + string.Join("\n  ", lv.Warnings));
        }
    }
}
