using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Num = System.Numerics;

namespace Amethyst.Action
{
    /// <summary>
    /// The player character (docs/game-design.md, Controls and camera; The hero's kit): WASD runs,
    /// Space dodges, left click or F attacks (Cleave, or the Lunge out of a dash), right click or Q
    /// is the Leap Slam, and <see cref="ActionFlags.Aim"/> decides where the hero faces and aims.
    /// It has 100 HP and is invulnerable for a while after any hit, which interrupts it with a Hit
    /// React or, for a heavy one, a Stagger (docs/game-design.md, Health and death).
    /// <see cref="Moveset"/> holds the rules; this plays them. The Synty knight when the project
    /// has a <see cref="HeroKit"/>, else a capsule.
    /// </summary>
    public sealed class Hero : MonoBehaviour
    {
        const float SnapRange = 10f;
        const float Radius = 0.45f;
        const float Frame = 1f / 60f;
        public const int MaxHealth = 100;

        readonly Moveset moves = new Moveset(0.15f);
        HeroAnimator animator;
        HeroKit kit;
        AudioSource voice;
        Vector3 facing = Vector3.back;
        float born;
        float demoTime = -1f;

        Move playing;
        int pressFrame;
        Vector3 moveFrom,
            moveTo;
        float hurtUntil = -1f,
            stunUntil = -1f,
            reactUntil = -1f;
        Vector3 knock;

        /// <summary>Raised on each move's contact frame with what it hits.</summary>
        public event System.Action<Strike> Struck;

        /// <summary>Where the enemies are, for aim snapping; whoever spawns them keeps it current.</summary>
        public IReadOnlyList<Vector3> Targets { get; set; } = System.Array.Empty<Vector3>();

        /// <summary>Where the demo (<see cref="ActionFlags.Demo"/>) walks instead of fighting, when
        /// set: a chain's way on through its open gates.</summary>
        public Vector3? Goal { get; set; }

        /// <summary>The ground point under the mouse, or null without one.</summary>
        public Vector3? Cursor { get; private set; }

        public Vector3 Facing => facing;

        public bool Dashing => moves.Dash.Active(Time.time);

        /// <summary>Dash invulnerability: from the dash's first frame through most of it, until a
        /// Lunge ends it.</summary>
        public bool Invulnerable => moves.Dash.IsInvulnerable(Time.time);

        /// <summary>The sword move playing, or None.</summary>
        public Move Attacking => playing;

        public int Health { get; private set; } = MaxHealth;

        public bool Dead => Health <= 0;

        /// <summary>Whether a blow would land now: not dashing through it, not just hit, alive.</summary>
        public bool Vulnerable => !Dead && !Invulnerable && Time.time >= hurtUntil;

        /// <summary>Whether every press made now would be dropped (feel §2.4's lockout): committed
        /// to a move, or stunned by a hit.</summary>
        public bool Locked => Time.time < stunUntil || moves.Locked(Time.time);

        /// <summary>What became of every press so far (feel §11.2).</summary>
        public InputBuffer Presses => moves.Buffer;

        /// <summary>Raised when a blow takes the hero's last HP.</summary>
        public event System.Action Died;

        /// <summary>The share of the Leap Slam's cooldown still to run, 0 when it is ready.</summary>
        public float SlamCooldown =>
            moves.SlamCooldownLeft(Time.time) / Mathf.Max(moves.SlamCooldown, 1e-3f);

        public static Hero Create()
        {
            var go = new GameObject("Hero");
            var hero = go.AddComponent<Hero>();
            hero.born = Time.time;
            hero.kit = HeroKit.Load();
            if (hero.kit != null)
            {
                hero.animator = HeroAnimator.Dress(go.transform, hero.kit);
                hero.voice = go.AddComponent<AudioSource>();
                hero.voice.playOnAwake = false;
            }
            else
                StandIn(go.transform);
            SwordEffects.CooldownRing.Attach(hero);
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

        /// <summary>
        /// A blow of <paramref name="damage"/> from <paramref name="from"/>, if it lands
        /// (<see cref="Vulnerable"/>): it interrupts whatever the hero does, pushes it away, plays
        /// Hit React (or Stagger when <paramref name="heavy"/>) and makes it invulnerable for
        /// <see cref="ActionFlags.HurtInvulnerable"/> s. False when it didn't land.
        /// </summary>
        public bool Hurt(int damage, Vector3 from, bool heavy)
        {
            if (!Vulnerable)
                return false;
            var now = Time.time;
            Health = Mathf.Max(0, Health - damage);
            hurtUntil = now + ActionFlags.HurtInvulnerable.Value;
            stunUntil = now + (heavy ? 0.35f : 0.15f);
            moves.Interrupt();
            if (playing != Move.None)
                End();
            var away = transform.position - from;
            away.y = 0f;
            knock = away.sqrMagnitude > 1e-4f ? away.normalized * (heavy ? 7f : 4f) : Vector3.zero;
            if (animator != null)
            {
                var length = animator.React(transform.InverseTransformDirection(-away), heavy);
                reactUntil = now + Mathf.Min(length, heavy ? 0.6f : 0.45f);
            }
            Play(kit?.Hurts);
            if (Dead)
                Died?.Invoke();
            return true;
        }

        /// <summary>Holds the hero's pose for <paramref name="frames"/> (its share of a hit's hitstop).</summary>
        public void Freeze(int frames)
        {
            if (frames > 0)
                animator?.Freeze(frames * Frame);
        }

        void Update()
        {
            var now = Time.time;
            var dt = Time.deltaTime;
            if (Dead)
                return;
            var stunned = now < stunUntil;
            var input =
                stunned ? new Intent { Cursor = Cursor }
                : ActionFlags.Demo.Value ? Demo(now - born)
                : Devices();
            Cursor = input.Cursor;
            var cursorAim = ActionFlags.Aim.Value == AimMode.Cursor;
            Configure();

            // An action starts with the frame it was pressed in, so that frame already moves.
            var frameStart = now - dt;
            if (input.Dash)
                moves.Press(HeroAction.Dash, frameStart);
            if (input.Attack)
                moves.Press(HeroAction.Attack, frameStart);
            if (input.Special)
                moves.Press(HeroAction.Special, frameStart);
            if (
                ActionFlags.RunCancel.Value
                && input.Move.sqrMagnitude > 0.01f
                && moves.CancelRecovery(frameStart)
            )
                End();

            var toward = input.Move;
            if (toward.sqrMagnitude < 0.01f)
                toward = cursorAim && Cursor != null ? Cursor.Value - transform.position : facing;
            var started = moves.Step(frameStart, Flat(toward));
            if (started != HeroAction.None)
                reactUntil = -1f;
            if (started == HeroAction.Dash)
            {
                playing = Move.None;
                var direction = Ground(moves.Dash.Direction);
                if (!cursorAim)
                    facing = direction;
                animator?.Dodge(Quaternion.Inverse(Quaternion.LookRotation(facing)) * direction);
            }
            else if (started != HeroAction.None)
                Begin(moves.Current);
            else if (playing != Move.None && moves.Current == Move.None)
                End();

            if (
                reactUntil > 0f
                && now >= reactUntil
                && playing == Move.None
                && !moves.Dash.Active(now)
            )
            {
                reactUntil = -1f;
                animator?.EndAttack(ActionFlags.BlendOut.Value * Frame);
            }

            var dash = moves.Dash;
            var p = transform.position + Ground(dash.Step(frameStart, now)) + knock * dt;
            knock *= Mathf.Exp(-12f * dt);
            if (playing != Move.None)
                p += Carry(frameStart, now);
            else if (!dash.Active(now))
                p += input.Move * (ActionFlags.Speed.Value * dt);
            var at = new Num.Vector2(p.x, p.z);
            Arena.Obstacles.Collide(ref at, Radius, false);
            transform.position = new Vector3(at.X, 0f, at.Y);

            if (playing == Move.None && !dash.Active(now))
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

            if (playing != Move.None && moves.Contacts(frameStart, now))
                Contact();

            var run =
                dash.Active(now) || playing != Move.None
                    ? Vector3.zero
                    : input.Move * ActionFlags.Speed.Value;
            animator?.Tick(transform.InverseTransformDirection(run), dash.Active(now), dt);
        }

        void OnDestroy() => animator?.Dispose();

        /// <summary>How a move plays its clip: which one, its contact, and the flags that trim it.</summary>
        struct Clip
        {
            public int Index;
            public float Contact;
            public float Speed;
            public int Startup;
            public int Recovery;

            /// <summary>Where the clip starts: <see cref="Startup"/> frames before its contact.</summary>
            public float From => Mathf.Max(0f, Contact - Startup * Frame * Speed);

            /// <summary>Seconds from the press to the contact.</summary>
            public float StartupSeconds => (Contact - From) / Speed;
        }

        Clip Spec(Move move)
        {
            switch (move)
            {
                case Move.CleaveA:
                case Move.CleaveB:
                case Move.CleaveC:
                    int i = move - Move.CleaveA;
                    var cut =
                        i == 0 ? kit?.CleaveA
                        : i == 1 ? kit?.CleaveB
                        : kit?.CleaveC;
                    return Make(
                        i,
                        cut,
                        ActionFlags.CleaveSpeed,
                        ActionFlags.CleaveStartup,
                        ActionFlags.CleaveRecovery
                    );
                case Move.Lunge:
                    var stab = ActionFlags.LungeClipFlag.Value == LungeClip.Stab;
                    return Make(
                        stab ? 4 : 3,
                        stab ? kit?.Stab : kit?.Fencing,
                        ActionFlags.LungeSpeed,
                        ActionFlags.LungeStartup,
                        ActionFlags.LungeRecovery
                    );
                default:
                    return Make(
                        5,
                        kit?.Leap,
                        ActionFlags.SlamSpeed,
                        ActionFlags.SlamStartup,
                        ActionFlags.SlamRecovery
                    );
            }
        }

        // Without a kit there are no clips: the stand-in's contact is just its startup.
        static Clip Make(
            int index,
            HeroKit.Attack? attack,
            Flags.Flag<float> speed,
            Flags.Flag<int> startup,
            Flags.Flag<int> recovery
        )
        {
            var frames = Mathf.Max(1, startup.Value);
            return new Clip
            {
                Index = index,
                Speed = Mathf.Max(0.05f, speed.Value),
                Startup = frames,
                Recovery = Mathf.Max(0, recovery.Value),
                Contact = attack?.Contact ?? frames * Frame * Mathf.Max(0.05f, speed.Value),
            };
        }

        void Configure()
        {
            moves.Buffer.Window = ActionFlags.Buffer.Value * Frame;
            moves.Cadence = ActionFlags.CleaveCadence.Value * Frame;
            moves.LungeWindow = ActionFlags.LungeWindow.Value * Frame;
            moves.SlamCooldown = ActionFlags.SlamCooldown.Value;
            var dash = moves.Dash;
            dash.Distance = ActionFlags.DashDistance.Value;
            dash.Duration = Mathf.Max(1, ActionFlags.DashFrames.Value) * Frame;
            dash.Cooldown = ActionFlags.DashCooldown.Value;
            for (var move = Move.CleaveA; move <= Move.LeapSlam; move++)
            {
                var clip = Spec(move);
                moves.SetTiming(move, clip.StartupSeconds, clip.Recovery * Frame);
            }
        }

        void Begin(Move move)
        {
            playing = move;
            pressFrame = Time.frameCount;
            facing = move == Move.Lunge ? Ground(moves.Dash.Direction) : Aim(Targets);
            moveFrom = transform.position;
            moveTo = moveFrom;
            if (move == Move.Lunge)
                moveTo += facing * ActionFlags.LungeTravel.Value;
            else if (move == Move.LeapSlam)
            {
                var range = ActionFlags.SlamRange.Value;
                var leap =
                    ActionFlags.Aim.Value == AimMode.Cursor && Cursor != null
                        ? (Cursor.Value - moveFrom).magnitude
                        : range;
                moveTo += facing * Mathf.Min(leap, range);
            }

            var clip = Spec(move);
            animator?.Attack(clip.Index, clip.From, clip.Speed, ActionFlags.BlendIn.Value * Frame);
            var strike = Sized(move);
            var visual = 1f / Mathf.Max(ActionFlags.Hitbox.Value, 1f);
            var sweep = clip.StartupSeconds;
            switch (move)
            {
                case Move.Lunge:
                    SwordEffects.Streak(
                        moveTo + Vector3.up,
                        facing,
                        strike.Reach * visual,
                        0.22f,
                        sweep
                    );
                    Play(kit?.Swings);
                    break;
                case Move.LeapSlam:
                    break;
                default:
                    // A cuts from the right, B back from the left, C wide from the right again.
                    SwordEffects.Crescent(
                        moveFrom + Vector3.up,
                        facing,
                        strike.Reach * visual,
                        strike.Width,
                        move == Move.CleaveC ? 0.38f : 0.28f,
                        move != Move.CleaveB,
                        sweep
                    );
                    Play(move == Move.CleaveC ? kit?.Finishers : kit?.Swings);
                    break;
            }
        }

        void End()
        {
            playing = Move.None;
            animator?.EndAttack(ActionFlags.BlendOut.Value * Frame);
        }

        // The Lunge's and the Leap Slam's travel toward moveTo over their startup.
        Vector3 Carry(float from, float to)
        {
            if (playing != Move.Lunge && playing != Move.LeapSlam)
                return Vector3.zero;
            var length = Mathf.Max(moves.ContactAt - moves.Started, 1e-4f);
            float At(float t)
            {
                var f = Mathf.Clamp01((t - moves.Started) / length);
                return playing == Move.Lunge ? 1f - (1f - f) * (1f - f) : f;
            }
            return (moveTo - moveFrom) * (At(to) - At(from));
        }

        void Contact()
        {
            var strike = Sized(playing);
            Struck?.Invoke(strike);
            var visual = 1f / Mathf.Max(ActionFlags.Hitbox.Value, 1f);
            if (playing == Move.LeapSlam)
            {
                SwordEffects.Ring(
                    transform.position + Vector3.up * 0.05f,
                    strike.Reach * visual,
                    0.2f
                );
                Play(kit != null ? new[] { kit.Slam } : null);
            }
            Debug.Log(
                $"Hero: {playing} contact on frame {Time.frameCount - pressFrame + 1} of the press, {Time.time - born:F2} s in"
            );
        }

        /// <summary>The move's strike from where the hero stands, at hitbox size.</summary>
        Strike Sized(Move move)
        {
            var strike = Strike.Of(move);
            strike.Origin = Flat(transform.position);
            strike.Direction = Num.Vector2.Normalize(Flat(facing));
            var hitbox = ActionFlags.Hitbox.Value;
            strike.Reach *= hitbox;
            if (strike.Shape == StrikeShape.Line)
                strike.Width *= hitbox;
            return strike;
        }

        void Play(AudioClip[] clips)
        {
            if (voice == null || clips == null || clips.Length == 0)
                return;
            voice.pitch = Random.Range(0.93f, 1.07f);
            voice.PlayOneShot(clips[Random.Range(0, clips.Length)]);
        }

        struct Intent
        {
            public Vector3 Move;
            public Vector3? Cursor;
            public bool Dash;
            public bool Attack;
            public bool Special;
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
                intent.Attack = k.fKey.wasPressedThisFrame;
                intent.Special = k.qKey.wasPressedThisFrame;
            }
            // Batch runs have no pointer, only a virtual mouse parked in a corner.
            var mouse = Application.isBatchMode ? null : Mouse.current;
            var camera = Camera.main;
            if (mouse != null && camera != null)
            {
                intent.Attack |= mouse.leftButton.wasPressedThisFrame;
                intent.Special |= mouse.rightButton.wasPressedThisFrame;
                var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var hit))
                    intent.Cursor = ray.GetPoint(hit);
            }
            return intent;
        }

        // A deterministic script for headless runs and shots, in seconds since the hero appeared
        // (or, while it has a Goal, a walk there):
        // still for a second, every move once with the cursor 5 m to the right (Cleave A, B, C at
        // 1.0 s, a dash and Lunge at 2.2 s, the Leap Slam at 3.2 s), then a run in a circle with
        // the cursor on the nearest target (else orbiting the other way), dashing into a Lunge and
        // cleaving every 1.25 s.
        static readonly (float At, HeroAction Action)[] Script =
        {
            (1f, HeroAction.Attack),
            (1.25f, HeroAction.Attack),
            (1.5f, HeroAction.Attack),
            (2.2f, HeroAction.Dash),
            (2.3f, HeroAction.Attack),
            (3.2f, HeroAction.Special),
        };

        const float Loop = 4.5f;

        Intent Demo(float t)
        {
            var intent = new Intent();
            var last = demoTime;
            demoTime = t;
            bool Crossed(float at) => t >= at && last < at;
            if (Goal is Vector3 goal)
            {
                var to = goal - transform.position;
                to.y = 0f;
                intent.Move =
                    to.sqrMagnitude > 0.04f ? Vector3.ClampMagnitude(to, 1f) : Vector3.zero;
                intent.Cursor = transform.position + intent.Move * 5f;
                return intent;
            }
            if (t < Loop)
            {
                intent.Cursor = transform.position + Vector3.right * 5f;
                foreach (var (at, action) in Script)
                    if (Crossed(at))
                    {
                        intent.Dash |= action == HeroAction.Dash;
                        intent.Attack |= action == HeroAction.Attack;
                        intent.Special |= action == HeroAction.Special;
                    }
                return intent;
            }
            var spin = t * 0.6f;
            intent.Cursor =
                Nearest()
                ?? transform.position + new Vector3(Mathf.Cos(-spin), 0f, Mathf.Sin(-spin)) * 5f;
            var angle = (t - Loop) * 1.4f;
            intent.Move = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var phase = (t - Loop) % 1.25f;
            var previous = (last - Loop) % 1.25f;
            bool At(float p) => phase >= p && (previous < p || previous > phase);
            intent.Dash = At(0f);
            intent.Attack = At(0.08f) || At(0.6f) || At(0.85f);
            return intent;
        }

        // The nearest target within 8 m, for the demo's loop to fight it.
        Vector3? Nearest()
        {
            Vector3? best = null;
            var bestDistance = 64f;
            foreach (var target in Targets)
            {
                var d = (target - transform.position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = target;
                }
            }
            return best;
        }

        static Num.Vector2 Flat(Vector3 v) => new Num.Vector2(v.x, v.z);

        static Vector3 Ground(Num.Vector2 v) => new Vector3(v.X, 0f, v.Y);
    }
}
