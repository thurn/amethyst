using System.Collections.Generic;
using Amethyst.Flags;
using NUnit.Framework;

namespace Amethyst.Tests
{
    public enum Arc
    {
        Wide,
        ThrustLine,
    }

    public sealed class FlagTests
    {
        static readonly Flag<Arc> ArcFlag = Flag.Enum("test.arc", Arc.Wide, "an enum");
        static readonly Flag<float> Speed = Flag.Float("test.speed", 1f, "a float");

        [SetUp]
        public void Fresh() => FlagState.Reset();

        static KeyValuePair<string, string>[] Pair(string name, string value) =>
            new[] { new KeyValuePair<string, string>(name, value) };

        [Test]
        public void HigherLayersWinAndChangesShowImmediately()
        {
            var state = FlagState.Current;
            Assert.That(Speed.Value, Is.EqualTo(1f));
            state.SetLayer(FlagState.CommandLine, Pair("test.speed", "3"));
            state.SetLayer(FlagState.Preset, Pair("test.speed", "2"));
            Assert.That(Speed.Value, Is.EqualTo(3f));
            using (Speed.Override(4f))
                Assert.That(Speed.Value, Is.EqualTo(4f));
            Assert.That(Speed.Value, Is.EqualTo(3f));
            state.RemoveLayer(FlagState.CommandLine);
            Assert.That(Speed.Value, Is.EqualTo(2f));
        }

        [Test]
        public void EnumsReadKebabCase()
        {
            FlagState.Current.Set(FlagState.Live, "test.arc", "thrust-line");
            Assert.That(ArcFlag.Value, Is.EqualTo(Arc.ThrustLine));
            Assert.That(ArcFlag.Choices, Is.EqualTo(new[] { "wide", "thrust-line" }));
            Assert.That(ArcFlag.Text, Is.EqualTo("thrust-line"));
        }

        [Test]
        public void ValidateReportsUnknownFlagsAndBadValues()
        {
            FlagState.Current.SetLayer(FlagState.Variant, Pair("test.arc", "spin"));
            FlagState.Current.SetLayer(FlagState.CommandLine, Pair("test.nope", "1"));
            var errors = FlagState.Current.Validate();
            Assert.That(errors, Has.Count.EqualTo(2));
            Assert.That(
                ArcFlag.Value,
                Is.EqualTo(Arc.Wide),
                "a bad value falls back to the default"
            );
        }

        [Test]
        public void FlagFilesKeepTheirLeadingComment()
        {
            var values = FlagFile.Parse(
                "# Thrust\n# fast\ntest.arc = thrust-line\n# later\n",
                "t.flags",
                out var about
            );
            Assert.That(about, Is.EqualTo("Thrust fast"));
            Assert.That(values, Is.EqualTo(Pair("test.arc", "thrust-line")));
            Assert.Throws<System.FormatException>(() => FlagFile.Parse("oops", "t.flags", out _));
        }

        [Test]
        public void LaunchArgsSkipUnitysOwnAndRejectUnknownOnes()
        {
            var args = LaunchArgs.Parse(
                new[]
                {
                    "-batchmode",
                    "--flag",
                    "test.arc=wide",
                    "--mute",
                    "--preset=p",
                    "--bogus",
                    "--at",
                    "1,2.5",
                },
                "test.speed=2"
            );
            Assert.That(
                args.Flags,
                Is.EqualTo(new[] { Pair("test.arc", "wide")[0], Pair("audio.mute", "true")[0] })
            );
            Assert.That(args.EnvFlags, Is.EqualTo(Pair("test.speed", "2")));
            Assert.That(args.Presets, Is.EqualTo(new[] { "p" }));
            Assert.That(args.CaptureAt, Is.EqualTo(new[] { 1f, 2.5f }));
            Assert.That(args.Errors, Is.EqualTo(new[] { "unknown argument --bogus" }));
        }
    }
}
