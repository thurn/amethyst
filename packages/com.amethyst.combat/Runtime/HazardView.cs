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
    /// shapes above the hero's effects.
    /// </summary>
    sealed class HazardView : IDisposable
    {
        const float WallHeight = 0.8f,
            PillarHeight = 1.8f,
            PylonHeight = 1.2f;
        const int PostSides = 8;
        const float BurstSeconds = 0.5f;

        const int BodyQueue = Flat.Effects - 30,
            LineQueue = Flat.Effects - 20,
            CoreQueue = Flat.Effects - 10;

        readonly GameObject root;
        readonly GameObject[] pylons;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Material> materials = new List<Material>();
        readonly List<(Vector3 At, float Time)> bursts = new List<(Vector3, float)>();
        readonly Color white,
            danger,
            dangerFill,
            dangerIdle;

        /// <summary>The view of <paramref name="layout"/>, its lines <paramref name="line"/> m
        /// wide (wider for a farther camera).</summary>
        public HazardView(HazardLayout layout, Palette palette, float line)
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
        }

        /// <summary>Notes a burst to draw.</summary>
        public void Note(in RoomEvent e, float time)
        {
            if (e.Kind == RoomEventKind.Burst)
                bursts.Add((new Vector3(e.Position.X, 0.04f, e.Position.Y), time));
        }

        /// <summary>This frame's pylons, bursts and vents, for <paramref name="room"/>.</summary>
        public void Present(Shapes shapes, CombatRoom room)
        {
            var hazards = room.Hazards;
            for (int i = 0; i < pylons.Length; i++)
            {
                var up = !hazards.Broken(i);
                if (pylons[i].activeSelf != up)
                    pylons[i].SetActive(up);
            }
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var (at, born) = bursts[i];
                var age = (room.Time - born) / BurstSeconds;
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
                switch (hazards.Phase(i, room.Time, out var fill))
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

            void Beam(Vector3 a, Vector3 b, float width)
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
