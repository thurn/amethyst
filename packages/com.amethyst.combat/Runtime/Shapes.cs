using System.Collections.Generic;
using Amethyst.Action;
using UnityEngine;
using UnityEngine.Rendering;

namespace Amethyst.Combat
{
    /// <summary>
    /// The fight's flat shapes, redrawn every frame from a pool of renderers in flat colours
    /// (<see cref="Flat"/>): discs, rings, wedges and lanes on the ground, and bars facing the camera.
    /// Call <see cref="Begin"/>, draw, then <see cref="End"/>, which hides what wasn't drawn.
    /// </summary>
    sealed class Shapes : System.IDisposable
    {
        const int Segments = 40;

        struct Item
        {
            public Transform Transform;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
        }

        readonly Transform root;
        readonly List<Item> pool = new List<Item>();
        readonly Dictionary<(Color, int), Material> materials =
            new Dictionary<(Color, int), Material>();
        readonly Dictionary<int, Mesh> wedges = new Dictionary<int, Mesh>();
        readonly Mesh disc,
            ring,
            lane,
            quad;
        int used;

        public Shapes()
        {
            root = new GameObject("Shapes").transform;
            disc = Sector(360f, 0f);
            ring = Sector(360f, 0.82f);
            quad = new Mesh { name = "Quad" };
            quad.vertices = new[]
            {
                new Vector3(0f, -0.5f, 0f),
                new Vector3(1f, -0.5f, 0f),
                new Vector3(0f, 0.5f, 0f),
                new Vector3(1f, 0.5f, 0f),
            };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            lane = new Mesh { name = "Lane" };
            lane.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 0f, 1f),
                new Vector3(0.5f, 0f, 1f),
            };
            lane.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        }

        public void Begin() => used = 0;

        /// <summary>A filled disc of <paramref name="radius"/> m on the ground at <paramref name="at"/>.</summary>
        public void Disc(Vector3 at, float radius, Color color, int queue) =>
            Draw(disc, at, Quaternion.identity, Vector3.one * radius, color, queue);

        /// <summary>A thin ring whose outer edge is <paramref name="radius"/> m.</summary>
        public void Ring(Vector3 at, float radius, Color color, int queue) =>
            Draw(ring, at, Quaternion.identity, Vector3.one * radius, color, queue);

        /// <summary>A filled wedge of <paramref name="degrees"/> out to <paramref name="reach"/> m
        /// toward <paramref name="toward"/>.</summary>
        public void Wedge(
            Vector3 at,
            Vector3 toward,
            float reach,
            int degrees,
            Color color,
            int queue
        )
        {
            if (!wedges.TryGetValue(degrees, out var mesh))
                wedges[degrees] = mesh = Sector(degrees, 0f);
            Draw(mesh, at, Quaternion.LookRotation(toward), Vector3.one * reach, color, queue);
        }

        /// <summary>A filled lane <paramref name="width"/> m wide from <paramref name="at"/> out to
        /// <paramref name="length"/> m toward <paramref name="toward"/>.</summary>
        public void Lane(
            Vector3 at,
            Vector3 toward,
            float length,
            float width,
            Color color,
            int queue
        ) =>
            Draw(
                lane,
                at,
                Quaternion.LookRotation(toward),
                new Vector3(width, 1f, length),
                color,
                queue
            );

        /// <summary>A bar facing <paramref name="camera"/>, centred at <paramref name="at"/>, filled
        /// from the left to <paramref name="fill"/>.</summary>
        public void Bar(Vector3 at, Camera camera, float width, float fill, Color back, Color front)
        {
            var turn = camera.transform.rotation;
            var left = at - turn * Vector3.right * (width / 2f);
            var height = width * 0.12f;
            Draw(quad, left, turn, new Vector3(width, height, 1f), back, Flat.Bars);
            if (fill > 0f)
                Draw(
                    quad,
                    left - camera.transform.forward * 0.01f,
                    turn,
                    new Vector3(width * Mathf.Clamp01(fill), height, 1f),
                    front,
                    Flat.Bars + 1
                );
        }

        public void End()
        {
            for (int i = used; i < pool.Count; i++)
                if (pool[i].Renderer.enabled)
                    pool[i].Renderer.enabled = false;
        }

        void Draw(Mesh mesh, Vector3 at, Quaternion turn, Vector3 scale, Color color, int queue)
        {
            if (used == pool.Count)
            {
                var go = new GameObject("Shape");
                go.transform.SetParent(root, false);
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                pool.Add(
                    new Item
                    {
                        Transform = go.transform,
                        Filter = go.AddComponent<MeshFilter>(),
                        Renderer = r,
                    }
                );
            }
            var item = pool[used++];
            item.Transform.SetPositionAndRotation(at, turn);
            item.Transform.localScale = scale;
            item.Filter.sharedMesh = mesh;
            if (!materials.TryGetValue((color, queue), out var material))
                materials[(color, queue)] = material = Flat.Make(color, queue);
            item.Renderer.sharedMaterial = material;
            item.Renderer.enabled = true;
        }

        // A unit sector of `degrees` about +z on the ground, from `inner` of its radius out.
        static Mesh Sector(float degrees, float inner)
        {
            var vertices = new Vector3[(Segments + 1) * 2];
            var triangles = new int[Segments * 6];
            for (int k = 0; k <= Segments; k++)
            {
                var a = ((float)k / Segments - 0.5f) * degrees * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                vertices[k * 2] = dir * inner;
                vertices[k * 2 + 1] = dir;
                if (k == Segments)
                    break;
                int v = k * 2,
                    t = k * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 3;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }
            var mesh = new Mesh { name = $"Sector {degrees}" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            foreach (var m in materials.Values)
                Object.Destroy(m);
            foreach (var m in wedges.Values)
                Object.Destroy(m);
            Object.Destroy(disc);
            Object.Destroy(ring);
            Object.Destroy(lane);
            Object.Destroy(quad);
            if (root != null)
                Object.Destroy(root.gameObject);
        }
    }
}
