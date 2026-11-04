using System;
using System.Collections.Generic;
using Amethyst.Look;
using UnityEngine;

namespace Amethyst.LookTestbed
{
    /// <summary>
    /// A few hundred figures in one-type packs on two counter-rotating rings round the arena's
    /// centre, every figure walking its ring, so the crowd forms the trailer's arcs and streams with
    /// dark gaps between bodies. Every ninth figure wears a white status overlay for a while.
    /// </summary>
    sealed class CrowdDemo : IDisposable
    {
        /// <summary>Each type's share of the crowd, by roster id.</summary>
        static readonly Dictionary<string, float> Shares = new Dictionary<string, float>
        {
            ["skeleton"] = 0.28f,
            ["demon"] = 0.2f,
            ["goblin"] = 0.17f,
            ["mushroom"] = 0.12f,
            ["bat"] = 0.09f,
            ["bigOrk"] = 0.1f,
        };

        const float WalkSpeed = 1.3f; // m/s
        const float Gap = 0.45f; // m of dark between neighbours

        struct Figure
        {
            public int Type,
                Id;
            public float Radius,
                Angle,
                Direction; // +1 counter-clockwise
            public uint Seed;
            public Overlay Wears;
        }

        public readonly MonsterCrowd Crowd;
        readonly MonsterLibrary lib;
        readonly List<Figure>[] byType;
        readonly StatusOverlays overlays = new StatusOverlays();

        public CrowdDemo(MonsterLibrary lib, Palette palette, int count)
        {
            this.lib = lib;
            byType = new List<Figure>[lib.Types.Length];
            for (int i = 0; i < byType.Length; i++)
                byType[i] = new List<Figure>();

            // Packs alternate between an inner and an outer ring, each pack a sector of its ring.
            var packs = new List<(int type, int n)>();
            float total = 0;
            foreach (var t in lib.Types)
                total += Share(t.Id);
            for (int i = 0; i < lib.Types.Length; i++)
                packs.Add((i, Mathf.RoundToInt(count * Share(lib.Types[i].Id) / total)));
            var rings = new[] { (r0: 7.5f, dir: 1f), (r0: 14f, dir: -1f) };
            var sector = new float[2];
            var plan = new List<(int type, int n, int ring)>();
            for (int k = 0; k < packs.Count; k++)
                plan.Add((packs[k].type, packs[k].n, k % 2));
            var demand = new float[2];
            foreach (var p in plan)
                demand[p.ring] += p.n * Spacing(lib.Types[p.type]) * Spacing(lib.Types[p.type]);
            int id = 0;
            foreach (var (type, n, ring) in plan)
            {
                var t = lib.Types[type];
                float s = Spacing(t);
                // A pack's share of its ring's turn, by the area it needs, less a gap between packs.
                float span = Mathf.PI * 2f * (n * s * s) / Mathf.Max(demand[ring], 1e-3f);
                float start = sector[ring] + 0.12f;
                float end = sector[ring] + span - 0.12f;
                sector[ring] += span;
                int placed = 0;
                for (float r = rings[ring].r0; placed < n; r += s)
                for (float a = start; a < end && placed < n; a += s / r)
                {
                    uint seed = (uint)(id * 2654435761u);
                    byType[type]
                        .Add(
                            new Figure
                            {
                                Type = type,
                                Id = id,
                                Radius = r + (Hash(seed, 1) - 0.5f) * 0.3f * s,
                                Angle = a + (Hash(seed, 2) - 0.5f) * 0.3f * s / r,
                                Direction = rings[ring].dir,
                                Seed = seed,
                                Wears = id % 9 == 4 ? (Overlay)(1u << (id / 9 % 3)) : Overlay.None,
                            }
                        );
                    placed++;
                    id++;
                }
            }
            int capacity = 1;
            foreach (var list in byType)
                capacity = Mathf.Max(capacity, list.Count);
            Crowd = new MonsterCrowd(lib, capacity, palette);
        }

        static float Share(string id) => Shares.TryGetValue(id, out var s) ? s : 0f;

        /// <summary>Centre to centre between neighbours: the body's footprint plus the dark gap.</summary>
        static float Spacing(MonsterType t) => Mathf.Max(0.8f, 0.5f * t.HeightM) + Gap;

        static float Hash(uint seed, uint salt)
        {
            uint h = seed * 0x9E3779B1u + salt * 0x85EBCA77u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            return (h & 0xFFFFFF) / 16777216f;
        }

        public void Update(float time)
        {
            for (int type = 0; type < byType.Length; type++)
            {
                var list = byType[type];
                if (list.Count == 0)
                    continue;
                var t = lib.Types[type];
                int walk = Mathf.Max(0, t.Clip("walk"));
                var clip = t.Clips[walk];
                var instances = Crowd.Instances(type, list.Count);
                var motion = Crowd.Motion(type, list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    float travel = WalkSpeed * time;
                    float a = f.Angle + f.Direction * travel / f.Radius;
                    var pos = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * f.Radius;
                    pos.y = t.HoverM;
                    var heading = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * f.Direction;
                    float phase = clip.StrideM > 0 ? travel / clip.StrideM * clip.Length : time;
                    phase += Hash(f.Seed, 3) * clip.Length;
                    var wears = Overlays(f, time);
                    instances[i] = new MonsterInstance
                    {
                        Position = pos,
                        Yaw = Mathf.Atan2(heading.x, heading.y),
                        ClipA = (uint)walk,
                        TimeA = phase,
                        ClipB = (uint)walk,
                        TimeB = phase,
                        Blend = 1,
                        Scale = 1,
                        Flags = (uint)CrowdFlags.Alive,
                        Overlay = (uint)wears.kinds,
                        OverlayAge = wears.age,
                    };
                    motion[i] = MonsterMotion.Still(f.Seed);
                }
            }
            Crowd.Present(time);
        }

        /// <summary>A wearer puts its overlay on for 2.5 s in every 4.</summary>
        (Overlay kinds, float age) Overlays(Figure f, float time)
        {
            if (f.Wears == Overlay.None)
                return (Overlay.None, 0);
            float cycle = (time + Hash(f.Seed, 4) * 4f) % 4f;
            var kinds = overlays.At(f.Id, time, out float age);
            if (kinds == Overlay.None && cycle < 2.5f)
            {
                overlays.Attach(f.Id, f.Wears, time - cycle, 2.5f);
                kinds = overlays.At(f.Id, time, out age);
            }
            return (kinds, age);
        }

        public void Dispose() => Crowd.Dispose();
    }
}
