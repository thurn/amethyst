using System;
using System.Numerics;

namespace Amethyst.Action
{
    /// <summary>
    /// The dodge: a straight move of <see cref="Distance"/> m over <see cref="Duration"/> s at a
    /// constant speed, invulnerable from its first frame for <see cref="Invulnerable"/> of its
    /// length, then <see cref="Cooldown"/> s before the next (one charge). A Lunge cuts it short.
    /// </summary>
    public sealed class Dash
    {
        // Long ago, but finite, so the arithmetic below stays finite before the first dash.
        float start = -1e6f;
        float end = -1e6f;

        public float Distance { get; set; } = 3.5f;
        public float Duration { get; set; } = 10f / 60f;
        public float Cooldown { get; set; } = 0.35f;
        public float Invulnerable { get; set; } = 0.8f;

        /// <summary>The unit ground direction of the current or last dash.</summary>
        public Vector2 Direction { get; private set; } = Vector2.UnitY;

        public bool Active(float now) => now >= start && now < end;

        public bool IsInvulnerable(float now) =>
            now >= start && now < Math.Min(end, start + Duration * Invulnerable);

        public bool Ready(float now) => now >= end + Cooldown;

        /// <summary>Seconds until the next dash may start, 0 when it may now.</summary>
        public float ReadyIn(float now) => Math.Max(0f, end + Cooldown - now);

        /// <summary>Seconds since the current or last dash ended, negative while it runs.</summary>
        public float SinceEnd(float now) => now - end;

        /// <summary>Starts a dash if one is ready; a zero direction keeps the last one.</summary>
        public bool TryStart(Vector2 direction, float now)
        {
            if (!Ready(now))
                return false;
            if (direction.LengthSquared() > 1e-6f)
                Direction = Vector2.Normalize(direction);
            start = now;
            end = now + Duration;
            return true;
        }

        /// <summary>Cuts a running dash short: movement and invulnerability stop at
        /// <paramref name="now"/>, and the cooldown starts there.</summary>
        public void End(float now)
        {
            if (Active(now))
                end = now;
        }

        /// <summary>
        /// The displacement the dash makes between two times, so frames of any length add up to
        /// exactly <see cref="Distance"/>.
        /// </summary>
        public Vector2 Step(float from, float to)
        {
            var length = end - start;
            var a = Math.Clamp(from - start, 0f, length);
            var b = Math.Clamp(to - start, 0f, length);
            return Direction * (Distance * (b - a) / Duration);
        }
    }
}
