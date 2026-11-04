using System;
using System.Collections.Generic;

namespace Amethyst.Action
{
    /// <summary>The roster's palette families (docs/game-design.md, Look &gt; Color roles).</summary>
    public enum EnemyFamily
    {
        /// <summary>Fodder: cyan and blue.</summary>
        Key,

        /// <summary>Special types: magenta and violet.</summary>
        Accent,

        /// <summary>Heavies and the Warden: orange and amber.</summary>
        Counter,
    }

    public enum EnemyRole
    {
        /// <summary>Walks into the hero in the horde; telegraphs only focused or Empowered.</summary>
        Fodder,

        /// <summary>Telegraphs every attack in both modes.</summary>
        Special,

        /// <summary>Telegraphs every attack, always armored.</summary>
        Heavy,
    }

    /// <summary>
    /// How an enemy attacks once it holds a token: <see cref="CombatRoom"/> dispatches on it for
    /// the reach its wind-up starts from and what its impact does, and the view for its telegraph.
    /// </summary>
    public enum AttackKind
    {
        /// <summary>A cone in front of it, <see cref="EnemyKind.Reach"/> deep, at the clip's impact:
        /// the skeleton's Attack 1, the slime's Spit Attack.</summary>
        Cone,

        /// <summary>A crouch behind a red lane marker <see cref="EnemyKind.Reach"/> long, then a
        /// dash along it that hits whatever is in it: the goblin's.</summary>
        Lunge,

        /// <summary>Slow spores lofted at the hero's spot under a filling red circle, from
        /// <see cref="EnemyKind.Reach"/> away: the mushroom's.</summary>
        Volley,

        /// <summary>A filling red ring at the hero's spot, at most <see cref="EnemyKind.Reach"/>
        /// away, then a leap there whose landing hits whatever is in the ring: the troll's.</summary>
        Leap,
    }

    /// <summary>How a horde pack arrives (the trailer's crowd shapes); focused waves come in packs.</summary>
    public enum Formation
    {
        /// <summary>Bunched together at one spot on the room's edge.</summary>
        Pack,

        /// <summary>Spread around the hero, closing in from every side.</summary>
        Ring,

        /// <summary>Single file out of one spot on the edge, one after another.</summary>
        Stream,
    }

    /// <summary>One roster type's numbers; mode-dependent ones are multiples of <see cref="ModeTuning"/>'s.</summary>
    public sealed class EnemyKind
    {
        /// <summary>Its crowd type id (packages/com.amethyst.look/Editor/roster.json).</summary>
        public string Id;
        public EnemyFamily Family;
        public EnemyRole Role;

        /// <summary>HP, times the mode's <see cref="ModeTuning.BasicHp"/>.</summary>
        public float Hp = 1f;

        /// <summary>Walk speed, times the mode's <see cref="ModeTuning.WalkSpeed"/>.</summary>
        public float Speed = 1f;

        /// <summary>Its body's radius at scale 1, m.</summary>
        public float Radius = 0.35f;

        /// <summary>Flies over what blocks walkers (pillars).</summary>
        public bool Flies;

        /// <summary>Carries an armor bar even when not Empowered.</summary>
        public bool Armored;

        /// <summary>Its telegraph's wind-up, s; 0 for the mode's.</summary>
        public float Windup;

        /// <summary>A volley's spores' speed over the ground, m/s: at most half the dash's.</summary>
        public float ProjectileSpeed = 8f;
        public AttackKind Attack;

        /// <summary>Its attack's reach at scale 1, m; its wind-up starts from 0.8 of it.</summary>
        public float Reach = 2f;

        /// <summary>How its horde packs arrive.</summary>
        public Formation Formation;

        /// <summary>Its share of a drawn schedule's mix, before the mode's
        /// <see cref="ModeTuning.SpecialWeight"/>.</summary>
        public float Weight = 1f;

        public override string ToString() => Id;
    }

    /// <summary>The roster (docs/game-design.md, Enemies &gt; Roster), one row per type.</summary>
    public static class EnemyKinds
    {
        public static readonly EnemyKind Skeleton = new EnemyKind
        {
            Id = "skeleton",
            Family = EnemyFamily.Key,
            Role = EnemyRole.Fodder,
            Attack = AttackKind.Cone,
        };

        /// <summary>Slow, tough fodder: soaks hits; spits a short cone.</summary>
        public static readonly EnemyKind Slime = new EnemyKind
        {
            Id = "slime",
            Weight = 0.6f,
            Family = EnemyFamily.Key,
            Role = EnemyRole.Fodder,
            Hp = 2f,
            Speed = 0.6f,
            Radius = 0.7f,
            Attack = AttackKind.Cone,
            Reach = 3f,
            Formation = Formation.Ring,
        };

        /// <summary>Fast fodder: closes in quickly.</summary>
        public static readonly EnemyKind Demon = new EnemyKind
        {
            Id = "demon",
            Weight = 0.8f,
            Family = EnemyFamily.Key,
            Role = EnemyRole.Fodder,
            Speed = 1.6f,
            Radius = 0.4f,
            Attack = AttackKind.Cone,
        };

        /// <summary>A flying swarm: frail, arriving in streams that pass over pillars.</summary>
        public static readonly EnemyKind Bat = new EnemyKind
        {
            Id = "bat",
            Weight = 0.8f,
            Family = EnemyFamily.Key,
            Role = EnemyRole.Fodder,
            Hp = 0.5f,
            Speed = 1.3f,
            Radius = 0.3f,
            Flies = true,
            Attack = AttackKind.Cone,
            Reach = 1.5f,
            Formation = Formation.Stream,
        };

        /// <summary>A rusher: crouches 300 ms behind its lane marker, then lunges along it.</summary>
        public static readonly EnemyKind Goblin = new EnemyKind
        {
            Id = "goblin",
            Weight = 0.6f,
            Family = EnemyFamily.Accent,
            Role = EnemyRole.Special,
            Speed = 1.3f,
            Radius = 0.35f,
            Attack = AttackKind.Lunge,
            Windup = 0.3f,
            Reach = 5f,
        };

        /// <summary>Ranged: hangs back 6-8 m and lofts slow spore volleys at a marked spot.</summary>
        public static readonly EnemyKind Mushroom = new EnemyKind
        {
            Id = "mushroom",
            Weight = 0.5f,
            Family = EnemyFamily.Accent,
            Role = EnemyRole.Special,
            Speed = 0.8f,
            Radius = 0.4f,
            Attack = AttackKind.Volley,
            Windup = 0.4f,
            Reach = 9f,
        };

        /// <summary>A heavy: winds up 500 ms behind a long lane, then lunges along it.</summary>
        public static readonly EnemyKind BigOrk = new EnemyKind
        {
            Id = "bigOrk",
            Weight = 0.4f,
            Family = EnemyFamily.Counter,
            Role = EnemyRole.Heavy,
            Hp = 3f,
            Speed = 0.75f,
            Radius = 0.6f,
            Armored = true,
            Attack = AttackKind.Lunge,
            Windup = 0.5f,
            Reach = 7f,
        };

        /// <summary>A heavy: marks a ring at the hero's spot for 800 ms, then leaps into it.</summary>
        public static readonly EnemyKind Troll = new EnemyKind
        {
            Id = "troll",
            Weight = 0.4f,
            Family = EnemyFamily.Counter,
            Role = EnemyRole.Heavy,
            Hp = 4f,
            Speed = 0.65f,
            Radius = 0.7f,
            Armored = true,
            Attack = AttackKind.Leap,
            Windup = 0.8f,
            Reach = 7f,
        };

        public static readonly IReadOnlyList<EnemyKind> All = new[]
        {
            Skeleton,
            Slime,
            Demon,
            Bat,
            Goblin,
            Mushroom,
            BigOrk,
            Troll,
        };

        public static EnemyKind Of(string id)
        {
            foreach (var k in All)
                if (k.Id == id)
                    return k;
            throw new ArgumentException($"EnemyKinds: no kind '{id}'");
        }
    }

    /// <summary>
    /// What a <see cref="CombatRoom"/> spawns: each pack's kind drawn from <see cref="Mix"/> by
    /// weight, its Empowered ones' from <see cref="Elites"/> in turn. The mode's tuning sets how many.
    /// </summary>
    public sealed class SpawnList
    {
        public readonly (EnemyKind Kind, float Weight)[] Mix;
        public readonly EnemyKind[] Elites;

        public SpawnList((EnemyKind Kind, float Weight)[] mix, params EnemyKind[] elites)
        {
            if (mix.Length == 0)
                throw new ArgumentException("SpawnList: an empty mix");
            Mix = mix;
            Elites = elites.Length > 0 ? elites : new[] { mix[0].Kind };
        }

        /// <summary>M1's rooms: skeletons, and Empowered skeletons.</summary>
        public static readonly SpawnList Skeletons = new SpawnList(
            new[] { (EnemyKinds.Skeleton, 1f) }
        );

        /// <summary>The key family's fodder, mixed: skeletons, slimes, demons and bats.</summary>
        public static readonly SpawnList KeyFodder = new SpawnList(
            new[]
            {
                (EnemyKinds.Skeleton, 1f),
                (EnemyKinds.Slime, 0.5f),
                (EnemyKinds.Demon, 1f),
                (EnemyKinds.Bat, 0.8f),
            },
            EnemyKinds.Skeleton,
            EnemyKinds.Slime,
            EnemyKinds.Demon
        );

        /// <summary>Skeletons with the counter family's heavies, big orks and trolls, among them.</summary>
        public static readonly SpawnList Heavies = new SpawnList(
            new[] { (EnemyKinds.Skeleton, 1.5f), (EnemyKinds.BigOrk, 1f), (EnemyKinds.Troll, 1f) }
        );

        /// <summary>Key-family fodder with every fodder kind Empowered.</summary>
        public static readonly SpawnList FodderElites = new SpawnList(
            KeyFodder.Mix,
            EnemyKinds.Skeleton,
            EnemyKinds.Slime,
            EnemyKinds.Demon,
            EnemyKinds.Bat
        );

        /// <summary>Key-family fodder with every special and heavy Empowered.</summary>
        public static readonly SpawnList SpecialElites = new SpawnList(
            KeyFodder.Mix,
            EnemyKinds.Goblin,
            EnemyKinds.Mushroom,
            EnemyKinds.BigOrk,
            EnemyKinds.Troll
        );

        /// <summary>A pack's kind, with specials' and heavies' weights times
        /// <paramref name="special"/>; a list of one kind draws nothing from <paramref name="rng"/>.</summary>
        public EnemyKind Pick(Random rng, float special = 1f)
        {
            if (Mix.Length == 1)
                return Mix[0].Kind;
            float total = 0f;
            foreach (var (kind, w) in Mix)
                total += Weight(kind, w, special);
            var at = (float)rng.NextDouble() * total;
            foreach (var (kind, w) in Mix)
            {
                at -= Weight(kind, w, special);
                if (at < 0f)
                    return kind;
            }
            return Mix[Mix.Length - 1].Kind;
        }

        static float Weight(EnemyKind kind, float weight, float special) =>
            kind.Role == EnemyRole.Fodder ? weight : weight * special;

        /// <summary>The <paramref name="n"/>th Empowered one's kind: the elites in turn, so each
        /// kind comes before any repeats.</summary>
        public EnemyKind PickElite(int n) =>
            Elites[(n % Elites.Length + Elites.Length) % Elites.Length];
    }
}
