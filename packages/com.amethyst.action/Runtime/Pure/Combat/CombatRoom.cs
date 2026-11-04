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

        /// <summary>The current attack's wind-up, s, and its way.</summary>
        public float Windup;
        public Vector2 AttackDirection;

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
    /// stagger, knockback and per-target hitstop; the horde's timer and buzzer wave. Everything it
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
        const float KnockDamping = 7f;
        const float TurnRate = 9f;
        const float WaveSpeed = 40f;
        const float Cell = 1.2f;
        const float StreamGap = 0.3f; // s between a stream's arrivals

        readonly Random rng;
        readonly List<Enemy> enemies = new List<Enemy>();
        readonly Dictionary<long, List<Enemy>> grid = new Dictionary<long, List<Enemy>>();
        float spawnBudget;
        EnemyKind nextKind; // the horde's next pack, once drawn
        int nextCount;
        float nextElite;
        int nextId;

        public CombatRoom(ModeTuning mode, int seed, SpawnList spawns = null)
        {
            Mode = mode;
            Spawns = spawns ?? SpawnList.Skeletons;
            rng = new Random(seed);
            Tokens = new AggressionTokens(mode.AggressionTokens);
            nextElite = mode.EliteEvery * 0.5f;
        }

        public ModeTuning Mode { get; }
        public SpawnList Spawns { get; }
        public AggressionTokens Tokens { get; }
        public readonly List<RoomEvent> Events = new List<RoomEvent>();

        public IReadOnlyList<Enemy> Enemies => enemies;
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
                    var stop = telegraphs ? range * 0.75f : 0f;
                    if (distance > stop + s.Radius + HeroRadius)
                    {
                        var step = Mode.WalkSpeed * s.Kind.Speed * dt;
                        s.Position += toward * step;
                        s.Walked += step;
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
                            Mode.TelegraphMin
                            + (float)rng.NextDouble() * (Mode.TelegraphMax - Mode.TelegraphMin);
                        s.AttackDirection = toward;
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
                    {
                        Strike(s, hero, ref vulnerable);
                        Enter(s, EnemyState.Recovering);
                    }
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
            s.Position += s.Knock * dt;
            s.Knock *= MathF.Exp(-KnockDamping * dt);
            var yaw = MathF.Atan2(face.X, face.Y);
            var turn = Wrap(yaw - s.Yaw);
            s.Yaw = Wrap(s.Yaw + Math.Clamp(turn, -TurnRate * dt, TurnRate * dt));
        }

        // An attack's impact, by its kind: what it reaches and what it does.
        void Strike(Enemy s, Vector2 hero, ref bool vulnerable)
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
                        HurtHero(s, ref vulnerable);
                    break;
            }
        }

        // The attack kinds' one other difference: how near the hero each starts its wind-up.
        static float Range(Enemy s) =>
            s.Kind.Attack switch
            {
                AttackKind.Cone => s.Kind.Reach * WindupReach * s.Scale,
                _ => throw new ArgumentOutOfRangeException(nameof(s), s.Kind.Attack, null),
            };

        // A telegraphed attack's damage, if the hero can take it this step.
        void HurtHero(Enemy s, ref bool vulnerable)
        {
            if (!vulnerable)
                return;
            vulnerable = false;
            Events.Add(
                new RoomEvent
                {
                    Kind = RoomEventKind.HeroHit,
                    Enemy = s,
                    Position = s.Position,
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
            int hits = 0,
                kills = 0;
            var weight =
                strike.Move == Move.CleaveC || strike.Move == Move.LeapSlam
                    ? HitWeight.Finisher
                    : HitWeight.Light;
            foreach (var s in enemies)
            {
                if (!s.Fighting || !strike.Hits(s.Position, s.Radius))
                    continue;
                hits++;
                var damage = strike.Damage;
                var wasArmored = s.Armored;
                if (wasArmored)
                {
                    s.Armor -= damage;
                    damage = Math.Max(0, -s.Armor);
                    s.Armor = Math.Max(0, s.Armor);
                    if (!s.Armored)
                        Emit(RoomEventKind.ArmorBreak, s, s.Position, strike.Direction);
                }
                s.Hp -= damage;
                s.Flash = FlashFrames / 60f;
                var push = strike.Push(s.Position) * KnockbackScale;
                var blow =
                    push.LengthSquared() > 1e-6f ? Vector2.Normalize(push) : strike.Direction;
                if (!wasArmored)
                    s.Knock = push;
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
                        nextKind = Spawns.Pick(rng);
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
                    Pack(hero, Spawns.Pick(rng), pack, false);
                    left -= pack;
                }
            }
        }

        // A horde pack's size by its kind's formation: rings and streams are bigger.
        int PackSize(EnemyKind kind) =>
            kind.Formation switch
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
            var half = Mode.ArenaHalfSize;
            var at = Vector2.Zero;
            var best = -1f;
            for (int tries = 0; tries < 8; tries++)
            {
                var along = ((float)rng.NextDouble() * 2f - 1f) * (half - 1f);
                var inset = half - 0.8f - (float)rng.NextDouble() * 1.5f;
                var side = rng.Next(4);
                var p = side switch
                {
                    0 => new Vector2(along, inset),
                    1 => new Vector2(along, -inset),
                    2 => new Vector2(inset, along),
                    _ => new Vector2(-inset, along),
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
                p = Vector2.Clamp(p, new Vector2(-half + 0.5f), new Vector2(half - 0.5f));
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
            var half = Mode.ArenaHalfSize - 0.4f;
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
                s.Position = Vector2.Clamp(s.Position, new Vector2(-half), new Vector2(half));
            }
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
