using System;
using System.Collections.Generic;

namespace Amethyst.Action
{
    /// <summary>The fight's sounds (docs/game-design.md, Sound and music).</summary>
    public enum CombatSound
    {
        Hit,
        Kill,
        Hurt,
        Telegraph,
        Break,
    }

    /// <summary>One sound to play: its layers stack variations of it, louder for a bigger count.</summary>
    public struct Voice
    {
        public CombatSound Sound;

        /// <summary>The events it stands for (hits or kills on one frame).</summary>
        public int Count;

        /// <summary>Variations to play at once: 1 for one event, more as the count grows.</summary>
        public int Layers;

        /// <summary>0..1.</summary>
        public float Volume;
    }

    /// <summary>
    /// Keeps a horde's hundreds of hits from playing hundreds of sounds (docs/game-design.md, Sound
    /// and music): every event of a kind on one frame becomes one voice whose layers and volume
    /// grow with the count, so a swing that kills twenty plays one big kill, and each kind starts
    /// at most <see cref="PerWindow"/> voices in any <see cref="Window"/> s, dropping the rest.
    /// </summary>
    public sealed class VoiceBudget
    {
        readonly Dictionary<CombatSound, int> pending = new Dictionary<CombatSound, int>();
        readonly Dictionary<CombatSound, Queue<float>> started =
            new Dictionary<CombatSound, Queue<float>>();
        readonly List<Voice> voices = new List<Voice>();

        public int MaxLayers { get; set; } = 3;
        public int PerWindow { get; set; } = 4;
        public float Window { get; set; } = 0.1f;

        /// <summary>Voices dropped so far because their kind's window was full.</summary>
        public int Dropped { get; private set; }

        /// <summary><paramref name="count"/> events of a sound on this frame.</summary>
        public void Add(CombatSound sound, int count = 1)
        {
            if (count <= 0)
                return;
            pending[sound] = (pending.TryGetValue(sound, out var n) ? n : 0) + count;
        }

        /// <summary>This frame's voices, one per kind with events, then a clean slate. The list
        /// is reused by the next call.</summary>
        public IReadOnlyList<Voice> Flush(float now)
        {
            voices.Clear();
            foreach (var (sound, count) in pending)
            {
                if (!started.TryGetValue(sound, out var times))
                    started[sound] = times = new Queue<float>();
                while (times.Count > 0 && times.Peek() <= now - Window)
                    times.Dequeue();
                if (times.Count >= PerWindow)
                {
                    Dropped++;
                    continue;
                }
                times.Enqueue(now);
                int layers = 1; // 1 + floor(log2(count))
                for (int c = count; c >= 2 && layers < MaxLayers; c >>= 1)
                    layers++;
                voices.Add(
                    new Voice
                    {
                        Sound = sound,
                        Count = count,
                        Layers = layers,
                        Volume = Math.Min(1f, 0.7f + 0.075f * MathF.Log(count, 2f)),
                    }
                );
            }
            pending.Clear();
            return voices;
        }
    }
}
