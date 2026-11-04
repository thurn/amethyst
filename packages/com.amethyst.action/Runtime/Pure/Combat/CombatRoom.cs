using System;
using System.Collections.Generic;
using System.Numerics;

namespace Amethyst.Action
{
    public enum EnemyState
    {
        /// <summary>Behind its spawn marker, not yet in the fight.</summary>
        Arriving,
        Walking,

        /// <summary>The telegraph: Attack 1 up to its impact, holding a token.</summary>
        WindingUp,

        /// <summary>A lunge's dash along its lane.</summary>
        Lunging,

        /// <summary>A leap through the air to its ring, landing at <see cref="CombatRoom.LeapSeconds"/>.</summary>
        Leaping,
        Recovering,
        Staggered,

        /// <summary>Killed, frozen for the kill's hitstop (or waiting for the buzzer's wave), then shattered.</summary>
        Dying,
    }

    /// <summary>One enemy of a <see cref="CombatRoom"/>, of a roster <see cref="EnemyKind"/>.</summary>
    public sealed class Enemy
    {
        public int Id;
        public EnemyKind Kind;
        public bool Empowered;
        public Vector2 Position;

        /// <summary>Its heading as the crowd draws it: atan2(x, z) of the way it faces.</summary>
        public float Yaw;
        public float Scale = 1f;
        public int Hp,
            MaxHp,
            Armor,
            MaxArmor;
        public EnemyState State;

        /// <summary>Its own clock: room time less its hitstop, so a frozen enemy's pose holds.</summary>
        public float Clock;

        /// <summary>When it entered <see cref="State"/>, on its own clock.</summary>
        public float Since;

        /// <summary>Its hitstop left, s.</summary>
        public float Frozen;

        /// <summary>Its hit flash left, s.</summary>
        public float Flash;

        /// <summary>Metres walked, for its walk cycle's phase.</summary>
        public float Walked;

        public Vector2 Knock;

        /// <summary>Whether its knockback has already struck another enemy.</summary>
        public bool Bumped;

        /// <summary>The current attack's wind-up, s, and its way.</summary>
        public float Windup;
        public Vector2 AttackDirection;

        /// <summary>Where the current attack aims: a lunge's lane starts there, a volley or a leap
        /// lands there.</summary>
        public Vector2 Aim;

        /// <summary>Whether the current lunge has hit the hero.</summary>
        public bool Landed;

        /// <summary>The blow that killed it (unit), its force and when it shatters (room time).</summary>
        public Vector2 Blow;
        public float Force;
        public float BreakAt;

        /// <summary>Room time its spawn marker ends.</summary>
        public float ArriveAt;
        public float MarkerSeconds;
        public float NextAttack;
        public uint Seed;

        public float Radius => Kind.Radius * Scale;

        public bool Armored => Armor > 0;

        /// <summary>In the fight: hittable, moving, a threat.</summary>
        public bool Fighting => State != EnemyState.Arriving && State != EnemyState.Dying;

        public float Age => Clock - Since;
    }

    /// <summary>
    /// A volley's spores in flight from <see cref="From"/>, each lofted on its own arc to its
    /// landing, all landing together at <see cref="Lands"/> inside the marked circle around
    /// <see cref="Target"/>, which hurts the hero then.
    /// </summary>
    public sealed class Volley
    {
        /// <summary>An arc's peak, times its length, so a short lob is as slow as a long one.</summary>
        public const float Loft = 0.15f;

        public Enemy Owner;
        public Vector2 From,
            Target;
        public float Radius;
        public Vector2[] Landings;
        public float Launched,
            Lands;

        /// <summary>Spore <paramref name="i"/> at room time <paramref name="time"/>: where over the
        /// ground and how high.</summary>
        public (Vector2 At, float Height) Spore(int i, float time)
        {
            var f = Math.Clamp((time - Launched) / Math.Max(Lands - Launched, 1e-4f), 0f, 1f);
            var to = Landings[i];
            var peak = Loft * Vector2.Distance(From, to);
            return (Vector2.Lerp(From, to, f), 4f * peak * f * (1f - f));
        }
    }

    public enum RoomEventKind
    {
        /// <summary>A spawn marker appears.</summary>
        Marker,

        /// <summary>A telegraph starts.</summary>
        Windup,

        /// <summary>A telegraphed attack lands (on the hero or not).</summary>
        Attack,

        /// <summary>A sword hit on an enemy; <see cref="RoomEvent.Killed"/> when it was the last.</summary>
        Hit,
        ArmorBreak,

        /// <summary>A body breaks into shards.</summary>
        Shatter,

        /// <summary>The hero takes damage, by contact or from a telegraphed attack.</summary>
        HeroHit,
        Wave,
        Buzzer,
        Clear,

        /// <summary>A pushed enemy strikes a wall, pillar, pylon or another enemy at <see cref="RoomEvent.Speed"/>.</summary>
        Impact,

        /// <summary>A pylon bursts (<see cref="RoomEvent.Amount"/> is its index).</summary>
        Burst,

        /// <summary>A Spike Vent erupts (<see cref="RoomEvent.Amount"/> is its index).</summary>
        Erupt,
    }

    public struct RoomEvent
    {
        public RoomEventKind Kind;
        public Enemy Enemy;
        public Vector2 Position;
        public Vector2 Direction;
        public int Amount;
        public bool Killed;
        public bool Telegraphed;
        public HitWeight Weight;
        public float Speed;
        public HazardKind Hazard;
    }

    public enum RoomPhase
    {
        Fighting,

        /// <summary>Horde: the buzzer went; the survivors shatter and the room waits for elites.</summary>
        Buzzer,
        Cleared,
    }

    /// <summary>
    /// One room of a fight in either combat mode (docs/game-design.md, Two combat modes; Enemies):
    /// its <see cref="SpawnList"/>'s enemies arriving from the room's edges behind spawn markers,
    /// never near the hero, walking at it with small gaps between bodies; the horde's contact
    /// damage and Empowered elites, the focused mode's waves and telegraphs; aggression tokens,
    /// stagger, knockback and per-target hitstop; the horde's timer and buzzer wave; its
    /// <see cref="Hazards"/>: walls and pillars that block and split the crowd, pylons that burst on
    /// enemies, Spike Vents that hurt everyone. Everything it
    /// does is a <see cref="RoomEvent"/> in <see cref="Events"/>, which its presenter drains.
    /// </summary>
    public sealed class CombatRoom
    {
        const float Gap = 0.3f;
        const float HeroRadius = 0.45f;
        const float MinSpawnDistance = 7f;
        const float WindupReach = 0.8f; // of an attack's reach, where its wind-up starts
        const float ConeHalfAngle = 1.2f; // radians
        const float StaggerSeconds = 0.4f;
        const float RecoverSeconds = 0.6f;
        const float LungeSpeed = 14f;
        const int Spores = 3;
        const float KnockDamping = 7f;
        const float TurnRate = 9f;
        const float WaveSpeed = 40f;
        const float Cell = 1.2f;
        const float StreamGap = 0.3f; // s between a stream's arrivals

        /// <summary>A lunge's lane's half width and a volley's landing circle's radius at scale 1,
        /// m: the hero's centre in them is hit.</summary>
        public const float LaneHalfWidth = 0.6f,
            VolleyRadius = 1.3f;

        /// <summary>A leap's ring's radius at scale 1, m (the hero's centre in it is hit), and the
        /// leap's time in the air, s.</summary>
        public const float LeapRadius = 2.2f,
            LeapSeconds = 0.4f;

        readonly Random rng;
        readonly List<Enemy> enemies = new List<Enemy>();
        readonly List<Volley> volleys = new List<Volley>();
        readonly Dictionary<long, List<Enemy>> grid = new Dictionary<long, List<Enemy>>();
        float spawnBudget;
        EnemyKind nextKind; // the horde's next pack, once drawn
        int nextCount;
        float nextElite;
        int nextId;
        readonly int[] erupted;

        /// <summary>A room of <paramref name="spawns"/> (M1's skeletons by default) in
        /// <paramref name="layout"/> (by default the mode's square room, walled in).</summary>
        public CombatRoom(
            ModeTuning mode,
            int seed,
            SpawnList spawns = null,
            HazardLayout layout = null
        )
        {
            Mode = mode;
            Spawns = spawns ?? SpawnList.Skeletons;
            Hazards = new Hazards(layout ?? HazardLayout.Open(new Vector2(mode.ArenaHalfSize)));
            erupted = new int[Hazards.Layout.Vents.Length];
            for (int i = 0; i < erupted.Length; i++)
                erupted[i] = Hazards.Eruption(i, 0f);
            rng = new Random(seed);
            Tokens = new AggressionTokens(mode.AggressionTokens);
            nextElite = mode.EliteEvery * 0.5f;
        }

        public ModeTuning Mode { get; }
        public SpawnList Spawns { get; }
        public AggressionTokens Tokens { get; }
        public Hazards Hazards { get; }
        public readonly List<RoomEvent> Events = new List<RoomEvent>();

        public IReadOnlyList<Enemy> Enemies => enemies;

        /// <summary>Volleys in flight.</summary>
        public IReadOnlyList<Volley> Volleys => volleys;
        public float Time { get; private set; }
        public RoomPhase Phase { get; private set; }
        public int Wave { get; private set; }

        /// <summary>The horde's timer, s; infinite without one.</summary>
        public float TimeLeft =>
            Mode.RoomSeconds > 0f ? Math.Max(0f, Mode.RoomSeconds - Time) : float.PositiveInfinity;

        /// <summary>Feel §11.1's toggles and scales: hitstop frames, flash frames, knockback.</summary>
        public float HitstopScale { get; set; } = 1f;
        public int FlashFrames { get; set; } = 2;
        public float KnockbackScale { get; set; } = 1f;

        /// <summary>Enemies in the fight or arriving.</summary>
        public int Alive
        {
            get
            {
                int n = 0;
                foreach (var s in enemies)
                    if (s.State != EnemyState.Dying)
                        n++;
                return n;
            }
        }

        /// <summary>
        /// Advances the room by <paramref name="dt"/> s with the hero at <paramref name="hero"/>,
        /// which only hits while <paramref name="heroVulnerable"/>; the first hit of a step makes
        /// it invulnerable for the rest of it, as the hero's own invulnerability will.
        /// </summary>
        public void Step(float dt, Vector2 hero, bool heroVulnerable)
        {
            Time += dt;
            Spawn(hero, dt);
            Grid();
            var vulnerable = heroVulnerable;
            Vents(hero, ref vulnerable);
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var s = enemies[i];
                if (s.State == EnemyState.Arriving)
                {
                    if (Time >= s.ArriveAt)
                        Enter(s, EnemyState.Walking);
                    continue;
                }
                if (s.State == EnemyState.Dying)
                {
                    if (Time >= s.BreakAt)
                        Break(i);
                    continue;
                }
                s.Flash = Math.Max(0f, s.Flash - dt);
                if (s.Frozen > 0f)
                {
                    s.Frozen = Math.Max(0f, s.Frozen - dt);
                    continue;
                }
                s.Clock += dt;
                Think(s, hero, dt, ref vulnerable);
            }
            Land(hero, ref vulnerable);
            Separate(hero);
            End();
        }

        void Think(Enemy s, Vector2 hero, float dt, ref bool vulnerable)
        {
            var to = hero - s.Position;
            var distance = to.Length();
            var toward = distance > 1e-4f ? to / distance : Vector2.UnitY;
            var telegraphs =
                Mode.FodderTelegraphs || s.Empowered || s.Kind.Role != EnemyRole.Fodder;
            var face = toward;
            switch (s.State)
            {
                case EnemyState.Walking:
                    var range = Range(s);
                    var (near, far) = telegraphs ? Keep(s, range) : (0f, s.Radius + HeroRadius);
                    if (distance > far || distance < near)
                    {
                        var step = Mode.WalkSpeed * s.Kind.Speed * dt;
                        var way = distance > far ? toward : -toward;
                        if (!s.Kind.Flies)
                            way = Hazards.Steer(
                                s.Position,
                                way,
                                s.Radius,
                                distance > far ? distance : float.MaxValue
                            );
                        s.Position += way * step;
                        s.Walked += step;
                        face = way;
                    }
                    if (
                        telegraphs
                        && distance <= range + HeroRadius
                        && s.Clock >= s.NextAttack
                        && Tokens.TryTake(s.Id)
                    )
                    {
                        Enter(s, EnemyState.WindingUp);
                        s.Windup =
                            s.Kind.Windup > 0f
                                ? s.Kind.Windup
                                : Mode.TelegraphMin
                                    + (float)rng.NextDouble()
                                        * (Mode.TelegraphMax - Mode.TelegraphMin);
                        s.AttackDirection = toward;
                        s.Aim = s.Kind.Attack switch
                        {
                            AttackKind.Volley => hero,
                            AttackKind.Leap => s.Position + to * Math.Min(1f, Reach(s) / distance),
                            _ => s.Position,
                        };
                        Emit(RoomEventKind.Windup, s, s.Position, toward);
                    }
                    else if (
                        !telegraphs
                        && Mode.ContactDamageMax > 0
                        && vulnerable
                        && distance <= s.Radius + HeroRadius + 0.2f
                    )
                    {
                        vulnerable = false;
                        Events.Add(
                            new RoomEvent
                            {
                                Kind = RoomEventKind.HeroHit,
                                Enemy = s,
                                Position = s.Position,
                                Direction = toward,
                                Amount = rng.Next(Mode.ContactDamageMin, Mode.ContactDamageMax + 1),
                            }
                        );
                    }
                    break;
                case EnemyState.WindingUp:
                    face = s.AttackDirection;
                    if (s.Age >= s.Windup)
                        Enter(s, Strike(s, hero, ref vulnerable));
                    break;
                case EnemyState.Lunging:
                    face = s.AttackDirection;
                    Lunge(s, hero, dt, ref vulnerable);
                    break;
                case EnemyState.Leaping:
                    face = s.AttackDirection;
                    Leap(s, hero, dt, ref vulnerable);
                    break;
                case EnemyState.Recovering:
                    face = s.AttackDirection;
                    if (s.Age >= RecoverSeconds)
                        Enter(s, EnemyState.Walking);
                    break;
                case EnemyState.Staggered:
                    face = new Vector2(MathF.Sin(s.Yaw), MathF.Cos(s.Yaw));
                    if (s.Age >= StaggerSeconds)
                        Enter(s, EnemyState.Walking);
                    break;
            }
            if (s.Knock.LengthSquared() > 1e-4f)
            {
                s.Position += s.Knock * dt;
                Knocked(s);
            }
            s.Knock *= MathF.Exp(-KnockDamping * dt);
            var yaw = MathF.Atan2(face.X, face.Y);
            var turn = Wrap(yaw - s.Yaw);
            s.Yaw = Wrap(s.Yaw + Math.Clamp(turn, -TurnRate * dt, TurnRate * dt));
        }

        // An attack's impact, by its kind: what it reaches and what it does, and the state it
        // leaves the attacker in.
        EnemyState Strike(Enemy s, Vector2 hero, ref bool vulnerable)
        {
            Tokens.Release(s.Id);
            s.NextAttack = s.Clock + 1.4f + (float)rng.NextDouble();
            Emit(RoomEventKind.Attack, s, s.Position, s.AttackDirection);
            switch (s.Kind.Attack)
            {
                case AttackKind.Cone:
                    var to = hero - s.Position;
                    var distance = to.Length();
                    var angle =
                        distance > 1e-4f
                            ? MathF.Acos(
                                Math.Clamp(Vector2.Dot(to / distance, s.AttackDirection), -1f, 1f)
                            )
                            : 0f;
                    if (distance <= s.Kind.Reach * s.Scale + HeroRadius && angle <= ConeHalfAngle)
                        HurtHero(s, s.Position, ref vulnerable);
                    return EnemyState.Recovering;
                case AttackKind.Lunge:
                    s.Landed = false;
                    return EnemyState.Lunging;
                case AttackKind.Volley:
                    Launch(s);
                    return EnemyState.Recovering;
                case AttackKind.Leap:
                    return EnemyState.Leaping;
                default:
                    throw new ArgumentOutOfRangeException(nameof(s), s.Kind.Attack, null);
            }
        }

        // How near the hero an attacker starts its wind-up, and how far its attack reaches.
        static float Range(Enemy s) => Reach(s) * WindupReach;

        static float Reach(Enemy s) => s.Kind.Reach * s.Scale;

        // The attack kinds' other difference: how near the hero each keeps while it walks, centre
        // to centre: a volley's thrower backs off when nearer than `Near`.
        static (float Near, float Far) Keep(Enemy s, float range) =>
            s.Kind.Attack switch
            {
                AttackKind.Volley => (range * 0.8f, range),
                _ => (0f, range * 0.75f + s.Radius + HeroRadius),
            };

        // A lunge's dash along its lane, which hits the hero once if it is in the lane where the
        // attacker reaches; it ends when the lane does.
        void Lunge(Enemy s, Vector2 hero, float dt, ref bool vulnerable)
        {
            var length = s.Kind.Reach * s.Scale;
            var duration = length / LungeSpeed;
            s.Position += s.AttackDirection * (LungeSpeed * dt);
            if (!s.Landed)
            {
                var off = hero - s.Aim;
                var along = Vector2.Dot(off, s.AttackDirection);
                var across = MathF.Abs(off.X * s.AttackDirection.Y - off.Y * s.AttackDirection.X);
                var front = Vector2.Dot(s.Position - s.Aim, s.AttackDirection) + s.Radius;
                if (
                    across <= LaneHalfWidth * s.Scale
                    && along >= -HeroRadius
                    && along <= Math.Min(front + HeroRadius, length)
                    && vulnerable
                )
                {
                    s.Landed = true;
                    HurtHero(s, s.Position, ref vulnerable);
                }
            }
            if (s.Age >= duration)
                Enter(s, EnemyState.Recovering);
        }

        // A leap to its ring, closing what is left of the way to it by the time it lands, which
        // hurts the hero if it is in the ring then.
        void Leap(Enemy s, Vector2 hero, float dt, ref bool vulnerable)
        {
            var left = LeapSeconds - s.Age;
            if (left > 0f)
            {
                s.Position += (s.Aim - s.Position) * Math.Min(1f, dt / (left + dt));
                return;
            }
            s.Position = s.Aim;
            if (Vector2.Distance(hero, s.Aim) <= LeapRadius * s.Scale)
                HurtHero(s, s.Aim, ref vulnerable);
            Enter(s, EnemyState.Recovering);
        }

        // A volley's launch: its spores leave for their landings around its aim and land together,
        // none faster than the kind's projectile speed (feel §5: at most half the dash's).
        void Launch(Enemy s)
        {
            var radius = VolleyRadius * s.Scale;
            var landings = new Vector2[Spores];
            landings[0] = s.Aim;
            var far = Vector2.Distance(s.Position, s.Aim);
            for (int i = 1; i < Spores; i++)
            {
                var angle = (float)rng.NextDouble() * MathF.PI * 2f;
                var spread = radius * (0.4f + 0.3f * (float)rng.NextDouble());
                landings[i] = s.Aim + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * spread;
                far = Math.Max(far, Vector2.Distance(s.Position, landings[i]));
            }
            // Over the ground at the kind's speed; the arc's climb adds at most Loft * 4 of it.
            var ground = s.Kind.ProjectileSpeed / MathF.Sqrt(1f + 16f * Volley.Loft * Volley.Loft);
            volleys.Add(
                new Volley
                {
                    Owner = s,
                    From = s.Position,
                    Target = s.Aim,
                    Radius = radius,
                    Landings = landings,
                    Launched = Time,
                    Lands = Time + Math.Max(far, 0.5f) / ground,
                }
            );
        }

        // Volleys that land this step hurt a hero inside their circle.
        void Land(Vector2 hero, ref bool vulnerable)
        {
            for (int i = volleys.Count - 1; i >= 0; i--)
            {
                var v = volleys[i];
                if (Time < v.Lands)
                    continue;
                volleys.RemoveAt(i);
                if (Vector2.Distance(hero, v.Target) <= v.Radius)
                    HurtHero(v.Owner, v.Target, ref vulnerable);
            }
        }

        // A telegraphed attack's damage from `from`, if the hero can take it this step.
        void HurtHero(Enemy s, Vector2 from, ref bool vulnerable)
        {
            if (!vulnerable)
                return;
            vulnerable = false;
            Events.Add(
                new RoomEvent
                {
                    Kind = RoomEventKind.HeroHit,
                    Enemy = s,
                    Position = from,
                    Direction = s.AttackDirection,
                    Amount = s.Empowered ? Mode.AttackDamage * 3 / 2 : Mode.AttackDamage,
                    Telegraphed = true,
                }
            );
        }

        /// <summary>
        /// A sword move's contact: every enemy in the fight it reaches takes its damage (armor
        /// first), its knockback (not while armored) and, focused, a stagger, freezing for the hit's
        /// hitstop. Returns the hits and the kills.
        /// </summary>
        public (int Hits, int Kills) Hit(Strike strike)
        {
            var weight =
                strike.Move == Move.CleaveC || strike.Move == Move.LeapSlam
                    ? HitWeight.Finisher
                    : HitWeight.Light;
            // The Lunge and the Leap Slam are the breaking tools: they chip armor faster.
            var chip =
                strike.Move == Move.Lunge || strike.Move == Move.LeapSlam ? Mode.ArmorBreakers : 1f;
            var result = Deal(strike, weight, chip);
            var pylons = Hazards.Layout.Pylons;
            for (int i = 0; i < pylons.Length; i++)
                if (!Hazards.Broken(i) && strike.Hits(pylons[i].Center, pylons[i].Radius))
                    Burst(i);
            return result;
        }

        // A blow on every enemy in the fight it reaches, from the hero's sword or a hazard, taking
        // armor at `chip` times its damage.
        (int Hits, int Kills) Deal(Strike strike, HitWeight weight, float chip = 1f)
        {
            int hits = 0,
                kills = 0;
            foreach (var s in enemies)
            {
                if (!s.Fighting || !strike.Hits(s.Position, s.Radius))
                    continue;
                hits++;
                var damage = strike.Damage;
                var wasArmored = s.Armored;
                if (wasArmored)
                {
                    // Armor first, at the move's chip rate; what breaks through hurts at its own.
                    var chipped = (int)MathF.Round(damage * chip);
                    damage = chipped >= s.Armor ? (int)((chipped - s.Armor) / chip) : 0;
                    s.Armor = Math.Max(0, s.Armor - chipped);
                    if (!s.Armored)
                        Emit(RoomEventKind.ArmorBreak, s, s.Position, strike.Direction);
                }
                s.Hp -= damage;
                s.Flash = FlashFrames / 60f;
                var push = strike.Push(s.Position) * KnockbackScale;
                var blow =
                    push.LengthSquared() > 1e-6f ? Vector2.Normalize(push) : strike.Direction;
                if (!wasArmored)
                {
                    s.Knock = push;
                    s.Bumped = false;
                }
                var killed = s.Hp <= 0;
                var hitWeight = killed ? HitWeight.Kill : weight;
                Events.Add(
                    new RoomEvent
                    {
                        Kind = RoomEventKind.Hit,
                        Enemy = s,
                        Position = s.Position,
                        Direction = blow,
                        Amount = strike.Damage,
                        Killed = killed,
                        Weight = hitWeight,
                    }
                );
                var frames = Hitstop.Target(Mode, hitWeight, HitstopScale);
                if (killed)
                {
                    kills++;
                    Die(s, blow, strike.Knockback, Time + frames / 60f);
                    continue;
                }
                s.Frozen = Hitstop.Extend(s.Frozen, frames);
                if (Mode.Stagger && !wasArmored)
                {
                    Tokens.Release(s.Id);
                    Enter(s, EnemyState.Staggered);
                }
            }
            if (kills > 0)
                End();
            return (hits, kills);
        }

        // A pylon's burst: a ring that hurts and throws only enemies, and may burst its neighbours.
        void Burst(int pylon)
        {
            if (!Hazards.Break(pylon))
                return;
            var at = Hazards.Layout.Pylons[pylon].Center;
            Events.Add(
                new RoomEvent
                {
                    Kind = RoomEventKind.Burst,
                    Position = at,
                    Amount = pylon,
                }
            );
            Deal(
                new Strike
                {
                    Move = Move.CleaveC,
                    Shape = StrikeShape.Circle,
                    Origin = at,
                    Direction = Vector2.UnitY,
                    Reach = Hazards.BurstRadius,
                    Damage = Hazards.BurstDamage,
                    Knockback = Hazards.BurstKnockback,
                },
                HitWeight.Finisher
            );
            var pylons = Hazards.Layout.Pylons;
            for (int i = 0; i < pylons.Length; i++)
                if (
                    !Hazards.Broken(i)
                    && Vector2.Distance(pylons[i].Center, at)
                        <= Hazards.BurstRadius + pylons[i].Radius
                )
                    Burst(i);
        }

        // Each vent erupts as its marker fills, hurting the hero (a telegraphed hit) and every
        // enemy in it.
        void Vents(Vector2 hero, ref bool vulnerable)
        {
            var vents = Hazards.Layout.Vents;
            for (int i = 0; i < vents.Length; i++)
            {
                var n = Hazards.Eruption(i, Time);
                if (n <= erupted[i])
                    continue;
                erupted[i] = n;
                var v = vents[i];
                Events.Add(
                    new RoomEvent
                    {
                        Kind = RoomEventKind.Erupt,
                        Position = v.Center,
                        Amount = i,
                    }
                );
                var off = hero - v.Center;
                if (vulnerable && off.Length() <= v.Radius + HeroRadius * 0.5f)
                {
                    vulnerable = false;
                    Events.Add(
                        new RoomEvent
                        {
                            Kind = RoomEventKind.HeroHit,
                            Position = v.Center,
                            Direction =
                                off.LengthSquared() > 1e-6f
                                    ? Vector2.Normalize(off)
                                    : Vector2.UnitY,
                            Amount = Mode.VentDamage,
                            Telegraphed = true,
                        }
                    );
                }
                Deal(
                    new Strike
                    {
                        Move = Move.LeapSlam,
                        Shape = StrikeShape.Circle,
                        Origin = v.Center,
                        Direction = Vector2.UnitY,
                        Reach = v.Radius,
                        Damage = Mode.VentEnemyDamage,
                        Knockback = 2f,
                    },
                    HitWeight.Light
                );
            }
        }

        // A knocked-back enemy stops at what blocks it; striking it fast enough is an Impact,
        // and a fast Impact on a pylon bursts it.
        void Knocked(Enemy s)
        {
            if (
                !Hazards.Collide(
                    ref s.Position,
                    s.Radius,
                    s.Kind.Flies,
                    out var what,
                    out var index,
                    out var normal
                )
            )
                return;
            var knock = s.Knock;
            if (Vector2.Dot(knock, normal) >= 0f)
                return;
            s.Knock = Vector2.Zero;
            var speed = knock.Length();
            if (speed < Hazards.ImpactSpeed)
                return;
            Impact(s, knock, what);
            if (what == HazardKind.Pylon && speed >= Hazards.PylonSpeed)
                Burst(index);
        }

        void Impact(Enemy s, Vector2 knock, HazardKind what) =>
            Events.Add(
                new RoomEvent
                {
                    Kind = RoomEventKind.Impact,
                    Enemy = s,
                    Position = s.Position,
                    Direction = Vector2.Normalize(knock),
                    Speed = knock.Length(),
                    Hazard = what,
                }
            );

        void Die(Enemy s, Vector2 blow, float force, float breakAt)
        {
            Tokens.Release(s.Id);
            s.Blow = blow;
            s.Force = force;
            s.BreakAt = breakAt;
            s.State = EnemyState.Dying;
        }

        void Break(int index)
        {
            var s = enemies[index];
            Emit(RoomEventKind.Shatter, s, s.Position, s.Blow);
            enemies.RemoveAt(index);
        }

        // Spawning: the horde's stream toward its target and its elites, the focused mode's waves.
        void Spawn(Vector2 hero, float dt)
        {
            if (Phase != RoomPhase.Fighting)
                return;
            if (Mode.Mode == CombatMode.Horde)
            {
                if (Mode.RoomSeconds > 0f && Time >= Mode.RoomSeconds)
                {
                    Buzzer(hero);
                    return;
                }
                // The stream saves up for its next pack, so each arrives whole, in its formation.
                var deficit = Mode.Alive - Alive;
                spawnBudget = Math.Min(spawnBudget + Mode.Alive / 2.5f * dt, deficit);
                while (true)
                {
                    if (nextKind == null)
                    {
                        nextKind = Spawns.Pick(rng, Mode.SpecialWeight);
                        nextCount = Math.Min(Mode.Alive, PackSize(nextKind));
                    }
                    if (nextCount < 1 || spawnBudget < nextCount)
                        break;
                    Pack(hero, nextKind, nextCount, false);
                    spawnBudget -= nextCount;
                    nextKind = null;
                }
                if (Mode.EliteEvery > 0f && Time >= nextElite)
                {
                    nextElite = Time + Mode.EliteEvery;
                    int elites = 0;
                    foreach (var s in enemies)
                        if (s.Empowered && s.State != EnemyState.Dying)
                            elites++;
                    if (elites < Mode.ElitesAlive)
                        Pack(hero, Spawns.PickElite(rng), 1, true);
                }
                return;
            }
            if (Wave < Mode.Waves && (Wave == 0 || Alive <= Mode.WaveCallAt))
            {
                Wave++;
                Emit(RoomEventKind.Wave, null, hero, Vector2.Zero);
                var n = Math.Min(
                    Mode.Alive,
                    Mode.WaveMin + rng.Next(Mode.WaveMax - Mode.WaveMin + 1)
                );
                for (int left = n; left > 0; )
                {
                    var pack = Math.Min(left, 1 + rng.Next(3));
                    var kind = Spawns.Pick(rng, Mode.SpecialWeight);
                    if (kind.Role == EnemyRole.Heavy)
                        pack = 1; // heavies come alone
                    Pack(hero, kind, pack, false);
                    left -= pack;
                }
            }
        }

        // A horde pack's size by its kind's formation: rings and streams are bigger; heavies come
        // alone.
        int PackSize(EnemyKind kind) =>
            kind.Role == EnemyRole.Heavy
                ? 1
                : kind.Formation switch
                {
                    Formation.Ring => 7 + rng.Next(4),
                    Formation.Stream => 6 + rng.Next(5),
                    _ => 3 + rng.Next(4),
                };

        // A pack of one kind arrives at one spot on the room's edge, at least MinSpawnDistance from
        // the hero; in the horde, as its kind's formation: bunched there, single file out of it,
        // or in a ring around the hero instead.
        void Pack(Vector2 hero, EnemyKind kind, int count, bool empowered)
        {
            var half = Hazards.Layout.Half;
            var at = Vector2.Zero;
            var best = -1f;
            for (int tries = 0; tries < 8; tries++)
            {
                var along = (float)rng.NextDouble() * 2f - 1f;
                var inset = 0.8f + (float)rng.NextDouble() * 1.5f;
                var side = rng.Next(4);
                var p = side switch
                {
                    0 => new Vector2(along * (half.X - 1f), half.Y - inset),
                    1 => new Vector2(along * (half.X - 1f), inset - half.Y),
                    2 => new Vector2(half.X - inset, along * (half.Y - 1f)),
                    _ => new Vector2(inset - half.X, along * (half.Y - 1f)),
                };
                var d = Vector2.Distance(p, hero);
                if (d > best)
                {
                    best = d;
                    at = p;
                }
                if (d >= MinSpawnDistance)
                    break;
            }
            var marker = 0.5f + (float)rng.NextDouble() * 0.5f;
            var hp = Math.Max(1, (int)MathF.Round(Mode.BasicHp * kind.Hp));
            var formation =
                Mode.Mode == CombatMode.Horde && !empowered ? kind.Formation : Formation.Pack;
            float ringRadius = 0f,
                ringFrom = 0f;
            if (formation == Formation.Ring)
            {
                ringRadius = MinSpawnDistance + 3f + (float)rng.NextDouble() * 3f;
                ringFrom = (float)rng.NextDouble() * MathF.PI * 2f;
            }
            var inward = hero - at;
            var across =
                inward.LengthSquared() > 1e-6f
                    ? Vector2.Normalize(new Vector2(-inward.Y, inward.X))
                    : Vector2.UnitX;
            for (int k = 0; k < count; k++)
            {
                var delay = 0f;
                Vector2 p;
                switch (formation)
                {
                    case Formation.Ring:
                        var around =
                            ringFrom + (k + 0.3f * (float)rng.NextDouble()) * MathF.PI * 2f / count;
                        p = hero + new Vector2(MathF.Cos(around), MathF.Sin(around)) * ringRadius;
                        break;
                    case Formation.Stream:
                        delay = k * StreamGap;
                        p = at + across * (((float)rng.NextDouble() - 0.5f) * 0.5f);
                        break;
                    default:
                        var angle = (float)rng.NextDouble() * MathF.PI * 2f;
                        var spread = k == 0 ? 0f : 0.9f + (float)rng.NextDouble() * 1.2f;
                        p = at + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * spread;
                        break;
                }
                p = Vector2.Clamp(p, -half + new Vector2(0.5f), half - new Vector2(0.5f));
                Hazards.Collide(ref p, kind.Radius * (empowered ? 1.5f : 1f), kind.Flies);
                var to = hero - p;
                var s = new Enemy
                {
                    Id = nextId++,
                    Kind = kind,
                    Empowered = empowered,
                    Position = p,
                    Yaw = MathF.Atan2(to.X, to.Y),
                    Scale = empowered ? 1.5f : 1f,
                    MaxHp = empowered ? hp * 5 / 2 : hp,
                    MaxArmor = empowered || kind.Armored ? 30 : 0,
                    State = EnemyState.Arriving,
                    MarkerSeconds = marker + delay,
                    ArriveAt = Time + marker + delay,
                    Seed = (uint)rng.Next(),
                };
                s.Hp = s.MaxHp;
                s.Armor = s.MaxArmor;
                s.NextAttack = 0.3f + (float)rng.NextDouble() * 0.8f;
                enemies.Add(s);
                Emit(RoomEventKind.Marker, s, p, Vector2.Zero);
            }
        }

        // The buzzer: spawning stops, markers vanish, and every plain survivor shatters in one wave
        // outward from the hero; elites fight on.
        void Buzzer(Vector2 hero)
        {
            Phase = RoomPhase.Buzzer;
            Emit(RoomEventKind.Buzzer, null, hero, Vector2.Zero);
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var s = enemies[i];
                if (s.Empowered || s.State == EnemyState.Dying)
                    continue;
                if (s.State == EnemyState.Arriving)
                {
                    enemies.RemoveAt(i);
                    continue;
                }
                var away = s.Position - hero;
                var d = away.Length();
                Die(s, d > 1e-4f ? away / d : Vector2.UnitY, 6f, Time + d / WaveSpeed);
            }
            volleys.RemoveAll(v => !v.Owner.Empowered);
        }

        // The room clears when its last wave or its last elite after the buzzer is dead.
        void End()
        {
            if (Phase == RoomPhase.Cleared)
                return;
            var done =
                Mode.Mode == CombatMode.Horde
                    ? Phase == RoomPhase.Buzzer && Alive == 0
                    : Wave >= Mode.Waves && Alive == 0;
            if (!done)
                return;
            Phase = RoomPhase.Cleared;
            volleys.Clear();
            Emit(RoomEventKind.Clear, null, Vector2.Zero, Vector2.Zero);
        }

        void Enter(Enemy s, EnemyState state)
        {
            s.State = state;
            s.Since = s.Clock;
        }

        void Emit(RoomEventKind kind, Enemy s, Vector2 at, Vector2 direction) =>
            Events.Add(
                new RoomEvent
                {
                    Kind = kind,
                    Enemy = s,
                    Position = at,
                    Direction = direction,
                }
            );

        // Keeps a gap between bodies, the hero's body clear and everyone in the room: each pair of
        // overlapping neighbours moves apart by half the overlap, once a step.
        void Separate(Vector2 hero)
        {
            foreach (var s in enemies)
            {
                if (!s.Fighting)
                    continue;
                var (cx, cy) = CellOf(s.Position);
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                {
                    if (!grid.TryGetValue(Key(cx + x, cy + y), out var list))
                        continue;
                    foreach (var o in list)
                    {
                        // Fliers keep apart from fliers, walkers from walkers: bats pass over a crowd.
                        if (o.Id <= s.Id || !o.Fighting || o.Kind.Flies != s.Kind.Flies)
                            continue;
                        var apart = s.Position - o.Position;
                        var want = s.Radius + o.Radius + Gap;
                        var d2 = apart.LengthSquared();
                        if (d2 >= want * want)
                            continue;
                        var d = MathF.Sqrt(d2);
                        var n =
                            d > 1e-4f ? apart / d : new Vector2(MathF.Cos(s.Id), MathF.Sin(s.Id));
                        var push = n * ((want - d) * 0.5f);
                        Bump(s);
                        Bump(o);
                        if (s.Frozen <= 0f)
                            s.Position += push;
                        if (o.Frozen <= 0f)
                            o.Position -= push;
                    }
                }
                var off = s.Position - hero;
                var clear = s.Radius + HeroRadius;
                var dh = off.Length();
                if (dh < clear)
                    s.Position = hero + (dh > 1e-4f ? off / dh : Vector2.UnitY) * clear;
                // A leap passes over walls and pillars as a flier does.
                Hazards.Collide(
                    ref s.Position,
                    s.Radius,
                    s.Kind.Flies || s.State == EnemyState.Leaping
                );
            }
        }

        // An enemy knocked into another strikes it once per knockback.
        void Bump(Enemy s)
        {
            if (s.Bumped || s.Knock.LengthSquared() < Hazards.ImpactSpeed * Hazards.ImpactSpeed)
                return;
            s.Bumped = true;
            Impact(s, s.Knock, HazardKind.Enemy);
        }

        void Grid()
        {
            foreach (var list in grid.Values)
                list.Clear();
            foreach (var s in enemies)
            {
                if (!s.Fighting)
                    continue;
                var (cx, cy) = CellOf(s.Position);
                var key = Key(cx, cy);
                if (!grid.TryGetValue(key, out var list))
                    grid[key] = list = new List<Enemy>(8);
                list.Add(s);
            }
        }

        static (int, int) CellOf(Vector2 p) =>
            ((int)MathF.Floor(p.X / Cell), (int)MathF.Floor(p.Y / Cell));

        static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        static float Wrap(float a)
        {
            while (a > MathF.PI)
                a -= 2f * MathF.PI;
            while (a < -MathF.PI)
                a += 2f * MathF.PI;
            return a;
        }
    }
}
