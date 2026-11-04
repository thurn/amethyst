namespace Amethyst.Action
{
    /// <summary>What the player can ask the hero to do; attacks join as they arrive.</summary>
    public enum HeroAction
    {
        None,
        Dash,
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

        /// <summary>True, once, if <paramref name="action"/> is held and still fresh.</summary>
        public bool Take(HeroAction action, float now)
        {
            if (held != action || now - pressedAt > Window)
                return false;
            held = HeroAction.None;
            return true;
        }

        /// <summary>Drops the held press, as hit-stun does.</summary>
        public void Clear() => held = HeroAction.None;
    }
}
