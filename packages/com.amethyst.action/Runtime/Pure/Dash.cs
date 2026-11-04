using System;
using System.Numerics;

namespace Amethyst.Action
{
    /// <summary>
    /// The dodge: a straight move of <see cref="Distance"/> m over <see cref="Duration"/> s at a
    /// constant speed, invulnerable from its first frame for <see cref="Invulnerable"/> of its
    /// length, then <see cref="Cooldown"/> s before the next (one charge).
    /// </summary>
    public sealed class Dash
    {
        float start = float.NegativeInfinity;

        public float Distance { get; set; } = 3.5f;
        public float Duration { get; set; } = 10f / 60f;
        public float Cooldown { get; set; } = 0.35f;
        public float Invulnerable { get; set; } = 0.8f;

        /// <summary>The unit ground direction of the current or last dash.</summary>
        public Vector2 Direction { get; private set; } = Vector2.UnitY;

        public bool Active(float now) => now >= start && now < start + Duration;

        public bool IsInvulnerable(float now) =>
            now >= start && now < start + Duration * Invulnerable;

        public bool Ready(float now) => now >= start + Duration + Cooldown;

        /// <summary>Seconds since the current or last dash started.</summary>
        public float Elapsed(float now) => now - start;

        /// <summary>Starts a dash if one is ready; a zero direction keeps the last one.</summary>
        public bool TryStart(Vector2 direction, float now)
        {
            if (!Ready(now))
                return false;
            if (direction.LengthSquared() > 1e-6f)
                Direction = Vector2.Normalize(direction);
            start = now;
            return true;
        }

        /// <summary>
        /// The displacement the dash makes between two times, so frames of any length add up to
        /// exactly <see cref="Distance"/>.
        /// </summary>
        public Vector2 Step(float from, float to)
        {
            var a = Math.Clamp(from - start, 0f, Duration);
            var b = Math.Clamp(to - start, 0f, Duration);
            return Direction * (Distance * (b - a) / Duration);
        }
    }
}
