using Amethyst.Action;
using UnityEngine;

namespace Amethyst.Game
{
    /// <summary>The game: every package together, from the empty Main scene.</summary>
    static class GameEntry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Enter() => Arena.Build();
    }
}
