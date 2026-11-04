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

        [Test]
        public void FocusedRoomNeverHasMoreThanTwoWindingUp()
        {
            var room = new CombatRoom(ModeTuning.Focused, 7);
            int most = 0;
            for (int f = 0; f < 60 * 20; f++)
            {
                room.Step(1f / 60f, Vector2.Zero, false);
                most = System.Math.Max(
                    most,
                    room.Enemies.Count(s => s.State == EnemyState.WindingUp)
                );
            }
            Assert.That(most, Is.EqualTo(2), "the wave crowds the hero, two at a time wind up");
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
