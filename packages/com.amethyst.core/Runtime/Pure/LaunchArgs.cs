using System;
using System.Collections.Generic;
using System.Globalization;

namespace Amethyst
{
    /// <summary>
    /// Amethyst's player arguments, all with two dashes (Unity's own take one and are skipped):
    /// <list type="bullet">
    /// <item><c>--flag NAME=VALUE</c> (repeatable), <c>--mute</c>, <c>--audio</c> (audio.mute)</item>
    /// <item><c>--preset NAME|PATH</c> (repeatable, later ones win), <c>--variant NAME|N</c></item>
    /// <item><c>--seconds N</c>: quit after N seconds of game time</item>
    /// <item><c>--capture DIR --at T1,T2</c>: PNGs at those seconds after each scene load;
    /// <c>--capture-variants</c> does it for every variant in turn; <c>--size WxH</c></item>
    /// <item><c>--fixed-time</c>: exactly 1/60 s per frame</item>
    /// <item><c>--data-dir DIR</c>: where the run saves, instead of the shared persistent path</item>
    /// <item><c>--file PATH</c>: the music, an M3U playlist or an audio file, instead of the
    /// soundtrack; <c>--shuffle</c> shuffles it</item>
    /// </list>
    /// <c>AMETHYST_FLAGS</c> holds whitespace-separated <c>NAME=VALUE</c> pairs below
    /// <c>--flag</c>'s.
    /// </summary>
    public sealed class LaunchArgs
    {
        public readonly List<KeyValuePair<string, string>> Flags =
            new List<KeyValuePair<string, string>>();
        public readonly List<KeyValuePair<string, string>> EnvFlags =
            new List<KeyValuePair<string, string>>();
        public readonly List<string> Presets = new List<string>();
        public readonly List<float> CaptureAt = new List<float>();
        public readonly List<string> Errors = new List<string>();
        public string Variant;
        public float Seconds = -1;
        public string CaptureDir;
        public bool CaptureVariants;
        public int CaptureWidth = 1280;
        public int CaptureHeight = 720;
        public bool FixedTime;
        public string DataDir;
        public string MusicFile;
        public bool Shuffle;

        public bool Captures => CaptureDir != null;

        public static LaunchArgs Parse(IReadOnlyList<string> args, string env = null)
        {
            var result = new LaunchArgs();
            foreach (
                var pair in (env ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            )
                result.AddPair(result.EnvFlags, pair, "AMETHYST_FLAGS");
            for (int i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--"))
                    continue;
                string inline = null;
                var eq = arg.IndexOf('=');
                if (eq > 0)
                {
                    inline = arg.Substring(eq + 1);
                    arg = arg.Substring(0, eq);
                }
                string Next()
                {
                    if (inline != null)
                        return inline;
                    if (i + 1 < args.Count)
                        return args[++i];
                    result.Errors.Add($"{arg} needs a value");
                    return null;
                }
                switch (arg)
                {
                    case "--flag":
                        var pair = Next();
                        if (pair != null)
                            result.AddPair(result.Flags, pair, "--flag");
                        break;
                    case "--mute":
                        result.Flags.Add(new KeyValuePair<string, string>("audio.mute", "true"));
                        break;
                    case "--audio":
                        result.Flags.Add(new KeyValuePair<string, string>("audio.mute", "false"));
                        break;
                    case "--preset":
                        Add(result.Presets, Next());
                        break;
                    case "--variant":
                        result.Variant = Next();
                        break;
                    case "--seconds":
                        result.Seconds = result.Number(arg, Next());
                        break;
                    case "--capture":
                        result.CaptureDir = Next();
                        break;
                    case "--at":
                        foreach (var t in (Next() ?? "").Split(','))
                            result.CaptureAt.Add(result.Number(arg, t));
                        break;
                    case "--capture-variants":
                        result.CaptureVariants = true;
                        break;
                    case "--size":
                        result.Size(Next());
                        break;
                    case "--fixed-time":
                        result.FixedTime = true;
                        break;
                    case "--data-dir":
                        result.DataDir = Next();
                        break;
                    case "--file":
                        result.MusicFile = Next();
                        break;
                    case "--shuffle":
                        result.Shuffle = true;
                        break;
                    default:
                        result.Errors.Add($"unknown argument {arg}");
                        break;
                }
            }
            if (result.Captures && result.CaptureAt.Count == 0)
                result.CaptureAt.Add(1);
            if (result.CaptureVariants && !result.Captures)
                result.Errors.Add("--capture-variants needs --capture DIR");
            return result;
        }

        static void Add(List<string> list, string value)
        {
            if (value != null)
                list.Add(value);
        }

        void AddPair(List<KeyValuePair<string, string>> list, string pair, string source)
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                Errors.Add($"{source} expects NAME=VALUE, got '{pair}'");
                return;
            }
            list.Add(
                new KeyValuePair<string, string>(pair.Substring(0, eq), pair.Substring(eq + 1))
            );
        }

        float Number(string arg, string text)
        {
            if (
                text != null
                && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            )
                return v;
            Errors.Add($"{arg} expects a number, got '{text}'");
            return -1;
        }

        void Size(string text)
        {
            var parts = (text ?? "").Split('x');
            if (
                parts.Length == 2
                && int.TryParse(parts[0], out var w)
                && int.TryParse(parts[1], out var h)
                && w > 0
                && h > 0
            )
            {
                CaptureWidth = w;
                CaptureHeight = h;
            }
            else
                Errors.Add($"--size expects WIDTHxHEIGHT, got '{text}'");
        }
    }
}
