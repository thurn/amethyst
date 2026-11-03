using System.IO;
using System.Linq;
using Amethyst.Flags;
using NUnit.Framework;
using UnityEngine;

namespace Amethyst.Tests
{
    /// <summary>Every preset and variant in the project names real flags with valid values.</summary>
    public sealed class FlagFileTests
    {
        [Test]
        public void PresetsAndVariantsAreValid()
        {
            FlagCatalog.Load(FlagCatalog.Declaring(System.AppDomain.CurrentDomain.GetAssemblies()));
            Assert.That(Flag.Find(CoreFlags.Mute.Name), Is.Not.Null);
            var root = Application.streamingAssetsPath;
            var files = Directory.Exists(root)
                ? Directory.GetFiles(root, "*.flags", SearchOption.AllDirectories)
                : new string[0];
            var errors = files
                .SelectMany(path =>
                    FlagFile
                        .Parse(File.ReadAllText(path), Path.GetFileName(path), out _)
                        .Select(kv =>
                            (
                                path,
                                error: Flag.Find(kv.Key)?.Check(kv.Value)
                                    ?? (Flag.Find(kv.Key) == null ? $"unknown flag {kv.Key}" : null)
                            )
                        )
                )
                .Where(e => e.error != null)
                .Select(e => $"{Path.GetFileName(e.path)}: {e.error}")
                .ToArray();
            Assert.That(errors, Is.Empty);
        }
    }
}
