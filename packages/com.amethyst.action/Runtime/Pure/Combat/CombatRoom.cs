using System;
using System.Collections.Generic;
using System.Numerics;

namespace Amethyst.Action
{
    public enum SkeletonState
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

    /// <summary>One skeleton of a <see cref="CombatRoom"/>.</summary>
    public sealed class Skeleton
    {
        public int Id;
        public bool Empowered;
        public Vector2 Position;

        /// <summary>Its heading as the crowd draws it: atan2(x, z) of the way it faces.</summary>
        public float Yaw;
        public float Scale = 1f;
        public int Hp,
            MaxHp,
            Armor,
            MaxArmor;
        public SkeletonState State;

        /// <summary>Its own clock: room time less its hitstop, so a frozen skeleton's pose holds.</summary>
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

        public float Radius => 0.35f * Scale;

        public bool Armored => Armor > 0;

        /// <summary>In the fight: hittable, moving, a threat.</summary>
        public bool Fighting => State != SkeletonState.Arriving && State != SkeletonState.Dying;

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

        /// <summary>A sword hit on a skeleton; <see cref="RoomEvent.Killed"/> when it was the last.</summary>
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
        public Skeleton Skeleton;
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
    /// One room of the feel testbed in either combat mode (docs/game-design.md, Two combat modes;
    /// Enemies): skeletons arriving from the room's edges behind spawn markers, never near the hero,
    /// walking at it with small gaps between bodies; the horde's contact damage and Empowered
    /// elites, the focused mode's waves and telegraphs; aggression tokens, stagger, knockback and
    /// per-target hitstop; the horde's timer and buzzer wave. Everything it does is a
    /// <see cref="RoomEvent"/> in <see cref="Events"/>, which its presenter drains.
    /// </summary>
    public sealed class CombatRoom
    {
        const float Gap = 0.3f;
        const float HeroRadius = 0.45f;
        const float MinSpawnDistance = 7f;
        const float AttackRange = 1.6f;
        const float AttackReach = 2f;
        const float AttackHalfAngle = 1.2f; // radians
        const float StaggerSeconds = 0.4f;
        const float RecoverSeconds = 0.6f;
        const float KnockDamping = 7f;
        const float TurnRate = 9f;
        const float WaveSpeed = 40f;
        const float Cell = 1.2f;

        readonly Random rng;
        readonly List<Skeleton> skeletons = new List<Skeleton>();
        readonly Dictionary<long, List<Skeleton>> grid = new Dictionary<long, List<Skeleton>>();
        float spawnBudget;
        float nextElite;
        int nextId;

        public CombatRoom(ModeTuning mode, int seed)
        {
            Mode = mode;
            rng = new Random(seed);
            Tokens = new AggressionTokens(mode.AggressionTokens);
            nextElite = mode.EliteEvery * 0.5f;
        }

        public ModeTuning Mode { get; }
        public AggressionTokens Tokens { get; }
        public readonly List<RoomEvent> Events = new List<RoomEvent>();

        public IReadOnlyList<Skeleton> Skeletons => skeletons;
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

        /// <summary>Skeletons in the fight or arriving.</summary>
        public int Alive
        {
            get
            {
                int n = 0;
                foreach (var s in skeletons)
                    if (s.State != SkeletonState.Dying)
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
            for (int i = skeletons.Count - 1; i >= 0; i--)
            {
                var s = skeletons[i];
                if (s.State == SkeletonState.Arriving)
                {
                    if (Time >= s.ArriveAt)
                        Enter(s, SkeletonState.Walking);
                    continue;
                }
                if (s.State == SkeletonState.Dying)
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

        void Think(Skeleton s, Vector2 hero, float dt, ref bool vulnerable)
        {
            var to = hero - s.Position;
            var distance = to.Length();
            var toward = distance > 1e-4f ? to / distance : Vector2.UnitY;
            var telegraphs = Mode.FodderTelegraphs || s.Empowered;
            var face = toward;
            switch (s.State)
            {
                case SkeletonState.Walking:
                    var stop = telegraphs ? AttackRange * 0.75f * s.Scale : 0f;
                    if (distance > stop + s.Radius + HeroRadius)
                    {
                        var step = Mode.WalkSpeed * dt;
                        s.Position += toward * step;
                        s.Walked += step;
                    }
                    if (
                        telegraphs
                        && distance <= AttackRange * s.Scale + HeroRadius
                        && s.Clock >= s.NextAttack
                        && Tokens.TryTake(s.Id)
                    )
                    {
                        Enter(s, SkeletonState.WindingUp);
                        s.Windup =
                            Mode.TelegraphMin
                            + (float)rng.NextDouble() * (Mode.TelegraphMax - Mode.TelegraphMin);
                        s.AttackDirection = toward;
                        Emit(RoomEventKind.Windup, s, s.Position, toward);
                    }
                    else if (
                        !s.Empowered
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
                                Skeleton = s,
                                Position = s.Position,
                                Direction = toward,
                                Amount = rng.Next(Mode.ContactDamageMin, Mode.ContactDamageMax + 1),
                            }
                        );
                    }
                    break;
                case SkeletonState.WindingUp:
                    face = s.AttackDirection;
                    if (s.Age >= s.Windup)
                    {
                        Strike(s, hero, ref vulnerable);
                        Enter(s, SkeletonState.Recovering);
                    }
                    break;
                case SkeletonState.Recovering:
                    face = s.AttackDirection;
                    if (s.Age >= RecoverSeconds)
                        Enter(s, SkeletonState.Walking);
                    break;
                case SkeletonState.Staggered:
                    face = new Vector2(MathF.Sin(s.Yaw), MathF.Cos(s.Yaw));
                    if (s.Age >= StaggerSeconds)
                        Enter(s, SkeletonState.Walking);
                    break;
            }
            s.Position += s.Knock * dt;
            s.Knock *= MathF.Exp(-KnockDamping * dt);
            var yaw = MathF.Atan2(face.X, face.Y);
            var turn = Wrap(yaw - s.Yaw);
            s.Yaw = Wrap(s.Yaw + Math.Clamp(turn, -TurnRate * dt, TurnRate * dt));
        }

        void Strike(Skeleton s, Vector2 hero, ref bool vulnerable)
        {
            Tokens.Release(s.Id);
            s.NextAttack = s.Clock + 1.4f + (float)rng.NextDouble();
            Emit(RoomEventKind.Attack, s, s.Position, s.AttackDirection);
            var to = hero - s.Position;
            var distance = to.Length();
            var angle =
                distance > 1e-4f
                    ? MathF.Acos(Math.Clamp(Vector2.Dot(to / distance, s.AttackDirection), -1f, 1f))
                    : 0f;
            if (
                !vulnerable
                || distance > AttackReach * s.Scale + HeroRadius
                || angle > AttackHalfAngle
            )
                return;
            vulnerable = false;
            Events.Add(
                new RoomEvent
                {
                    Kind = RoomEventKind.HeroHit,
                    Skeleton = s,
                    Position = s.Position,
                    Direction = s.AttackDirection,
                    Amount = s.Empowered ? Mode.AttackDamage * 3 / 2 : Mode.AttackDamage,
                    Telegraphed = true,
                }
            );
        }

        /// <summary>
        /// A sword move's contact: every skeleton in the fight it reaches takes its damage (armor
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
            foreach (var s in skeletons)
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
                        Skeleton = s,
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
                    Enter(s, SkeletonState.Staggered);
                }
            }
            if (kills > 0)
                End();
            return (hits, kills);
        }

        void Die(Skeleton s, Vector2 blow, float force, float breakAt)
        {
            Tokens.Release(s.Id);
            s.Blow = blow;
            s.Force = force;
            s.BreakAt = breakAt;
            s.State = SkeletonState.Dying;
        }

        void Break(int index)
        {
            var s = skeletons[index];
            Emit(RoomEventKind.Shatter, s, s.Position, s.Blow);
            skeletons.RemoveAt(index);
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
                var deficit = Mode.Alive - Alive;
                spawnBudget = Math.Min(spawnBudget + Mode.Alive / 2.5f * dt, deficit);
                while (spawnBudget >= 1f)
                {
                    var pack = Math.Min((int)spawnBudget, 3 + rng.Next(4));
                    Pack(hero, pack, false);
                    spawnBudget -= pack;
                }
                if (Mode.EliteEvery > 0f && Time >= nextElite)
                {
                    nextElite = Time + Mode.EliteEvery;
                    int elites = 0;
                    foreach (var s in skeletons)
                        if (s.Empowered && s.State != SkeletonState.Dying)
                            elites++;
                    if (elites < Mode.ElitesAlive)
                        Pack(hero, 1, true);
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
                    Pack(hero, pack, false);
                    left -= pack;
                }
            }
        }

        // A pack arrives together at one spot on the room's edge, at least MinSpawnDistance from the hero.
        void Pack(Vector2 hero, int count, bool empowered)
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
            for (int k = 0; k < count; k++)
            {
                var angle = (float)rng.NextDouble() * MathF.PI * 2f;
                var spread = k == 0 ? 0f : 0.9f + (float)rng.NextDouble() * 1.2f;
                var p = at + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * spread;
                p = Vector2.Clamp(p, new Vector2(-half + 0.5f), new Vector2(half - 0.5f));
                var to = hero - p;
                var s = new Skeleton
                {
                    Id = nextId++,
                    Empowered = empowered,
                    Position = p,
                    Yaw = MathF.Atan2(to.X, to.Y),
                    Scale = empowered ? 1.5f : 1f,
                    MaxHp = empowered ? Mode.SkeletonHp * 5 / 2 : Mode.SkeletonHp,
                    MaxArmor = empowered ? 30 : 0,
                    State = SkeletonState.Arriving,
                    MarkerSeconds = marker,
                    ArriveAt = Time + marker,
                    Seed = (uint)rng.Next(),
                };
                s.Hp = s.MaxHp;
                s.Armor = s.MaxArmor;
                s.NextAttack = 0.3f + (float)rng.NextDouble() * 0.8f;
                skeletons.Add(s);
                Emit(RoomEventKind.Marker, s, p, Vector2.Zero);
            }
        }

        // The buzzer: spawning stops, markers vanish, and every plain survivor shatters in one wave
        // outward from the hero; elites fight on.
        void Buzzer(Vector2 hero)
        {
            Phase = RoomPhase.Buzzer;
            Emit(RoomEventKind.Buzzer, null, hero, Vector2.Zero);
            for (int i = skeletons.Count - 1; i >= 0; i--)
            {
                var s = skeletons[i];
                if (s.Empowered || s.State == SkeletonState.Dying)
                    continue;
                if (s.State == SkeletonState.Arriving)
                {
                    skeletons.RemoveAt(i);
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

        void Enter(Skeleton s, SkeletonState state)
        {
            s.State = state;
            s.Since = s.Clock;
        }

        void Emit(RoomEventKind kind, Skeleton s, Vector2 at, Vector2 direction) =>
            Events.Add(
                new RoomEvent
                {
                    Kind = kind,
                    Skeleton = s,
                    Position = at,
                    Direction = direction,
                }
            );

        // Keeps a gap between bodies, the hero's body clear and everyone in the room: each pair of
        // overlapping neighbours moves apart by half the overlap, once a step.
        void Separate(Vector2 hero)
        {
            var half = Mode.ArenaHalfSize - 0.4f;
            foreach (var s in skeletons)
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
                        if (o.Id <= s.Id || !o.Fighting)
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
            foreach (var s in skeletons)
            {
                if (!s.Fighting)
                    continue;
                var (cx, cy) = CellOf(s.Position);
                var key = Key(cx, cy);
                if (!grid.TryGetValue(key, out var list))
                    grid[key] = list = new List<Skeleton>(8);
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
