using System.Collections.Generic;
using UnityEngine;

namespace Amethyst.Look
{
    /// <summary>
    /// The hero in the edge look: every renderer under it drawn from a style mesh of its own mesh
    /// (<see cref="StyleMesh.Unweld"/>: a figure's baked one, else made now from a readable mesh;
    /// skinned by Unity as before) in
    /// HeroEdge, the palette's white, a rung above every crowd hue; or, where the figure has a
    /// wireframe mesh recipe, all of them replaced by that one skin. While it lives it keeps the
    /// chest core on the chest bone. Works on any character: the roster's knight
    /// (<see cref="Spawn"/>) or a hero a testbed builds and animates itself.
    /// </summary>
    public sealed class HeroLook : MonoBehaviour
    {
        static readonly int IdCorePos = Shader.PropertyToID("_CorePos");

        Material material;
        Transform chest;
        readonly List<Mesh> meshes = new List<Mesh>();

        public Material Material => material;

        /// <summary>Dresses <paramref name="hero"/> in the edge look, with <paramref name="figure"/>'s
        /// baked style meshes where it has them: its recipe's mesh unless
        /// <paramref name="vendorMesh"/>.</summary>
        public static HeroLook Dress(
            GameObject hero,
            Palette palette,
            LookFigure figure = null,
            bool vendorMesh = false
        )
        {
            var look = hero.AddComponent<HeroLook>();
            look.material = EdgeLook.Hero(palette);
            bool recipe = figure != null && figure.Recipe != null && !vendorMesh;
            int seed = 1;
            foreach (var r in hero.GetComponentsInChildren<Renderer>(true))
            {
                if (recipe)
                {
                    r.enabled = false;
                    continue;
                }
                var skinned = r as SkinnedMeshRenderer;
                var filter = r is MeshRenderer ? r.GetComponent<MeshFilter>() : null;
                var source =
                    skinned != null ? skinned.sharedMesh
                    : filter != null ? filter.sharedMesh
                    : null;
                if (source == null)
                    continue;
                var mesh = figure != null ? figure.StyleOf(source) : null;
                if (mesh == null)
                {
                    if (!source.isReadable)
                    {
                        Debug.LogWarning(
                            $"HeroLook: {source.name} is neither baked nor readable; left as it is"
                        );
                        continue;
                    }
                    mesh = StyleMesh.Unweld(source, seed++);
                    look.meshes.Add(mesh);
                }
                if (skinned != null)
                    skinned.sharedMesh = mesh;
                else
                    filter.sharedMesh = mesh;
                var mats = new Material[mesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = look.material;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            if (recipe)
                look.Wear(figure);
            var animator = hero.GetComponentInChildren<Animator>();
            look.chest =
                animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.Chest)
                    : null;
            look.LateUpdate();
            return look;
        }

        /// <summary>The figure's recipe mesh as one skin on its bones, found by path.</summary>
        void Wear(LookFigure figure)
        {
            var root = transform;
            Transform Find(string path) =>
                path.Length == 0
                    ? root
                    : root.Find(path)
                        ?? throw new System.InvalidOperationException(
                            $"HeroLook: {figure.Id}'s recipe mesh needs {path} under {root.name}"
                        );
            var go = new GameObject("Wireframe");
            go.transform.SetParent(root, false);
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.bones = System.Array.ConvertAll(figure.RecipeBones, Find);
            skin.rootBone = Find(figure.RecipeRootBone);
            skin.sharedMesh = figure.Recipe;
            skin.sharedMaterial = material;
            skin.updateWhenOffscreen = true;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            skin.receiveShadows = false;
            // Its vendor renderers are off, and an animator culled by them would stop.
            var animator = GetComponentInChildren<Animator>();
            if (animator != null)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        /// <summary>
        /// The roster figure <paramref name="figure"/> at the origin: its prefab with its weapons
        /// in hand, its animator stripped of controller and root motion (the caller animates it),
        /// dressed in the edge look (its recipe's mesh unless <paramref name="vendorMesh"/>).
        /// </summary>
        public static HeroLook Spawn(LookFigure figure, Palette palette, bool vendorMesh = false)
        {
            var go = Instantiate(figure.Prefab);
            go.name = figure.Id;
            var animator = go.GetComponentInChildren<Animator>();
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            foreach (var w in figure.Weapons)
                Hold(w, animator);
            return Dress(go, palette, figure, vendorMesh);
        }

        /// <summary>Puts a weapon in its hand (the crowd baker holds a figure's the same way).</summary>
        public static void Hold(LookFigure.Weapon w, Animator animator)
        {
            var fist = Fist.Of(animator, w.Right);
            var weapon = Instantiate(w.Prefab).transform;
            weapon.SetParent(fist.Hand, false);
            float scale = fist.Hand.lossyScale.x;
            if (!w.OnBone)
            {
                weapon.localRotation = fist.Turn * Quaternion.Euler(w.Pitch, w.Yaw, w.Roll);
                weapon.localPosition = fist.Knuckles + fist.Palm * (w.Palm / scale);
            }
            // At the character's scale, not the hand bone's.
            weapon.localScale = Vector3.one * (fist.Hand.root.lossyScale.x / scale);
        }

        void LateUpdate()
        {
            var at = chest != null ? chest.position : transform.position + Vector3.up * 1.15f;
            material.SetVector(IdCorePos, at);
        }

        void OnDestroy()
        {
            foreach (var m in meshes)
                EdgeLook.Destroy(m);
            EdgeLook.Destroy(material);
        }
    }
}
