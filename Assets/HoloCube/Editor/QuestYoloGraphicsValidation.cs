using System;
using System.IO;
using HoloCube.QuestYOLO;
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
        [MenuItem("HoloCube/Validate camera preprocessing")]
        public static void Run()
        {
            Require(SystemInfo.supportsComputeShaders, "Compute shaders are required for this check.");
            var definition = AssetDatabase.LoadAssetAtPath<YOLOModel>(QuestYoloSceneSetup.ModelPath);
            CheckLetterboxShader(definition, false);
            CheckLetterboxShader(definition, true);
            SaveReferencePixels(definition);
            Debug.Log("HOLOCUBE_PREPROCESSING_PASSED: padding, orientation and sRGB handling on " + SystemInfo.graphicsDeviceName);
        }

        private static void CheckLetterboxShader(YOLOModel definition, bool srgb)
        {
            var image = new Texture2D(4, 2, TextureFormat.RGBA32, false, !srgb);
            try
            {
                var gray = new Color32(128, 128, 128, 255);
                image.SetPixels32(srgb ? new[] { gray,gray,gray,gray,gray,gray,gray,gray } :
                    new[] { new Color32(0,0,255,255),new Color32(0,0,255,255),new Color32(0,0,255,255),new Color32(0,0,255,255),
                            new Color32(255,0,0,255),new Color32(255,0,0,255),new Color32(255,0,0,255),new Color32(255,0,0,255) });
                image.filterMode = FilterMode.Point;
                image.Apply();
                using var processor = new ImagePreprocessor(definition.LetterboxShader);
                processor.Capture(image);
                AsyncGPUReadback.WaitAllRequests();
                byte[] pixels = processor.ReadPixels();
                Require(Mathf.Abs(pixels[(10 * 320 + 10) * 4] - 114) <= 1, "Gray padding");
                if (srgb)
                    Require(Mathf.Abs(pixels[(100 * 320 + 10) * 4] - 128) <= 1, "Restore camera sRGB values");
                else
                {
                    Require(pixels[(220 * 320 + 10) * 4] == 255 && pixels[(220 * 320 + 10) * 4 + 2] == 0, "Red at top of RGBA image");
                    Require(pixels[(100 * 320 + 10) * 4 + 2] == 255 && pixels[(100 * 320 + 10) * 4] == 0, "Blue at bottom of RGBA image");
                }
            }
            finally { Object.DestroyImmediate(image); }
        }

        private static void SaveReferencePixels(YOLOModel definition)
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
                using var processor = new ImagePreprocessor(definition.LetterboxShader);
                processor.Capture(texture);
                AsyncGPUReadback.WaitAllRequests();
                Directory.CreateDirectory("Builds");
                File.WriteAllBytes("Builds/pt-preprocessed.rgba", processor.ReadPixels());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void Require(bool condition, string check)
        {
            if (!condition) throw new Exception("GPU validation failed: " + check);
        }
    }
}
