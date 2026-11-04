using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Amethyst.Action;
using NUnit.Framework;

namespace Amethyst.Tests
{
    /// <summary>Spawn schedules drawn from the Escalation tables (docs/game-design.md, Enemies &gt;
    /// Escalation), played through <see cref="CombatRoom"/>.</summary>
    public sealed class EscalationTests
    {
        const int Rows = 5;

        static void Kill(CombatRoom room, Enemy enemy)
        {
            enemy.Armor = 0;
            enemy.Hp = 1;
            var strike = Strike.Of(Move.CleaveA);
            strike.Origin = enemy.Position - Vector2.UnitY;
            strike.Direction = Vector2.UnitY;
            room.Hit(strike);
        }

        // What a room spawned, from its events.
        static void Count(CombatRoom room, List<Enemy> spawned, ref int waves)
        {
            foreach (var e in room.Events)
                if (e.Kind == RoomEventKind.Marker)
                    spawned.Add(e.Enemy);
                else if (e.Kind == RoomEventKind.Wave)
                    waves++;
            room.Events.Clear();
        }

        [Test]
        public void FocusedRoomsBringTheirRowsWavesEnemiesAndElitesWithinItsAliveCap()
        {
            var mode = ModeTuning.Focused;
            for (int index = 0; index < Rows; index++)
            {
                var row = mode.Room(index);
                var allowed = mode.KindsBy(index);
                var drawn = new HashSet<string>();
                for (int seed = 0; seed < 40; seed++)
                {
                    var schedule = SpawnSchedule.Draw(mode, index, seed);
                    drawn.Add(schedule.ToString());
                    var room = new CombatRoom(schedule, seed);
                    var spawned = new List<Enemy>();
                    int waves = 0,
                        peak = 0;
                    for (int f = 0; f < 60 * 120 && room.Phase != RoomPhase.Cleared; f++)
                    {
                        room.Step(1f / 30f, Vector2.Zero, false);
                        peak = System.Math.Max(peak, room.Alive);
                        if (f % 9 == 0 && room.Enemies.FirstOrDefault(e => e.Fighting) is Enemy s)
                            Kill(room, s);
                        Count(room, spawned, ref waves);
                    }
                    var at = $"room {index} seed {seed}: {schedule}";
                    Assert.That(room.Phase, Is.EqualTo(RoomPhase.Cleared), at);
                    Assert.That(waves, Is.InRange(row.WavesMin, row.WavesMax), at);
                    Assert.That(spawned.Count, Is.InRange(row.EnemiesMin, row.EnemiesMax), at);
                    Assert.That(peak, Is.LessThanOrEqualTo(row.Alive), at);
                    Assert.That(
                        spawned.Count(e => e.Empowered),
                        Is.InRange(row.ElitesMin, row.ElitesMax),
                        at
                    );
                    Assert.That(spawned.Select(e => e.Kind).Distinct(), Is.SubsetOf(allowed), at);
                }
                Assert.That(drawn.Count, Is.GreaterThan(1), $"room {index} plays differently");
            }
        }

        [Test]
        public void HordeRoomsBuildToTheirRowsTargetAndEndAtItsBuzzer()
        {
            var mode = ModeTuning.Horde;
            const float dt = 1f / 20f;
            for (int index = 0; index < Rows; index++)
            {
                var row = mode.Room(index);
                var allowed = mode.KindsBy(index);
                for (int seed = 0; seed < 3; seed++)
                {
                    var schedule = SpawnSchedule.Draw(mode, index, seed);
                    var room = new CombatRoom(schedule, seed);
                    var at = $"room {index} seed {seed}: {schedule}";
                    Assert.That(room.TimeLeft, Is.EqualTo(row.Seconds), at);
                    var spawned = new List<Enemy>();
                    int waves = 0,
                        peak = 0;
                    while (room.Phase == RoomPhase.Fighting)
                    {
                        room.Step(dt, Vector2.Zero, false);
                        var plain = room.Enemies.Where(e =>
                            !e.Empowered && e.State != EnemyState.Dying
                        );
                        peak = System.Math.Max(peak, plain.Count());
                        if (room.Time <= row.Seconds * 0.1f)
                            Assert.That(plain.Count(), Is.LessThan(row.Alive * 0.6f), at);
                        Assert.That(
                            plain.Count(e => e.Kind.Role == EnemyRole.Special),
                            Is.LessThanOrEqualTo(mode.SpecialsAlive),
                            at
                        );
                        Assert.That(
                            plain.Count(e => e.Kind.Role == EnemyRole.Heavy),
                            Is.LessThanOrEqualTo(mode.HeaviesAlive),
                            at
                        );
                        Count(room, spawned, ref waves);
                    }
                    Assert.That(room.Time, Is.EqualTo(row.Seconds).Within(dt * 1.5f), at);
                    Assert.That(peak, Is.InRange(row.Alive - 10, row.Alive), at);
                    Assert.That(
                        spawned.Count(e => e.Empowered),
                        Is.InRange(row.ElitesMin, row.ElitesMax),
                        at
                    );
                    Assert.That(spawned.Select(e => e.Kind).Distinct(), Is.SubsetOf(allowed), at);
                    Assert.That(
                        spawned.Count(e => e.Kind.Role == EnemyRole.Fodder),
                        Is.GreaterThan(spawned.Count * 0.85f),
                        "specials and heavies are a few among the fodder"
                    );
                }
            }
        }

        [Test]
        public void NoKindComesBeforeTheRowThatBringsItIn()
        {
            foreach (var mode in new[] { ModeTuning.Focused, ModeTuning.Horde })
                for (int index = 0; index < Rows; index++)
                {
                    var later = Enumerable
                        .Range(index + 1, Rows - index - 1)
                        .SelectMany(i => mode.Room(i).Adds);
                    for (int seed = 0; seed < 200; seed++)
                    {
                        var spawns = SpawnSchedule.Draw(mode, index, seed).Spawns;
                        var kinds = spawns.Mix.Select(m => m.Kind).Concat(spawns.Elites);
                        Assert.That(kinds.Intersect(later), Is.Empty, $"{mode.Mode} room {index}");
                    }
                }
        }
    }
}
