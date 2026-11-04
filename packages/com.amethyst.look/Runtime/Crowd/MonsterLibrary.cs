using System;
using UnityEngine;

namespace Amethyst.Look
{
    /// <summary>
    /// The baked crowd types (<see cref="MonsterType"/>, from Resources/AmethystLook/Types/, which
    /// the crowd baker writes before every build) and the crowd's compute shaders. Types are in id
    /// order; presenters find theirs with <see cref="IndexOf"/>.
    /// </summary>
    public sealed class MonsterLibrary
    {
        /// <summary>The bone textures' rate: the trailer's crowd's.</summary>
        public const float ClipRate = 30f;

        public const string TypesPath = "AmethystLook/Types";

        public readonly MonsterType[] Types;
        public readonly ComputeShader Cull,
            Palette;

        public MonsterLibrary(MonsterType[] types)
        {
            Types = types;
            Cull = Load<ComputeShader>("AmethystLook/CrowdCull");
            Palette = Load<ComputeShader>("AmethystLook/CrowdPalette");
        }

        /// <summary>The baked types, or null when none is (a project without the vendor files).</summary>
        public static MonsterLibrary Load()
        {
            var types = Resources.LoadAll<MonsterType>(TypesPath);
            if (types.Length == 0)
                return null;
            Array.Sort(types, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            return new MonsterLibrary(types);
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Types.Length; i++)
                if (Types[i].Id == id)
                    return i;
            return -1;
        }

        internal static T Load<T>(string path)
            where T : UnityEngine.Object =>
            Resources.Load<T>(path)
            ?? throw new InvalidOperationException($"Amethyst.Look: no Resources/{path}");
    }
}
