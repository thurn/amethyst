using System;

namespace Amethyst.Action
{
    /// <summary>The two combat modes and the blind pick between them (docs/game-design.md, Two
    /// combat modes).</summary>
    public enum CombatMode
    {
        Focused,
        Horde,
        Blind,
    }

    /// <summary>
    /// Everything that differs by combat mode, one row per mode (docs/game-design.md, Two combat
    /// modes), read once when a run starts. Later milestones add fields and rows, not switches.
    /// Times are seconds, distances metres, hitstop frames at 60 Hz.
    /// </summary>
    public sealed class ModeTuning
    {
        public CombatMode Mode;

        /// <summary>The room's half width: enemies arrive from its edges.</summary>
        public float ArenaHalfSize;

        /// <summary>The camera's distance from the hero.</summary>
        public float CameraDistance;

        /// <summary>Skeletons alive at once: the stream's target, or the most a wave brings.</summary>
        public int Alive;

        /// <summary>Focused: skeletons per wave, at least and at most, and the waves.</summary>
        public int WaveMin,
            WaveMax,
            Waves;

        /// <summary>Focused: the next wave comes when this many of the last are left.</summary>
        public int WaveCallAt;

        /// <summary>Horde: the room's timer; at its buzzer the survivors shatter. 0 for no timer.</summary>
        public float RoomSeconds;

        /// <summary>Horde: seconds between Empowered skeletons, 0 for none, and the most alive.</summary>
        public float EliteEvery;
        public int ElitesAlive;

        /// <summary>A skeleton's HP: 2-4 cleaves focused, 1-2 in the horde.</summary>
        public int SkeletonHp;

        public float WalkSpeed;

        /// <summary>Whether plain skeletons wind up a telegraphed Attack 1 (focused: every enemy).</summary>
        public bool FodderTelegraphs;

        /// <summary>Damage a plain skeleton deals by walking into the hero, 0 for none.</summary>
        public int ContactDamageMin,
            ContactDamageMax;

        /// <summary>Damage a telegraphed Attack 1 deals.</summary>
        public int AttackDamage;

        /// <summary>The telegraph's wind-up, at least and at most (feel §6.1: 400-600 ms melee).</summary>
        public float TelegraphMin,
            TelegraphMax;

        /// <summary>Whether every hit staggers a plain skeleton (focused) or only knocks it back.</summary>
        public bool Stagger;

        /// <summary>Enemies winding up at once at most (feel §6.4).</summary>
        public int AggressionTokens;

        /// <summary>Per-target hitstop on fodder: frames on a light hit, a finisher and a kill, and
        /// on the hero for a light hit; the cap (feel §3.1).</summary>
        public int HitstopLight,
            HitstopFinisher,
            HitstopKill,
            HitstopHero,
            HitstopCap;

        /// <summary>Global freezes, frames: a room's clear and an Empowered kill.</summary>
        public int FreezeClear,
            FreezeElite;

        /// <summary>Shards per kill, on average (later milestones drop them).</summary>
        public float ShardsPerKill;

        /// <summary>Upgrade counts' and ranges' scale (later milestones read them).</summary>
        public float UpgradeCounts,
            UpgradeRanges;

        public static readonly ModeTuning Focused = new ModeTuning
        {
            Mode = CombatMode.Focused,
            ArenaHalfSize = 11f,
            CameraDistance = 14f,
            Alive = 6,
            WaveMin = 4,
            WaveMax = 6,
            Waves = 2,
            WaveCallAt = 2,
            SkeletonHp = 30,
            WalkSpeed = 1.9f,
            FodderTelegraphs = true,
            AttackDamage = 12,
            TelegraphMin = 0.45f,
            TelegraphMax = 0.55f,
            Stagger = true,
            AggressionTokens = 2,
            HitstopLight = 3,
            HitstopFinisher = 6,
            HitstopKill = 6,
            HitstopHero = 1,
            HitstopCap = 12,
            FreezeClear = 16,
            FreezeElite = 12,
            ShardsPerKill = 1.5f,
            UpgradeCounts = 0.5f,
            UpgradeRanges = 0.7f,
        };

        public static readonly ModeTuning Horde = new ModeTuning
        {
            Mode = CombatMode.Horde,
            ArenaHalfSize = 20f,
            CameraDistance = 34f,
            Alive = 100,
            RoomSeconds = 60f,
            EliteEvery = 15f,
            ElitesAlive = 1,
            SkeletonHp = 12,
            WalkSpeed = 2.2f,
            ContactDamageMin = 4,
            ContactDamageMax = 6,
            AttackDamage = 14,
            TelegraphMin = 0.5f,
            TelegraphMax = 0.6f,
            AggressionTokens = 3,
            HitstopLight = 1,
            HitstopFinisher = 2,
            HitstopKill = 2,
            HitstopCap = 2,
            FreezeClear = 18,
            FreezeElite = 12,
            ShardsPerKill = 1f / 3f,
            UpgradeCounts = 1f,
            UpgradeRanges = 1f,
        };

        /// <summary>The row of a picked mode; pick <see cref="CombatMode.Blind"/> first.</summary>
        public static ModeTuning For(CombatMode mode) =>
            mode switch
            {
                CombatMode.Focused => Focused,
                CombatMode.Horde => Horde,
                _ => throw new ArgumentException("pick the blind mode first (CombatModes.Pick)"),
            };

        /// <summary>A copy, for a run that overrides a field from a flag.</summary>
        public ModeTuning Copy() => (ModeTuning)MemberwiseClone();
    }

    public static class CombatModes
    {
        /// <summary>
        /// The mode a run plays: <paramref name="mode"/> itself, or for <see cref="CombatMode.Blind"/>
        /// focused or horde by <paramref name="seed"/> (one bit of a hash, so neighbouring seeds
        /// don't alternate), so the player doesn't choose which they compare (feel §11.3).
        /// </summary>
        public static CombatMode Pick(CombatMode mode, ulong seed)
        {
            if (mode != CombatMode.Blind)
                return mode;
            var h = seed * 0x9E3779B97F4A7C15ul;
            h ^= h >> 31;
            h *= 0xBF58476D1CE4E5B9ul;
            h ^= h >> 29;
            return (h & 1) == 0 ? CombatMode.Focused : CombatMode.Horde;
        }
    }
}
