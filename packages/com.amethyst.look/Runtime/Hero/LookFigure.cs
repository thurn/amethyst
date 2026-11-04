using System;
using UnityEngine;

namespace Amethyst.Look
{
    /// <summary>
    /// A figure drawn apart from the crowd, as an ordinary animated character: the roster's hero,
    /// which the crowd baker writes to Resources/AmethystLook/Figures/ID.asset from vendor files by
    /// GUID. It references the prefab, its weapons and its clips, and holds its renderers' style meshes,
    /// all in the ignored Assets/Baked/. <see cref="HeroLook.Spawn"/> builds it.
    /// </summary>
    public sealed class LookFigure : ScriptableObject
    {
        public const string Path = "AmethystLook/Figures";

        public string Id;
        public GameObject Prefab;
        public Weapon[] Weapons = Array.Empty<Weapon>();
        public string[] Roles = Array.Empty<string>();
        public AnimationClip[] Clips = Array.Empty<AnimationClip>();

        /// <summary>Its renderers' meshes (the prefab's and its weapons') and their style meshes,
        /// made at bake time: a player can't read a vendor mesh to make them.</summary>
        public Mesh[] Sources = Array.Empty<Mesh>(),
            Styles = Array.Empty<Mesh>();

        /// <summary>A weapon held in a humanoid fist (<see cref="Fist"/>), or riding the hand bone
        /// as its prefab comes.</summary>
        [Serializable]
        public struct Weapon
        {
            public GameObject Prefab;
            public bool Right,
                OnBone;
            public float Palm,
                Pitch,
                Yaw,
                Roll;
        }

        /// <summary>The baked figure, or null when it isn't (a project without its vendor files).</summary>
        public static LookFigure Load(string id) => Resources.Load<LookFigure>(Path + "/" + id);

        /// <summary>The style mesh baked for <paramref name="source"/>, or null.</summary>
        public Mesh StyleOf(Mesh source)
        {
            int i = Array.IndexOf(Sources, source);
            return i >= 0 && i < Styles.Length ? Styles[i] : null;
        }

        /// <summary>The clip with this role, or null.</summary>
        public AnimationClip Clip(string role)
        {
            int i = Array.IndexOf(Roles, role);
            return i >= 0 && i < Clips.Length ? Clips[i] : null;
        }
    }
}
