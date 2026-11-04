using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Num = System.Numerics;

namespace Amethyst.Action
{
    /// <summary>
    /// The player character (docs/game-design.md, Controls and camera; The hero's kit): WASD runs,
    /// Space dodges, and <see cref="ActionFlags.Aim"/> decides where the hero faces and aims. The
    /// Synty knight when the project has a <see cref="HeroKit"/>, else a capsule.
    /// </summary>
    public sealed class Hero : MonoBehaviour
    {
        const float SnapRange = 10f;

        readonly InputBuffer buffer = new InputBuffer(0.15f);
        readonly Dash dash = new Dash();
        HeroAnimator animator;
        Vector3 facing = Vector3.back;
        int demoDashes;

        /// <summary>The ground point under the mouse, or null without one.</summary>
        public Vector3? Cursor { get; private set; }

        public Vector3 Facing => facing;

        public bool Dashing => dash.Active(Time.time);

        /// <summary>Dash invulnerability: from the dash's first frame through most of it.</summary>
        public bool Invulnerable => dash.IsInvulnerable(Time.time);

        public static Hero Create()
        {
            var go = new GameObject("Hero");
            var hero = go.AddComponent<Hero>();
            var kit = HeroKit.Load();
            if (kit != null)
                hero.animator = HeroAnimator.Dress(go.transform, kit);
            else
                StandIn(go.transform);
            return hero;
        }

        static void StandIn(Transform parent)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Stand-in";
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(parent, false);
            body.transform.localPosition = Vector3.up;
            body.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
            Arena.Paint(body, new Color(0.95f, 0.95f, 0.95f));
        }

        /// <summary>
        /// Where an attack goes: toward the cursor, snapped to the nearest of
        /// <paramref name="targets"/> within <see cref="ActionFlags.SnapAngle"/>, or where the hero
        /// faces under movement aim.
        /// </summary>
        public Vector3 Aim(IReadOnlyList<Vector3> targets)
        {
            if (ActionFlags.Aim.Value != AimMode.Cursor || Cursor == null)
                return facing;
            var origin = Flat(transform.position);
            var points = new Num.Vector2[targets.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = Flat(targets[i]);
            var aim = AimSnap.Snap(
                origin,
                Flat(Cursor.Value) - origin,
                points,
                ActionFlags.SnapAngle.Value,
                SnapRange
            );
            return aim.LengthSquared() > 1e-6f ? Ground(aim) : facing;
        }

        void Update()
        {
            var now = Time.time;
            var dt = Time.deltaTime;
            var input = ActionFlags.Demo.Value ? Demo(now) : Devices();
            Cursor = input.Cursor;
            var cursorAim = ActionFlags.Aim.Value == AimMode.Cursor;

            buffer.Window = ActionFlags.Buffer.Value / 60f;
            dash.Distance = ActionFlags.DashDistance.Value;
            dash.Duration = Mathf.Max(1, ActionFlags.DashFrames.Value) / 60f;
            dash.Cooldown = ActionFlags.DashCooldown.Value;
            // A dash starts with the frame it was pressed in, so that frame already moves.
            var frameStart = now - dt;
            if (input.Dash)
                buffer.Press(HeroAction.Dash, frameStart);
            if (dash.Ready(frameStart) && buffer.Take(HeroAction.Dash, frameStart))
            {
                var toward = input.Move;
                if (toward.sqrMagnitude < 0.01f)
                    toward =
                        cursorAim && Cursor != null ? Cursor.Value - transform.position : facing;
                dash.TryStart(Flat(toward), frameStart);
                var direction = Ground(dash.Direction);
                if (!cursorAim)
                    facing = direction;
                animator?.Dodge(Quaternion.Inverse(Quaternion.LookRotation(facing)) * direction);
            }

            var step = Ground(dash.Step(frameStart, now));
            if (!dash.Active(now))
                step += input.Move * (ActionFlags.Speed.Value * dt);
            var p = transform.position + step;
            var limit = Arena.HalfSize - 1f;
            transform.position = new Vector3(
                Mathf.Clamp(p.x, -limit, limit),
                0f,
                Mathf.Clamp(p.z, -limit, limit)
            );

            if (!dash.Active(now))
            {
                var look = input.Move;
                if (cursorAim && Cursor != null)
                {
                    var toCursor = Cursor.Value - transform.position;
                    toCursor.y = 0f;
                    if (toCursor.sqrMagnitude > 0.09f)
                        look = toCursor;
                }
                if (look.sqrMagnitude > 0.01f)
                    facing = look.normalized;
            }
            transform.rotation = Quaternion.LookRotation(facing);

            var run = dash.Active(now) ? Vector3.zero : input.Move * ActionFlags.Speed.Value;
            animator?.Tick(transform.InverseTransformDirection(run), dash.Active(now), dt);
        }

        void OnDestroy() => animator?.Dispose();

        struct Intent
        {
            public Vector3 Move;
            public Vector3? Cursor;
            public bool Dash;
        }

        static Intent Devices()
        {
            var intent = new Intent();
            var k = Keyboard.current;
            if (k != null)
            {
                var x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                var z = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                intent.Move = Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
                intent.Dash = k.spaceKey.wasPressedThisFrame;
            }
            // Batch runs have no pointer, only a virtual mouse parked in a corner.
            var mouse = Application.isBatchMode ? null : Mouse.current;
            var camera = Camera.main;
            if (mouse != null && camera != null)
            {
                var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var hit))
                    intent.Cursor = ray.GetPoint(hit);
            }
            return intent;
        }

        // A deterministic loop for headless runs and shots: still for a second, then a circle
        // with the cursor orbiting the other way, and a dash every 1.25 s.
        Intent Demo(float now)
        {
            var intent = new Intent();
            var spin = now * 0.6f;
            intent.Cursor =
                transform.position + new Vector3(Mathf.Cos(-spin), 0f, Mathf.Sin(-spin)) * 5f;
            if (now < 1f)
                return intent;
            var angle = (now - 1f) * 1.4f;
            intent.Move = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var dashes = Mathf.FloorToInt((now - 1f) / 1.25f);
            intent.Dash = dashes > demoDashes;
            demoDashes = dashes;
            return intent;
        }

        static Num.Vector2 Flat(Vector3 v) => new Num.Vector2(v.x, v.z);

        static Vector3 Ground(Num.Vector2 v) => new Vector3(v.X, 0f, v.Y);
    }
}
