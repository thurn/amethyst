using Amethyst.Combat;
using Amethyst.Look;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amethyst.Feel
{
    /// <summary>
    /// The feel room (docs/game-design.md, Build order step 1): the knight's kit against skeletons
    /// in either combat mode (<see cref="CombatFlags.Mode"/>), drawn in the neon edge look, with a
    /// variant per mode and horde density (StreamingAssets/variants/).
    /// </summary>
    static class FeelEntry
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
