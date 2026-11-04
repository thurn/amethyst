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
        /// <summary>A melee cone in front of it at the clip's impact: the skeleton's Attack 1.</summary>
        Cone,
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

        public static readonly IReadOnlyList<EnemyKind> All = new[] { Skeleton };

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
