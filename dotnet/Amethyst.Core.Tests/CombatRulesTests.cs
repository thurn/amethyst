using System.Linq;
using System.Numerics;
using Amethyst.Action;
using Amethyst.Look;
using NUnit.Framework;

namespace Amethyst.Tests
{
    /// <summary>The combat modes' rules: hitstop, aggression tokens, spawn lists, the voice budget, the blind pick.</summary>
    public sealed class CombatRulesTests
    {
        [Test]
        public void FocusedHitstopFollowsFeelAndCaps()
        {
            var focused = ModeTuning.Focused;
            Assert.That(Hitstop.Target(focused, HitWeight.Light), Is.InRange(2, 4));
            Assert.That(Hitstop.Target(focused, HitWeight.Finisher), Is.InRange(5, 8));
            Assert.That(Hitstop.Target(focused, HitWeight.Kill), Is.InRange(4, 8));
            Assert.That(Hitstop.Hero(focused, 1), Is.InRange(0, 2));
            Assert.That(Hitstop.Target(focused, HitWeight.Finisher, 10f), Is.EqualTo(12));
        }

        [Test]
        public void HordeFodderFreezesBrieflyAndTheHeroNotAtAll()
        {
            var horde = ModeTuning.Horde;
            foreach (var weight in new[] { HitWeight.Light, HitWeight.Finisher, HitWeight.Kill })
                Assert.That(Hitstop.Target(horde, weight), Is.InRange(1, 2));
            Assert.That(Hitstop.Hero(horde, 30), Is.Zero);
        }

        [Test]
        public void HitstopNeverStacksPastOneHit()
        {
            var left = Hitstop.Extend(0f, 3);
            left = Hitstop.Extend(left, 3);
            Assert.That(left, Is.EqualTo(3 / 60f).Within(1e-6f));
        }

        [Test]
        public void TokensCapWhoWindsUpAndFreeOnRelease()
        {
            var tokens = new AggressionTokens(2);
            Assert.That(tokens.TryTake(1), Is.True);
            Assert.That(tokens.TryTake(2), Is.True);
            Assert.That(tokens.TryTake(3), Is.False);
            Assert.That(tokens.TryTake(1), Is.True, "a holder keeps its token");
            tokens.Release(1);
            Assert.That(tokens.TryTake(3), Is.True);
            Assert.That(tokens.Held, Is.EqualTo(2));
        }

        [TestCase(CombatMode.Focused, false)]
        [TestCase(CombatMode.Focused, true)]
        [TestCase(CombatMode.Horde, true)]
        public void TokensCapWindUpsInBothModes(CombatMode mode, bool specials)
        {
            var tuning = ModeTuning.For(mode);
            var spawns = specials
                ? new SpawnList(new[] { (EnemyKinds.Goblin, 1f), (EnemyKinds.Mushroom, 1f) })
                : SpawnList.Skeletons;
            var room = new CombatRoom(tuning, 7, spawns);
            int most = 0;
            for (int f = 0; f < 60 * 20; f++)
            {
                room.Step(1f / 60f, Vector2.Zero, false);
                most = System.Math.Max(
                    most,
                    room.Enemies.Count(s => s.State == EnemyState.WindingUp)
                );
            }
            Assert.That(most, Is.EqualTo(tuning.AggressionTokens), "the crowd fills the tokens");
        }

        [TestCase(0f, true)]
        [TestCase(2f, false)]
        public void AGoblinsLungeHitsOnlyAHeroStillInItsLane(float sidestep, bool hit)
        {
            var mode = ModeTuning.Focused.Copy();
            mode.WaveMin = mode.WaveMax = 1;
            mode.Waves = 1;
            var room = new CombatRoom(mode, 5, new SpawnList(new[] { (EnemyKinds.Goblin, 1f) }));
            var hero = Vector2.Zero;
            Enemy goblin = null;
            for (int f = 0; f < 60 * 20 && goblin == null; f++)
            {
                room.Step(1f / 60f, hero, false);
                goblin = room.Enemies.FirstOrDefault(s => s.State == EnemyState.WindingUp);
            }
            Assert.That(goblin, Is.Not.Null, "the goblin crouches behind its lane");
            var side = new Vector2(-goblin.AttackDirection.Y, goblin.AttackDirection.X);
            hero += side * sidestep;
            bool lunged = false,
                struck = false;
            for (int f = 0; f < 60 * 2 && goblin.State != EnemyState.Recovering; f++)
            {
                room.Step(1f / 60f, hero, true);
                lunged |= goblin.State == EnemyState.Lunging;
                struck |= room.Events.Any(e => e.Kind == RoomEventKind.HeroHit && e.Telegraphed);
                room.Events.Clear();
            }
            Assert.That(lunged, Is.True);
            Assert.That(struck, Is.EqualTo(hit));
        }

        // A room with one enemy of `kind`, stepped until it is in the fight.
        static (CombatRoom Room, Enemy Enemy) One(CombatMode mode, EnemyKind kind, int seed = 5)
        {
            var tuning = ModeTuning.For(mode).Copy();
            tuning.WaveMin = tuning.WaveMax = tuning.Waves = 1;
            tuning.Alive = 1;
            tuning.EliteEvery = 0f;
            var room = new CombatRoom(tuning, seed, new SpawnList(new[] { (kind, 1f) }));
            for (int f = 0; f < 60 * 10 && !room.Enemies.Any(e => e.Fighting); f++)
                room.Step(1f / 60f, new Vector2(0f, -30f), false);
            return (room, room.Enemies.Single(e => e.Fighting));
        }

        // A move's hit on `enemy`, from a metre away.
        static void HitWith(CombatRoom room, Enemy enemy, Move move)
        {
            var strike = Strike.Of(move);
            strike.Origin = enemy.Position - Vector2.UnitY;
            strike.Direction = Vector2.UnitY;
            room.Hit(strike);
        }

        [TestCase(CombatMode.Focused)]
        [TestCase(CombatMode.Horde)]
        public void AnArmoredHeavyLosesArmorFirstAndHoldsItsGround(CombatMode mode)
        {
            foreach (var kind in new[] { EnemyKinds.BigOrk, EnemyKinds.Troll })
            {
                var (room, heavy) = One(mode, kind);
                Assert.That(heavy.Armored, Is.True, kind.Id);
                HitWith(room, heavy, Move.CleaveA);
                Assert.That(heavy.Hp, Is.EqualTo(heavy.MaxHp), kind.Id);
                Assert.That(heavy.Armor, Is.LessThan(heavy.MaxArmor), kind.Id);
                Assert.That(heavy.State, Is.Not.EqualTo(EnemyState.Staggered), kind.Id);
                Assert.That(heavy.Knock, Is.EqualTo(Vector2.Zero), kind.Id);
                while (heavy.Armored)
                    HitWith(room, heavy, Move.CleaveA);
                HitWith(room, heavy, Move.CleaveA);
                Assert.That(heavy.Hp, Is.LessThan(heavy.MaxHp), kind.Id);
                Assert.That(heavy.Knock, Is.Not.EqualTo(Vector2.Zero), kind.Id);
                if (room.Mode.Stagger)
                    Assert.That(heavy.State, Is.EqualTo(EnemyState.Staggered), kind.Id);
            }
        }

        [TestCase(Move.Lunge)]
        [TestCase(Move.LeapSlam)]
        public void TheLungeAndTheLeapSlamBreakArmorFasterThanACleave(Move breaker)
        {
            float ChippedPerDamage(Move move)
            {
                var (room, heavy) = One(CombatMode.Focused, EnemyKinds.BigOrk);
                heavy.MaxArmor = heavy.Armor = 1000;
                HitWith(room, heavy, move);
                return (heavy.MaxArmor - heavy.Armor) / (float)Strike.Of(move).Damage;
            }
            Assert.That(ChippedPerDamage(breaker), Is.GreaterThan(ChippedPerDamage(Move.CleaveA)));
        }

        [TestCase(0f, true)]
        [TestCase(4f, false)]
        public void ATrollsLandingHitsOnlyAHeroInsideItsRing(float sidestep, bool hit)
        {
            var (room, troll) = One(CombatMode.Focused, EnemyKinds.Troll);
            var hero = Vector2.Zero;
            for (int f = 0; f < 60 * 20 && troll.State != EnemyState.WindingUp; f++)
                room.Step(1f / 60f, hero, false);
            Assert.That(troll.State, Is.EqualTo(EnemyState.WindingUp), "the troll marks its ring");
            hero += new Vector2(-troll.AttackDirection.Y, troll.AttackDirection.X) * sidestep;
            bool leapt = false,
                struck = false;
            for (int f = 0; f < 60 * 3 && troll.State != EnemyState.Recovering; f++)
            {
                room.Step(1f / 60f, hero, true);
                leapt |= troll.State == EnemyState.Leaping;
                struck |= room.Events.Any(e => e.Kind == RoomEventKind.HeroHit && e.Telegraphed);
                room.Events.Clear();
            }
            Assert.That(leapt, Is.True);
            Assert.That(struck, Is.EqualTo(hit));
        }

        [TestCase(CombatMode.Focused)]
        [TestCase(CombatMode.Horde)]
        public void SporesNeverOutpaceHalfTheDash(CombatMode mode)
        {
            var dash = new Dash();
            var most = 0.5f * dash.Distance / dash.Duration;
            var room = new CombatRoom(
                ModeTuning.For(mode),
                11,
                new SpawnList(new[] { (EnemyKinds.Mushroom, 1f) })
            );
            var last = new System.Collections.Generic.Dictionary<(Volley, int), Vector3>();
            const float dt = 1f / 60f;
            int moves = 0;
            for (int f = 0; f < 60 * 20; f++)
            {
                // The hero circles, so the volleys come from every distance.
                var hero =
                    new Vector2(System.MathF.Cos(f * 0.01f), System.MathF.Sin(f * 0.01f)) * 3f;
                room.Step(dt, hero, false);
                foreach (var v in room.Volleys)
                    for (int i = 0; i < v.Landings.Length; i++)
                    {
                        var (at, height) = v.Spore(i, room.Time);
                        var p = new Vector3(at.X, height, at.Y);
                        if (last.TryGetValue((v, i), out var was))
                        {
                            Assert.That(Vector3.Distance(p, was) / dt, Is.LessThanOrEqualTo(most));
                            moves++;
                        }
                        last[(v, i)] = p;
                    }
            }
            Assert.That(moves, Is.GreaterThan(100), "mushrooms lofted volleys");
        }

        [Test]
        public void ARoomSpawnsItsListsKindsAtTheirNumbers()
        {
            var heavy = new EnemyKind
            {
                Id = "heavy",
                Role = EnemyRole.Heavy,
                Hp = 2f,
                Radius = 0.6f,
                Armored = true,
            };
            var spawns = new SpawnList(new[] { (EnemyKinds.Skeleton, 1f), (heavy, 1f) });
            var room = new CombatRoom(ModeTuning.Horde, 3, spawns);
            for (int f = 0; f < 60 * 3; f++)
                room.Step(1f / 60f, Vector2.Zero, false);
            var heavies = room.Enemies.Where(e => e.Kind == heavy && !e.Empowered).ToList();
            var skeletons = room.Enemies.Where(e => e.Kind == EnemyKinds.Skeleton).ToList();
            Assert.That(heavies, Is.Not.Empty);
            Assert.That(skeletons, Is.Not.Empty);
            Assert.That(heavies, Has.All.Matches<Enemy>(e => e.MaxHp == 24 && e.Armored));
            Assert.That(heavies[0].Radius, Is.EqualTo(0.6f));
            Assert.That(
                skeletons.Where(e => !e.Empowered),
                Has.All.Matches<Enemy>(e => e.MaxHp == 12 && !e.Armored)
            );
        }

        [Test]
        public void EveryKindDrawsInItsFamilysHue()
        {
            var palette = Palette.Default;
            foreach (var kind in EnemyKinds.All)
            {
                var family = kind.Family switch
                {
                    EnemyFamily.Key => new[] { palette.Cyan, palette.Blue },
                    EnemyFamily.Accent => new[] { palette.Magenta, palette.Violet },
                    _ => new[] { palette.Orange, palette.Amber },
                };
                Assert.That(family, Has.Member(palette.ForType(kind.Id)), kind.Id);
            }
        }

        [Test]
        public void TheHeroOutrunsEveryKindInBothModes()
        {
            foreach (var mode in new[] { ModeTuning.Focused, ModeTuning.Horde })
            foreach (var kind in EnemyKinds.All)
                Assert.That(
                    mode.WalkSpeed * kind.Speed,
                    Is.LessThan(ModeTuning.HeroRunSpeed),
                    $"{kind.Id} {mode.Mode}"
                );
        }

        [Test]
        public void ManyKillsOnOneFramePlayOneGrowingVoice()
        {
            var budget = new VoiceBudget();
            budget.Add(CombatSound.Kill);
            var one = budget.Flush(0f).Single();
            for (int i = 0; i < 20; i++)
                budget.Add(CombatSound.Kill);
            budget.Add(CombatSound.Hit, 20);
            var many = budget.Flush(1f);
            Assert.That(many.Count(v => v.Sound == CombatSound.Kill), Is.EqualTo(1));
            var kill = many.Single(v => v.Sound == CombatSound.Kill);
            Assert.That(kill.Count, Is.EqualTo(20));
            Assert.That(kill.Layers, Is.GreaterThan(one.Layers).And.LessThanOrEqualTo(3));
            Assert.That(kill.Volume, Is.GreaterThan(one.Volume).And.LessThanOrEqualTo(1f));
        }

        [Test]
        public void EachSoundStartsAFewVoicesPerWindowAtMost()
        {
            var budget = new VoiceBudget { PerWindow = 4, Window = 0.1f };
            int played = 0;
            for (int f = 0; f < 6; f++)
            {
                budget.Add(CombatSound.Hit);
                played += budget.Flush(f / 60f).Count;
            }
            Assert.That(played, Is.EqualTo(4));
            budget.Add(CombatSound.Hit);
            Assert.That(budget.Flush(0.2f).Count, Is.EqualTo(1), "the window moved on");
        }

        [Test]
        public void BlindPicksBothModesAndKeepsAPickedOne()
        {
            var picks = Enumerable
                .Range(0, 64)
                .Select(seed => CombatModes.Pick(CombatMode.Blind, (ulong)seed))
                .ToList();
            Assert.That(picks, Has.Some.EqualTo(CombatMode.Focused));
            Assert.That(picks, Has.Some.EqualTo(CombatMode.Horde));
            Assert.That(picks, Has.None.EqualTo(CombatMode.Blind));
            Assert.That(
                CombatModes.Pick(CombatMode.Blind, 5),
                Is.EqualTo(CombatModes.Pick(CombatMode.Blind, 5))
            );
            Assert.That(CombatModes.Pick(CombatMode.Horde, 5), Is.EqualTo(CombatMode.Horde));
        }
    }
}
