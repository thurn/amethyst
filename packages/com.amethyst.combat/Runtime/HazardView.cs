using System;
using System.Collections.Generic;
using Amethyst.Action;
using Amethyst.Look;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Amethyst.Combat
{
    /// <summary>
    /// Draws a room's <see cref="Hazards"/> (docs/game-design.md, Arenas and hazards; Look &gt;
    /// Color roles): walls and pillars as grey line structures over dark bodies, below the hero's
    /// effects; pylons the same with a white core, gone once burst, and each burst a white ring;
    /// each Spike Vent's red marker filling toward its eruption, and the eruption, filled red
    /// shapes above the hero's effects; an arena module's laser gates as bright grey-white line
    /// curtains (not red: red means "this will hurt you"), breaking into white shards as they
    /// dissolve.
    /// </summary>
    sealed class HazardView : IDisposable
    {
        const float WallHeight = 0.8f,
            PillarHeight = 1.8f,
            PylonHeight = 1.2f;
        const int PostSides = 8;
        const float BurstSeconds = 0.5f;
        const float GateHeight = 2f,
            GateBars = 0.5f; // m between a curtain's bars
        const float ShardSeconds = 0.8f;
        const int GateShards = 22;

        const int BodyQueue = Flat.Effects - 30,
            LineQueue = Flat.Effects - 20,
            CoreQueue = Flat.Effects - 10;

        readonly GameObject root;
        readonly GameObject[] pylons;
        readonly GameObject[] gates;
        readonly (System.Numerics.Vector2 A, System.Numerics.Vector2 B)[] gateEnds;
        readonly List<(int Gate, float Time)> dissolves = new List<(int, float)>();
        readonly List<(int Gate, float Time)> seals = new List<(int, float)>();
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Material> materials = new List<Material>();
        readonly List<(Vector3 At, float Time)> bursts = new List<(Vector3, float)>();
        readonly Color white,
            danger,
            dangerFill,
            dangerIdle;

        /// <summary>The view of <paramref name="layout"/>, its lines <paramref name="line"/> m
        /// wide (wider for a farther camera), with laser <paramref name="gates"/> across its
        /// doorways (their two ends), shut until <see cref="Gate"/> opens them.</summary>
        public HazardView(
            HazardLayout layout,
            Palette palette,
            float line,
            (System.Numerics.Vector2 A, System.Numerics.Vector2 B)[] gates = null
        )
        {
            root = new GameObject("Hazards");
            var grey = EdgeLook.Linear(palette.Wall) * 1.1f;
            grey.a = 1f;
            var dark = EdgeLook.Linear(palette.Void);
            dark.a = 0.9f;
            white = EdgeLook.Linear(palette.Hero) * 2f;
            white.a = 1f;
            danger = EnemyView.Danger(palette, 0.9f);
            dangerFill = EnemyView.Danger(palette, 0.5f);
            dangerIdle = EnemyView.Danger(palette, 0.22f);
            var body = Paint(dark, BodyQueue);
            var lines = Paint(grey, LineQueue);
            var core = Paint(white, CoreQueue);

            var structure = new Builder();
            var outline = new Builder();
            foreach (var w in layout.Walls)
            {
                var c = w.Center;
                var h = w.Half;
                var corners = new[]
                {
                    new Vector3(c.X - h.X, 0f, c.Y - h.Y),
                    new Vector3(c.X - h.X, 0f, c.Y + h.Y),
                    new Vector3(c.X + h.X, 0f, c.Y + h.Y),
                    new Vector3(c.X + h.X, 0f, c.Y - h.Y),
                };
                structure.Prism(corners, WallHeight);
                outline.PrismEdges(corners, WallHeight, line);
            }
            foreach (var p in layout.Pillars)
            {
                var corners = Polygon(p.Center, p.Radius);
                structure.Prism(corners, PillarHeight);
                outline.PrismEdges(corners, PillarHeight, line);
            }
            Part("Structures", structure, body, root.transform);
            Part("Lines", outline, lines, root.transform);

            pylons = new GameObject[layout.Pylons.Length];
            for (int i = 0; i < pylons.Length; i++)
            {
                var p = layout.Pylons[i];
                var go = new GameObject("Pylon");
                go.transform.SetParent(root.transform, false);
                var corners = Polygon(p.Center, p.Radius);
                var b = new Builder();
                b.Prism(corners, PylonHeight);
                Part("Body", b, body, go.transform);
                var e = new Builder();
                e.PrismEdges(corners, PylonHeight, line);
                Part("Lines", e, lines, go.transform);
                var k = new Builder();
                k.Diamond(
                    new Vector3(p.Center.X, PylonHeight * 0.55f, p.Center.Y),
                    p.Radius * 0.55f,
                    PylonHeight * 0.3f
                );
                Part("Core", k, core, go.transform);
                pylons[i] = go;
            }

            gateEnds = gates ?? Array.Empty<(System.Numerics.Vector2, System.Numerics.Vector2)>();
            this.gates = new GameObject[gateEnds.Length];
            var bright = white * 0.8f;
            bright.a = 1f;
            var curtain = Paint(bright, CoreQueue);
            for (int i = 0; i < gateEnds.Length; i++)
            {
                var go = new GameObject("Gate");
                go.transform.SetParent(root.transform, false);
                Vector3 a = Ground(gateEnds[i].A),
                    b = Ground(gateEnds[i].B);
                var c = new Builder();
                foreach (var h in new[] { 0.05f, GateHeight * 0.5f, GateHeight })
                    c.Beam(a + Vector3.up * h, b + Vector3.up * h, line * 1.2f);
                var bars = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / GateBars));
                for (int k = 0; k <= bars; k++)
                {
                    var at = Vector3.Lerp(a, b, (float)k / bars);
                    c.Beam(
                        at,
                        at + Vector3.up * GateHeight,
                        line * (k == 0 || k == bars ? 1.6f : 0.9f)
                    );
                }
                Part("Curtain", c, curtain, go.transform);
                this.gates[i] = go;
            }
        }

        /// <summary>Shuts or opens gate <paramref name="index"/>: shutting flashes it in, opening
        /// breaks it into white shards, unless <paramref name="quiet"/>.</summary>
        public void Gate(int index, bool shut, bool quiet = false)
        {
            var go = gates[index];
            if (go.activeSelf == shut)
                return;
            go.SetActive(shut);
            if (!quiet)
                (shut ? seals : dissolves).Add((index, Time.time));
        }

        static Vector3 Ground(System.Numerics.Vector2 p) => new Vector3(p.X, 0f, p.Y);

        /// <summary>Notes a burst to draw.</summary>
        public void Note(in RoomEvent e, float time)
        {
            if (e.Kind == RoomEventKind.Burst)
                bursts.Add((new Vector3(e.Position.X, 0.04f, e.Position.Y), time));
        }

        /// <summary>This frame's pylons, bursts, gates' flashes and shards, and vents, by
        /// <paramref name="hazards"/> at room time <paramref name="time"/>; vents cycle only while
        /// <paramref name="live"/> (resting rings otherwise).</summary>
        public void Present(Shapes shapes, Hazards hazards, float time, bool live = true)
        {
            Gates(shapes);
            for (int i = 0; i < pylons.Length; i++)
            {
                var up = !hazards.Broken(i);
                if (pylons[i].activeSelf != up)
                    pylons[i].SetActive(up);
            }
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var (at, born) = bursts[i];
                var age = (time - born) / BurstSeconds;
                if (age < 0f || age >= 1f)
                {
                    bursts.RemoveAt(i);
                    continue;
                }
                var ease = 1f - (1f - age) * (1f - age);
                var c = white;
                c.a = 1f - age;
                shapes.Ring(at, Hazards.BurstRadius * (0.3f + 0.7f * ease), c, Flat.Effects);
                c.a = 0.25f * (1f - age);
                shapes.Disc(at, Hazards.BurstRadius * (0.3f + 0.7f * ease), c, Flat.Effects);
            }
            var vents = hazards.Layout.Vents;
            for (int i = 0; i < vents.Length; i++)
            {
                var v = vents[i];
                var at = new Vector3(v.Center.X, 0.03f, v.Center.Y);
                var fill = 0f;
                var phase = live ? hazards.Phase(i, time, out fill) : VentPhase.Idle;
                switch (phase)
                {
                    case VentPhase.Idle:
                        shapes.Ring(at, v.Radius, dangerIdle, Flat.Telegraphs);
                        break;
                    case VentPhase.Marker:
                        shapes.Ring(at, v.Radius, danger, Flat.Telegraphs);
                        shapes.Disc(at, v.Radius * fill, dangerFill, Flat.Telegraphs + 1);
                        break;
                    case VentPhase.Erupting:
                        var fade = 1f - fill;
                        var burst = danger;
                        burst.a = 0.85f * fade;
                        shapes.Disc(at, v.Radius, burst, Flat.Telegraphs);
                        var spike = danger * 1.6f;
                        spike.a = fade;
                        var reach = v.Radius * (0.7f + 0.5f * Mathf.Min(1f, fill * 4f));
                        for (int k = 0; k < 10; k++)
                        {
                            var a = (k + 0.5f * (i & 1)) * Mathf.PI * 2f / 10f;
                            shapes.Wedge(
                                at + Vector3.up * 0.01f,
                                new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)),
                                reach,
                                9,
                                spike,
                                Flat.Telegraphs + 1
                            );
                        }
                        break;
                }
            }
        }

        // A gate shutting flashes white across its doorway; one opening breaks into shards that
        // tumble out both ways and fall.
        void Gates(Shapes shapes)
        {
            var now = Time.time;
            for (int i = seals.Count - 1; i >= 0; i--)
            {
                var (g, born) = seals[i];
                var age = (now - born) / 0.3f;
                if (age >= 1f)
                {
                    seals.RemoveAt(i);
                    continue;
                }
                Vector3 a = Ground(gateEnds[g].A),
                    b = Ground(gateEnds[g].B);
                var c = white;
                c.a = 0.8f * (1f - age);
                shapes.Lane(
                    a + Vector3.up * 0.04f,
                    b - a,
                    Vector3.Distance(a, b),
                    1.2f,
                    c,
                    Flat.Effects
                );
            }
            for (int i = dissolves.Count - 1; i >= 0; i--)
            {
                var (g, born) = dissolves[i];
                var t = now - born;
                if (t >= ShardSeconds)
                {
                    dissolves.RemoveAt(i);
                    continue;
                }
                Vector3 a = Ground(gateEnds[g].A),
                    b = Ground(gateEnds[g].B);
                var across = Vector3.Cross(Vector3.up, b - a).normalized;
                var c = white;
                c.a = 1f - t / ShardSeconds;
                for (int k = 0; k < GateShards; k++)
                {
                    float R(int salt) =>
                        Mathf.Repeat(
                            Mathf.Sin((k * 7 + salt) * 12.9898f + g * 78.233f) * 43758.5453f,
                            1f
                        );
                    var from =
                        Vector3.Lerp(a, b, R(1)) + Vector3.up * (0.2f + R(2) * (GateHeight - 0.2f));
                    var velocity =
                        across * ((R(3) < 0.5f ? -1f : 1f) * (1.5f + 2.5f * R(4)))
                        + (b - a).normalized * (R(5) - 0.5f) * 2f
                        + Vector3.up * (1f + 2f * R(6));
                    var at = from + velocity * t + Vector3.down * (4.9f * t * t);
                    at.y = Mathf.Max(0.02f, at.y);
                    var spin = R(7) * 6.3f + t * (6f + 8f * R(8));
                    shapes.Wedge(
                        at,
                        new Vector3(
                            Mathf.Cos(spin),
                            0.6f * Mathf.Sin(spin * 0.7f),
                            Mathf.Sin(spin)
                        ),
                        0.22f + 0.2f * R(9),
                        40,
                        c,
                        Flat.Effects
                    );
                }
            }
        }

        Material Paint(Color color, int queue)
        {
            var m = Flat.Make(color, queue);
            materials.Add(m);
            return m;
        }

        void Part(string name, Builder builder, Material material, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = builder.Build(name);
            meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static Vector3[] Polygon(System.Numerics.Vector2 center, float radius)
        {
            var corners = new Vector3[PostSides];
            for (int k = 0; k < PostSides; k++)
            {
                var a = (k + 0.5f) * Mathf.PI * 2f / PostSides;
                corners[k] = new Vector3(
                    center.X + Mathf.Cos(a) * radius,
                    0f,
                    center.Y + Mathf.Sin(a) * radius
                );
            }
            return corners;
        }

        public void Dispose()
        {
            foreach (var m in meshes)
                Object.Destroy(m);
            foreach (var m in materials)
                Object.Destroy(m);
            if (root != null)
                Object.Destroy(root);
        }

        // Triangles for one flat-coloured mesh.
        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var v = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                triangles.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }

            /// <summary>A solid on the ground: its sides and its top.</summary>
            public void Prism(Vector3[] floor, float height)
            {
                var up = Vector3.up * height;
                for (int k = 0; k < floor.Length; k++)
                {
                    var a = floor[k];
                    var b = floor[(k + 1) % floor.Length];
                    Quad(a, a + up, b + up, b);
                }
                var top = vertices.Count;
                foreach (var p in floor)
                    vertices.Add(p + up);
                for (int k = 1; k + 1 < floor.Length; k++)
                    triangles.AddRange(new[] { top, top + k, top + k + 1 });
            }

            /// <summary>The solid's edges as beams <paramref name="width"/> m across.</summary>
            public void PrismEdges(Vector3[] floor, float height, float width)
            {
                var up = Vector3.up * height;
                for (int k = 0; k < floor.Length; k++)
                {
                    var a = floor[k];
                    var b = floor[(k + 1) % floor.Length];
                    Beam(a, b, width);
                    Beam(a + up, b + up, width);
                    Beam(a, a + up, width);
                }
            }

            /// <summary>Two pyramids base to base about <paramref name="at"/>.</summary>
            public void Diamond(Vector3 at, float radius, float half)
            {
                var top = at + Vector3.up * half;
                var bottom = at - Vector3.up * half;
                for (int k = 0; k < 4; k++)
                {
                    var a0 = k * Mathf.PI / 2f;
                    var a1 = (k + 1) * Mathf.PI / 2f;
                    var p = at + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                    var q = at + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                    var v = vertices.Count;
                    vertices.Add(top);
                    vertices.Add(p);
                    vertices.Add(q);
                    vertices.Add(bottom);
                    triangles.AddRange(new[] { v, v + 1, v + 2, v + 3, v + 2, v + 1 });
                }
            }

            public void Beam(Vector3 a, Vector3 b, float width)
            {
                var along = (b - a).normalized;
                var side =
                    Vector3
                        .Cross(along, Mathf.Abs(along.y) > 0.9f ? Vector3.right : Vector3.up)
                        .normalized * (width / 2f);
                var lift = Vector3.Cross(along, side).normalized * (width / 2f);
                Quad(a + side + lift, b + side + lift, b - side + lift, a - side + lift);
                Quad(a + side - lift, b + side - lift, b - side - lift, a - side - lift);
                Quad(a + side + lift, b + side + lift, b + side - lift, a + side - lift);
                Quad(a - side + lift, b - side + lift, b - side - lift, a - side - lift);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65000)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
