using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Amethyst.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Amethyst.Look.Editor
{
    /// <summary>
    /// The crowd baker: Playlist Survivors' monster baker, which made the neon edge trailer's
    /// crowd, without its shards. Before every player build it bakes each roster
    /// entry (roster.json beside this file) whose vendor files the project has, unless its key
    /// (the entry and this code) is unchanged, writing only under the ignored Assets/Baked/Look/:
    /// - a crowd type: the combined vendor mesh's three LODs as style meshes (every active skinned
    ///   mesh plus its rigid attachments and held weapons, in root space at the type's height; LOD1
    ///   and LOD2 by Unity's MeshLodUtility), one float texture of skinning matrices (every clip
    ///   at 30 Hz) and Resources/AmethystLook/Types/ID.asset (<see cref="MonsterType"/>);
    /// - a figure (the hero): its renderers' style meshes and
    ///   Resources/AmethystLook/Figures/ID.asset (<see cref="LookFigure"/>).
    /// An entry with a wireframe mesh recipe (CrowdBaker.Recipes.cs) also bakes the recipe's mesh:
    /// a type draws it at every LOD and keeps the vendor's LODs beside it; a figure wears it as one
    /// skin. `uv run --project tools am unity PROJECT Amethyst.Look.Editor.CrowdBaker.Bake` bakes
    /// without building.
    /// </summary>
    public static partial class CrowdBaker
    {
        const string Root = "Assets/Baked/Look";
        const string KeyFile = "bake.key";

        [Serializable]
        sealed class Entry
        {
            public string id;
            public string prefab;
            public float heightM;
            public float hoverM;
            public bool figure;
            public string recipe; // tools/recipes/RECIPE.py builds its wireframe mesh
            public string[] hide = Array.Empty<string>();
            public Weapon[] weapons = Array.Empty<Weapon>();
            public Clip[] clips = Array.Empty<Clip>();
        }

        [Serializable]
        sealed class Weapon
        {
            public string prefab;
            public string hand = "right";
            public float palm,
                pitch,
                yaw,
                roll;
            public bool onBone;
        }

        [Serializable]
        sealed class Clip
        {
            public string role;
            public string guid;
            public string clip;
            public bool loop;
            public float impact = -1,
                end = -1,
                strideM = -1;
            public float liftM;
        }

        [Serializable]
        sealed class Roster
        {
            public Entry[] types = Array.Empty<Entry>();
        }

        /// <summary>A bone (or rigid attachment) of the combined mesh.</summary>
        sealed class Bone
        {
            public Transform Transform;
            public Matrix4x4 Bind; // maps the combined mesh's root-space vertices into the bone's space
        }

        /// <summary>A combined mesh in root space: positions, normals, 4 bones and weights per vertex.</summary>
        sealed class Geometry
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> BoneIndices = new List<Vector4>();
            public readonly List<Vector4> BoneWeights = new List<Vector4>();
            public readonly List<int> Indices = new List<int>();
            public readonly List<Part> Parts = new List<Part>();
        }

        static string PackageDir =>
            UnityEditor
                .PackageManager.PackageInfo.FindForAssembly(typeof(CrowdBaker).Assembly)
                .resolvedPath;

        /// <summary>Bakes every roster entry the project has the vendor files for.</summary>
        [BeforeBuild]
        public static void Bake()
        {
            var roster = JsonUtility.FromJson<Roster>(
                File.ReadAllText(Path.Combine(PackageDir, "Editor", "roster.json"))
            );
            var code = CodeHash();
            bool any = false;
            var baked = new List<string>();
            foreach (var entry in roster.types)
            {
                if (!VendorPresent(entry))
                {
                    // A bake whose vendor files the project dropped goes too.
                    if (AssetDatabase.DeleteAsset(AssetPath(entry)))
                        AssetDatabase.DeleteAsset($"{Root}/{entry.id}");
                    continue;
                }
                any = true;
                if (BakeOne(entry, code))
                    baked.Add(entry.id);
            }
            if (baked.Count > 0)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.SaveAssets();
            }
            Debug.Log(
                any
                    ? $"CrowdBaker: baked {(baked.Count > 0 ? string.Join(", ", baked) : "nothing (all current)")}"
                    : "CrowdBaker: the project has no roster entry's vendor files"
            );
        }

        static bool VendorPresent(Entry e)
        {
            foreach (
                var guid in e
                    .clips.Select(c => c.guid)
                    .Prepend(e.prefab)
                    .Concat(e.weapons.Select(w => w.prefab))
            )
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return false;
            }
            return true;
        }

        static string CodeHash()
        {
            using var sha = SHA256.Create();
            var dir = PackageDir;
            var files = new[]
            {
                "Editor",
                "Runtime/StyleMesh.cs",
                "Runtime/Crowd/MonsterType.cs",
                "Runtime/Hero/LookFigure.cs",
            }
                .SelectMany(p =>
                {
                    var full = Path.Combine(dir, p);
                    return Directory.Exists(full)
                        ? Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories)
                        : new[] { full };
                })
                .OrderBy(f => f, StringComparer.Ordinal);
            foreach (var f in files)
            {
                var b = File.ReadAllBytes(f);
                sha.TransformBlock(b, 0, b.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToBase64String(sha.Hash);
        }

        static bool BakeOne(Entry e, string code)
        {
            string dir = $"{Root}/{e.id}";
            string asset = AssetPath(e);
            string key = Hex(code + JsonUtility.ToJson(e) + RecipeHash(e));
            string keyPath = $"{dir}/{KeyFile}";
            if (
                File.Exists(keyPath)
                && File.ReadAllText(keyPath).Trim() == key
                && File.Exists(asset)
            )
                return false;
            var started = DateTime.UtcNow;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(asset));
            var root = Spawn(e, out var animator);
            try
            {
                string what = e.figure
                    ? BakeFigure(e, root, dir, asset)
                    : BakeType(e, root, animator, dir, asset);
                Debug.Log(
                    $"CrowdBaker: {e.id}: {what} in {(DateTime.UtcNow - started).TotalSeconds:F1} s"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            File.WriteAllText(keyPath, key + "\n");
            return true;
        }

        static string AssetPath(Entry e) =>
            $"{Root}/Resources/{(e.figure ? LookFigure.Path : MonsterLibrary.TypesPath)}/{e.id}.asset";

        // ---------------------------------------------------------------------------------
        // Spawning.

        static string PathOf(string guid) => AssetDatabase.GUIDToAssetPath(guid);

        static GameObject Instantiate(string guid)
        {
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(guid))
                ?? throw new Exception($"CrowdBaker: {PathOf(guid)} is not a prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(
                go,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction
            );
            return go;
        }

        static LookFigure.Weapon WeaponOf(Weapon w) =>
            new LookFigure.Weapon
            {
                Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(w.prefab)),
                Right = w.hand != "left",
                OnBone = w.onBone,
                Palm = w.palm,
                Pitch = w.pitch,
                Yaw = w.yaw,
                Roll = w.roll,
            };

        /// <summary>
        /// The prefab at the origin, unpacked, its hidden children gone, its weapons in their fists,
        /// its animator free of root motion and controller; a crowd type is scaled to its height
        /// (measured on its skins' bind-pose vertices).
        /// </summary>
        static GameObject Spawn(Entry e, out Animator animator)
        {
            var go = Instantiate(e.prefab);
            foreach (var name in e.hide)
            {
                var child =
                    go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name)
                    ?? throw new Exception(
                        $"CrowdBaker: {e.id}'s prefab has no child '{name}' to hide"
                    );
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            animator =
                go.GetComponentInChildren<Animator>()
                ?? throw new Exception($"CrowdBaker: {e.id} has no Animator");
            foreach (var w in e.weapons)
                HeroLook.Hold(WeaponOf(w), animator);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            if (!e.figure)
            {
                float native = MeasureHeight(go);
                if (native > 0.05f)
                    go.transform.localScale *= e.heightM / native;
            }
            return go;
        }

        static float MeasureHeight(GameObject go)
        {
            float top = float.MinValue,
                bottom = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (r.sharedMesh == null)
                    continue;
                foreach (var v in r.sharedMesh.vertices)
                {
                    float y = r.transform.TransformPoint(v).y;
                    top = Mathf.Max(top, y);
                    bottom = Mathf.Min(bottom, y);
                }
            }
            return top == float.MinValue ? 0 : top - Mathf.Min(bottom, go.transform.position.y);
        }

        static AnimationClip LoadClip(Clip c)
        {
            var path = PathOf(c.guid);
            var all = AssetDatabase
                .LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(a => !a.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToList();
            return all.FirstOrDefault(a => a.name == c.clip)
                ?? throw new Exception(
                    $"CrowdBaker: no clip '{c.clip}' in {path} (has {string.Join(", ", all.Select(a => a.name))})"
                );
        }

        // ---------------------------------------------------------------------------------
        // A figure: references and its renderers' style meshes.

        static string BakeFigure(Entry e, GameObject root, string dir, string asset)
        {
            var f = ScriptableObject.CreateInstance<LookFigure>();
            f.name = e.id;
            f.Id = e.id;
            f.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(e.prefab));
            f.Weapons = e.weapons.Select(WeaponOf).ToArray();
            f.Roles = e.clips.Select(c => c.role).ToArray();
            f.Clips = e.clips.Select(LoadClip).ToArray();
            var sources = new List<Mesh>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh =
                    r is SkinnedMeshRenderer s ? s.sharedMesh
                    : r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh
                    : null;
                if (mesh != null && !sources.Contains(mesh))
                    sources.Add(mesh);
            }
            f.Sources = sources.ToArray();
            f.Styles = new Mesh[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                f.Styles[i] = StyleMesh.Unweld(sources[i], StyleMesh.StableSeed(e.id) + i);
                f.Styles[i].name = $"{e.id}_{i}_style";
                Replace(f.Styles[i], $"{dir}/{e.id}_{i}_style.mesh");
            }
            string made = "";
            if (!string.IsNullOrEmpty(e.recipe))
            {
                // The recipe's mesh as one skin on the figure's own bones (a held weapon on its hand).
                var bones = new List<Bone>();
                var recipe = RunRecipe(e, root, Combine(e, root, bones), bones);
                f.Recipe = RecipeStyle(
                    e,
                    recipe,
                    StyleMesh.StableSeed(e.id) + sources.Count,
                    bones.Select(b => b.Bind).ToArray()
                );
                Replace(f.Recipe, $"{dir}/{e.id}_recipe.mesh");
                f.RecipeBones = bones
                    .Select(b =>
                        AnimationUtility.CalculateTransformPath(b.Transform, root.transform)
                    )
                    .ToArray();
                var animator = root.GetComponentInChildren<Animator>();
                f.RecipeRootBone = AnimationUtility.CalculateTransformPath(
                    BodyBone(root, animator),
                    root.transform
                );
                made = $", recipe {f.Recipe.GetIndexCount(0) / 3} triangles";
            }
            Replace(f, asset);
            return $"figure, {sources.Count} style meshes{made}, {f.Clips.Length} clips";
        }

        // ---------------------------------------------------------------------------------
        // A crowd type.

        static string BakeType(
            Entry e,
            GameObject root,
            Animator animator,
            string dir,
            string asset
        )
        {
            var bones = new List<Bone>();
            var g = Combine(e, root, bones);
            var recipe = string.IsNullOrEmpty(e.recipe) ? null : RunRecipe(e, root, g, bones);
            var clips = new List<MonsterClip>();
            float[] texels;
            int width,
                height;
            AnimationMode.StartAnimationMode();
            try
            {
                texels = SampleClips(e, root, animator, bones, clips, out width, out height);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
            var tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
            {
                name = e.id + "_bones",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            tex.SetPixelData(texels, 0);
            tex.Apply(false, false);
            Replace(tex, $"{dir}/{e.id}_bones.asset");

            var clipArray = clips.ToArray();
            var measured = g;
            if (recipe != null)
            {
                // Bounds round both meshes, so either draws uncut.
                measured = new Geometry();
                foreach (var part in new[] { g, recipe.Geometry })
                {
                    measured.Positions.AddRange(part.Positions);
                    measured.BoneIndices.AddRange(part.BoneIndices);
                    measured.BoneWeights.AddRange(part.BoneWeights);
                }
            }
            MeasureBounds(
                measured,
                texels,
                width,
                bones.Count,
                clipArray,
                out float centerY,
                out float radius,
                out float standing
            );
            var welded = Welded(e.id + "_welded", g);
            welded.bounds = new Bounds(new Vector3(0, centerY, 0), Vector3.one * (2 * radius));
            int seed = StyleMesh.StableSeed(e.id);
            var decimated = BuildLods(welded);
            var lods = new[]
            {
                StyleMesh.Unweld(welded, seed),
                StyleMesh.Unweld(decimated[0], seed + 1),
                StyleMesh.Unweld(decimated[1], seed + 2),
            };
            for (int l = 0; l < lods.Length; l++)
            {
                lods[l].name = $"{e.id}_lod{l}";
                Replace(lods[l], $"{dir}/{e.id}_lod{l}.mesh");
            }
            foreach (var m in decimated)
                UnityEngine.Object.DestroyImmediate(m);
            UnityEngine.Object.DestroyImmediate(welded);
            var vendorLods = Array.Empty<Mesh>();
            if (recipe != null)
            {
                var style = RecipeStyle(e, recipe, seed + 3);
                style.bounds = lods[0].bounds;
                Replace(style, $"{dir}/{e.id}_recipe.mesh");
                vendorLods = lods;
                lods = new[] { style, style, style };
            }

            var t = ScriptableObject.CreateInstance<MonsterType>();
            t.name = e.id;
            t.Id = e.id;
            t.Lods = lods;
            t.VendorLods = vendorLods;
            t.Bones = tex;
            t.BoneCount = bones.Count;
            t.Clips = clipArray;
            t.CenterY = centerY;
            t.Radius = radius;
            t.HeightM = standing;
            t.HoverM = e.hoverM;
            Replace(t, asset);
            string tris(Mesh[] ms) => string.Join("/", ms.Select(m => m.GetIndexCount(0) / 3));
            return (
                    recipe != null
                        ? $"recipe {tris(lods.Take(1).ToArray())} triangles (vendor LODs {tris(vendorLods)}), "
                        : $"LOD triangles {tris(lods)}, "
                ) + $"{bones.Count} bones, {height} frames, height {standing:F2} m";
        }

        static int BoneIndex(List<Bone> bones, Transform t, Matrix4x4 bind)
        {
            for (int i = 0; i < bones.Count; i++)
                if (bones[i].Transform == t && Near(bones[i].Bind, bind))
                    return i;
            bones.Add(new Bone { Transform = t, Bind = bind });
            return bones.Count - 1;
        }

        static bool Near(Matrix4x4 a, Matrix4x4 c)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - c[i]) > 1e-3f)
                    return false; // 1 mm: the same bone seen from two skins
            return true;
        }

        /// <summary>The root's rotation and translation (not its scale: the mesh is at the type's height).</summary>
        static Matrix4x4 RootTR(GameObject root) =>
            Matrix4x4.TRS(root.transform.position, root.transform.rotation, Vector3.one);

        /// <summary>
        /// Every active renderer's mesh in root space at the reference pose, each vertex with its 4
        /// strongest bones: a skin's by its bones (each bind pose absorbing the move into root
        /// space), a rigid attachment (a held weapon) by its own transform.
        /// </summary>
        static Geometry Combine(Entry e, GameObject root, List<Bone> bones)
        {
            var g = new Geometry();
            var rootInv = RootTR(root).inverse;
            var skeleton = new HashSet<Transform>(
                root.GetComponentsInChildren<SkinnedMeshRenderer>(false)
                    .SelectMany(s => s.bones)
                    .Where(b => b != null)
            );
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled)
                    continue;
                Mesh mesh;
                Transform[] skinBones = null;
                Matrix4x4[] bindposes = null;
                if (r is SkinnedMeshRenderer smr)
                {
                    mesh = smr.sharedMesh;
                    skinBones = smr.bones;
                    bindposes = mesh != null ? mesh.bindposes : null;
                }
                else if (r is MeshRenderer)
                    mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                else
                    continue;
                if (mesh == null)
                    continue;
                bool rigid =
                    skinBones == null
                    || skinBones.Length == 0
                    || bindposes == null
                    || bindposes.Length == 0;
                var toRoot = rootInv * r.transform.localToWorldMatrix;
                var fromRoot = toRoot.inverse;
                int[] boneOf;
                if (rigid)
                {
                    // A held weapon rides the skeleton's bone it hangs from, so a recipe
                    // and a figure's skin see it on the hand.
                    var on = r.transform.parent;
                    while (on != null && on != root.transform && !skeleton.Contains(on))
                        on = on.parent;
                    boneOf =
                        on != null && skeleton.Contains(on)
                            ? new[]
                            {
                                BoneIndex(bones, on, (rootInv * on.localToWorldMatrix).inverse),
                            }
                            : new[] { BoneIndex(bones, r.transform, fromRoot) };
                }
                else
                {
                    boneOf = new int[skinBones.Length];
                    for (int i = 0; i < skinBones.Length; i++)
                        boneOf[i] =
                            skinBones[i] == null
                                ? -1
                                : BoneIndex(bones, skinBones[i], bindposes[i] * fromRoot);
                }

                int baseVertex = g.Positions.Count;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var perVertex = rigid ? default : mesh.GetBonesPerVertex();
                var weights = rigid ? default : mesh.GetAllBoneWeights();
                int w0 = 0;
                for (int v = 0; v < verts.Length; v++)
                {
                    g.Positions.Add(toRoot.MultiplyPoint3x4(verts[v]));
                    g.Normals.Add(
                        normals.Length > v
                            ? toRoot.MultiplyVector(normals[v]).normalized
                            : Vector3.up
                    );
                    var idx = Vector4.zero;
                    var wt = Vector4.zero;
                    if (rigid)
                    {
                        idx.x = boneOf[0];
                        wt.x = 1;
                    }
                    else
                    {
                        // The 4 strongest influences (Unity sorts them by weight), renormalized.
                        int count = perVertex[v];
                        float sum = 0;
                        int k = 0;
                        for (int i = 0; i < count && k < 4; i++)
                        {
                            var bw = weights[w0 + i];
                            int b = bw.boneIndex < boneOf.Length ? boneOf[bw.boneIndex] : -1;
                            if (b < 0 || bw.weight <= 0)
                                continue;
                            idx[k] = b;
                            wt[k] = bw.weight;
                            sum += bw.weight;
                            k++;
                        }
                        w0 += count;
                        if (sum > 0)
                            wt /= sum;
                        else
                        {
                            idx.x = BoneIndex(bones, r.transform, fromRoot);
                            wt = new Vector4(1, 0, 0, 0);
                        }
                    }
                    g.BoneIndices.Add(idx);
                    g.BoneWeights.Add(wt);
                }
                for (int s = 0; s < mesh.subMeshCount; s++)
                    foreach (var i in mesh.GetTriangles(s))
                        g.Indices.Add(baseVertex + i);
                g.Parts.Add(
                    new Part
                    {
                        name = r.name,
                        first = baseVertex,
                        count = verts.Length,
                        rigid = rigid,
                    }
                );
            }
            if (g.Positions.Count == 0)
                throw new Exception($"CrowdBaker: {e.id} has no active renderers");
            return g;
        }

        /// <summary>The skeleton's root: the hips on a humanoid, else the largest skin's root bone.</summary>
        static Transform BodyBone(GameObject root, Animator animator)
        {
            if (animator.isHuman)
                return animator.GetBoneTransform(HumanBodyBones.Hips);
            var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(false)
                .Where(r => r.rootBone != null && r.sharedMesh != null)
                .OrderByDescending(r => r.sharedMesh.vertexCount)
                .FirstOrDefault();
            return smr != null ? smr.rootBone : animator.transform;
        }

        static void Pose(Animator animator, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, time);
            AnimationMode.EndSampling();
        }

        /// <summary>Every clip's skinning matrices at the clip rate: 3 float4 rows per bone per frame.</summary>
        static float[] SampleClips(
            Entry e,
            GameObject root,
            Animator animator,
            List<Bone> bones,
            List<MonsterClip> clips,
            out int width,
            out int height
        )
        {
            int row = 0;
            var animationClips = new List<AnimationClip>();
            foreach (var r in e.clips)
            {
                var clip = LoadClip(r);
                var c = new MonsterClip
                {
                    Role = r.role,
                    Name = clip.name,
                    FirstRow = row,
                    Length = clip.length,
                    Frames = MonsterClip.FrameCount(clip.length),
                    Loop = r.loop,
                    Impact = r.impact,
                    End = r.end,
                    StrideM = r.strideM,
                };
                clips.Add(c);
                animationClips.Add(clip);
                row += c.Frames;
            }
            width = bones.Count * 3;
            height = row;
            if (width > 16384 || height > 16384)
                throw new Exception(
                    $"CrowdBaker: {e.id} bone texture {width}x{height} is too large"
                );
            var body = BodyBone(root, animator);
            var texels = new float[width * height * 4];
            var rootTRInv = RootTR(root).inverse;
            for (int ci = 0; ci < clips.Count; ci++)
            {
                var c = clips[ci];
                var clip = animationClips[ci];
                // A looping clip that travels would jump at its seam: its horizontal drift is removed
                // linearly (the presenter moves the figure instead).
                var drift = Vector3.zero;
                if (c.Loop)
                {
                    Pose(animator, clip, c.Length);
                    var end = rootTRInv.MultiplyPoint3x4(body.position);
                    Pose(animator, clip, 0);
                    drift = end - rootTRInv.MultiplyPoint3x4(body.position);
                    drift.y = 0;
                    if (drift.magnitude <= 0.05f)
                        drift = Vector3.zero;
                }
                float lift = e.clips[ci].liftM;
                for (int f = 0; f < c.Frames; f++)
                {
                    float time = c.FrameTime(f);
                    Pose(animator, clip, time);
                    var rootInv =
                        Matrix4x4.Translate(Vector3.up * lift - drift * (time / c.Length))
                        * rootTRInv;
                    for (int b = 0; b < bones.Count; b++)
                    {
                        var m = rootInv * bones[b].Transform.localToWorldMatrix * bones[b].Bind;
                        for (int r = 0; r < 3; r++)
                        {
                            int at = (((c.FirstRow + f) * width) + b * 3 + r) * 4;
                            var v = m.GetRow(r);
                            texels[at] = v.x;
                            texels[at + 1] = v.y;
                            texels[at + 2] = v.z;
                            texels[at + 3] = v.w;
                        }
                    }
                }
            }
            return texels;
        }

        /// <summary>
        /// Yaw-invariant bounding spheres around the vertical axis, one per clip (the culling pass
        /// takes the larger of a figure's two clips) and one over every clip; every second vertex of
        /// every second frame, skinned, with a 5% margin. The standing height is the idle's.
        /// </summary>
        static void MeasureBounds(
            Geometry g,
            float[] texels,
            int width,
            int boneCount,
            MonsterClip[] clips,
            out float centerY,
            out float radius,
            out float standing
        )
        {
            var all = new List<Vector3>();
            var mats = new Matrix4x4[boneCount];
            for (int ci = 0; ci < clips.Length; ci++)
            {
                var pts = new List<Vector3>();
                for (int f = 0; f < clips[ci].Frames; f += f + 2 < clips[ci].Frames ? 2 : 1)
                {
                    for (int b = 0; b < boneCount; b++)
                    {
                        var m = Matrix4x4.identity;
                        for (int r = 0; r < 3; r++)
                        {
                            int at = (((clips[ci].FirstRow + f) * width) + b * 3 + r) * 4;
                            m.SetRow(
                                r,
                                new Vector4(
                                    texels[at],
                                    texels[at + 1],
                                    texels[at + 2],
                                    texels[at + 3]
                                )
                            );
                        }
                        mats[b] = m;
                    }
                    for (int v = 0; v < g.Positions.Count; v += 2)
                    {
                        var p = Vector3.zero;
                        var bi = g.BoneIndices[v];
                        var bw = g.BoneWeights[v];
                        for (int k = 0; k < 4; k++)
                            if (bw[k] > 0)
                                p += bw[k] * mats[(int)bi[k]].MultiplyPoint3x4(g.Positions[v]);
                        pts.Add(p);
                    }
                    if (f == clips[ci].Frames - 1)
                        break;
                }
                Sphere(pts, out clips[ci].CenterY, out clips[ci].Radius, out clips[ci].HeightM);
                all.AddRange(pts);
            }
            Sphere(all, out centerY, out radius, out _);
            int idle = Array.FindIndex(clips, c => c.Role == "idle");
            standing = clips[Math.Max(0, idle)].HeightM;
        }

        static void Sphere(List<Vector3> pts, out float centerY, out float radius, out float height)
        {
            float minY = float.MaxValue,
                maxY = float.MinValue;
            foreach (var p in pts)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            centerY = 0.5f * (minY + maxY);
            float r2 = 0;
            foreach (var p in pts)
                r2 = Mathf.Max(r2, p.x * p.x + p.z * p.z + (p.y - centerY) * (p.y - centerY));
            radius = Mathf.Sqrt(r2) * 1.05f + 0.02f;
            height = Mathf.Max(0.05f, maxY);
        }

        /// <summary>
        /// LOD1 and LOD2 of the welded mesh by Unity's Mesh LOD generation (MeshLodUtility, about
        /// half the indices per level, every vertex kept), each level's indices its own mesh.
        /// </summary>
        static Mesh[] BuildLods(Mesh lod0)
        {
            var work = UnityEngine.Object.Instantiate(lod0);
            try
            {
                MeshLodUtility.GenerateMeshLods(work, 2);
                var result = new Mesh[2];
                for (int level = 1; level <= 2; level++)
                {
                    var m = UnityEngine.Object.Instantiate(lod0);
                    int[] idx =
                        work.lodCount > level
                            ? LodIndices(work, work.GetSubMesh(0), work.GetLod(0, level))
                            : lod0.GetIndices(0, false);
                    m.SetIndices(
                        idx,
                        MeshTopology.Triangles,
                        0,
                        false,
                        lod0.GetSubMesh(0).baseVertex
                    );
                    result[level - 1] = m;
                }
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(work);
            }
        }

        static int[] LodIndices(Mesh work, SubMeshDescriptor d, MeshLodRange range)
        {
            using var data = Mesh.AcquireReadOnlyMeshData(work);
            var md = data[0];
            var idx = new int[range.indexCount];
            int start = d.indexStart + (int)range.indexStart;
            if (md.indexFormat == IndexFormat.UInt16)
            {
                var all = md.GetIndexData<ushort>();
                for (int i = 0; i < idx.Length; i++)
                    idx[i] = all[start + i];
            }
            else
            {
                var all = md.GetIndexData<int>();
                for (int i = 0; i < idx.Length; i++)
                    idx[i] = all[start + i];
            }
            return idx;
        }

        static void Replace(UnityEngine.Object asset, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        static string Hex(string s)
        {
            using var sha = SHA256.Create();
            return string.Concat(
                sha.ComputeHash(Encoding.UTF8.GetBytes(s)).Take(8).Select(b => b.ToString("x2"))
            );
        }
    }
}
