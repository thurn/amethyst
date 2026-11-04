using System.Collections.Generic;
using System.Numerics;
using Amethyst.Action;
using NUnit.Framework;

namespace Amethyst.Tests
{
    /// <summary>The sword moves' cancel and buffer rules, frame by frame at 60 Hz.</summary>
    public sealed class MovesetTests
    {
        const float Frame = 1f / 60f;

        // Startup and recovery in frames, as the feel testbed's defaults roughly set them.
        static Moveset Kit()
        {
            var kit = new Moveset(9 * Frame) { Cadence = 15 * Frame, LungeWindow = 9 * Frame };
            foreach (var cleave in new[] { Move.CleaveA, Move.CleaveB, Move.CleaveC })
                kit.SetTiming(cleave, 4 * Frame, 14 * Frame);
            kit.SetTiming(Move.Lunge, 5 * Frame, 16 * Frame);
            kit.SetTiming(Move.LeapSlam, 12 * Frame, 20 * Frame);
            return kit;
        }

        /// <summary>Runs frames <paramref name="from"/> to <paramref name="to"/>, pressing
        /// whatever <paramref name="presses"/> holds for a frame, and lists each start.</summary>
        static List<(int Frame, HeroAction Action, Move Move)> Run(
            Moveset kit,
            int from,
            int to,
            Dictionary<int, HeroAction> presses
        )
        {
            var started = new List<(int, HeroAction, Move)>();
            for (int f = from; f <= to; f++)
            {
                var now = f * Frame;
                if (presses.TryGetValue(f, out var press))
                    kit.Press(press, now);
                var action = kit.Step(now, Vector2.UnitX);
                if (action != HeroAction.None)
                    started.Add((f, action, action == HeroAction.Dash ? Move.None : kit.Current));
            }
            return started;
        }

        static Dictionary<int, HeroAction> Every(int from, int to, HeroAction action)
        {
            var presses = new Dictionary<int, HeroAction>();
            for (int f = from; f <= to; f++)
                presses[f] = action;
            return presses;
        }

        [Test]
        public void MashingCleavesChainsABCAtTheCadenceThenWraps()
        {
            var started = Run(Kit(), 0, 61, Every(0, 61, HeroAction.Attack));
            Assert.That(
                started.ConvertAll(s => (s.Frame, s.Move)),
                Is.EqualTo(
                    new[]
                    {
                        (0, Move.CleaveA),
                        (15, Move.CleaveB),
                        (30, Move.CleaveC),
                        (45, Move.CleaveA),
                        (60, Move.CleaveB),
                    }
                )
            );
        }

        [Test]
        public void APauseRestartsTheComboAtA()
        {
            var kit = Kit();
            Run(kit, 0, 0, Every(0, 0, HeroAction.Attack));
            var started = Run(kit, 1, 60, Every(60, 60, HeroAction.Attack));
            Assert.That(started, Has.Count.EqualTo(1));
            Assert.That(started[0].Move, Is.EqualTo(Move.CleaveA));
        }

        [Test]
        public void ADashPressedInStartupFiresOnTheContactFrame()
        {
            var kit = Kit();
            var presses = Every(0, 0, HeroAction.Attack);
            presses[2] = HeroAction.Dash;
            var started = Run(kit, 0, 10, presses);
            Assert.That(started[1].Frame, Is.EqualTo(4), "contact cancels into the dash");
            Assert.That(started[1].Action, Is.EqualTo(HeroAction.Dash));
            Assert.That(kit.Current, Is.EqualTo(Move.None));
        }

        [Test]
        public void TheLastPressWins()
        {
            var kit = Kit();
            var presses = Every(0, 0, HeroAction.Attack);
            presses[1] = HeroAction.Attack;
            presses[2] = HeroAction.Dash;
            var started = Run(kit, 0, 30, presses);
            Assert.That(started, Has.Count.EqualTo(2));
            Assert.That(started[1].Action, Is.EqualTo(HeroAction.Dash));
        }

        [Test]
        public void AStalePressIsDropped()
        {
            var kit = Kit();
            var presses = Every(0, 0, HeroAction.Special);
            presses[1] = HeroAction.Dash; // 11 frames before landing, past the 9-frame buffer
            var started = Run(kit, 0, 40, presses);
            Assert.That(started, Has.Count.EqualTo(1));
        }

        [Test]
        public void LeapSlamCannotBeDashCancelledUntilItLands()
        {
            var kit = Kit();
            var presses = Every(0, 0, HeroAction.Special);
            presses[9] = HeroAction.Dash;
            var started = Run(kit, 0, 20, presses);
            Assert.That(started[1].Frame, Is.EqualTo(12), "the landing frame");
            Assert.That(started[1].Action, Is.EqualTo(HeroAction.Dash));
        }

        [Test]
        public void AttackingDuringOrJustAfterADashLungesAndEndsTheDash()
        {
            var kit = Kit();
            var presses = Every(0, 0, HeroAction.Dash);
            presses[3] = HeroAction.Attack;
            var started = Run(kit, 0, 3, presses);
            Assert.That(started[1].Move, Is.EqualTo(Move.Lunge));
            Assert.That(kit.Dash.Active(3 * Frame), Is.False);
            Assert.That(kit.Dash.IsInvulnerable(3 * Frame), Is.False);
            Assert.That(Run(kit, 4, 60, presses), Is.Empty, "one press, one move");

            // The dash lasts 10 frames; the Lunge window is 9 more.
            kit = Kit();
            presses = Every(0, 0, HeroAction.Dash);
            presses[18] = HeroAction.Attack;
            Assert.That(Run(kit, 0, 18, presses)[1].Move, Is.EqualTo(Move.Lunge));

            kit = Kit();
            presses = Every(0, 0, HeroAction.Dash);
            presses[21] = HeroAction.Attack;
            Assert.That(Run(kit, 0, 21, presses)[1].Move, Is.EqualTo(Move.CleaveA));
        }

        [Test]
        public void TheDashCancelsEveryRecovery()
        {
            foreach (var press in new[] { HeroAction.Attack, HeroAction.Special })
            {
                var kit = Kit();
                Run(kit, 0, 0, Every(0, 0, press));
                var contact = Frames(kit.ContactAt);
                var started = Run(kit, 1, contact, Every(contact, contact, HeroAction.Dash));
                Assert.That(started, Has.Count.EqualTo(1), press.ToString());
                Assert.That(started[0].Action, Is.EqualTo(HeroAction.Dash));
            }
        }

        [Test]
        public void TheSpecialWaitsForItsCooldownAndForTheDashToEnd()
        {
            var kit = Kit();
            var presses = Every(0, 0, HeroAction.Special);
            presses[60] = HeroAction.Special; // 1 s in: cooling down
            presses[120] = HeroAction.Dash;
            presses[122] = HeroAction.Special; // 2 s in, but mid-dash: waits for frame 130
            var started = Run(kit, 0, 140, presses);
            Assert.That(
                started.ConvertAll(s => (s.Frame, s.Action)),
                Is.EqualTo(
                    new[]
                    {
                        (0, HeroAction.Special),
                        (120, HeroAction.Dash),
                        (130, HeroAction.Special),
                    }
                )
            );
        }

        [Test]
        public void StrikesReachBodiesThatOverlapTheirShape()
        {
            var cone = Strike.Of(Move.CleaveA); // 120°, 3 m, facing +Y from the origin
            Assert.That(cone.Hits(new Vector2(0f, 3.3f), 0.4f), Is.True, "overlaps the rim");
            Assert.That(cone.Hits(new Vector2(2.4f, 1.2f), 0.1f), Is.False, "63° off the axis");
            Assert.That(cone.Hits(new Vector2(0f, -0.3f), 0.4f), Is.True, "standing on the hero");

            var line = Strike.Of(Move.Lunge); // 4.5 m long, 1.2 m wide
            line.Origin = new Vector2(1f, 1f);
            line.Direction = Vector2.UnitX;
            Assert.That(line.Hits(new Vector2(5f, 1.5f), 0.1f), Is.True, "pierces to the end");
            Assert.That(line.Hits(new Vector2(3f, 2f), 0.2f), Is.False, "beside the lane");

            var ring = Strike.Of(Move.LeapSlam);
            Assert.That(ring.Hits(new Vector2(-3.6f, 0f), 0.2f), Is.True);
            Assert.That(ring.Push(new Vector2(-2f, 0f)).X, Is.LessThan(0f), "pushes outward");
        }

        static int Frames(float seconds) => (int)System.MathF.Round(seconds / Frame);
    }
}
