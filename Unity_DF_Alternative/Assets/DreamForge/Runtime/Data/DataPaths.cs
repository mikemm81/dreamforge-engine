// SPDX-License-Identifier: GPL-3.0-only
// Finds the user's game folders, as specified in docs/data-setup.md.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DreamForge.Data
{
    public static class DataPaths
    {
        public const string FileName = "data-path.txt";
        public const string EnvVar = "DREAMFORGE_DATA_PATH";

        /// <summary>Candidate data-path.txt locations, in lookup order.</summary>
        public static IEnumerable<string> Candidates()
        {
            string env = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrWhiteSpace(env))
                yield return env;
            yield return Path.Combine(Application.persistentDataPath, FileName);
#if UNITY_EDITOR
            // Top of the repository (development only): Assets/../../data-path.txt
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", FileName));
#endif
        }

        /// <summary>Parses "key = path" lines; '#' starts a comment line. Keys are case-insensitive.</summary>
        public static Dictionary<string, string> Parse(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return map;
        }

        /// <summary>Loads the first data-path.txt that exists. Returns null if none does.</summary>
        public static Dictionary<string, string> Load(out string usedFile)
        {
            foreach (string candidate in Candidates())
            {
                if (File.Exists(candidate))
                {
                    usedFile = candidate;
                    return Parse(File.ReadAllText(candidate));
                }
            }
            usedFile = null;
            return null;
        }

        /// <summary>The game's folders in search order: "install" first, then "cd".</summary>
        public static List<string> GameFolders(Dictionary<string, string> map, string game)
        {
            var dirs = new List<string>();
            foreach (string key in new[] { game + ".install", game + ".cd" })
                if (map != null && map.TryGetValue(key, out string dir) && dir.Length > 0)
                    dirs.Add(dir);
            return dirs;
        }

        /// <summary>Finds a file by name, ignoring case, in the given folders in order. Returns null if absent.</summary>
        public static string FindFile(IEnumerable<string> folders, string name)
        {
            foreach (string dir in folders)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.EnumerateFiles(dir))
                    if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
                        return f;
            }
            return null;
        }
    }
}
