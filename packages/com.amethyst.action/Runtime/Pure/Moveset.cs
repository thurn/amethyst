using System;
using System.Numerics;

namespace Amethyst.Action
{
    /// <summary>The hero's sword moves (docs/game-design.md, The hero's kit).</summary>
    public enum Move
    {
        None,
        CleaveA,
        CleaveB,
        CleaveC,
        Lunge,
        LeapSlam,
    }

    /// <summary>
    /// When the hero may act (feel §2.3, §2.4): presses wait in the <see cref="Buffer"/> until
    /// they become legal, the last press winning. A move has a startup that ends on its contact
    /// frame, then a recovery. The dash cancels any recovery, but never a startup, so a Leap Slam
    /// commits until it lands. Attacking during a dash, or just after one, is the Lunge, which
    /// ends the dash; otherwise attacks chain Cleave A, B, C no faster than
    /// <see cref="Cadence"/>. The special waits for its cooldown and for the dash to end.
    /// </summary>
    public sealed class Moveset
    {
        // Contact times are sums of frame lengths, so let a frame boundary round either way.
        const float Slop = 1e-4f;
        const float Never = -1e6f;

        readonly float[] startup = new float[6];
        readonly float[] recovery = new float[6];
        Move comboNext = Move.CleaveA;
        float comboUntil = Never;
        float slamReady = Never;

        public Moveset(float buffer) => Buffer = new InputBuffer(buffer);

        public InputBuffer Buffer { get; }

        public Dash Dash { get; } = new Dash();

        /// <summary>Seconds from one Cleave's start to the next's at the earliest.</summary>
        public float Cadence { get; set; } = 0.25f;

        /// <summary>Seconds after a Cleave ends in which the next attack still continues the combo.</summary>
        public float ComboGrace { get; set; } = 0.15f;

        /// <summary>Seconds after a dash ends in which an attack is still the Lunge.</summary>
        public float LungeWindow { get; set; } = 0.15f;

        public float SlamCooldown { get; set; } = 2f;

        /// <summary>The move playing, from its start through its recovery.</summary>
        public Move Current { get; private set; }

        /// <summary>When <see cref="Current"/> started.</summary>
        public float Started { get; private set; }

        /// <summary>When <see cref="Current"/> lands its hit.</summary>
        public float ContactAt => Started + startup[(int)Current];

        /// <summary>When <see cref="Current"/>'s recovery ends.</summary>
        public float EndsAt => ContactAt + recovery[(int)Current];

        /// <summary>Sets a move's startup (press to contact) and recovery (contact to end), s.</summary>
        public void SetTiming(Move move, float startupSeconds, float recoverySeconds)
        {
            startup[(int)move] = startupSeconds;
            recovery[(int)move] = recoverySeconds;
        }

        public bool Recovering(float now) => Current != Move.None && now >= ContactAt - Slop;

        public float SlamCooldownLeft(float now) => Math.Max(0f, slamReady - now);

        public void Press(HeroAction action, float now) => Buffer.Press(action, now);

        /// <summary>
        /// Starts the held press if it has become legal at <paramref name="now"/> and returns it,
        /// else None. A dash goes toward <paramref name="dashToward"/> (zero keeps the last
        /// direction); a started attack is <see cref="Current"/>.
        /// </summary>
        public HeroAction Step(float now, Vector2 dashToward)
        {
            if (Current != Move.None && now >= EndsAt - Slop)
                Current = Move.None;
            Buffer.Expire(now);
            var held = Buffer.Peek(now);
            switch (held)
            {
                case HeroAction.Dash:
                    if (!(Current == Move.None || Recovering(now)) || !Dash.Ready(now))
                        return HeroAction.None;
                    Current = Move.None;
                    comboUntil = Never;
                    Dash.TryStart(dashToward, now);
                    break;
                case HeroAction.Attack:
                    var lunge =
                        Dash.Active(now)
                        || (Current == Move.None && Dash.SinceEnd(now) <= LungeWindow);
                    if (lunge)
                    {
                        Dash.End(now);
                        Begin(Move.Lunge, now);
                    }
                    else if (CanChain(now))
                        Begin(now <= comboUntil ? comboNext : Move.CleaveA, now);
                    else
                        return HeroAction.None;
                    break;
                case HeroAction.Special:
                    if (Dash.Active(now) || !CanChain(now) || now < slamReady)
                        return HeroAction.None;
                    Begin(Move.LeapSlam, now);
                    slamReady = now + SlamCooldown;
                    break;
                default:
                    return HeroAction.None;
            }
            Buffer.Take(held, now);
            return held;
        }

        /// <summary>Whether <see cref="Current"/>'s contact falls in (from, to].</summary>
        public bool Contacts(float from, float to) =>
            Current != Move.None && ContactAt > from + Slop && ContactAt <= to + Slop;

        /// <summary>
        /// Whether every press made now would be dropped (feel §2.4's lockout): neither a dash nor
        /// an attack could start within the buffer's window.
        /// </summary>
        public bool Locked(float now)
        {
            var committed = Current != Move.None && !Recovering(now) ? ContactAt - now : 0f;
            var dash = Math.Max(committed, Dash.ReadyIn(now));
            var attack =
                Dash.Active(now) || Current == Move.None ? 0f : Started + Gate(Current) - now;
            return Math.Min(dash, attack) > Buffer.Window + Slop;
        }

        /// <summary>Ends the move if it is recovering, as running out of it does.</summary>
        public bool CancelRecovery(float now)
        {
            if (!Recovering(now))
                return false;
            Current = Move.None;
            return true;
        }

        /// <summary>Drops the move, the held press and the combo, as hit-stun does.</summary>
        public void Interrupt()
        {
            Current = Move.None;
            comboUntil = Never;
            Buffer.Clear();
        }

        bool CanChain(float now)
        {
            return Current == Move.None || now >= Started + Gate(Current) - Slop;
        }

        // How long after a move starts the next attack or special may.
        float Gate(Move move) =>
            IsCleave(move) ? Math.Max(startup[(int)move], Cadence) : startup[(int)move];

        void Begin(Move move, float now)
        {
            Current = move;
            Started = now;
            if (IsCleave(move))
            {
                comboNext = move == Move.CleaveC ? Move.CleaveA : move + 1;
                comboUntil = EndsAt + ComboGrace;
            }
            else
                comboUntil = Never;
        }

        static bool IsCleave(Move move) => move >= Move.CleaveA && move <= Move.CleaveC;
    }
}
