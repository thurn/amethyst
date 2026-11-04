using System.Linq;
using System.Numerics;
using Amethyst.Action;
using NUnit.Framework;

namespace Amethyst.Tests
{
    /// <summary>Hazards' rules: walls, pillars, pylons and Spike Vents in a combat room.</summary>
    public sealed class HazardTests
    {
        const float Dt = 1f / 60f;

        static readonly EnemyKind Bat = new EnemyKind
        {
            Id = "bat",
            Role = EnemyRole.Fodder,
            Flies = true,
            Attack = AttackKind.Cone,
        };

        [Test]
        public void WallsKeepTheHeroAndEveryEnemyIn()
        {
            var half = ModeTuning.Horde.ArenaHalfSize;
            var room = new CombatRoom(ModeTuning.Horde, 3);
            var hero = new Vector2(30f, -25f);
            room.Hazards.Collide(ref hero, 0.45f, false);
            Assert.That(hero.X, Is.LessThanOrEqualTo(half - 0.45f + 1e-3f));
            Assert.That(hero.Y, Is.GreaterThanOrEqualTo(-half + 0.45f - 1e-3f));
            for (int f = 0; f < 60 * 15; f++)
            {
                room.Step(Dt, hero, false);
                foreach (var s in room.Enemies)
                    Assert.That(
                        System.Math.Max(
                            System.Math.Abs(s.Position.X),
                            System.Math.Abs(s.Position.Y)
                        ),
                        Is.LessThanOrEqualTo(half - s.Radius + 1e-3f)
                    );
            }
        }

        [Test]
        public void PillarsBlockWalkersButNotBats()
        {
            var layout = HazardLayout.Of(HazardSet.Pillars, ModeTuning.Horde.ArenaHalfSize);
            bool Inside(Enemy s, float slack) =>
                layout.Pillars.Any(p =>
                    Vector2.Distance(p.Center, s.Position) < p.Radius + s.Radius - slack
                );

            var walkers = new CombatRoom(ModeTuning.Horde, 5, null, layout);
            var bats = new CombatRoom(
                ModeTuning.Horde,
                5,
                new SpawnList(new[] { (Bat, 1f) }),
                layout
            );
            bool batOver = false;
            for (int f = 0; f < 60 * 20; f++)
            {
                walkers.Step(Dt, Vector2.Zero, false);
                bats.Step(Dt, Vector2.Zero, false);
                foreach (var s in walkers.Enemies)
                    if (s.Fighting)
                        Assert.That(Inside(s, 1e-3f), Is.False, "a walker inside a pillar");
                batOver |= bats.Enemies.Any(s => s.Fighting && Inside(s, 0.3f));
            }
            Assert.That(batOver, Is.True, "bats fly over pillars");
        }

        [Test]
        public void APylonBurstHurtsEnemiesAndNeverTheHero()
        {
            var layout = HazardLayout.Of(HazardSet.Pillars, ModeTuning.Focused.ArenaHalfSize);
            var pylon = layout.Pylons[0];
            var room = Fighting(new CombatRoom(ModeTuning.Focused, 2, null, layout));
            var near = room.Enemies.First(s => s.Fighting);
            near.Position = pylon.Center + new Vector2(2f, 0f);
            var hp = near.Hp;

            var cleave = Strike.Of(Move.CleaveA);
            cleave.Origin = Vector2.Zero;
            cleave.Direction = Vector2.UnitX;
            Assert.That(cleave.Hits(near.Position, near.Radius), Is.False);
            room.Hit(cleave);

            Assert.That(room.Events.Count(e => e.Kind == RoomEventKind.Burst), Is.EqualTo(1));
            Assert.That(room.Events.Any(e => e.Kind == RoomEventKind.HeroHit), Is.False);
            Assert.That(near.Hp, Is.EqualTo(hp - Hazards.BurstDamage));
            Assert.That(room.Hazards.Broken(0), Is.True);
            var through = pylon.Center;
            Assert.That(
                room.Hazards.Collide(ref through, 0.45f, false),
                Is.False,
                "a burst pylon is gone"
            );
        }

        [Test]
        public void AnEnemyKnockedIntoAPylonBurstsIt()
        {
            var layout = HazardLayout.Of(HazardSet.Pillars, ModeTuning.Focused.ArenaHalfSize);
            var pylon = layout.Pylons[1];
            var room = Fighting(new CombatRoom(ModeTuning.Focused, 4, null, layout));
            var s = room.Enemies.First(e => e.Fighting);
            var way = Vector2.Normalize(pylon.Center);
            s.Position = pylon.Center - way * (pylon.Radius + s.Radius + 0.03f);
            s.Knock = way * 6f;
            room.Events.Clear();
            room.Step(Dt, -pylon.Center, false);
            Assert.That(
                room.Events.Any(e =>
                    e.Kind == RoomEventKind.Impact && e.Enemy == s && e.Hazard == HazardKind.Pylon
                ),
                Is.True
            );
            Assert.That(room.Hazards.Broken(1), Is.True);
        }

        [Test]
        public void AVentHurtsTheHeroAndEnemiesOnlyOnceItsMarkerHasFilled()
        {
            var layout = HazardLayout.Of(HazardSet.Vents, ModeTuning.Focused.ArenaHalfSize);
            var vent = layout.Vents[0];
            var room = Fighting(new CombatRoom(ModeTuning.Focused, 6, null, layout));
            var s = room.Enemies.First(e => e.Fighting);
            var hp = s.Hp;
            int erupted = 0;
            for (int f = 0; f < 60 * 6 && erupted == 0; f++)
            {
                s.Position = vent.Center;
                var phase = room.Hazards.Phase(0, room.Time, out _);
                room.Events.Clear();
                room.Step(Dt, vent.Center, true);
                var hurt = room.Events.Any(e => e.Kind == RoomEventKind.HeroHit && e.Enemy == null);
                erupted = room.Events.Count(e => e.Kind == RoomEventKind.Erupt && e.Amount == 0);
                if (erupted == 0)
                {
                    Assert.That(hurt, Is.False, "hurt before the marker filled");
                    Assert.That(s.Hp, Is.EqualTo(hp));
                    continue;
                }
                Assert.That(phase, Is.EqualTo(VentPhase.Marker));
                Assert.That(hurt, Is.True);
                var hit = room.Events.First(e =>
                    e.Kind == RoomEventKind.HeroHit && e.Enemy == null
                );
                Assert.That(hit.Telegraphed, Is.True);
                Assert.That(s.Hp, Is.EqualTo(hp - ModeTuning.Focused.VentEnemyDamage));
            }
            Assert.That(erupted, Is.EqualTo(1));
        }

        // The room once its first enemies have arrived, with no events left over.
        static CombatRoom Fighting(CombatRoom room)
        {
            for (int f = 0; f < 90; f++)
                room.Step(Dt, new Vector2(0f, -8f), false);
            Assume.That(room.Enemies.Any(e => e.Fighting), Is.True);
            room.Events.Clear();
            return room;
        }
    }
}
