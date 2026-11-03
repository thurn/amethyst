using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Amethyst.Flags;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amethyst
{
    /// <summary>The flags every project has.</summary>
    public static class CoreFlags
    {
        public static readonly Flag<bool> Mute = Flag.Bool(
            "audio.mute",
            false,
            "Silence all audio; batch runs and agents set it"
        );

        public static readonly Flag<bool> Overlay = Flag.Bool(
            "preview.overlay",
            true,
            "Show the variant switcher where a project has variants"
        );
    }

    /// <summary>A named set of flag values: a file in StreamingAssets/variants/.</summary>
    public sealed class Variant
    {
        public Variant(string name, string about, List<KeyValuePair<string, string>> values)
        {
            Name = name;
            About = about;
            Values = values;
        }

        public string Name { get; }
        public string About { get; }
        public List<KeyValuePair<string, string>> Values { get; }
    }

    /// <summary>
    /// Sets each run up before its first scene loads, from <see cref="LaunchArgs"/>: the flag
    /// layers, audio muting, the data directory and the <see cref="RunDriver"/> that quits,
    /// captures and shows the variant switcher. A batch-mode run with bad arguments or flags
    /// logs why and exits 2.
    /// </summary>
    public static class Run
    {
        public static LaunchArgs Args { get; private set; } = new LaunchArgs();

        /// <summary>Where this run saves: use it instead of Application.persistentDataPath or
        /// PlayerPrefs, which every player of the project shares.</summary>
        public static string DataDir { get; private set; }

        public static IReadOnlyList<Variant> Variants { get; private set; } =
            Array.Empty<Variant>();

        /// <summary>The selected variant's index, or -1.</summary>
        public static int VariantIndex { get; private set; } = -1;

        public static Variant CurrentVariant => VariantIndex >= 0 ? Variants[VariantIndex] : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            FlagState.Reset();
            Args = new LaunchArgs();
            DataDir = null;
            Variants = Array.Empty<Variant>();
            VariantIndex = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Begin()
        {
            var errors = new List<string>();
            Args = LaunchArgs.Parse(
                Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable("AMETHYST_FLAGS")
            );
            errors.AddRange(Args.Errors);
            FlagCatalog.Load(FlagCatalog.Declaring(AppDomain.CurrentDomain.GetAssemblies()));

            var state = FlagState.Current;
            var preset = new List<KeyValuePair<string, string>>();
            foreach (var name in Args.Presets)
            {
                try
                {
                    preset.AddRange(Load(ResolvePreset(name), out _));
                }
                catch (Exception e) when (e is IOException || e is FormatException)
                {
                    errors.Add($"--preset {name}: {e.Message}");
                }
            }
            state.SetLayer(FlagState.Preset, Distinct(preset));
            Variants = LoadVariants(errors);
            state.SetLayer(FlagState.Env, Distinct(Args.EnvFlags));
            var cli = new List<KeyValuePair<string, string>>();
            if (Application.isBatchMode)
                cli.Add(new KeyValuePair<string, string>(CoreFlags.Mute.Name, "true"));
            cli.AddRange(Args.Flags);
            state.SetLayer(FlagState.CommandLine, Distinct(cli));
            if (Args.Variant != null)
            {
                var index = FindVariant(Args.Variant);
                if (index < 0)
                    errors.Add(
                        $"--variant {Args.Variant}: no such variant ({string.Join(", ", Variants.Select(v => v.Name))})"
                    );
                else
                    ApplyVariant(index);
            }
            else if (Variants.Count > 0)
                ApplyVariant(0);
            errors.AddRange(state.Validate());

            DataDir = Args.DataDir ?? Application.persistentDataPath;
            Directory.CreateDirectory(DataDir);
            if (Args.FixedTime)
                Time.captureFramerate = 60;
            ApplyAudio();
            state.Changed += ApplyAudio;

            var overridden = string.Join(
                ", ",
                state.Overridden().Select(o => $"{o.Name}={o.Text} ({o.Layer})")
            );
            Debug.Log(
                $"amethyst: flags {(overridden.Length > 0 ? overridden : "all default")}; data {DataDir}"
            );
            foreach (var error in errors)
                Debug.LogError("amethyst: " + error);
            if (errors.Count > 0 && Application.isBatchMode)
            {
                Application.Quit(2);
                return;
            }
            var driver = new GameObject("Amethyst Run");
            UnityEngine.Object.DontDestroyOnLoad(driver);
            driver.AddComponent<RunDriver>();
        }

        /// <summary>Selects a variant and reloads the scene, so everything reads its flags anew.</summary>
        public static void SelectVariant(int index)
        {
            if (index < 0 || index >= Variants.Count)
                return;
            ApplyVariant(index);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public static int FindVariant(string nameOrNumber)
        {
            if (int.TryParse(nameOrNumber, out var n) && n >= 1 && n <= Variants.Count)
                return n - 1;
            for (int i = 0; i < Variants.Count; i++)
                if (
                    string.Equals(
                        Variants[i].Name,
                        nameOrNumber,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return i;
            return -1;
        }

        static void ApplyVariant(int index)
        {
            VariantIndex = index;
            FlagState.Current.SetLayer(FlagState.Variant, Distinct(Variants[index].Values));
        }

        static void ApplyAudio() => AudioListener.volume = CoreFlags.Mute.Value ? 0f : 1f;

        static string ResolvePreset(string name)
        {
            if (File.Exists(name))
                return name;
            var path = Path.Combine(Application.streamingAssetsPath, "presets", name + ".flags");
            if (File.Exists(path))
                return path;
            throw new IOException(
                $"no preset {name} (StreamingAssets/presets/{name}.flags or a path)"
            );
        }

        static IReadOnlyList<Variant> LoadVariants(List<string> errors)
        {
            var dir = Path.Combine(Application.streamingAssetsPath, "variants");
            if (!Directory.Exists(dir))
                return Array.Empty<Variant>();
            var variants = new List<Variant>();
            foreach (
                var path in Directory
                    .GetFiles(dir, "*.flags")
                    .OrderBy(p => p, StringComparer.Ordinal)
            )
            {
                try
                {
                    var values = Load(path, out var about);
                    variants.Add(
                        new Variant(Path.GetFileNameWithoutExtension(path), about, values)
                    );
                }
                catch (Exception e) when (e is IOException || e is FormatException)
                {
                    errors.Add(e.Message);
                }
            }
            return variants;
        }

        static List<KeyValuePair<string, string>> Load(string path, out string about) =>
            FlagFile.Parse(File.ReadAllText(path), Path.GetFileName(path), out about);

        static IEnumerable<KeyValuePair<string, string>> Distinct(
            IEnumerable<KeyValuePair<string, string>> values
        )
        {
            var last = new Dictionary<string, string>();
            foreach (var kv in values)
                last[kv.Key] = kv.Value;
            return last;
        }
    }
}
