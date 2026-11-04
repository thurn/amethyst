using Amethyst.Action;
using Amethyst.Combat;
using Amethyst.Flags;
using Amethyst.Look;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amethyst.Chain
{
    public enum ChainCrowd
    {
        /// <summary>M1's rooms: skeletons only.</summary>
        Skeletons,

        /// <summary>The key family's fodder mixed: skeletons, slimes, demons and bats.</summary>
        Mixed,

        /// <summary>Skeletons with the accent family's goblins and mushrooms among them.</summary>
        Accent,
    }

    public static class ChainFlags
    {
        public static readonly Flag<ChainCrowd> Crowd = Flag.Enum(
            "chain.crowd",
            ChainCrowd.Skeletons,
            "What the room spawns: skeletons, the key family's fodder mixed, or skeletons with the accent family's goblins and mushrooms"
        );
    }

    /// <summary>
    /// The chain (docs/game-design.md, Build order step 2): the roster, elites, hazards and
    /// connected rooms as M2 builds them, on the combat package's fight, with a variant per
    /// combat mode (StreamingAssets/variants/).
    /// </summary>
    static class ChainEntry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Enter()
        {
            Grade.Use();
            Build(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            SceneManager.sceneLoaded += Build;
        }

        // A variant switch or the hero's death reloads the scene, which must be rebuilt.
        static void Build(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindAnyObjectByType<Fight>() == null)
                Fight.Begin(
                    ChainFlags.Crowd.Value switch
                    {
                        ChainCrowd.Mixed => SpawnList.KeyFodder,
                        ChainCrowd.Accent => SpawnList.Accent,
                        _ => SpawnList.Skeletons,
                    }
                );
        }
    }
}
