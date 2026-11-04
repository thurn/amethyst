using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amethyst.Flags;
using UnityEngine;

namespace Amethyst.Native
{
    public static class MusicFlags
    {
        public static readonly Flag<float> Volume = Flag.Float(
            "music.volume",
            0.7f,
            "The music's level, 0 to 1"
        );

        public static readonly Flag<float> Duck = Flag.Float(
            "music.duck",
            8f,
            "How far a full-strength MusicPlayer.Duck lowers the music, dB"
        );

        public static readonly Flag<float> DuckRelease = Flag.Float(
            "music.duck-release",
            0.45f,
            "How long the music takes to come back from a duck, s"
        );
    }

    /// <summary>
    /// The music, with no rhythm: <c>--file</c>'s playlist decoded by the native plugin a track
    /// ahead, or else the project's <see cref="Soundtrack"/>, track after track and round again,
    /// shuffled with <c>--shuffle</c>. It outlives scene loads. Big impacts lower it briefly
    /// through <see cref="Duck"/>; nothing reads its beat.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        static MusicPlayer current;

        AudioSource source;
        List<string> files; // --file's tracks, or null
        AudioClip[] clips; // else the soundtrack's
        int next;
        int rate;
        bool playing;
        float endsAt;
        float duck;
        int failures;
        Task<float[]> decoding;
        AudioClip decoded; // the clip made from a file, destroyed with the next

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => current = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            var args = Run.Args;
            List<string> files = null;
            AudioClip[] clips = null;
            string from;
            if (args.MusicFile != null)
            {
                try
                {
                    files = Playlist.Load(args.MusicFile);
                }
                catch (Exception e) when (e is IOException || e is FormatException)
                {
                    Debug.LogError($"amethyst: --file {args.MusicFile}: {e.Message}");
                    return;
                }
                from = args.MusicFile;
            }
            else
            {
                var soundtrack = Resources.Load<Soundtrack>("Soundtrack");
                if (
                    soundtrack == null
                    || soundtrack.Tracks == null
                    || soundtrack.Tracks.Length == 0
                )
                    return;
                clips = (AudioClip[])soundtrack.Tracks.Clone();
                from = "the soundtrack";
            }
            var shuffle = "";
            if (args.Shuffle)
            {
                var seed = (ulong)DateTime.UtcNow.Ticks;
                if (files != null)
                    Playlist.Shuffle(files, seed);
                else
                    Playlist.Shuffle(clips, seed);
                shuffle = $", shuffled by seed {seed}";
            }
            var go = new GameObject("Music");
            DontDestroyOnLoad(go);
            current = go.AddComponent<MusicPlayer>();
            current.files = files;
            current.clips = clips;
            Debug.Log(
                $"amethyst: music playing {files?.Count ?? clips.Length} tracks from {from}{shuffle}"
            );
        }

        /// <summary>
        /// Lowers the music at once by up to <see cref="MusicFlags.Duck"/> (at
        /// <paramref name="strength"/> 1), recovering over <see cref="MusicFlags.DuckRelease"/>:
        /// for big impacts, so they read clearly. Does nothing without music.
        /// </summary>
        public static void Duck(float strength = 1f)
        {
            if (current != null)
                current.duck = Mathf.Max(current.duck, Mathf.Clamp01(strength));
        }

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 0; // never the voice Unity steals
            rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        }

        void Update()
        {
            // Unscaled: hitstop and slow motion must not hold the duck down.
            var release = Mathf.Max(MusicFlags.DuckRelease.Value, 0.01f);
            duck = Mathf.Max(0f, duck - Time.unscaledDeltaTime / release);
            source.volume =
                Mathf.Clamp01(MusicFlags.Volume.Value)
                * Mathf.Pow(10f, -MusicFlags.Duck.Value * duck / 20f);

            // A track ends when the source stops and its length has passed, so a player
            // without audio output still moves through the list in time.
            if (playing && (source.isPlaying || Time.unscaledTime < endsAt))
                return;
            playing = false;
            if (files == null)
            {
                Play(clips[next], next, clips.Length);
                next = (next + 1) % clips.Length;
                return;
            }
            decoding ??= Decode(files[next]);
            if (!decoding.IsCompleted)
                return;
            var task = decoding;
            var index = next;
            decoding = null;
            next = (next + 1) % files.Count;
            if (task.IsFaulted)
            {
                Debug.LogError(
                    $"amethyst: music can't play {files[index]}: {task.Exception.GetBaseException().Message}"
                );
                if (++failures >= files.Count)
                    enabled = false;
                return;
            }
            failures = 0;
            var samples = task.Result;
            var clip = AudioClip.Create(
                Path.GetFileNameWithoutExtension(files[index]),
                samples.Length / 2,
                2,
                rate,
                false
            );
            clip.SetData(samples, 0);
            if (decoded != null)
                Destroy(decoded);
            decoded = clip;
            Play(clip, index, files.Count);
            decoding = Decode(files[next]);
        }

        Task<float[]> Decode(string path)
        {
            var at = rate;
            return Task.Run(() => NativeLib.Decode(path, at));
        }

        void Play(AudioClip clip, int index, int count)
        {
            source.clip = clip;
            source.Play();
            playing = true;
            endsAt = Time.unscaledTime + clip.length;
            Debug.Log($"amethyst: music playing {clip.name} ({index + 1}/{count})");
        }

        void OnDestroy()
        {
            if (current == this)
                current = null;
            if (decoded != null)
                Destroy(decoded);
        }
    }
}
