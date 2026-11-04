using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Amethyst.Editor
{
    /// <summary>
    /// Batch-mode entry points, run by tools/ (<c>am unity PROJECT METHOD</c>). A task throws on
    /// failure, which a batch launch turns into exit code 1.
    /// </summary>
    public static class Tasks
    {
        /// <summary>Opens the project, imports and compiles.</summary>
        public static void Noop()
        {
            if (EditorUtility.scriptCompilationFailed)
                throw new Exception("Tasks.Noop: scripts have compile errors");
            Debug.Log(
                "Tasks.Noop: opened "
                    + Application.productName
                    + " in Unity "
                    + Application.unityVersion
            );
        }

        /// <summary>
        /// <c>-amScene PATH</c>: saves an empty scene there and makes it the only one in the build
        /// settings (a new project's entry scene; its code builds the world).
        /// </summary>
        public static void CreateScene()
        {
            var path = TaskArgs.Get("-amScene", "Assets/Scenes/Main.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new Exception("Tasks.CreateScene: could not save " + path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Tasks.CreateScene: " + path);
        }

        /// <summary>
        /// Runs every <see cref="BeforeBuildAttribute"/> method, then builds the Apple silicon Mac
        /// player: <c>-amOut APP</c> (the .app path),
        /// <c>-amBackend mono|il2cpp</c> (default mono), <c>-amDevelopment</c>. The build's
        /// rewrites of tracked settings are undone, so it leaves the checkout clean.
        /// </summary>
        public static void BuildPlayer()
        {
            var output =
                TaskArgs.Get("-amOut")
                ?? throw new Exception("Tasks.BuildPlayer: needs -amOut APP");
            var backend = TaskArgs.Get("-amBackend", "mono");
            var impl = backend switch
            {
                "mono" => ScriptingImplementation.Mono2x,
                "il2cpp" => ScriptingImplementation.IL2CPP,
                _ => throw new Exception("Tasks.BuildPlayer: unknown backend " + backend),
            };
            foreach (var method in TypeCache.GetMethodsWithAttribute<BeforeBuildAttribute>())
                method.Invoke(null, null);
            var scenes = EditorBuildSettings
                .scenes.Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new Exception("Tasks.BuildPlayer: no scenes in the build settings");

            var kept = TrackedSettings().ToDictionary(p => p, File.ReadAllBytes);
            var previous = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, impl);
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
            ShaderVariantCount.Reset();
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = BuildTarget.StandaloneOSX,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = TaskArgs.Has("-amDevelopment")
                        ? BuildOptions.Development
                        : BuildOptions.None,
                };
                var summary = BuildPipeline.BuildPlayer(options).summary;
                Debug.Log(
                    $"Tasks.BuildPlayer: {summary.result} ({backend}) in {summary.totalTime.TotalSeconds:F1} s, "
                        + $"{summary.totalSize / (1024 * 1024)} MiB, {ShaderVariantCount.Kept} shader variants -> {output}"
                );
                if (summary.result != BuildResult.Succeeded)
                    throw new Exception("Tasks.BuildPlayer: " + summary.result);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previous);
                AssetDatabase.SaveAssets();
                foreach (var kv in kept)
                {
                    if (File.ReadAllBytes(kv.Key).SequenceEqual(kv.Value))
                        continue;
                    File.WriteAllBytes(kv.Key, kv.Value);
                    if (kv.Key.StartsWith("Assets/"))
                        AssetDatabase.ImportAsset(kv.Key, ImportAssetOptions.ForceUpdate);
                    Debug.Log("Tasks.BuildPlayer: restored " + kv.Key);
                }
            }
        }

        // URP's build preprocessing rewrites its assets for the target, and the backend setters
        // serialize defaults into ProjectSettings.
        static IEnumerable<string> TrackedSettings()
        {
            yield return "ProjectSettings/ProjectSettings.asset";
            yield return "ProjectSettings/GraphicsSettings.asset";
            foreach (
                var path in Directory.GetFiles("Assets", "*.asset", SearchOption.TopDirectoryOnly)
            )
                yield return path.Replace('\\', '/');
            if (Directory.Exists("Assets/Settings"))
                foreach (var path in Directory.GetFiles("Assets/Settings", "*.asset"))
                    yield return path.Replace('\\', '/');
        }
    }

    /// <summary>
    /// Marks a static method <see cref="Tasks.BuildPlayer"/> calls before it builds: a package's
    /// bake of what the build needs (the look's crowd).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class BeforeBuildAttribute : Attribute { }

    /// <summary>Counts the shader variants a build keeps, for the build's log line.</summary>
    sealed class ShaderVariantCount : IPreprocessShaders
    {
        public static int Kept { get; private set; }

        public int callbackOrder => int.MaxValue;

        public static void Reset() => Kept = 0;

        public void OnProcessShader(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> data
        ) => Kept += data.Count;
    }

    static class TaskArgs
    {
        public static string Get(string name, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return fallback;
        }

        public static bool Has(string name) =>
            Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
    }
}
