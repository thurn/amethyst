using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Amethyst
{
    /// <summary>
    /// The run's own object (<see cref="Run"/>): quits after <c>--seconds</c>, keeps the
    /// <c>--capture</c> stills (every variant's with <c>--capture-variants</c>), and draws the
    /// variant switcher in interactive runs of a project with variants. A batch-mode run exits
    /// 3 if anything logged an error or exception.
    /// </summary>
    sealed class RunDriver : MonoBehaviour
    {
        float sceneStart;
        int nextCapture;
        int errors;
        bool overlay = true;

        void Awake()
        {
            Application.logMessageReceived += Count;
            SceneManager.sceneLoaded += OnSceneLoaded;
            sceneStart = Time.time;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Count;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Count(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors++;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            sceneStart = Time.time;
            nextCapture = 0;
        }

        void Update()
        {
            var args = Run.Args;
            if (args.Seconds >= 0 && Time.time >= args.Seconds)
            {
                Quit();
                return;
            }
            if (!args.Captures || nextCapture >= args.CaptureAt.Count)
                return;
            if (Time.time - sceneStart < args.CaptureAt[nextCapture])
                return;
            Capture(args, args.CaptureAt[nextCapture]);
            nextCapture++;
            if (nextCapture < args.CaptureAt.Count)
                return;
            if (args.CaptureVariants && Run.VariantIndex + 1 < Run.Variants.Count)
                Run.SelectVariant(Run.VariantIndex + 1);
            else if (args.Seconds < 0)
                Quit();
        }

        void Quit()
        {
            var code = Application.isBatchMode && errors > 0 ? 3 : 0;
            Debug.Log($"amethyst: quitting with {code} after {Time.time:F1} s ({errors} errors)");
            Application.Quit(code);
            enabled = false;
        }

        void Capture(LaunchArgs args, float at)
        {
            Directory.CreateDirectory(args.CaptureDir);
            var label = Run.CurrentVariant?.Name ?? SceneManager.GetActiveScene().name;
            var path = Path.Combine(args.CaptureDir, $"{label}-{at:0.##}s.png");
            var target = RenderTexture.GetTemporary(
                args.CaptureWidth,
                args.CaptureHeight,
                24,
                RenderTextureFormat.ARGB32
            );
            try
            {
                foreach (var camera in Camera.allCameras.OrderBy(c => c.depth))
                {
                    var previous = camera.targetTexture;
                    camera.targetTexture = target;
                    try
                    {
                        camera.Render();
                    }
                    finally
                    {
                        camera.targetTexture = previous;
                    }
                }
                var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
                request.WaitForCompletion();
                if (request.hasError)
                    throw new Exception("GPU readback failed");
                var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.LoadRawTextureData(request.GetData<byte>());
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Destroy(image);
                Debug.Log("amethyst: captured " + path);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(target);
            }
        }

        void OnGUI()
        {
            var variants = Run.Variants;
            if (Application.isBatchMode || variants.Count == 0 || !CoreFlags.Overlay.Value)
                return;
            var e = Event.current;
            if (e.type == EventType.KeyDown)
                HandleKey(e.keyCode, variants.Count);
            if (!overlay)
                return;
            var lines = new List<string>();
            for (int i = 0; i < variants.Count; i++)
                lines.Add($"{(i == Run.VariantIndex ? "▶" : "  ")} {i + 1}  {variants[i].Name}");
            var about = Run.CurrentVariant?.About;
            if (!string.IsNullOrEmpty(about))
                lines.Add("\n" + about);
            lines.Add("\n1-9 or Tab: switch   R: restart   H: hide");
            var text = string.Join("\n", lines);
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                wordWrap = true,
                padding = new RectOffset(12, 12, 10, 10),
            };
            var width = 360f;
            var height = style.CalcHeight(new GUIContent(text), width);
            GUI.Box(new Rect(16, 16, width, height), text, style);
        }

        void HandleKey(KeyCode key, int count)
        {
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9 && key - KeyCode.Alpha1 < count)
                Run.SelectVariant(key - KeyCode.Alpha1);
            else if (key == KeyCode.Tab)
                Run.SelectVariant((Run.VariantIndex + 1) % count);
            else if (key == KeyCode.R)
                Run.SelectVariant(Run.VariantIndex);
            else if (key == KeyCode.H)
                overlay = !overlay;
        }
    }
}
