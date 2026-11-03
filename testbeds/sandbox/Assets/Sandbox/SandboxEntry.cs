using Amethyst.Action;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amethyst.Sandbox
{
    /// <summary>
    /// The template testbed: the action baseline with three movement-and-look variants
    /// (StreamingAssets/variants/). `just testbed-new NAME` forks it.
    /// </summary>
    static class SandboxEntry
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
