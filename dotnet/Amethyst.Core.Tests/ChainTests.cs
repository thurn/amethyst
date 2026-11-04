using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Amethyst.Action;
using NUnit.Framework;

namespace Amethyst.Tests
{
    /// <summary>The chain: arena modules laid out through their sockets, its gates and its walk.</summary>
    public sealed class ChainTests
    {
        const float Dt = 1f / 60f;
        const float Speed = 7f;
        const float Radius = 0.45f;

        static IEnumerable<ArenaModule[]> Orders(ArenaModule[] modules) =>
            modules.Length <= 1
                ? new[] { modules }
                : modules.SelectMany(m =>
                    Orders(modules.Where(o => o != m).ToArray())
                        .Select(rest => new[] { m }.Concat(rest).ToArray())
                );

        [Test]
        public void EveryOrderJoinsThroughTheSocketsWithoutOverlap()
        {
            var orders = Orders(ArenaModule.Early).ToList();
            Assert.That(orders, Has.Count.EqualTo(24));
            foreach (var order in orders)
            {
                var c = ChainLayout.In(order);
                var names = string.Join(", ", order.Select(m => m.Name));
                var modules = order.Select((m, k) => ChainLayout.Footprint(m, c.Rooms[k].Center));
                var boxes = modules.Concat(c.Legs).ToList();
                var n = order.Length;
                for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    // A corridor's two legs share the corner where it turns.
                    var turn = i >= n && j == i + 1;
                    Assert.That(
                        !turn && boxes[i].Overlaps(boxes[j]),
                        Is.False,
                        $"{names}: {i} overlaps {j}"
                    );
                }
                for (int k = 0; k < n; k++)
                {
                    bool OnFloor(int passage, (Vector2 A, Vector2 B) gate) =>
                        c.Passages[passage].Any(r => r.Contains((gate.A + gate.B) / 2f));
                    Assert.That(OnFloor(k, c.Entries[k]), $"{names}: entry {k}");
                    Assert.That(OnFloor(k + 1, c.Exits[k]), $"{names}: exit {k}");
                }
            }
        }

        [Test]
        public void AWalkClearingEachRoomOnEntryPassesEveryModuleInOrder()
        {
            foreach (var order in Orders(ArenaModule.Early))
            {
                var chain = new Chain(ChainLayout.In(order));
                var hero = chain.Layout.Start;
                var entries = new List<int>();
                for (int f = 0; f < 60 * 120 && !chain.Done; f++)
                {
                    if (chain.Step(hero))
                    {
                        entries.Add(chain.Room);
                        chain.Clear();
                    }
                    Walk(chain, ref hero);
                }
                var names = string.Join(", ", order.Select(m => m.Name));
                Assert.That(entries, Is.EqualTo(new[] { 0, 1, 2, 3 }), names);
                Assert.That(chain.Done, Is.True, $"{names}: stopped at {hero}");
            }
        }

        [Test]
        public void TheEntrySealsOnlyOnceTheHeroIsInsideAndHoldsItUntilTheClear()
        {
            var chain = new Chain(ChainLayout.Of(ArenaModule.Early, 7));
            var room = chain.Layout.Rooms[0];
            var hero = chain.Layout.Start;
            bool Inside(Vector2 p, float depth) =>
                System.Math.Abs(p.X - room.Center.X) <= room.Half.X - depth
                && System.Math.Abs(p.Y - room.Center.Y) <= room.Half.Y - depth;

            var sealedAt = -1;
            for (int f = 0; f < 60 * 10 && sealedAt < 0; f++)
            {
                if (chain.Step(hero))
                    sealedAt = f;
                else
                    Assert.That(chain.Entered(0), Is.False);
                if (!Inside(hero, Chain.SealDepth))
                    Assert.That(chain.Entered(0), Is.False, "sealed with the hero outside");
                Walk(chain, ref hero);
            }
            Assert.That(sealedAt, Is.GreaterThanOrEqualTo(0), "never sealed");
            Assert.That(chain.Fighting && chain.Goal == null, Is.True);

            // Sealed in: running back out the entry, or at the shut exit, leaves the hero inside.
            foreach (
                var target in new[]
                {
                    chain.Layout.Start,
                    chain.Layout.Path[2] + chain.Layout.Modules[0].Exit.Out * 10f,
                }
            )
                for (int f = 0; f < 60 * 15; f++)
                {
                    Toward(target, ref hero);
                    chain.Collide(ref hero, Radius, false);
                    Assert.That(Inside(hero, 0f), Is.True, $"left the sealed room at {hero}");
                }

            chain.Clear();
            Assert.That(chain.Room, Is.EqualTo(1));
            for (int f = 0; f < 60 * 15 && Inside(hero, 0f); f++)
            {
                chain.Step(hero);
                Walk(chain, ref hero);
            }
            Assert.That(Inside(hero, 0f), Is.False, "the cleared room's exit stayed shut");
        }

        static void Walk(Chain chain, ref Vector2 hero)
        {
            if (chain.Goal is Vector2 goal)
                Toward(goal, ref hero);
            chain.Collide(ref hero, Radius, false);
        }

        static void Toward(Vector2 goal, ref Vector2 hero)
        {
            var to = goal - hero;
            var d = to.Length();
            if (d > 1e-4f)
                hero += to / d * System.Math.Min(d, Speed * Dt);
        }
    }
}
