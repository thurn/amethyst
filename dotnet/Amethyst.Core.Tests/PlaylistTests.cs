using System;
using System.IO;
using System.Linq;
using Amethyst.Native;
using NUnit.Framework;

namespace Amethyst.Tests
{
    public sealed class PlaylistTests
    {
        [Test]
        public void ParsesLocalTracksRelativeToTheList()
        {
            var tracks = Playlist.Parse(
                "﻿#EXTM3U\r\n#EXTINF:123,Artist - Song\r\na song.mp3\r\n\r\n/abs/b.m4a\nsub/../c.wav\n",
                "/music"
            );
            Assert.That(
                tracks,
                Is.EqualTo(new[] { "/music/a song.mp3", "/abs/b.m4a", "/music/c.wav" })
            );
        }

        [Test]
        public void MalformedListsFailBeforePlaying()
        {
            foreach (
                var text in new[] { "", "#EXTM3U\n", "https://example.com/a.mp3", "other.m3u" }
            )
                Assert.Throws<FormatException>(() => Playlist.Parse(text, "/m"), text);
        }

        [Test]
        public void LoadingNeedsEveryTrackToExist()
        {
            var dir = Directory
                .CreateDirectory(
                    Path.Combine(Path.GetTempPath(), "amethyst-playlist-" + Guid.NewGuid())
                )
                .FullName;
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.mp3"), "");
                var list = Path.Combine(dir, "list.m3u");
                File.WriteAllText(list, "a.mp3\n");
                Assert.That(Playlist.Load(list), Is.EqualTo(new[] { Path.Combine(dir, "a.mp3") }));
                Assert.That(Playlist.Load(Path.Combine(dir, "a.mp3")).Count, Is.EqualTo(1));
                File.WriteAllText(list, "a.mp3\nmissing.mp3\n");
                Assert.Throws<IOException>(() => Playlist.Load(list));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void AShuffleIsAPermutationItsSeedPicks()
        {
            int[] Shuffled(ulong seed)
            {
                var items = Enumerable.Range(0, 20).ToArray();
                Playlist.Shuffle(items, seed);
                return items;
            }
            Assert.That(Shuffled(7), Is.EqualTo(Shuffled(7)));
            Assert.That(Shuffled(7), Is.Not.EqualTo(Shuffled(8)));
            Assert.That(Shuffled(7), Is.Not.EqualTo(Enumerable.Range(0, 20).ToArray()));
            Assert.That(Shuffled(7).OrderBy(i => i), Is.EqualTo(Enumerable.Range(0, 20)));
        }

        [Test]
        public void FileAndShuffleAreLaunchArguments()
        {
            var args = LaunchArgs.Parse(
                new[] { "player", "--file", "~/Music/songs.m3u", "--shuffle" }
            );
            Assert.That(args.Errors, Is.Empty);
            Assert.That(args.MusicFile, Is.EqualTo("~/Music/songs.m3u"));
            Assert.That(args.Shuffle, Is.True);
            Assert.That(LaunchArgs.Parse(new[] { "--file" }).Errors, Is.Not.Empty);
        }
    }
}
