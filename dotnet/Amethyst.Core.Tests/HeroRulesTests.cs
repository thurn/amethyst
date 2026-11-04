using System.Numerics;
using Amethyst.Action;
using NUnit.Framework;

namespace Amethyst.Tests
{
    public sealed class HeroRulesTests
    {
        const float Frame = 1f / 60f;

        [Test]
        public void BufferKeepsOnlyTheLatestFreshPress()
        {
            var buffer = new InputBuffer(9 * Frame);
            buffer.Press(HeroAction.Dash, 0f);
            Assert.That(buffer.Take(HeroAction.Dash, 10 * Frame), Is.False, "stale");

            buffer.Press(HeroAction.Dash, 1f);
            buffer.Press(HeroAction.None, 1f + Frame);
            Assert.That(buffer.Take(HeroAction.Dash, 1f + 2 * Frame), Is.False, "replaced");

            buffer.Press(HeroAction.Dash, 2f);
            Assert.That(buffer.Take(HeroAction.Dash, 2f + 8 * Frame), Is.True);
            Assert.That(buffer.Take(HeroAction.Dash, 2f + 8 * Frame), Is.False, "taken once");

            buffer.Press(HeroAction.Dash, 3f);
            buffer.Clear();
            Assert.That(buffer.Take(HeroAction.Dash, 3f), Is.False, "cleared by hit-stun");
        }

        [Test]
        public void DashCoversItsDistanceOverAnyFramesAndIsInvulnerableFromTheFirst()
        {
            var dash = new Dash();
            Assert.That(dash.TryStart(new Vector2(3f, 4f), 0f), Is.True);
            Assert.That(dash.IsInvulnerable(0f), Is.True);
            Assert.That(dash.IsInvulnerable(dash.Duration * 0.99f), Is.False);

            var moved = Vector2.Zero;
            var t = 0f;
            foreach (var dt in new[] { Frame, 0.05f, Frame * 0.5f, 0.1f, 0.2f })
            {
                moved += dash.Step(t, t + dt);
                t += dt;
            }
            Assert.That(moved.Length(), Is.EqualTo(dash.Distance).Within(1e-4f));
            Assert.That(Vector2.Normalize(moved).X, Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void DashWaitsForItsCooldownAndKeepsItsDirectionWithoutInput()
        {
            var dash = new Dash();
            dash.TryStart(Vector2.UnitX, 0f);
            var end = dash.Duration;
            Assert.That(dash.TryStart(Vector2.UnitY, end + dash.Cooldown * 0.9f), Is.False);
            Assert.That(dash.TryStart(Vector2.Zero, end + dash.Cooldown), Is.True);
            Assert.That(dash.Direction, Is.EqualTo(Vector2.UnitX));
        }

        [Test]
        public void AimSnapsToTheNearestTargetInsideTheCone()
        {
            var aim = Vector2.UnitY;
            var targets = new[]
            {
                new Vector2(4f, 4f), // 45° off: outside the cone
                new Vector2(1f, 6f), // ~9.5° off, farther
                new Vector2(-1f, 3f), // ~18° off, nearest
                new Vector2(0f, 20f), // dead ahead, out of range
            };
            var snapped = AimSnap.Snap(Vector2.Zero, aim, targets, 25f, 10f);
            Assert.That(
                snapped.X,
                Is.EqualTo(Vector2.Normalize(new Vector2(-1f, 3f)).X).Within(1e-4f)
            );

            var none = AimSnap.Snap(
                Vector2.Zero,
                aim * 3f,
                new[] { new Vector2(5f, 0f) },
                25f,
                10f
            );
            Assert.That(none, Is.EqualTo(aim));
        }
    }
}
