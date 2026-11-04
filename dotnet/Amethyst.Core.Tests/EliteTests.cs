using System.Linq;
using System.Numerics;
using Amethyst.Action;
using NUnit.Framework;

namespace Amethyst.Tests
{
    /// <summary>Empowered elites of every kind (docs/game-design.md, Enemies &gt; Elites).</summary>
    public sealed class EliteTests
    {
        const float Dt = 1f / 60f;

        // A room of one Empowered `kind` and nothing else, stepped until it is in the fight, with
        // the hero far off.
        static (CombatRoom Room, Enemy Elite) Elite(CombatMode mode, EnemyKind kind)
        {
            var tuning = ModeTuning.For(mode).Copy();
            tuning.WaveMin = tuning.WaveMax = tuning.Waves = 1;
            tuning.RoomElites = 1;
            tuning.Alive = mode == CombatMode.Horde ? 0 : 1;
            tuning.ElitesAlive = 1;
            tuning.EliteEvery = 0.1f;
            tuning.RoomSeconds = 0f;
            var room = new CombatRoom(tuning, 5, new SpawnList(new[] { (kind, 1f) }, kind));
            for (int f = 0; f < 60 * 10 && !room.Enemies.Any(e => e.Fighting); f++)
                room.Step(Dt, new Vector2(0f, -30f), false);
            return (room, room.Enemies.Single(e => e.Fighting));
        }

        static void HitWith(CombatRoom room, Enemy enemy, Move move)
        {
            var strike = Strike.Of(move);
            strike.Origin = enemy.Position - Vector2.UnitY;
            strike.Direction = Vector2.UnitY;
            room.Hit(strike);
        }

        [TestCase(CombatMode.Focused)]
        [TestCase(CombatMode.Horde)]
        public void EveryKindComesEmpoweredAndIgnoresStaggerAndKnockbackWhileArmored(
            CombatMode mode
        )
        {
            foreach (var kind in EnemyKinds.All)
            {
                var (room, elite) = Elite(mode, kind);
                Assert.That(elite.Empowered, Is.True, kind.Id);
                Assert.That(elite.Scale, Is.EqualTo(1.5f), kind.Id);
                Assert.That(elite.Armored, Is.True, kind.Id);
                HitWith(room, elite, Move.CleaveC);
                Assert.That(elite.Hp, Is.EqualTo(elite.MaxHp), kind.Id);
                Assert.That(elite.State, Is.Not.EqualTo(EnemyState.Staggered), kind.Id);
                Assert.That(elite.Knock, Is.EqualTo(Vector2.Zero), kind.Id);
                room.Events.Clear();
                while (elite.Armored)
                    HitWith(room, elite, Move.CleaveA);
                Assert.That(
                    room.Events.Count(e => e.Kind == RoomEventKind.ArmorBreak),
                    Is.EqualTo(1),
                    kind.Id
                );
            }
        }

        [Test]
        public void EmpoweredFodderTelegraphsInTheHordeWherePlainFodderDoesNot()
        {
            var (room, elite) = Elite(CombatMode.Horde, EnemyKinds.Skeleton);
            Assert.That(room.Mode.FodderTelegraphs, Is.False);
            bool woundUp = false;
            for (int f = 0; f < 60 * 20 && !woundUp; f++)
            {
                room.Step(Dt, Vector2.Zero, false);
                woundUp = room.Events.Any(e => e.Kind == RoomEventKind.Windup && e.Enemy == elite);
                room.Events.Clear();
            }
            Assert.That(woundUp, Is.True);
        }

        [Test]
        public void AtTheBuzzerOnlyElitesSurviveAndTheRoomWaitsForTheLast()
        {
            var tuning = ModeTuning.Horde.Copy();
            tuning.Alive = 40;
            tuning.RoomSeconds = 8f;
            tuning.ElitesAlive = EnemyKinds.All.Count;
            tuning.EliteEvery = 0.5f;
            tuning.SpecialWeight = 1f;
            var spawns = new SpawnList(
                new[]
                {
                    (EnemyKinds.Skeleton, 1f),
                    (EnemyKinds.Goblin, 1f),
                    (EnemyKinds.BigOrk, 1f),
                    (EnemyKinds.Troll, 1f),
                },
                EnemyKinds.All.ToArray()
            );
            var room = new CombatRoom(tuning, 9, spawns);
            var hero = Vector2.Zero;
            while (room.Phase == RoomPhase.Fighting)
                room.Step(Dt, hero, false);
            var standing = room.Enemies.Where(e => e.State != EnemyState.Dying).ToList();
            Assert.That(standing, Has.All.Matches<Enemy>(e => e.Empowered));
            Assert.That(
                standing.Select(e => e.Kind).Distinct(),
                Is.EquivalentTo(EnemyKinds.All),
                "an elite of every kind, in turn"
            );
            Assert.That(
                room.Enemies.Where(e => !e.Empowered),
                Has.All.Matches<Enemy>(e => e.State == EnemyState.Dying),
                "plain heavies shatter with the fodder"
            );
            Assert.That(room.Enemies.Any(e => !e.Empowered && e.Kind.Armored), Is.True);
            for (int f = 0; f < 60; f++)
                room.Step(Dt, hero, false);
            Assert.That(room.Phase, Is.EqualTo(RoomPhase.Buzzer), "the room waits for its elites");
            foreach (var elite in standing)
            {
                Assert.That(room.Phase, Is.EqualTo(RoomPhase.Buzzer));
                elite.Armor = 0;
                elite.Hp = 1;
                HitWith(room, elite, Move.CleaveA);
            }
            Assert.That(room.Phase, Is.EqualTo(RoomPhase.Cleared));
        }
    }
}
