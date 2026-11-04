using System;

namespace Amethyst.Action
{
    /// <summary>
    /// Feel §3.3's trauma model of screen shake: events add trauma (0..1), which decays linearly,
    /// and the shake is its square, so small events barely register and big ones stack. Light hits
    /// add none: only heavy hits and the hero's damage do.
    /// </summary>
    public sealed class Trauma
    {
        public float Value { get; private set; }

        /// <summary>Trauma lost per second.</summary>
        public float Decay { get; set; } = 1.6f;

        public void Add(float amount) => Value = Math.Clamp(Value + amount, 0f, 1f);

        public void Step(float dt) => Value = Math.Max(0f, Value - Decay * dt);

        /// <summary>The shake's strength, 0..1.</summary>
        public float Shake => Value * Value;
    }
}
