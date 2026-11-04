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
    /// latest, for <see cref="Window"/> seconds. Counts what became of every press (feel §11.2):
    /// honored on its own frame, buffered and later honored, or dropped (replaced or expired).
    /// </summary>
    public sealed class InputBuffer
    {
        HeroAction held;
        float pressedAt;

        public InputBuffer(float window) => Window = window;

        public float Window { get; set; }

        public int Honored { get; private set; }
        public int Buffered { get; private set; }
        public int Dropped { get; private set; }

        /// <summary>Records a press, replacing whatever was held: the last input wins.</summary>
        public void Press(HeroAction action, float now)
        {
            Expire(now);
            if (held != HeroAction.None)
                Dropped++;
            held = action;
            pressedAt = now;
        }

        /// <summary>Drops the held press if it went stale before <paramref name="now"/>.</summary>
        public void Expire(float now)
        {
            if (held != HeroAction.None && now - pressedAt > Window)
            {
                held = HeroAction.None;
                Dropped++;
            }
        }

        /// <summary>The held press if it is still fresh, else None.</summary>
        public HeroAction Peek(float now) => now - pressedAt > Window ? HeroAction.None : held;

        /// <summary>True, once, if <paramref name="action"/> is held and still fresh.</summary>
        public bool Take(HeroAction action, float now)
        {
            if (action == HeroAction.None || Peek(now) != action)
                return false;
            held = HeroAction.None;
            if (now - pressedAt <= 1e-4f)
                Honored++;
            else
                Buffered++;
            return true;
        }

        /// <summary>Drops the held press, as hit-stun does.</summary>
        public void Clear()
        {
            if (held != HeroAction.None)
                Dropped++;
            held = HeroAction.None;
        }
    }
}
