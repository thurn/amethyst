using System.Collections.Generic;
using UnityEngine;

namespace Amethyst.Look
{
    /// <summary>
    /// The hero in the edge look: every renderer under it drawn from a style mesh of its own mesh
    /// (<see cref="StyleMesh.Unweld"/>: a figure's baked one, else made now from a readable mesh;
    /// skinned by Unity as before) in
    /// HeroEdge, the palette's white, a rung above every crowd hue. While it lives it keeps the
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
        /// baked style meshes where it has them.</summary>
        public static HeroLook Dress(GameObject hero, Palette palette, LookFigure figure = null)
        {
            var look = hero.AddComponent<HeroLook>();
            look.material = EdgeLook.Hero(palette);
            int seed = 1;
            foreach (var r in hero.GetComponentsInChildren<Renderer>(true))
            {
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
            var animator = hero.GetComponentInChildren<Animator>();
            look.chest =
                animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.Chest)
                    : null;
            look.LateUpdate();
            return look;
        }

        /// <summary>
        /// The roster figure <paramref name="figure"/> at the origin: its prefab with its weapons
        /// in hand, its animator stripped of controller and root motion (the caller animates it),
        /// dressed in the edge look.
        /// </summary>
        public static HeroLook Spawn(LookFigure figure, Palette palette)
        {
            var go = Instantiate(figure.Prefab);
            go.name = figure.Id;
            var animator = go.GetComponentInChildren<Animator>();
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            foreach (var w in figure.Weapons)
                Hold(w, animator);
            return Dress(go, palette, figure);
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
