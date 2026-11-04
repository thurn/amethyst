using UnityEngine;
using UnityEngine.Rendering;

namespace Amethyst.Look
{
    /// <summary>
    /// The endless void floor (docs/game-design.md, Look): Playlist Survivors' grid floor, a flat
    /// patch of <see cref="PatchM"/> metres kept centred under where the camera looks
    /// (<see cref="Follow"/>), drawn black with a faint grid in a zone key's line colour
    /// (Resources/AmethystLook/GridFloor.shader).
    /// </summary>
    public sealed class GridFloor : MonoBehaviour
    {
        public const float PatchM = 160f;

        /// <summary>The patch snaps to this (m): a whole number of the grid's major cells.</summary>
        public const float SnapM = 6f;

        static readonly int IdLine = Shader.PropertyToID("_Line");
        static readonly int IdGain = Shader.PropertyToID("_Gain");

        Material material;
        Mesh mesh;

        /// <summary>A floor in <paramref name="zone"/>'s colour, its lines at <paramref name="gain"/>
        /// times their resting light.</summary>
        public static GridFloor Create(Zone zone, float gain = 1f)
        {
            var go = new GameObject("Grid floor");
            var floor = go.AddComponent<GridFloor>();
            floor.material = new Material(MonsterLibrary.Load<Shader>("AmethystLook/GridFloor"))
            {
                name = "Grid floor",
            };
            floor.SetZone(zone, gain);
            floor.mesh = Quad();
            go.AddComponent<MeshFilter>().sharedMesh = floor.mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = floor.material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return floor;
        }

        public void SetZone(Zone zone, float gain)
        {
            material.SetVector(IdLine, (Vector4)EdgeLook.Linear(zone.Line));
            material.SetFloat(IdGain, gain);
        }

        /// <summary>Centres the patch under where <paramref name="camera"/> looks at the ground.</summary>
        public void Follow(Camera camera)
        {
            var t = camera.transform;
            Vector3 p = t.position,
                f = t.forward;
            Vector3 at = f.y < -1e-3f ? p - f * (p.y / f.y) : p;
            transform.position = new Vector3(
                Mathf.Round(at.x / SnapM) * SnapM,
                0f,
                Mathf.Round(at.z / SnapM) * SnapM
            );
        }

        static Mesh Quad()
        {
            float h = PatchM * 0.5f;
            var m = new Mesh { name = "Grid floor" };
            m.vertices = new[]
            {
                new Vector3(-h, 0, -h),
                new Vector3(-h, 0, h),
                new Vector3(h, 0, h),
                new Vector3(h, 0, -h),
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        void OnDestroy()
        {
            EdgeLook.Destroy(material);
            EdgeLook.Destroy(mesh);
        }
    }
}
