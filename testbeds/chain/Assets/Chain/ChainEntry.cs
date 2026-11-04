using Amethyst.Combat;
using Amethyst.Look;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amethyst.Chain
{
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
                Fight.Begin();
        }
    }
}
