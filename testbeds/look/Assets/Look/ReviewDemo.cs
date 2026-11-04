using System;
using System.Linq;
using Amethyst.Look;
using UnityEngine;

namespace Amethyst.LookTestbed
{
    /// <summary>
    /// The wireframe meshes up close, drawn by the crowd renderer: one row per type in look.review,
    /// each figure standing still (idle at its start), mid-stride, mid-swing and recoiling, turned
    /// three-quarters to the camera, spaced for the tallest of them.
    /// </summary>
    sealed class ReviewDemo : IDisposable
    {
        /// <summary>Each column's clip role and how far into the clip it is posed (0 to 1); the
        /// impact time where the roster gives one.</summary>
        static readonly (string role, float at)[] Poses =
        {
            ("idle", 0f),
            ("walk", 0.3f),
            ("attack", -1f),
            ("hit", 0.25f),
        };

        public readonly MonsterCrowd Crowd;
        readonly MonsterLibrary lib;
        readonly int[] rows;

        /// <summary>Between columns and between rows, m.</summary>
        public readonly float Spacing,
            RowGap;

        public ReviewDemo(MonsterLibrary lib, Palette palette, bool vendorMeshes, string types)
        {
            this.lib = lib;
            rows = types
                .Split(',')
                .Select(id => lib.IndexOf(id.Trim()))
                .Where(i => i >= 0)
                .ToArray();
            float tallest = rows.Length > 0 ? rows.Max(i => lib.Types[i].HeightM) : 0f;
            Spacing = Mathf.Max(1.6f, 0.95f * tallest);
            RowGap = Mathf.Max(2.6f, 1.45f * tallest);
            Crowd = new MonsterCrowd(lib, Poses.Length, palette, vendorMeshes);
        }

        /// <summary>Where a row's column stands: rows from the back, columns from the left.</summary>
        public Vector3 Spot(int row, float column) =>
            new Vector3(
                (column - (Poses.Length - 1) * 0.5f) * Spacing,
                0,
                ((rows.Length - 1) * 0.5f - row) * RowGap
            );

        /// <summary>Where the hero stands: at the end of the first row.</summary>
        public Vector3 HeroSpot => Spot(0, Poses.Length);

        public void Update(float time)
        {
            for (int row = 0; row < rows.Length; row++)
            {
                int type = rows[row];
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
