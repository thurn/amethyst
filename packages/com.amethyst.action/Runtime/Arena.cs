using Amethyst.Native;
using UnityEngine;

namespace Amethyst.Action
{
    /// <summary>The baseline world, built in code: ground, light, crystals, the hero and its camera.</summary>
    public static class Arena
    {
        public const float HalfSize = 28f;

        public static Hero Build(ulong seed = 1)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(HalfSize / 5f, 1f, HalfSize / 5f);
            Paint(ground, new Color(0.11f, 0.1f, 0.14f));

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.ambientLight = new Color(0.25f, 0.22f, 0.32f);

            for (int i = 0; i < ActionFlags.Props.Value; i++)
            {
                var x = (NativeLib.Hash01(seed, i, 0) * 2f - 1f) * (HalfSize - 2f);
                var z = (NativeLib.Hash01(seed, i, 1) * 2f - 1f) * (HalfSize - 2f);
                var size = 0.6f + NativeLib.Hash01(seed, i, 2) * 2.2f;
                if (Mathf.Abs(x) < 3f && Mathf.Abs(z) < 3f)
                    continue;
                var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crystal.name = "Crystal";
                crystal.transform.position = new Vector3(x, size * 0.7f, z);
                crystal.transform.localScale = new Vector3(size * 0.5f, size * 1.6f, size * 0.5f);
                crystal.transform.rotation = Quaternion.Euler(
                    0f,
                    NativeLib.Hash01(seed, i, 3) * 90f,
                    35f
                );
                Paint(
                    crystal,
                    Color.Lerp(
                        new Color(0.42f, 0.22f, 0.68f),
                        new Color(0.72f, 0.5f, 0.95f),
                        NativeLib.Hash01(seed, i, 4)
                    )
                );
            }

            var hero = Hero.Create();
            FollowCamera.Create(hero);
            return hero;
        }

        static Material greybox;

        static Material Greybox =>
            greybox != null ? greybox : greybox = Resources.Load<Material>("AmethystGreybox");

        /// <summary>Gives a primitive its own copy of the greybox material (URP Simple Lit, in
        /// Resources/) in a color: the primitives' built-in material doesn't render in URP players.</summary>
        public static void Paint(GameObject go, Color color) =>
            go.GetComponent<Renderer>().material = new Material(Greybox) { color = color };

        /// <summary>Redraws a vendor renderer's built-in-pipeline materials in the greybox one,
        /// keeping their textures.</summary>
        public static void Restyle(Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                materials[i] = new Material(Greybox)
                {
                    mainTexture = materials[i] != null ? materials[i].mainTexture : null,
                    color = Color.white,
                };
            renderer.sharedMaterials = materials;
        }
    }
}
