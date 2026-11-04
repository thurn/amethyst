using Amethyst.Flags;
using Amethyst.Look;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Amethyst.LookTestbed
{
    public enum LookScene
    {
        Crowd,
        Review,
    }

    public enum LookMesh
    {
        Recipe,
        Vendor,
    }

    public static class LookFlags
    {
        public static readonly Flag<LookScene> Scene = Flag.Enum(
            "look.scene",
            LookScene.Crowd,
            "The crowd round the running hero, or the wireframe meshes posed up close"
        );

        public static readonly Flag<LookMesh> Mesh = Flag.Enum(
            "look.mesh",
            LookMesh.Recipe,
            "Figures' meshes: their wireframe recipes' where they have one, or the vendor's"
        );

        public static readonly Flag<float> View = Flag.Float(
            "look.view",
            36f,
            "Ground the camera sees across, m (the trailer's 36)"
        );

        public static readonly Flag<float> Grid = Flag.Float(
            "look.grid",
            1f,
            "The floor grid's brightness, times its resting light"
        );

        public static readonly Flag<int> Crowd = Flag.Int(
            "look.crowd",
            320,
            "Crowd figures around the hero"
        );
    }

    /// <summary>
    /// The neon edge look testbed: a white knight running a small circle on the void grid, with a
    /// few hundred hue-coded figures from Playlist Survivors' roster circling it in packs, a few
    /// wearing white status overlays, framed as the trailer was. Or (look.scene) the wireframe
    /// meshes up close in a few poses.
    /// </summary>
    static class LookEntry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Enter()
        {
            Grade.Use();
            Build(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            SceneManager.sceneLoaded += Build;
        }

        // A variant switch reloads the scene, which must be rebuilt.
        static void Build(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindAnyObjectByType<Stage>() != null)
                return;
            var palette = Palette.Default;
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.fieldOfView = Stage.Fov;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = EdgeLook.Linear(palette.Void).gamma;
            cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing =
                true;
            var floor = GridFloor.Create(palette.Cobalt, LookFlags.Grid.Value);

            HeroLook hero = null;
            var figure = LookFigure.Load("hero");
            if (figure != null)
                hero = HeroLook.Spawn(figure, palette, LookFlags.Mesh.Value == LookMesh.Vendor);
            else
                Debug.LogWarning("look: no baked hero (its vendor files are missing)");

            var stage = new GameObject("Stage").AddComponent<Stage>();
            stage.Init(cam, floor, hero, figure, palette);
        }
    }
}
