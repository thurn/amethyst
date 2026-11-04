namespace Amethyst.Action
{
    /// <summary>What the player can ask the hero to do.</summary>
    public enum HeroAction
    {
        None,
        Dash,
        Attack,
        Special,
    }

    /// <summary>
    /// Holds a press made a little too early until it becomes legal (feel §2.3): one press, the
    /// latest, for <see cref="Window"/> seconds.
    /// </summary>
    public sealed class InputBuffer
    {
        HeroAction held;
        float pressedAt;

        public InputBuffer(float window) => Window = window;

        public float Window { get; set; }

        /// <summary>Records a press, replacing whatever was held: the last input wins.</summary>
        public void Press(HeroAction action, float now)
        {
            held = action;
            pressedAt = now;
        }

        /// <summary>The held press if it is still fresh, else None.</summary>
        public HeroAction Peek(float now) => now - pressedAt > Window ? HeroAction.None : held;

        /// <summary>True, once, if <paramref name="action"/> is held and still fresh.</summary>
        public bool Take(HeroAction action, float now)
        {
            if (action == HeroAction.None || Peek(now) != action)
                return false;
            held = HeroAction.None;
            return true;
        }

        /// <summary>Drops the held press, as hit-stun does.</summary>
        public void Clear() => held = HeroAction.None;
    }
}
