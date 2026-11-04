using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Amethyst.Look.Editor
{
    /// <summary>
    /// A roster entry's wireframe mesh recipe (tools/recipes/RECIPE.py; the wireframe-mesh
    /// skill): the combined vendor mesh exported to the project's Library/, the recipe run under
    /// Blender's bundled Python, its mesh read back. Nothing it writes is committed.
    /// </summary>
    public static partial class CrowdBaker
    {
        const string BlenderResources = "/Applications/Blender.app/Contents/Resources";

        /// <summary>A recipe's mesh: its geometry on the vendor bones, and which edges draw.</summary>
        sealed class Recipe
        {
            public Geometry Geometry;
            public bool HasNormals;
            public Vector3[] Edges; // per triangle, or null for creases over Angle
            public float Angle;
        }

        static string RecipesDir =>
            Path.GetFullPath(Path.Combine(PackageDir, "..", "..", "tools", "recipes"));

        /// <summary>The recipe's and the kit's hash, empty when the entry has no recipe.</summary>
        static string RecipeHash(Entry e)
        {
            if (string.IsNullOrEmpty(e.recipe))
                return "";
            using var sha = SHA256.Create();
            foreach (var f in new[] { "kit.py", e.recipe + ".py" })
            {
                var path = Path.Combine(RecipesDir, f);
                if (!File.Exists(path))
                    throw new Exception($"CrowdBaker: {e.id}'s recipe needs {path}");
                var b = File.ReadAllBytes(path);
                sha.TransformBlock(b, 0, b.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToBase64String(sha.Hash);
        }

        static string BlenderPython()
        {
            var python = Directory.Exists(BlenderResources)
                ? Directory
                    .GetDirectories(BlenderResources)
                    .Select(d => Path.Combine(d, "python", "bin"))
                    .Where(Directory.Exists)
                    .SelectMany(d => Directory.GetFiles(d, "python3.*"))
                    .FirstOrDefault(f => !f.EndsWith("-config", StringComparison.Ordinal))
                : null;
            return python
                ?? throw new Exception(
                    $"CrowdBaker: recipes run under Blender's Python; no Blender in {BlenderResources}"
                );
        }

        /// <summary>Runs <paramref name="e"/>'s recipe on its combined mesh.</summary>
        static Recipe RunRecipe(Entry e, GameObject root, Geometry g, List<Bone> bones)
        {
            var dir = Path.GetFullPath(Path.Combine("Library", "AmethystRecipes", e.id));
            var src = Path.Combine(dir, "source");
            var outDir = Path.Combine(dir, "mesh");
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            Directory.CreateDirectory(src);
            Export(e, root, g, bones, src);

            var info = new ProcessStartInfo
            {
                FileName = BlenderPython(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            // No user site-packages or PYTHON* variables: Blender's numpy and the kit's folder.
            info.ArgumentList.Add("-E");
            info.ArgumentList.Add("-s");
            info.ArgumentList.Add(Path.Combine(RecipesDir, "kit.py"));
            info.ArgumentList.Add(Path.Combine(RecipesDir, e.recipe + ".py"));
            info.ArgumentList.Add(src);
            info.ArgumentList.Add(outDir);
            using var p = Process.Start(info);
            var stderr = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new Exception(
                    $"CrowdBaker: {e.id}'s recipe {e.recipe}.py failed (exit {p.ExitCode}):\n{stdout}{stderr.Result}"
                );
            Debug.Log($"CrowdBaker: {e.id}: {stdout.Trim()}");
            return Read(outDir);
        }

        /// <summary>The combined mesh as kit.py's Source reads it.</summary>
        static void Export(Entry e, GameObject root, Geometry g, List<Bone> bones, string dir)
        {
            void Floats(string name, IEnumerable<float> values)
            {
                using var w = new BinaryWriter(File.Create(Path.Combine(dir, name)));
                foreach (var v in values)
                    w.Write(v);
            }
            void Ints(string name, IEnumerable<int> values)
            {
                using var w = new BinaryWriter(File.Create(Path.Combine(dir, name)));
                foreach (var v in values)
                    w.Write(v);
            }
            Floats("positions.f32", g.Positions.SelectMany(v => new[] { v.x, v.y, v.z }));
            Floats("normals.f32", g.Normals.SelectMany(v => new[] { v.x, v.y, v.z }));
            Ints("triangles.i32", g.Indices);
            Ints(
                "bones.i32",
                g.BoneIndices.SelectMany(v => new[] { (int)v.x, (int)v.y, (int)v.z, (int)v.w })
            );
            Floats("weights.f32", g.BoneWeights.SelectMany(v => new[] { v.x, v.y, v.z, v.w }));
            // Each bone's bind matrix, row-major: root space into the bone's.
            Floats(
                "binds.f32",
                bones.SelectMany(b => Enumerable.Range(0, 16).Select(i => b.Bind[i / 4, i % 4]))
            );
            var meta = new SourceMeta
            {
                id = e.id,
                heightM = e.heightM,
                vertices = g.Positions.Count,
                triangles = g.Indices.Count / 3,
                bones = bones
                    .Select(b => new SourceBone
                    {
                        name = b.Transform.name,
                        path = AnimationUtility.CalculateTransformPath(b.Transform, root.transform),
                    })
                    .ToArray(),
                parts = g.Parts.ToArray(),
            };
            File.WriteAllText(Path.Combine(dir, "source.json"), JsonUtility.ToJson(meta));
        }

        [Serializable]
        sealed class SourceMeta
        {
            public string id;
            public float heightM;
            public int vertices,
                triangles;
            public SourceBone[] bones;
            public Part[] parts;
        }

        [Serializable]
        struct SourceBone
        {
            public string name,
                path;
        }

        /// <summary>One renderer's vertex range in a combined mesh.</summary>
        [Serializable]
        struct Part
        {
            public string name;
            public int first,
                count;
            public bool rigid;
        }

        [Serializable]
        sealed class MeshMeta
        {
            public int vertices,
                triangles;
            public float edgeAngleDeg;
        }

        static Recipe Read(string dir)
        {
            var meta = JsonUtility.FromJson<MeshMeta>(
                File.ReadAllText(Path.Combine(dir, "mesh.json"))
            );
            float[] F(string name) => Bytes<float>(Path.Combine(dir, name), sizeof(float));
            int[] I(string name) => Bytes<int>(Path.Combine(dir, name), sizeof(int));
            var pos = F("positions.f32");
            var tris = I("triangles.i32");
            var bi = I("bones.i32");
            var bw = F("weights.f32");
            var normalsPath = Path.Combine(dir, "normals.f32");
            var nor = File.Exists(normalsPath) ? F("normals.f32") : null;
            var g = new Geometry();
            for (int v = 0; v < meta.vertices; v++)
            {
                g.Positions.Add(new Vector3(pos[3 * v], pos[3 * v + 1], pos[3 * v + 2]));
                g.Normals.Add(
                    nor != null
                        ? new Vector3(nor[3 * v], nor[3 * v + 1], nor[3 * v + 2])
                        : Vector3.up
                );
                g.BoneIndices.Add(
                    new Vector4(bi[4 * v], bi[4 * v + 1], bi[4 * v + 2], bi[4 * v + 3])
                );
                g.BoneWeights.Add(
                    new Vector4(bw[4 * v], bw[4 * v + 1], bw[4 * v + 2], bw[4 * v + 3])
                );
            }
            g.Indices.AddRange(tris);
            Vector3[] edges = null;
            var edgesPath = Path.Combine(dir, "edges.u8");
            if (File.Exists(edgesPath))
            {
                var b = File.ReadAllBytes(edgesPath);
                edges = new Vector3[meta.triangles];
                for (int t = 0; t < edges.Length; t++)
                    edges[t] = new Vector3(b[3 * t], b[3 * t + 1], b[3 * t + 2]);
            }
            return new Recipe
            {
                Geometry = g,
                HasNormals = nor != null,
                Edges = edges,
                Angle = meta.edgeAngleDeg,
            };
        }

        static T[] Bytes<T>(string path, int size)
            where T : struct
        {
            var b = File.ReadAllBytes(path);
            var a = new T[b.Length / size];
            Buffer.BlockCopy(b, 0, a, 0, a.Length * size);
            return a;
        }

        /// <summary>The geometry as a welded mesh, bone data in UV1 and UV2; normals recomputed
        /// where it has none.</summary>
        static Mesh Welded(string name, Geometry g, bool normals = true)
        {
            var m = new Mesh { name = name };
            if (g.Positions.Count > 65535)
                m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(g.Positions);
            m.SetUVs(1, g.BoneIndices);
            m.SetUVs(2, g.BoneWeights);
            m.SetTriangles(g.Indices, 0, false);
            if (normals)
                m.SetNormals(g.Normals);
            else
                m.RecalculateNormals();
            return m;
        }

        /// <summary>A recipe's style mesh: never decimated, its edges as the recipe chose.</summary>
        static Mesh RecipeStyle(Entry e, Recipe r, int seed, Matrix4x4[] bindposes = null)
        {
            var welded = Welded(e.id + "_recipe", r.Geometry, r.HasNormals);
            if (bindposes != null)
            {
                var weights = new BoneWeight[r.Geometry.Positions.Count];
                for (int v = 0; v < weights.Length; v++)
                {
                    var bi = r.Geometry.BoneIndices[v];
                    var bw = r.Geometry.BoneWeights[v];
                    weights[v] = new BoneWeight
                    {
                        boneIndex0 = (int)bi.x,
                        boneIndex1 = (int)bi.y,
                        boneIndex2 = (int)bi.z,
                        boneIndex3 = (int)bi.w,
                        weight0 = bw.x,
                        weight1 = bw.y,
                        weight2 = bw.z,
                        weight3 = bw.w,
                    };
                }
                welded.boneWeights = weights;
                welded.bindposes = bindposes;
            }
            welded.RecalculateBounds();
            var style = StyleMesh.Unweld(welded, seed, r.Edges, r.Angle);
            style.name = e.id + "_recipe";
            UnityEngine.Object.DestroyImmediate(welded);
            return style;
        }
    }
}
