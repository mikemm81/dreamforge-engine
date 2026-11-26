// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DreamForge.Data;
using DreamForge.Formats;
using UnityEngine;

namespace DreamForge.Diagnostics
{
    /// <summary>
    /// On Play, opens one RES file from the user's game folder and logs its directory.
    /// Logs only offsets, sizes and header fields, never file contents.
    /// </summary>
    public sealed class ResDirectoryLogger : MonoBehaviour
    {
        [Tooltip("Game key in data-path.txt: menzo, strahd or prophet.")]
        [SerializeField] string game = "menzo";

        [Tooltip("RES file name, matched ignoring case.")]
        [SerializeField] string fileName = "RES3";

        [Tooltip("Optional local folder that replaces data-path.txt. Leave empty in anything you commit.")]
        [SerializeField] string localFolderOverride = "";

        [Tooltip("Decode textures and column shapes to check each one yields its unpacked size.")]
        [SerializeField] bool checkCompressedPayloads = true;

        [Tooltip("Table rows per Console message.")]
        [SerializeField, Min(10)] int rowsPerMessage = 200;

        void Start()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RES] Failed to read {fileName}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        void Run()
        {
            string path = DataPaths.Resolve(game, fileName, localFolderOverride, out string error);
            if (path == null)
            {
                Debug.LogError($"[RES] {error}");
                return;
            }

            var res = ResFile.Open(path);
            var entries = res.Entries;

            // Summary
            var byKind = entries.GroupBy(e => e.Kind).OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}");
            var byType = entries.Where(e => e.Kind == ResEntryKind.Eh).GroupBy(e => e.Type).OrderBy(g => g.Key)
                .Select(g => $"type {g.Key}={g.Count()}");
            Debug.Log($"[RES] {fileName}: {entries.Count} entries, {res.FileLength} bytes ({path})\n" +
                      $"  kinds: {string.Join(", ", byKind)}\n  EH types: {string.Join(", ", byType)}");

            // Table, in chunks so the Console stays usable
            for (int start = 0; start < entries.Count; start += rowsPerMessage)
            {
                int end = Math.Min(entries.Count, start + rowsPerMessage);
                var sb = new StringBuilder();
                sb.Append($"[RES] {fileName} entries {start}-{end - 1}\n").Append(ResEntry.TableHeader);
                for (int i = start; i < end; i++)
                    sb.Append('\n').Append(entries[i].ToTableRow());
                Debug.Log(sb.ToString());
            }

            // Problems: directory first, then payloads
            var problems = new List<string>();
            foreach (var e in entries)
                if (e.Problem != null)
                    problems.Add($"entry {e.Index}: {e.Problem}");

            int checkedCount = 0;
            if (checkCompressedPayloads)
            {
                foreach (var e in entries)
                {
                    if (e.Problem != null || e.PayloadOffset < 0) continue;
                    checkedCount++;
                    string msg = res.CheckPayload(e);
                    if (msg != null)
                        problems.Add($"entry {e.Index} (type {e.Type}, compression {e.Compression}): {msg}");
                }
            }

            string checkNote = checkCompressedPayloads ? $", {checkedCount} payloads decoded" : "";
            if (problems.Count == 0)
                Debug.Log($"[RES] {fileName}: no problems{checkNote}.");
            else
                Debug.LogWarning($"[RES] {fileName}: {problems.Count} problem(s){checkNote}:\n  " +
                                 string.Join("\n  ", problems));
        }
    }
}
