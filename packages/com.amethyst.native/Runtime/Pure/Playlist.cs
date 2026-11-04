using System;
using System.Collections.Generic;
using System.IO;

namespace Amethyst.Native
{
    /// <summary>
    /// What <c>--file</c> plays: an M3U playlist's tracks (paths relative to the list, comments
    /// and <c>#EXT</c> lines skipped), or a single audio file. Each track must exist.
    /// </summary>
    public static class Playlist
    {
        public static bool IsPlaylist(string path)
        {
            var ext = Path.GetExtension(path);
            return string.Equals(ext, ".m3u", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".m3u8", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The tracks <paramref name="path"/> names; throws <see cref="IOException"/>
        /// or <see cref="FormatException"/> saying what is wrong.</summary>
        public static List<string> Load(string path)
        {
            if (path.StartsWith("~/"))
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    path.Substring(2)
                );
            path = Path.GetFullPath(path);
            var tracks = IsPlaylist(path)
                ? Parse(File.ReadAllText(path), Path.GetDirectoryName(path))
                : new List<string> { path };
            foreach (var track in tracks)
                if (!File.Exists(track))
                    throw new IOException($"{track}: no such file");
            return tracks;
        }

        /// <summary>A playlist's tracks as absolute paths, relative ones against
        /// <paramref name="baseDir"/>.</summary>
        public static List<string> Parse(string text, string baseDir)
        {
            var tracks = new List<string>();
            var lines = text.TrimStart('﻿').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                if (line.Contains("://"))
                    throw new FormatException(
                        $"playlist line {i + 1}: only local files can play, not {line}"
                    );
                if (IsPlaylist(line))
                    throw new FormatException($"playlist line {i + 1}: nested playlist {line}");
                tracks.Add(Path.GetFullPath(Path.Combine(baseDir, line)));
            }
            if (tracks.Count == 0)
                throw new FormatException("playlist has no tracks");
            return tracks;
        }

        /// <summary>Shuffles <paramref name="items"/> in place, in the order
        /// <paramref name="seed"/> picks (Fisher-Yates on SplitMix64).</summary>
        public static void Shuffle<T>(IList<T> items, ulong seed)
        {
            var state = seed;
            for (int i = items.Count - 1; i > 0; i--)
            {
                state += 0x9E3779B97F4A7C15UL;
                var z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                var j = (int)(z % (ulong)(i + 1));
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}
