using System.Runtime.InteropServices;
using UnityEngine;

namespace Amethyst.Look
{
    /// <summary>
    /// One figure's motion clocks for a frame, written beside its <see cref="MonsterInstance"/>
    /// (<see cref="MonsterCrowd.Motion"/>): the latest step and kick (a lunge, a hit, a knock),
    /// from which CrowdCommon.hlsl springs the body, and the life's seed for its swarm tilt and
    /// wobble. Only the body's shape moves, so its feet stay where the presenter put them. 48
    /// bytes, matching CrowdCommon.hlsl.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MonsterMotion
    {
        /// <summary>Seconds since the latest step's plant; negative while its move runs before it.</summary>
        public float StepAge;

        /// <summary>How long the step's move runs before its plant; 0 when the move follows it.</summary>
        public float StepMove;

        /// <summary>The step's travel on the ground (world x, z), in the figure's own heights.</summary>
        public Vector2 Travel;

        /// <summary>Seconds since the latest kick's contact; negative through a lunge's wind-up.</summary>
        public float KickAge;

        /// <summary>The wind-up before the contact; 0 for a hit or a knock.</summary>
        public float KickWind;

        /// <summary>The kick's way on the ground times its strength (0..1).</summary>
        public Vector2 Kick;

        /// <summary>Unique to the life: its swarm tilt and wobble.</summary>
        public uint Seed;
        public float Pad0,
            Pad1,
            Pad2;

        public const int Stride = 48;

        /// <summary>An age past every spring's settling: no event.</summary>
        public const float Long = 1e4f;

        /// <summary>No step and no kick: only the swarm's tilt and wobble.</summary>
        public static MonsterMotion Still(uint seed) =>
            new MonsterMotion
            {
                StepAge = Long,
                KickAge = Long,
                Seed = seed,
            };
    }
}
