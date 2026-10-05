using System;
using System.IO;
using HoloCube.QuestYOLO;
using PassthroughCameraSamples.MultiObjectDetection;
using Unity.InferenceEngine;
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
            Debug.Log("HOLOCUBE_VALIDATION_PASSED: YOLO26n scene, letterbox/decoder checks, real Unity inference matches PyTorch.");
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
            Require(app.Model.Backend == BackendType.GPUCompute, "On-device GPU backend");
            Require(app.Model.LetterboxShader != null, "Bundled letterbox shader");
            Require(AssetDatabase.GetAssetPath(app.Model.Weights) == QuestYoloSceneSetup.ModelFolder + "yolo26n.onnx", "YOLO26 model reference");
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

            using var output = new Tensor<float>(new TensorShape(1, 6, 6), new float[]
            {
                0, 70, 320, 250, 0.9f, 0,  // Full image.
                0, 70, 320, 250, 0.8f, 0,  // End-to-end outputs must not undergo NMS again.
                0, 70, 320, 250, 0.1f, 1,  // Too weak.
                0, 70, 320, 250, 0.9f, 80, // Invalid class.
                0, 70, 320, 250, float.NaN, 0,
                10, 80, 0, 70, 0.9f, 0    // Invalid rectangle.
            });
            var results = DetectionFilter.ReadYOLO26(output, landscape, 80, 0.35f, 24);
            Require(results.Count == 2 && results[0].Box == new Vector4(0, 0, 1280, 720), "Confidence/classes/coordinates; no second NMS");
            Require(DetectionFilter.ReadYOLO26(output, landscape, 80, 0.35f, 1).Count == 1, "Detection limit");
            Require(DetectionFilter.ReadYOLO26(output, landscape, 80, 1, 24).Count == 0, "No confident results");
            using var wrongShape = new Tensor<float>(new TensorShape(1, 84, 2100));
            bool rejected = false;
            try { DetectionFilter.ReadYOLO26(wrongShape, landscape, 80, 0.35f, 24); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Reject non-end-to-end model output");
        }

        private static void CheckRealModel()
        {
            var definition = AssetDatabase.LoadAssetAtPath<YOLOModel>(QuestYoloSceneSetup.ModelPath);
            var model = definition.Load();
            Require(model.inputs[0].shape.Get(0) == 1 && model.inputs[0].shape.Get(1) == 3 &&
                model.inputs[0].shape.Get(2) == 320 && model.inputs[0].shape.Get(3) == 320, "YOLO26 input shape");
            Require(definition.LoadLabels().Length == 80, "80 COCO classes");
            var fixture = JsonUtility.FromJson<ReferenceImage>(File.ReadAllText("Tools/Fixtures/YOLO26/expected.json"));
            byte[] bytes = File.ReadAllBytes("Tools/Fixtures/YOLO26/input.f32");
            Require(bytes.Length == 3 * 320 * 320 * sizeof(float), "Reference input size");
            var pixels = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, pixels, 0, bytes.Length);
            using var input = new Tensor<float>(new TensorShape(1, 3, 320, 320), pixels);
            using var worker = new Worker(model, BackendType.CPU);
            worker.Schedule(input);
            using var output = (worker.PeekOutput(0) as Tensor<float>).ReadbackAndClone();
            Require(output.shape.Equals(new TensorShape(1, 300, 6)), "YOLO26 output shape");
            var image = new ImageLetterbox(new Vector2Int(fixture.width, fixture.height), new Vector2Int(320, 320));
            var results = DetectionFilter.ReadYOLO26(output, image, 80, 0.35f, 24);
            Require(results.Count == fixture.detections.Length && results.Count > 0, "Reference detection count");
            for (int i = 0; i < results.Count; i++)
            {
                var expected = fixture.detections[i];
                var expectedBox = image.ToOriginalImage(new Vector4(expected.box[0], expected.box[1], expected.box[2], expected.box[3]));
                Require(results[i].ClassId == expected.classId, "Reference class " + i);
                Require(Mathf.Abs(results[i].Confidence - expected.confidence) < 0.001f, "Reference confidence " + i);
                Require((results[i].Box - expectedBox).magnitude < 1, "Reference box " + i);
            }
            Debug.Log($"HOLOCUBE_MODEL_PARITY_PASSED: {results.Count} detections match PyTorch within 0.001 confidence and 1 pixel.");
        }

        [Serializable] private sealed class ReferenceImage
        {
            public int width;
            public int height;
            public ReferenceDetection[] detections;
        }
        [Serializable] private sealed class ReferenceDetection
        {
            public int classId;
            public float confidence;
            public float[] box;
        }

        private static void Require(bool condition, string check)
        {
            if (!condition) throw new Exception("Validation failed: " + check);
        }
    }
}
