using System;
using System.IO;
using HoloCube.QuestYOLO;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HoloCube.Editor
{
    // Run separately with a graphics-enabled editor. No Quest camera is used.
    public static class QuestYoloGraphicsValidation
    {
        [MenuItem("HoloCube/Validate GPU preprocessing and inference")]
        public static void Run()
        {
            Require(SystemInfo.supportsComputeShaders, "Compute shaders are required for this check.");
            var definition = AssetDatabase.LoadAssetAtPath<YOLOModel>(QuestYoloSceneSetup.ModelPath);
            CheckLetterboxShader(definition, false);
            CheckLetterboxShader(definition, true);
            CheckGpuInference(definition);
            Debug.Log("HOLOCUBE_GPU_VALIDATION_PASSED: padding, orientation, sRGB handling and full YOLO26 inference on " + SystemInfo.graphicsDeviceName);
        }

        private static void CheckLetterboxShader(YOLOModel definition, bool srgb)
        {
            var image = new Texture2D(4, 2, TextureFormat.RGBA32, false, !srgb);
            var target = new RenderTexture(320, 320, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var material = new Material(definition.LetterboxShader);
            var oldTarget = RenderTexture.active;
            try
            {
                // Unity pixel arrays start at bottom-left: blue bottom, red top.
                var gray = new Color32(128, 128, 128, 255);
                image.SetPixels32(srgb ? new[] { gray,gray,gray,gray,gray,gray,gray,gray } :
                    new[] { new Color32(0,0,255,255),new Color32(0,0,255,255),new Color32(0,0,255,255),new Color32(0,0,255,255),
                            new Color32(255,0,0,255),new Color32(255,0,0,255),new Color32(255,0,0,255),new Color32(255,0,0,255) });
                image.filterMode = FilterMode.Point;
                image.Apply();
                var layout = new ImageLetterbox(new Vector2Int(4, 2), new Vector2Int(320, 320));
                material.SetVector("_ImageRect", layout.ShaderRectangle);
                material.SetFloat("_SourceIsSRGB", GraphicsFormatUtility.IsSRGBFormat(image.graphicsFormat) ? 1 : 0);
                target.Create();
                Graphics.Blit(image, target, material);
                using var tensor = new Tensor<float>(new TensorShape(1, 3, 320, 320));
                TextureConverter.ToTensor(target, tensor, new TextureTransform().SetCoordOrigin(CoordOrigin.TopLeft));
                using var pixels = tensor.ReadbackAndClone();
                Require(Mathf.Abs(pixels[0, 0, 10, 10] - 114f / 255) < 0.01f, "Gray padding");
                if (srgb)
                    Require(Mathf.Abs(pixels[0, 0, 100, 10] - 128f / 255) < 0.01f, "Restore camera sRGB values");
                else
                {
                    Require(pixels[0, 0, 100, 10] > 0.99f && pixels[0, 2, 100, 10] < 0.01f, "Red at top of tensor");
                    Require(pixels[0, 2, 220, 10] > 0.99f && pixels[0, 0, 220, 10] < 0.01f, "Blue at bottom of tensor");
                }
            }
            finally
            {
                RenderTexture.active = oldTarget;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(image);
            }
        }

        private static void CheckGpuInference(YOLOModel definition)
        {
            byte[] bytes = File.ReadAllBytes("Tools/Fixtures/YOLO26/input.f32");
            var values = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            var pixels = new Color[320 * 320];
            for (int y = 0; y < 320; y++)
                for (int x = 0; x < 320; x++)
                {
                    int index = y * 320 + x;
                    pixels[(319-y)*320+x] = new Color(values[index], values[320*320+index], values[2*320*320+index], 1);
                }
            var texture = new Texture2D(320, 320, TextureFormat.RGBA32, false, true);
            texture.SetPixels(pixels);
            texture.Apply();
            try
            {
                using var inference = new YOLOInference(definition);
                bool completed = false;
                var pending = inference.Detect(texture, 0.35f, 32, 24, detections =>
                {
                    Require(detections.Count == 5, "GPU reference detection count");
                    int people = 0, buses = 0;
                    foreach (var detection in detections)
                    {
                        if (detection.ClassId == 0) people++;
                        if (detection.ClassId == 5) buses++;
                    }
                    Require(people == 4 && buses == 1, "GPU reference object classes");
                    completed = true;
                });
                try
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    while (pending.MoveNext())
                    {
                        AsyncGPUReadback.WaitAllRequests();
                        Require(timer.Elapsed.TotalSeconds < 60, "GPU inference timed out");
                    }
                    Require(completed, "GPU completion callback");
                }
                finally { (pending as IDisposable)?.Dispose(); }
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void Require(bool condition, string check)
        {
            if (!condition) throw new Exception("GPU validation failed: " + check);
        }
    }
}
