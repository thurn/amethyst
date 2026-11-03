using System;
using System.Collections.Generic;

namespace Amethyst.Flags
{
    /// <summary>
    /// The <c>.flags</c> format of presets and variants: one <c>name = value</c> per line, and
    /// whole-line <c>#</c> comments, the leading ones describing the file:
    /// <code>
    /// # Thrusting sword: short reach, fast recovery
    /// sword.arc = thrust
    /// sword.speed = 1.4
    /// </code>
    /// </summary>
    public static class FlagFile
    {
        public static List<KeyValuePair<string, string>> Parse(
            string text,
            string source,
            out string about
        )
        {
            var values = new List<KeyValuePair<string, string>>();
            var notes = new List<string>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("#"))
                {
                    if (values.Count == 0)
                        notes.Add(line.TrimStart('#').Trim());
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    throw new FormatException(
                        $"{source}:{i + 1}: expected 'name = value', got '{line}'"
                    );
                values.Add(
                    new KeyValuePair<string, string>(
                        line.Substring(0, eq).Trim(),
                        line.Substring(eq + 1).Trim()
                    )
                );
            }
            about = string.Join(" ", notes).Trim();
            return values;
        }
    }
}
