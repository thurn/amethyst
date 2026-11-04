using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Amethyst.Flags
{
    /// <summary>
    /// Flag values as layers of text, lowest first: <see cref="Preset"/>, <see cref="Variant"/>,
    /// <see cref="Env"/>, <see cref="CommandLine"/>, <see cref="Live"/>, then any other layer
    /// (test overrides) in the order it was added. A flag takes its highest layer's value, else
    /// its default. Read and write flags on the main thread.
    /// </summary>
    public sealed class FlagState
    {
        public const string Preset = "preset";
        public const string Variant = "variant";
        public const string Env = "env";
        public const string CommandLine = "cli";
        public const string Live = "live";

        static readonly string[] Ranked = { Preset, Variant, Env, CommandLine, Live };
        static long versions;
        static int overrides;

        readonly List<KeyValuePair<string, Dictionary<string, string>>> layers =
            new List<KeyValuePair<string, Dictionary<string, string>>>();

        public static FlagState Current { get; private set; } = new FlagState();

        /// <summary>Replaces <see cref="Current"/> with an empty state (a new run, or a test).</summary>
        public static FlagState Reset()
        {
            Current = new FlagState();
            return Current;
        }

        public FlagState() => Version = Interlocked.Increment(ref versions);

        /// <summary>Changes on every write, and differs between states.</summary>
        public long Version { get; private set; }

        public event System.Action Changed;

        public void SetLayer(string layer, IEnumerable<KeyValuePair<string, string>> values)
        {
            var dict = values.ToDictionary(kv => kv.Key, kv => kv.Value);
            var at = layers.FindIndex(l => l.Key == layer);
            if (at >= 0)
                layers[at] = new KeyValuePair<string, Dictionary<string, string>>(layer, dict);
            else
                layers.Insert(
                    InsertionPoint(layer),
                    new KeyValuePair<string, Dictionary<string, string>>(layer, dict)
                );
            Bump();
        }

        public void Set(string layer, string name, string text)
        {
            var at = layers.FindIndex(l => l.Key == layer);
            if (at < 0)
                SetLayer(layer, new[] { new KeyValuePair<string, string>(name, text) });
            else
            {
                layers[at].Value[name] = text;
                Bump();
            }
        }

        public bool RemoveLayer(string layer)
        {
            var removed = layers.RemoveAll(l => l.Key == layer) > 0;
            if (removed)
                Bump();
            return removed;
        }

        public bool TryGet(string name, out string text)
        {
            for (int i = layers.Count - 1; i >= 0; i--)
                if (layers[i].Value.TryGetValue(name, out text))
                    return true;
            text = null;
            return false;
        }

        /// <summary>Sets one flag in a layer of its own until the scope is disposed.</summary>
        public IDisposable Override(string name, string text)
        {
            var layer = "override#" + Interlocked.Increment(ref overrides);
            Set(layer, name, text);
            return new Scope(() => RemoveLayer(layer));
        }

        /// <summary>Every flag a layer sets, with its effective text and layer, by name.</summary>
        public IReadOnlyList<(string Name, string Text, string Layer)> Overridden()
        {
            var seen = new Dictionary<string, (string Text, string Layer)>();
            foreach (var layer in layers)
            foreach (var kv in layer.Value)
                seen[kv.Key] = (kv.Value, layer.Key);
            return seen.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (kv.Key, kv.Value.Text, kv.Value.Layer))
                .ToArray();
        }

        /// <summary>
        /// Problems with the layers: unknown flags and values that don't parse. Call it after
        /// <see cref="FlagCatalog.Load"/>, which declares every flag.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            foreach (var layer in layers)
            {
                foreach (var kv in layer.Value)
                {
                    var flag = Flag.Find(kv.Key);
                    var error = flag == null ? $"unknown flag '{kv.Key}'" : flag.Check(kv.Value);
                    if (error != null)
                        errors.Add($"{error} (from {layer.Key})");
                }
            }
            return errors;
        }

        int InsertionPoint(string layer)
        {
            var rank = Array.IndexOf(Ranked, layer);
            if (rank < 0)
                return layers.Count;
            var at = 0;
            while (at < layers.Count)
            {
                var other = Array.IndexOf(Ranked, layers[at].Key);
                if (other < 0 || other > rank)
                    break;
                at++;
            }
            return at;
        }

        void Bump()
        {
            Version = Interlocked.Increment(ref versions);
            Changed?.Invoke();
        }

        sealed class Scope : IDisposable
        {
            System.Action dispose;

            public Scope(System.Action dispose) => this.dispose = dispose;

            public void Dispose()
            {
                dispose?.Invoke();
                dispose = null;
            }
        }
    }
}
