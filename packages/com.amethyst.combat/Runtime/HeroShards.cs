using Amethyst.Action;
using UnityEngine;

namespace Amethyst.Combat
{
    /// <summary>The hero's death (docs/game-design.md, Health and death): its body hidden and
    /// thrown out as white shards that tumble and fade within a second.</summary>
    sealed class HeroShards : MonoBehaviour
    {
        const int Count = 48;
        const float Life = 1f;

        readonly Transform[] shards = new Transform[Count];
        readonly Vector3[] velocity = new Vector3[Count];
        readonly Vector3[] spin = new Vector3[Count];
        float born;
        Mesh mesh;
        Material white;

        public static void Burst(Hero hero)
        {
            foreach (var r in hero.GetComponentsInChildren<Renderer>())
                r.enabled = false;
            var go = new GameObject("Hero shards");
            go.transform.position = hero.transform.position;
            go.AddComponent<HeroShards>().Make();
        }

        void Make()
        {
            born = Time.time;
            mesh = new Mesh { name = "Hero shard" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0.09f, 0f),
                new Vector3(0.07f, -0.05f, 0f),
                new Vector3(-0.07f, -0.05f, 0.03f),
            };
            mesh.triangles = new[] { 0, 1, 2 };
            white = Flat.Make(new Color(2.2f, 2.2f, 2.4f, 1f), Flat.Effects);
            for (int i = 0; i < Count; i++)
            {
                var shard = new GameObject("Shard").transform;
                shard.SetParent(transform, false);
                shard.localPosition = new Vector3(0f, Random.Range(0.2f, 1.8f), 0f);
                shard.localRotation = Random.rotation;
                shard.localScale = Vector3.one * Random.Range(0.6f, 1.8f);
                shard.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                shard.gameObject.AddComponent<MeshRenderer>().sharedMaterial = white;
                var out2 = Random.insideUnitCircle.normalized * Random.Range(1.5f, 5f);
                velocity[i] = new Vector3(out2.x, Random.Range(2f, 6f), out2.y);
                spin[i] = Random.insideUnitSphere * 720f;
                shards[i] = shard;
            }
        }

        void Update()
        {
            var age = Time.time - born;
            var dt = Time.deltaTime;
            var shrink = Mathf.Clamp01(1f - (age - 0.6f) / (Life - 0.6f));
            for (int i = 0; i < Count; i++)
            {
                velocity[i].y -= 14f * dt;
                var s = shards[i];
                s.localPosition += velocity[i] * dt;
                s.localRotation *= Quaternion.Euler(spin[i] * dt);
                if (s.localPosition.y < 0.02f)
                {
                    s.localPosition = new Vector3(s.localPosition.x, 0.02f, s.localPosition.z);
                    velocity[i] *= 0.4f;
                }
                if (age > 0.6f)
                    s.localScale = Vector3.one * shrink;
            }
            if (age >= Life)
                Destroy(gameObject);
        }

        void OnDestroy()
        {
            Destroy(mesh);
            Destroy(white);
        }
    }
}
