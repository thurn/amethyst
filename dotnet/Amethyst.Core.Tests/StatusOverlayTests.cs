using Amethyst.Look;
using NUnit.Framework;

namespace Amethyst.Tests
{
    public class StatusOverlayTests
    {
        [Test]
        public void AnOverlayShowsUntilItExpiresOrIsRemoved()
        {
            var o = new StatusOverlays();
            o.Attach(7, Overlay.Frost, now: 10, seconds: 2);
            o.Attach(7, Overlay.Sparks, now: 11, seconds: 5);
            Assert.AreEqual(Overlay.Frost | Overlay.Sparks, o.At(7, 11.5, out var age));
            Assert.AreEqual(0.5f, age, 1e-4f);
            Assert.AreEqual(Overlay.Sparks, o.At(7, 12.5, out _));
            o.Remove(7, Overlay.Sparks);
            Assert.AreEqual(Overlay.None, o.At(7, 12.6, out _));
            Assert.AreEqual(Overlay.None, o.At(8, 12.6, out _));
        }

        [Test]
        public void AttachingAgainExtendsIt()
        {
            var o = new StatusOverlays();
            o.Attach(1, Overlay.Ring, now: 0, seconds: 1);
            o.Attach(1, Overlay.Ring, now: 0.8, seconds: 1);
            Assert.AreEqual(Overlay.Ring, o.At(1, 1.5, out var age));
            Assert.AreEqual(0.7f, age, 1e-4f);
        }
    }
}
