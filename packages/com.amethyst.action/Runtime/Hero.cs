using UnityEngine;
using UnityEngine.InputSystem;

namespace Amethyst.Action
{
    /// <summary>The player character: WASD or arrows move it, Space dashes (<see cref="ActionFlags"/>).</summary>
    public sealed class Hero : MonoBehaviour
    {
        const float DashSeconds = 0.14f;
        const float DashCooldown = 0.6f;

        Vector3 velocity;
        Vector3 facing = Vector3.forward;
        float dashEnds;
        float dashReady;

        public static Hero Create()
        {
            var look = ActionFlags.Look.Value;
            var shape = look switch
            {
                HeroLook.Capsule => PrimitiveType.Capsule,
                HeroLook.Orb => PrimitiveType.Sphere,
                _ => PrimitiveType.Cube,
            };
            var go = GameObject.CreatePrimitive(shape);
            go.name = "Hero";
            if (look == HeroLook.Crystal)
                go.transform.localScale = new Vector3(0.7f, 1.6f, 0.7f);
            go.transform.position = new Vector3(0f, look == HeroLook.Orb ? 0.5f : 1f, 0f);
            Arena.Paint(go, new Color(0.95f, 0.78f, 0.3f));
            return go.AddComponent<Hero>();
        }

        void Update()
        {
            var dt = Time.deltaTime;
            var input = ReadInput();
            if (input.sqrMagnitude > 0.01f)
                facing = input.normalized;
            var target = input * ActionFlags.Speed.Value;
            if (Time.time < dashEnds)
                target = facing * ActionFlags.Speed.Value * 3.5f;
            velocity = ActionFlags.Move.Value switch
            {
                MoveModel.Floaty => Vector3.Lerp(velocity, target, 1f - Mathf.Exp(-3f * dt)),
                MoveModel.Momentum => Vector3.MoveTowards(velocity, target, 18f * dt),
                _ => target,
            };
            if (
                ActionFlags.Dash.Value
                && Pressed(Keyboard.current?.spaceKey)
                && Time.time >= dashReady
            )
            {
                dashEnds = Time.time + DashSeconds;
                dashReady = Time.time + DashCooldown;
            }
            var p = transform.position + velocity * dt;
            var limit = Arena.HalfSize - 1f;
            transform.position = new Vector3(
                Mathf.Clamp(p.x, -limit, limit),
                p.y,
                Mathf.Clamp(p.z, -limit, limit)
            );
            if (ActionFlags.Look.Value == HeroLook.Crystal)
                transform.Rotate(0f, 90f * dt, 0f, Space.World);
            else
                transform.rotation = Quaternion.LookRotation(facing);
        }

        static Vector3 ReadInput()
        {
            var k = Keyboard.current;
            if (k == null)
                return Vector3.zero;
            var x =
                (Held(k.dKey) || Held(k.rightArrowKey) ? 1f : 0f)
                - (Held(k.aKey) || Held(k.leftArrowKey) ? 1f : 0f);
            var z =
                (Held(k.wKey) || Held(k.upArrowKey) ? 1f : 0f)
                - (Held(k.sKey) || Held(k.downArrowKey) ? 1f : 0f);
            return Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
        }

        static bool Held(UnityEngine.InputSystem.Controls.KeyControl key) =>
            key != null && key.isPressed;

        static bool Pressed(UnityEngine.InputSystem.Controls.KeyControl key) =>
            key != null && key.wasPressedThisFrame;
    }
}
