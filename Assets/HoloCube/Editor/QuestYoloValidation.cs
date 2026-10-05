using System;
using System.IO;
using HoloCube.QuestYOLO;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HoloCube.Editor
{
    // Editor checks live here so they do not obscure the runtime detection flow.
    public static class QuestYoloValidation
    {
        public static void Run()
        {
            CheckSavedScene();
            CheckCoordinatesAndDecoder();
            CheckRealModel();
            Debug.Log("HOLOCUBE_VALIDATION_PASSED: YOLO26n scene, letterbox/decoder checks, original .pt inference matches reference.");
        }

        private static void CheckSavedScene()
        {
            if (!File.Exists(QuestYoloSceneSetup.ScenePath)) throw new Exception("Generate the standalone scene first.");
            EditorSceneManager.OpenScene(QuestYoloSceneSetup.ScenePath);
            var app = Object.FindFirstObjectByType<MainApp>();
            Require(app != null && app.CameraAccess != null && app.Model != null &&
                app.Overlay != null && app.StatusLabel != null, "Scene wiring");
            var label = app.Overlay.BoxTemplate.GetComponentInChildren<Text>(true);
            Require(label.fontSize == 48 && label.color == Color.white, "Readable labels");
            Require(Object.FindFirstObjectByType<SentisInferenceRunManager>() == null, "Single inference pipeline");
            Require(app.Model.CpuThreads >= 1 && app.Model.CpuThreads <= 4, "Background CPU worker threads");
            Require(app.Model.LetterboxShader != null, "Bundled letterbox shader");
            Require(File.Exists(QuestYoloSceneSetup.ModelFolder + YOLOModel.CheckpointFile), "Original .pt checkpoint");
            Require(AssetDatabase.GetAssetPath(app.Model.Labels) == QuestYoloSceneSetup.ModelFolder + "coco.names.txt", "YOLO26 class names");
        }

        private static void CheckCoordinatesAndDecoder()
        {
            var landscape = new ImageLetterbox(new Vector2Int(1280, 720), new Vector2Int(320, 320));
            Require(landscape.ResizedSize == new Vector2Int(320, 180) && landscape.Padding == new Vector2Int(0, 70), "Landscape padding");
            Require(landscape.ToOriginalImage(new Vector4(0, 70, 320, 250)) == new Vector4(0, 0, 1280, 720), "Undo landscape padding");
            var portrait = new ImageLetterbox(new Vector2Int(810, 1080), new Vector2Int(320, 320));
            Require(portrait.Padding == new Vector2Int(40, 0), "Portrait padding");
            var odd = new ImageLetterbox(new Vector2Int(320, 181), new Vector2Int(320, 320));
            Require(odd.Padding.y == 69 && Mathf.Approximately(odd.ShaderRectangle.y, 70f / 320), "Asymmetric top/bottom padding");

            var sampleRows = new float[]
            {
                0, 70, 320, 250, 0.9f, 0,  // Full image.
                0, 70, 320, 250, 0.8f, 0,  // End-to-end outputs must not undergo NMS again.
                0, 70, 320, 250, 0.1f, 1,  // Too weak.
                0, 70, 320, 250, 0.9f, 80, // Invalid class.
                0, 70, 320, 250, float.NaN, 0,
                10, 80, 0, 70, 0.9f, 0    // Invalid rectangle.
            };
            var output = new float[300 * 6];
            Array.Copy(sampleRows, output, sampleRows.Length);
            var results = DetectionFilter.ReadYOLO26(output, landscape, 80, 0.35f, 24);
            Require(results.Count == 2 && results[0].Box == new Vector4(0, 0, 1280, 720), "Confidence/classes/coordinates; no second NMS");
            Require(DetectionFilter.ReadYOLO26(output, landscape, 80, 0.35f, 1).Count == 1, "Detection limit");
            Require(DetectionFilter.ReadYOLO26(output, landscape, 80, 1, 24).Count == 0, "No confident results");
            var wrongShape = new float[84 * 2100];
            bool rejected = false;
            try { DetectionFilter.ReadYOLO26(wrongShape, landscape, 80, 0.35f, 24); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Reject non-end-to-end model output");
        }

        private static void CheckRealModel()
        {
            var definition = AssetDatabase.LoadAssetAtPath<YOLOModel>(QuestYoloSceneSetup.ModelPath);
            Require(definition.LoadLabels().Length == 80, "80 COCO classes");
            Require(File.Exists(PyTorchAndroidBuild.PythonPath), "Run bash Tools/setup-python.sh first");
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = PyTorchAndroidBuild.PythonPath,
                Arguments = "Tools/validate-pt.py",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            process.Start();
            if (!process.WaitForExit(120000))
            {
                process.Kill();
                throw new Exception("Direct .pt model validation timed out.");
            }
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            Require(process.ExitCode == 0 && output.Contains("HOLOCUBE_PT_PARITY_PASSED"), "Real .pt inference: " + output + errors);
            Debug.Log(output.Trim());
        }

        private static void Require(bool condition, string check)
        {
            if (!condition) throw new Exception("Validation failed: " + check);
        }
    }
}
