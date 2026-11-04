using UnityEngine;

namespace Amethyst.Action
{
    /// <summary>
    /// The fixed 3/4 top-down camera (docs/game-design.md, Controls and camera): it follows the
    /// hero from <see cref="ActionFlags.Distance"/>, leans toward the cursor by up to
    /// <see cref="ActionFlags.Lean"/> and never rotates. A fight may set its own
    /// <see cref="Distance"/> and shake it by <see cref="Shake"/>.
    /// </summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        const float Pitch = 55f;
        const float LeanShare = 0.25f;
        const float Follow = 10f;
        static readonly Quaternion Angle = Quaternion.Euler(Pitch, 0f, 0f);

        Hero hero;
        Vector3 focus;

        /// <summary>The distance from the hero, m; negative for <see cref="ActionFlags.Distance"/>'s.</summary>
        public float Distance { get; set; } = -1f;

        /// <summary>An offset this frame's shake adds, m.</summary>
        public Vector3 Shake { get; set; }

        public static Camera Create(Hero hero)
        {
            var go = new GameObject("Camera") { tag = "MainCamera" };
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.04f, 0.08f);
            camera.fieldOfView = 40f;
            go.AddComponent<AudioListener>();
            var follow = go.AddComponent<FollowCamera>();
            follow.hero = hero;
            follow.focus = hero.transform.position;
            follow.Place();
            return camera;
        }

        void LateUpdate()
        {
            var goal = hero.transform.position;
            if (hero.Cursor != null)
                goal += Vector3.ClampMagnitude(
                    (hero.Cursor.Value - goal) * LeanShare,
                    ActionFlags.Lean.Value
                );
            focus = Vector3.Lerp(focus, goal, 1f - Mathf.Exp(-Follow * Time.deltaTime));
            Place();
        }

        void Place()
        {
            transform.rotation = Angle;
            var distance = Distance >= 0f ? Distance : ActionFlags.Distance.Value;
            transform.position =
                focus + Vector3.up * 0.8f - Angle * Vector3.forward * distance + Shake;
        }
    }
}
