using UnityEngine;

namespace Amethyst.Native
{
    /// <summary>
    /// A project's default music, as <c>Resources/Soundtrack.asset</c>: streamed clips that
    /// <see cref="MusicPlayer"/> plays unless <c>--file</c> names a playlist.
    /// </summary>
    [CreateAssetMenu(menuName = "Amethyst/Soundtrack")]
    public sealed class Soundtrack : ScriptableObject
    {
        public AudioClip[] Tracks;
    }
}
