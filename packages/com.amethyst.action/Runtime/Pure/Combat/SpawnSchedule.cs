using System;
using System.Collections.Generic;
using System.Linq;

namespace Amethyst.Action
{
    /// <summary>
    /// What one <see cref="CombatRoom"/> spawns and when (docs/game-design.md, Variety between
    /// runs; Enemies &gt; Escalation): its kinds and their mix, each kind's horde formation, the
    /// room edges enemies arrive from, the focused waves' sizes or the horde stream's target and
    /// how it builds through the timer, its Empowered ones, and caps on specials and heavies.
    /// <see cref="Draw"/> draws one from a mode's Escalation row by a seed, so the same module
    /// plays differently; <see cref="Of"/> makes one from the mode's own numbers and a list.
    /// </summary>
    public sealed class SpawnSchedule
    {
        public ModeTuning Mode;
        public SpawnList Spawns;

        /// <summary>Enemies alive at once: focused, the most; horde, the stream's target.</summary>
        public int Alive;

        /// <summary>Focused: each wave's enemies, its Empowered ones included.</summary>
        public int[] WaveSizes = Array.Empty<int>();

        /// <summary>Focused: Empowered enemies, part of the first wave.</summary>
        public int Elites;

        /// <summary>Horde: the room's timer, s, until its buzzer; 0 for none.</summary>
        public float Seconds;

        /// <summary>Horde: the stream's target starts at this share of <see cref="Alive"/> and
        /// builds to all of it by <see cref="PressureFull"/> of the timer (0: all at once).</summary>
        public float PressureStart = 1f,
            PressureFull;

        /// <summary>Horde: the first Empowered one's time, s, the seconds between them, the most
        /// alive at once, and how many the room brings.</summary>
        public float EliteFirst,
            EliteEvery;
        public int ElitesAlive,
            EliteTotal = int.MaxValue;

        /// <summary>The room's sides enemies arrive from, a bit each: +y, -y, +x, -x.</summary>
        public int Edges = 0b1111;

        /// <summary>Plain specials and heavies alive at once at most.</summary>
        public int SpecialsAlive = int.MaxValue,
            HeaviesAlive = int.MaxValue;

        /// <summary>The Escalation row it was drawn from, -1 for none.</summary>
        public int Row = -1;

        readonly Dictionary<EnemyKind, Formation> formations =
            new Dictionary<EnemyKind, Formation>();

        /// <summary>Focused: the waves.</summary>
        public int Waves => WaveSizes.Length;

        /// <summary>Focused: the enemies over every wave.</summary>
        public int Total => WaveSizes.Sum();

        /// <summary>How a horde pack of <paramref name="kind"/> arrives.</summary>
        public Formation FormationOf(EnemyKind kind) =>
            formations.TryGetValue(kind, out var f) ? f : kind.Formation;

        /// <summary>A room of <paramref name="spawns"/> by the mode's own numbers: waves of
        /// <see cref="ModeTuning.WaveMin"/>-<see cref="ModeTuning.WaveMax"/>, or its stream and
        /// its elites every <see cref="ModeTuning.EliteEvery"/> s.</summary>
        public static SpawnSchedule Of(ModeTuning mode, SpawnList spawns, int seed)
        {
            var rng = new Random(seed);
            var waves = new int[mode.Waves];
            for (int w = 0; w < waves.Length; w++)
                waves[w] = Math.Min(
                    mode.Alive,
                    mode.WaveMin + rng.Next(mode.WaveMax - mode.WaveMin + 1)
                );
            return new SpawnSchedule
            {
                Mode = mode,
                Spawns = spawns,
                Alive = mode.Alive,
                WaveSizes = waves,
                Elites = mode.RoomElites,
                Seconds = mode.RoomSeconds,
                EliteFirst = mode.EliteEvery * 0.5f,
                EliteEvery = mode.EliteEvery,
                ElitesAlive = mode.ElitesAlive,
            };
        }

        /// <summary>
        /// Room <paramref name="room"/>'s schedule by <paramref name="mode"/>'s Escalation row,
        /// drawn by <paramref name="seed"/>: the row's kinds so far, weighted by kind and jittered,
        /// the ones it brings in featured and an older fodder kind sometimes left out; walking
        /// fodder's formations, mostly its kind's own (flyers keep their streams); two to four
        /// edges; the row's waves, enemies and elites (any kind it has may come Empowered), or its
        /// timer, target and elites.
        /// </summary>
        public static SpawnSchedule Draw(ModeTuning mode, int room, int seed)
        {
            var row = mode.Room(room);
            var rng = new Random(seed * 31 + room);
            var kinds = mode.KindsBy(room);
            var schedule = new SpawnSchedule
            {
                Mode = mode,
                Row = room,
                Alive = row.Alive,
                SpecialsAlive = mode.SpecialsAlive,
                HeaviesAlive = mode.HeaviesAlive,
            };

            var fodder = kinds.Where(k => k.Role == EnemyRole.Fodder && !row.Adds.Contains(k));
            var left =
                fodder.Count() >= 3 && rng.NextDouble() < 0.5
                    ? fodder.ElementAt(rng.Next(fodder.Count()))
                    : null;
            var mix = new List<(EnemyKind, float)>();
            foreach (var k in kinds)
            {
                if (k == left)
                    continue;
                var w = k.Weight * (0.6f + 0.8f * (float)rng.NextDouble());
                if (row.Adds.Contains(k) && room > 0)
                    w *= 2f;
                mix.Add((k, w));
                if (k.Role == EnemyRole.Fodder && !k.Flies && rng.NextDouble() < 0.4)
                    schedule.formations[k] = (Formation)rng.Next(3);
            }
            var mixed = mix.Select(m => m.Item1).ToList();
            var elites = row.ElitesMin + rng.Next(row.ElitesMax - row.ElitesMin + 1);
            var elitePool = mixed.OrderBy(_ => rng.Next()).ToArray();
            var eliteKinds = new EnemyKind[Math.Max(1, elites)];
            for (int e = 0; e < eliteKinds.Length; e++)
                eliteKinds[e] = elitePool[e % elitePool.Length];
            schedule.Spawns = new SpawnList(mix.ToArray(), eliteKinds);

            var sides = Enumerable.Range(0, 4).OrderBy(_ => rng.Next()).Take(2 + rng.Next(3));
            schedule.Edges = sides.Aggregate(0, (m, s) => m | 1 << s);

            if (mode.Mode == CombatMode.Horde)
            {
                schedule.Seconds = row.Seconds;
                schedule.PressureStart = mode.PressureStart;
                schedule.PressureFull = mode.PressureFull;
                schedule.EliteTotal = schedule.ElitesAlive = elites;
                schedule.EliteFirst = row.Seconds * 0.2f;
                schedule.EliteEvery = elites > 0 ? row.Seconds * 0.3f / elites : 0f;
            }
            else
            {
                schedule.Elites = elites;
                var waves = row.WavesMin + rng.Next(row.WavesMax - row.WavesMin + 1);
                var total = row.EnemiesMin + rng.Next(row.EnemiesMax - row.EnemiesMin + 1);
                schedule.WaveSizes = Split(total, waves, row.Alive, mode.WaveCallAt, rng);
            }
            return schedule;
        }

        // Splits `total` enemies into `waves`, about evenly with a little drawn unevenness, so
        // none overflows `alive`: the first wave comes into an empty room, each later one while
        // `callAt` of the last are still standing.
        static int[] Split(int total, int waves, int alive, int callAt, Random rng)
        {
            var sizes = new int[waves];
            var caps = new int[waves];
            for (int w = 0; w < waves; w++)
            {
                sizes[w] = total / waves + (w < total % waves ? 1 : 0);
                caps[w] = w == 0 ? alive : Math.Max(1, alive - callAt);
            }
            for (int k = 0; k < waves * 2; k++)
            {
                int a = rng.Next(waves),
                    b = rng.Next(waves);
                if (a != b && sizes[a] > callAt + 1 && sizes[b] < caps[b])
                {
                    sizes[a]--;
                    sizes[b]++;
                }
            }
            for (int w = 0; w < waves; w++)
                while (sizes[w] > caps[w])
                {
                    var to = -1;
                    for (int j = 0; j < waves && to < 0; j++)
                        if (sizes[j] < caps[j])
                            to = j;
                    if (to < 0)
                        throw new ArgumentException(
                            $"SpawnSchedule: {total} enemies don't fit {waves} waves of {alive} alive"
                        );
                    sizes[w]--;
                    sizes[to]++;
                }
            return sizes;
        }

        public override string ToString()
        {
            var mix = string.Join(
                ", ",
                Spawns.Mix.Select(m => $"{m.Kind.Id} {m.Weight:F2} {FormationOf(m.Kind)}")
            );
            var edges = string.Concat(
                new[] { "+y", "-y", "+x", "-x" }.Where((_, i) => (Edges & 1 << i) != 0)
            );
            var numbers =
                Mode.Mode == CombatMode.Horde
                    ? $"{Seconds:F0} s, {Alive} alive, {(EliteTotal == int.MaxValue ? ElitesAlive : EliteTotal)} elites"
                    : $"waves {string.Join("+", WaveSizes)}, {Alive} alive, {Elites} elites";
            var elites = string.Join(", ", Spawns.Elites.Select(k => k.Id));
            return $"row {Row}: {numbers} ({elites}); edges {edges}; mix {mix}";
        }
    }
}
