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
        public AttackKind Attack;

        /// <summary>Its attack's reach at scale 1, m; its wind-up starts from 0.8 of it.</summary>
        public float Reach = 2f;

        /// <summary>How its horde packs arrive.</summary>
        public Formation Formation;

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

        public static readonly IReadOnlyList<EnemyKind> All = new[] { Skeleton, Slime, Demon, Bat };

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
    /// weight, each Empowered one's from <see cref="Elites"/>. The mode's tuning sets how many.
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

        /// <summary>A pack's kind; a list of one kind draws nothing from <paramref name="rng"/>.</summary>
        public EnemyKind Pick(Random rng)
        {
            if (Mix.Length == 1)
                return Mix[0].Kind;
            float total = 0f;
            foreach (var (_, w) in Mix)
                total += w;
            var at = (float)rng.NextDouble() * total;
            foreach (var (kind, w) in Mix)
            {
                at -= w;
                if (at < 0f)
                    return kind;
            }
            return Mix[Mix.Length - 1].Kind;
        }

        public EnemyKind PickElite(Random rng) =>
            Elites.Length == 1 ? Elites[0] : Elites[rng.Next(Elites.Length)];
    }
}
