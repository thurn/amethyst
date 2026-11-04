using UnityEngine;

namespace Amethyst.Action
{
    /// <summary>
    /// The fixed 3/4 top-down camera (docs/game-design.md, Controls and camera): it follows the
    /// hero from <see cref="ActionFlags.Distance"/>, leans toward the cursor by up to
    /// <see cref="ActionFlags.Lean"/> and never rotates.
    /// </summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        const float Pitch = 55f;
        const float LeanShare = 0.25f;
        const float Follow = 10f;
        static readonly Quaternion Angle = Quaternion.Euler(Pitch, 0f, 0f);

        Hero hero;
        Vector3 focus;

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
            transform.position =
                focus + Vector3.up * 0.8f - Angle * Vector3.forward * ActionFlags.Distance.Value;
        }
    }
}
