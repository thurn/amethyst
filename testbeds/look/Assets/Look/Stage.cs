using Amethyst.Look;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Amethyst.LookTestbed
{
    /// <summary>Runs the scene: the hero's circle, the camera, the crowd, the frame log.</summary>
    sealed class Stage : MonoBehaviour
    {
        // The trailer's camera: 55° down, a 30° vertical field of view.
        public const float Pitch = 55f,
            Fov = 30f;

        const float HeroRadius = 3f,
            HeroSpeed = 3.2f;

        Camera cam;
        GridFloor floor;
        HeroLook hero;
        PlayableGraph graph;
        CrowdDemo crowd;
        float frames,
            frameTime,
            logAt = 2f;

        public void Init(Camera c, GridFloor f, HeroLook h, LookFigure figure, Palette palette)
        {
            cam = c;
            floor = f;
            hero = h;
            if (hero != null && figure.Clip("run") is AnimationClip run)
            {
                var animator = hero.GetComponentInChildren<Animator>();
                AnimationPlayableUtilities.PlayClip(animator, run, out graph);
            }
            var lib = MonsterLibrary.Load();
            if (lib != null)
                crowd = new CrowdDemo(lib, palette, LookFlags.Crowd.Value);
            else
                Debug.LogWarning("look: no baked crowd types (their vendor files are missing)");
        }

        void Update()
        {
            float t = Time.time;
            var heroPos = Vector3.zero;
            if (hero != null)
            {
                float a = t * HeroSpeed / HeroRadius;
                heroPos = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * HeroRadius;
                hero.transform.SetPositionAndRotation(
                    heroPos,
                    Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)))
                );
            }
            var rot = Quaternion.Euler(Pitch, 0, 0);
            // Far enough that the ground at the centre is look.view across.
            float distance =
                LookFlags.View.Value * 0.5f / (Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * cam.aspect);
            cam.transform.SetPositionAndRotation(heroPos - rot * Vector3.forward * distance, rot);
            floor.Follow(cam);
            crowd?.Update(t);
            Log();
        }

        void Log()
        {
            frames++;
            frameTime += Time.unscaledDeltaTime;
            if (Time.time < logAt)
                return;
            Debug.Log(
                $"look: frame {1000f * frameTime / frames:F2} ms over {frames:F0} frames; "
                    + $"{crowd?.Crowd.Written ?? 0} figures, {crowd?.Crowd.OnScreen ?? 0} on screen"
            );
            frames = frameTime = 0;
            logAt = Time.time + 2f;
        }

        void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
            crowd?.Dispose();
        }
    }
}
