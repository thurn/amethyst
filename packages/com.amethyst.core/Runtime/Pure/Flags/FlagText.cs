using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Amethyst.Flags
{
    /// <summary>
    /// Flag values as text, the form presets, variants and the command line use. Enum values
    /// are kebab-case member names (<c>WideArc</c> is <c>wide-arc</c>).
    /// </summary>
    public static class FlagText
    {
        public static bool TryBool(string text, out bool value)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "true":
                case "on":
                case "yes":
                case "1":
                    value = true;
                    return true;
                case "false":
                case "off":
                case "no":
                case "0":
                    value = false;
                    return true;
                default:
                    value = false;
                    return false;
            }
        }

        public static string Bool(bool value) => value ? "true" : "false";

        public static bool TryInt(string text, out int value) =>
            int.TryParse(
                text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );

        public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static bool TryFloat(string text, out float value) =>
            float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !float.IsNaN(value)
            && !float.IsInfinity(value);

        public static string Float(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

        public static bool TryString(string text, out string value)
        {
            value = text.Trim();
            return true;
        }

        public static bool TryEnum<T>(string text, out T value)
            where T : struct, System.Enum
        {
            var key = text.Trim().Replace("-", "");
            foreach (var name in System.Enum.GetNames(typeof(T)))
            {
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = (T)System.Enum.Parse(typeof(T), name);
                    return true;
                }
            }
            value = default;
            return false;
        }

        public static string Enum<T>(T value)
            where T : struct, System.Enum => Kebab(value.ToString());

        public static IReadOnlyList<string> Choices<T>()
            where T : struct, System.Enum =>
            System.Enum.GetNames(typeof(T)).Select(Kebab).ToArray();

        /// <summary><c>WideArc</c> to <c>wide-arc</c>: a dash before each capital that follows a
        /// lowercase letter or digit. tools/amethyst_tools/flags.py applies the same rule.</summary>
        public static string Kebab(string pascal)
        {
            var s = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                var c = pascal[i];
                if (
                    i > 0
                    && char.IsUpper(c)
                    && (char.IsLower(pascal[i - 1]) || char.IsDigit(pascal[i - 1]))
                )
                    s.Append('-');
                s.Append(char.ToLowerInvariant(c));
            }
            return s.ToString();
        }
    }
}
