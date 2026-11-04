using Amethyst.Action;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amethyst.Feel
{
    /// <summary>
    /// The feel room (docs/game-design.md, Build order step 1): the knight's movement, dash, aim
    /// and camera, with a variant per aim mode and per combat mode's camera
    /// (StreamingAssets/variants/).
    /// </summary>
    static class FeelEntry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Enter()
        {
            Build(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            SceneManager.sceneLoaded += Build;
        }

        // A variant switch reloads the scene, which must be rebuilt.
        static void Build(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindAnyObjectByType<Hero>() == null)
                Arena.Build();
        }
    }
}
