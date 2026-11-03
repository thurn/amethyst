using UnityEngine;

namespace Amethyst.Action
{
    /// <summary>Follows a target from the <see cref="ActionFlags.View"/> angle.</summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        Transform target;

        public static Camera Create(Transform target)
        {
            var go = new GameObject("Camera") { tag = "MainCamera" };
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.04f, 0.08f);
            camera.fieldOfView = 50f;
            go.AddComponent<AudioListener>();
            var follow = go.AddComponent<FollowCamera>();
            follow.target = target;
            follow.Snap(1f);
            return camera;
        }

        void LateUpdate() => Snap(1f - Mathf.Exp(-6f * Time.deltaTime));

        void Snap(float t)
        {
            var direction = ActionFlags.View.Value switch
            {
                CameraView.Chase => new Vector3(0f, 0.38f, -0.92f),
                CameraView.Side => new Vector3(0.92f, 0.38f, 0f),
                _ => new Vector3(0f, 0.8f, -0.6f),
            };
            var goal = target.position + direction.normalized * ActionFlags.Distance.Value;
            transform.position = Vector3.Lerp(transform.position, goal, t);
            transform.LookAt(target.position + Vector3.up * 0.5f);
        }
    }
}
