using UnityEngine;
using UnityEngine.Rendering;

namespace Amethyst.Action
{
    /// <summary>
    /// What the sword moves draw (docs/game-design.md, Look): white crescents for the cuts, a
    /// streak down the Lunge's lane and a ring where the Leap Slam lands, each flat, large but thin,
    /// sweeping in over the move's startup and thinning away within a few frames of contact; and
    /// the Leap Slam's cooldown as an arc under the hero. Hovl's Sword slash shapes, redrawn as
    /// meshes in the greybox white.
    /// </summary>
    public sealed class SwordEffects : MonoBehaviour
    {
        const int Segments = 32;
        const float Fade = 0.12f;

        // How much of a cut shows on its first frame, so that frame already reads as the strike.
        const float Lead = 0.4f;

        enum Kind
        {
            Crescent,
            Streak,
            Ring,
        }

        static Material white;

        Kind kind;
        float born;
        float sweep;
        float radius;
        float angle;
        float thickness;
        float side;
        Mesh mesh;
        readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];

        /// <summary>A cut of <paramref name="degrees"/> and <paramref name="reach"/> m around
        /// <paramref name="at"/>, sweeping from <paramref name="fromRight"/> over
        /// <paramref name="sweepSeconds"/>.</summary>
        public static void Crescent(
            Vector3 at,
            Vector3 toward,
            float reach,
            float degrees,
            float width,
            bool fromRight,
            float sweepSeconds
        ) =>
            Spawn(
                Kind.Crescent,
                at,
                toward,
                reach,
                degrees,
                width,
                fromRight ? 1f : -1f,
                sweepSeconds
            );

        /// <summary>A thrust <paramref name="length"/> m long from <paramref name="at"/>.</summary>
        public static void Streak(
            Vector3 at,
            Vector3 toward,
            float length,
            float width,
            float sweepSeconds
        ) => Spawn(Kind.Streak, at, toward, length, 0f, width, 1f, sweepSeconds);

        /// <summary>A landing ring growing out to <paramref name="reach"/> m.</summary>
        public static void Ring(Vector3 at, float reach, float width) =>
            Spawn(Kind.Ring, at, Vector3.forward, reach, 360f, width, 1f, 0.1f);

        static void Spawn(
            Kind kind,
            Vector3 at,
            Vector3 toward,
            float radius,
            float degrees,
            float thickness,
            float side,
            float sweep
        )
        {
            var go = new GameObject("Sword " + kind);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toward));
            var fx = go.AddComponent<SwordEffects>();
            fx.kind = kind;
            fx.born = Time.time;
            fx.radius = radius;
            fx.angle = degrees;
            fx.thickness = thickness;
            fx.side = side;
            fx.sweep = Mathf.Max(sweep, 1e-3f);
            fx.Draw(Lead, 1f);
        }

        /// <summary>A flat white strip of <see cref="Segments"/> quads on <paramref name="go"/>,
        /// whose <paramref name="vertices"/> alternate inner and outer edge.</summary>
        static Mesh Surface(GameObject go, Vector3[] vertices)
        {
            if (white == null)
            {
                white = new Material(Resources.Load<Material>("AmethystGreybox"))
                {
                    color = Color.white,
                };
                // The strips wind either way with the swing's side.
                white.SetFloat("_Cull", (float)CullMode.Off);
            }
            var mesh = new Mesh { name = go.name };
            mesh.MarkDynamic();
            var triangles = new int[Segments * 6];
            for (int k = 0; k < Segments; k++)
            {
                int a = k * 2;
                triangles[k * 6] = a;
                triangles[k * 6 + 1] = a + 2;
                triangles[k * 6 + 2] = a + 1;
                triangles[k * 6 + 3] = a + 1;
                triangles[k * 6 + 4] = a + 2;
                triangles[k * 6 + 5] = a + 3;
            }
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < normals.Length; i++)
                normals[i] = Vector3.up;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = white;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mesh;
        }

        void Update()
        {
            var age = Time.time - born;
            var reveal = Mathf.Lerp(Lead, 1f, Mathf.Clamp01(age / sweep));
            var thin = 1f - Mathf.Clamp01((age - sweep) / Fade);
            if (thin <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            Draw(reveal, thin);
        }

        void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }

        // Rebuilds the strip of quads: reveal is how much of it has swept in, thin how much of its
        // width is left.
        void Draw(float reveal, float thin)
        {
            if (mesh == null)
                mesh = Surface(gameObject, vertices);
            var grow =
                kind == Kind.Ring ? Mathf.Lerp(0.55f, 1f, 1f - (1f - reveal) * (1f - reveal)) : 1f;
            for (int k = 0; k <= Segments; k++)
            {
                var u = (float)k / Segments;
                var swept = kind == Kind.Ring || (side > 0f ? u <= reveal : 1f - u <= reveal);
                var taper = kind == Kind.Ring ? 1f : Mathf.Sin(Mathf.PI * u);
                var w = swept ? thickness * thin * taper : 0f;
                Vector3 inner,
                    outer;
                if (kind == Kind.Streak)
                {
                    var z = u * radius;
                    inner = new Vector3(-w / 2f, 0f, z);
                    outer = new Vector3(w / 2f, 0f, z);
                }
                else
                {
                    var a = (u - 0.5f) * angle * side * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    var r = radius * grow;
                    // A crescent hugs its rim; the ring's band is centred on its radius.
                    inner = dir * (kind == Kind.Ring ? r - w / 2f : r - w);
                    outer = dir * (kind == Kind.Ring ? r + w / 2f : r);
                }
                vertices[k * 2] = inner;
                vertices[k * 2 + 1] = outer;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// The Leap Slam's cooldown, drawn under <paramref name="hero"/> as an arc of what is left:
        /// a full ring right after a slam, gone once it is ready.
        /// </summary>
        public sealed class CooldownRing : MonoBehaviour
        {
            const float Radius = 0.85f,
                Width = 0.06f;

            Hero hero;
            Mesh mesh;
            readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
            MeshRenderer view;

            public static void Attach(Hero hero)
            {
                var go = new GameObject("Slam cooldown");
                go.transform.SetParent(hero.transform, false);
                go.transform.localPosition = Vector3.up * 0.03f;
                var ring = go.AddComponent<CooldownRing>();
                ring.hero = hero;
                ring.mesh = Surface(go, ring.vertices);
                ring.view = go.GetComponent<MeshRenderer>();
            }

            void LateUpdate()
            {
                var left = hero.SlamCooldown;
                view.enabled = left > 0f;
                if (!view.enabled)
                    return;
                // Fixed to the world, so it doesn't spin as the hero turns.
                transform.rotation = Quaternion.identity;
                for (int k = 0; k <= Segments; k++)
                {
                    var a = (float)k / Segments * left * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    vertices[k * 2] = dir * (Radius - Width / 2f);
                    vertices[k * 2 + 1] = dir * (Radius + Width / 2f);
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }

            void OnDestroy()
            {
                if (mesh != null)
                    Destroy(mesh);
            }
        }
    }
}
