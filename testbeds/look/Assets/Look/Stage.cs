using Amethyst.Look;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Amethyst.LookTestbed
{
    /// <summary>Runs the scene: the hero's circle (or its spot in the review), the camera, the
    /// crowd, the frame log.</summary>
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
        ReviewDemo review;
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
            bool vendor = LookFlags.Mesh.Value == LookMesh.Vendor;
            if (lib != null && LookFlags.Scene.Value == LookScene.Review)
                review = new ReviewDemo(lib, palette, vendor);
            else if (lib != null)
                crowd = new CrowdDemo(lib, palette, LookFlags.Crowd.Value, vendor);
            else
                Debug.LogWarning("look: no baked crowd types (their vendor files are missing)");
        }

        void Update()
        {
            float t = Time.time;
            var heroPos = Vector3.zero;
            var target = Vector3.zero;
            if (review != null)
            {
                // The hero runs on the spot at the end of the knights' row.
                target = new Vector3(ReviewDemo.Spacing * 0.5f, 0, 0);
                hero?.transform.SetPositionAndRotation(
                    ReviewDemo.Spot(0, 4),
                    Quaternion.Euler(0, (Mathf.PI - 0.6f) * Mathf.Rad2Deg, 0)
                );
            }
            else if (hero != null)
            {
                float a = t * HeroSpeed / HeroRadius;
                heroPos = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * HeroRadius;
                hero.transform.SetPositionAndRotation(
                    heroPos,
                    Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)))
                );
                target = heroPos;
            }
            var rot = Quaternion.Euler(Pitch, 0, 0);
            // Far enough that the ground at the centre is look.view across.
            float distance =
                LookFlags.View.Value * 0.5f / (Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * cam.aspect);
            cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * distance, rot);
            floor.Follow(cam);
            crowd?.Update(t);
            review?.Update(t);
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
                    + $"{(crowd?.Crowd ?? review?.Crowd)?.Written ?? 0} figures, "
                    + $"{(crowd?.Crowd ?? review?.Crowd)?.OnScreen ?? 0} on screen"
            );
            frames = frameTime = 0;
            logAt = Time.time + 2f;
        }

        void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
            crowd?.Dispose();
            review?.Dispose();
        }
    }
}
