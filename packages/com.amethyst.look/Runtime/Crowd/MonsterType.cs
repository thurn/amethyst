using System;
using UnityEngine;

namespace Amethyst.Look
{
    /// <summary>
    /// One crowd type as the crowd baker (Editor/CrowdBaker.cs) writes it into the ignored
    /// Assets/Baked/Look/: LOD0 to LOD2 as style meshes (<see cref="StyleMesh.Unweld"/>, bone
    /// indices in UV1 and weights in UV2, LOD1 about half LOD0's triangles, LOD2 a quarter), one
    /// float texture of skinning matrices and its clips. A type with a wireframe mesh recipe
    /// draws the recipe's mesh at every LOD and keeps the vendor's in <see cref="VendorLods"/>.
    /// </summary>
    public sealed class MonsterType : ScriptableObject
    {
        public string Id;
        public Mesh[] Lods = Array.Empty<Mesh>();

        /// <summary>The vendor mesh's LODs where <see cref="Lods"/> are a recipe's, else empty.</summary>
        public Mesh[] VendorLods = Array.Empty<Mesh>();

        /// <summary>The LODs to draw: the vendor's when asked and the type has them apart.</summary>
        public Mesh[] LodsOf(bool vendor) =>
            vendor && VendorLods.Length == Lods.Length ? VendorLods : Lods;

        /// <summary>Skinning matrices at <see cref="MonsterLibrary.ClipRate"/>: bone b's row r of
        /// frame f is texel (3b + r, f).</summary>
        public Texture2D Bones;
        public int BoneCount;
        public MonsterClip[] Clips = Array.Empty<MonsterClip>();

        /// <summary>Yaw-invariant bounds at scale 1: a sphere around (0, CenterY, 0), and the
        /// standing height.</summary>
        public float CenterY,
            Radius,
            HeightM;

        /// <summary>A flyer's height over the ground at scale 1 (the bat), for its presenter.</summary>
        public float HoverM;

        /// <summary>Its kill shatter's chunks (<see cref="MonsterShatter"/>): LOD0 on the rest pose
        /// cut into 20 to 40, joined into one mesh, and each chunk's pivot.</summary>
        public Mesh ShardMesh;
        public MonsterShard[] Shards = Array.Empty<MonsterShard>();

        /// <summary>The clip with this role ("idle", "walk", "attack", "hit"), or -1.</summary>
        public int Clip(string role)
        {
            for (int i = 0; i < Clips.Length; i++)
                if (Clips[i].Role == role)
                    return i;
            return -1;
        }
    }

    /// <summary>
    /// One chunk of a type's shatter at rest: its pivot (root space, scale 1) skinned by up to four
    /// bones, its size and whether it is of a weapon or shield. 64 bytes, matching MonsterShards.hlsl.
    /// </summary>
    [Serializable]
    public struct MonsterShard
    {
        public Vector3 Pivot;
        public float Size;
        public Vector4 Bones,
            Weights;
        public float Carried,
            Pad0,
            Pad1,
            Pad2;

        public const int Stride = 64;
    }

    /// <summary>One clip in a type's bone texture.</summary>
    [Serializable]
    public struct MonsterClip
    {
        public string Role;
        public string Name;
        public int FirstRow;
        public int Frames;
        public float Length;
        public bool Loop;

        /// <summary>From the roster's annotations; negative where absent.</summary>
        public float Impact,
            End,
            StrideM;

        /// <summary>This clip's own bounding sphere at scale 1 (see MonsterType).</summary>
        public float CenterY,
            Radius,
            HeightM;

        /// <summary>Frames a clip of this length bakes to at the clip rate: both ends included.</summary>
        public static int FrameCount(float length) =>
            Math.Max(2, (int)Math.Ceiling(length * MonsterLibrary.ClipRate - 1e-3f) + 1);

        /// <summary>The sample time of frame f (the last frame is the clip's end).</summary>
        public float FrameTime(int f) => Math.Min(Length, f / MonsterLibrary.ClipRate);
    }
}
