using System;
using Amethyst.Look;
using UnityEngine;

namespace Amethyst.LookTestbed
{
    /// <summary>
    /// The wireframe meshes up close, drawn by the crowd renderer: a row of knights and a row of
    /// skeletons, each standing still (idle at its start), mid-stride, mid-swing and recoiling,
    /// turned three-quarters to the camera.
    /// </summary>
    sealed class ReviewDemo : IDisposable
    {
        public static readonly string[] Types = { "knight", "skeleton" };

        /// <summary>Each column's clip role and how far into the clip it is posed (0 to 1); the
        /// impact time where the roster gives one.</summary>
        static readonly (string role, float at)[] Poses =
        {
            ("idle", 0f),
            ("walk", 0.3f),
            ("attack", -1f),
            ("hit", 0.25f),
        };

        public const float Spacing = 1.6f;
        public const float RowGap = 2.6f;

        public readonly MonsterCrowd Crowd;
        readonly MonsterLibrary lib;

        public ReviewDemo(MonsterLibrary lib, Palette palette, bool vendorMeshes)
        {
            this.lib = lib;
            Crowd = new MonsterCrowd(lib, Poses.Length, palette, vendorMeshes);
        }

        /// <summary>Where a row's column stands: rows from the back, columns from the left.</summary>
        public static Vector3 Spot(int row, float column) =>
            new Vector3((column - (Poses.Length - 1) * 0.5f) * Spacing, 0, (0.5f - row) * RowGap);

        public void Update(float time)
        {
            for (int row = 0; row < Types.Length; row++)
            {
                int type = lib.IndexOf(Types[row]);
                if (type < 0)
                    continue;
                var t = lib.Types[type];
                var instances = Crowd.Instances(type, Poses.Length);
                var motion = Crowd.Motion(type, Poses.Length);
                for (int i = 0; i < Poses.Length; i++)
                {
                    int clip = Mathf.Max(0, t.Clip(Poses[i].role));
                    var c = t.Clips[clip];
                    float at =
                        Poses[i].at >= 0 ? Poses[i].at * c.Length
                        : c.Impact >= 0 ? c.Impact
                        : 0.5f * c.Length;
                    instances[i] = new MonsterInstance
                    {
                        Position = Spot(row, i),
                        Yaw = Mathf.PI - 0.6f,
                        ClipA = (uint)clip,
                        TimeA = at,
                        ClipB = (uint)clip,
                        TimeB = at,
                        Blend = 1,
                        Scale = 1,
                        Flags = (uint)CrowdFlags.Alive,
                    };
                    motion[i] = MonsterMotion.Still((uint)(row * 31 + i));
                }
            }
            Crowd.Present(time);
        }

        public void Dispose() => Crowd.Dispose();
    }
}
