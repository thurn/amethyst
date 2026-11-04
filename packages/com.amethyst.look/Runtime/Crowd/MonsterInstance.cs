using System.Runtime.InteropServices;
using UnityEngine;

namespace Amethyst.Look
{
    /// <summary>
    /// One crowd figure's state for a frame, as its presenter writes it into
    /// <see cref="MonsterCrowd.Instances"/> and the GPU passes read it: 64 bytes, matching
    /// CrowdSkinning.hlsl. Clip indices are into the type's clips; times are seconds into each.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MonsterInstance
    {
        public Vector3 Position; // world metres; y includes a flyer's hover
        public float Yaw; // radians
        public uint ClipA; // the current clip
        public float TimeA;
        public uint ClipB; // the clip it is leaving (crossfade), or ClipA
        public float TimeB;
        public float Blend; // weight of ClipA, 0..1
        public float Scale; // 1 is the type's baked height
        public float HitFlash; // 0..1
        public float Flare; // 0..1: its lines and core lift (a telegraph's warning)
        public uint Tint; // its colour, sRGB RGBA8 (r in the low byte); 0 is its type's hue
        public uint Flags; // CrowdFlags
        public uint Overlay; // Amethyst.Look.Overlay bits: the white status overlays it wears
        public float OverlayAge; // seconds since the newest overlay was attached

        public const int Stride = 64;

        /// <summary>A colour packed for <see cref="Tint"/>.</summary>
        public static uint Pack(Color32 c) =>
            c.r | (uint)c.g << 8 | (uint)c.b << 16 | (uint)c.a << 24;
    }

    [System.Flags]
    public enum CrowdFlags : uint
    {
        None = 0,
        Alive = 1,

        /// <summary>Its death's frame: not drawn, but its bones are blended (a shatter's seed).</summary>
        Dying = 2,
        Hidden = 4,
    }
}
