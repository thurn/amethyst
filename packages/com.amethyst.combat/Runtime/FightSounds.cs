using Amethyst.Action;
using UnityEngine;

namespace Amethyst.Combat
{
    /// <summary>
    /// Plays the fight's voices (<see cref="VoiceBudget"/>) on the contact frame: each layer a
    /// different variation, pitched ±7%. A hit is an impact over a blade chop, a kill an ice break
    /// with glass fragments (docs/game-design.md, Sound and music).
    /// </summary>
    sealed class FightSounds
    {
        const int Sources = 12;

        readonly HeroKit kit;
        readonly AudioSource[] sources = new AudioSource[Sources];
        int next;

        public readonly VoiceBudget Budget = new VoiceBudget();

        public FightSounds(GameObject on, HeroKit kit)
        {
            this.kit = kit;
            for (int i = 0; i < Sources; i++)
            {
                sources[i] = on.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].spatialBlend = 0f;
            }
        }

        /// <summary>Plays this frame's voices.</summary>
        public void Flush(float now, bool enabled)
        {
            var voices = Budget.Flush(now);
            if (!enabled || kit == null)
                return;
            foreach (var v in voices)
                switch (v.Sound)
                {
                    case CombatSound.Hit:
                        Play(kit.Chops, 1, v.Volume * 0.8f);
                        Play(kit.Impacts, v.Layers, v.Volume);
                        break;
                    case CombatSound.Kill:
                        Play(kit.Breaks, 1, v.Volume);
                        Play(kit.Fragments, v.Layers, v.Volume * 0.8f);
                        break;
                    case CombatSound.Telegraph:
                        Play(kit.Telegraphs, 1, v.Volume * 0.7f);
                        break;
                    case CombatSound.Break:
                        Play(kit.Shatters, 1, v.Volume);
                        break;
                }
        }

        // `layers` distinct variations of a sound at once, from a random one on.
        void Play(AudioClip[] clips, int layers, float volume)
        {
            if (clips == null || clips.Length == 0)
                return;
            int first = Random.Range(0, clips.Length);
            for (int k = 0; k < Mathf.Min(layers, clips.Length); k++)
            {
                var source = sources[next];
                next = (next + 1) % Sources;
                source.pitch = Random.Range(0.93f, 1.07f);
                source.PlayOneShot(clips[(first + k) % clips.Length], volume);
            }
        }
    }
}
