using HoloCube.QuestYOLO;
using Meta.XR;
using PassthroughCameraSamples.MultiObjectDetection;
using PassthroughCameraSamples.StartScene;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HoloCube.Editor
{
    // Editor-only setup: reuse Meta's XR rig and connect our three-part pipeline.
    public static class QuestYoloSceneSetup
    {
        public const string ScenePath = "Assets/HoloCube/QuestYOLO.unity";
        public const string ModelFolder = "Assets/HoloCube/YOLO Model/";
        public const string ModelPath = ModelFolder + "QuestYOLO.asset";
        private const string SampleScene = "Assets/PassthroughCameraApiSamples/MultiObjectDetection/MultiObjectDetection.unity";
        private static readonly Color DialogTextColor = new Color32(255, 203, 5, 255);
        private static readonly Color DialogBackgroundColor = new Color32(0, 39, 76, 255);

        public static void Create()
        {
            var scene = EditorSceneManager.OpenScene(SampleScene);
            var camera = Object.FindFirstObjectByType<PassthroughCameraAccess>();
            var overlay = CreateOverlay(camera);
            var app = new GameObject("Main App").AddComponent<MainApp>();
            app.CameraAccess = camera;
            app.Model = ConfigureModel();
            app.Overlay = overlay;
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            app.StatusLabel = CreateStatus(rig.centerEyeAnchor, out var statusIcon);
            app.StatusIcon = statusIcon;
            app.RecordingIndicator = CreateRecordingIndicator(rig.centerEyeAnchor);
            app.RecordingFeedbackLabel = CreateRecordingFeedback(rig.centerEyeAnchor, out var feedbackIcon);
            app.RecordingFeedbackIcon = feedbackIcon;
            new GameObject("Passthrough").AddComponent<OVRPassthroughLayer>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ConfigureAndroidBuild();
            AssetDatabase.SaveAssets();
            Debug.Log("HOLOCUBE_SCENE_READY: " + ScenePath);
        }

        private static DetectionOverlay CreateOverlay(PassthroughCameraAccess camera)
        {
            // Keep Meta's camera/depth setup and box template; replace its demo logic.
            var oldUi = Object.FindFirstObjectByType<SentisInferenceUiManager>();
            var template = (RectTransform)new SerializedObject(oldUi).FindProperty("m_detectionBoxPrefab").objectReferenceValue;
            var overlayObject = oldUi.gameObject;
            Object.DestroyImmediate(oldUi);
            RemoveSampleBehaviours();

            overlayObject.name = "AR Detection Overlay";
            var overlay = overlayObject.AddComponent<DetectionOverlay>();
            overlay.CameraAccess = camera;
            overlay.Depth = Object.FindFirstObjectByType<EnvironmentRaycastManager>();
            overlay.BoxTemplate = template;
            MakeLabelsReadable(template);
            return overlay;
        }

        private static void RemoveSampleBehaviours()
        {
            foreach (var component in Object.FindObjectsByType<SentisInferenceRunManager>(FindObjectsSortMode.None))
                Object.DestroyImmediate(component);
            foreach (var component in Object.FindObjectsByType<DetectionManager>(FindObjectsSortMode.None))
                Object.DestroyImmediate(component.gameObject);
            foreach (var component in Object.FindObjectsByType<DetectionUiMenuManager>(FindObjectsSortMode.None))
                Object.DestroyImmediate(component.gameObject);
            foreach (var component in Object.FindObjectsByType<ReturnToStartScene>(FindObjectsSortMode.None))
                Object.DestroyImmediate(component.gameObject);
            foreach (var component in Object.FindObjectsByType<EnvironmentRayCastSampleManager>(FindObjectsSortMode.None))
                Object.DestroyImmediate(component);
        }

        private static YOLOModel ConfigureModel()
        {
            var model = AssetDatabase.LoadAssetAtPath<YOLOModel>(ModelPath);
            if (model == null)
            {
                model = ScriptableObject.CreateInstance<YOLOModel>();
                AssetDatabase.CreateAsset(model, ModelPath);
            }
            model.Labels = AssetDatabase.LoadAssetAtPath<TextAsset>(ModelFolder + "coco.names.txt");
            model.CpuThreads = 2;
            model.LetterboxShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/HoloCube/YOLO Inference/Letterbox.shader");
            EditorUtility.SetDirty(model);
            return model;
        }

        private static void MakeLabelsReadable(RectTransform template)
        {
            template.gameObject.SetActive(false);
            var label = template.GetComponentInChildren<Text>(true);
            label.rectTransform.localScale = Vector3.one * 0.0007f;
            label.rectTransform.sizeDelta = new Vector2(700, 90);
            label.fontSize = 48;
            label.color = Color.white;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            PrefabUtility.RecordPrefabInstancePropertyModifications(label);
            PrefabUtility.RecordPrefabInstancePropertyModifications(label.rectTransform);
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2, -2);
        }

        private static void ConfigureAndroidBuild()
        {
            PlayerSettings.companyName = "HoloCube";
            PlayerSettings.productName = "HoloCube";
            PlayerSettings.bundleVersion = "0.4";
            PlayerSettings.Android.bundleVersionCode = 4;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.holocube.questyolo");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.Android.useCustomKeystore = false;
        }

        private static GameObject CreateRecordingIndicator(Transform head)
        {
            var canvasObject = new GameObject("Recording Indicator", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(head, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0.28f, 1.25f);
            canvasObject.transform.localScale = Vector3.one * 0.001f;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 120);
            var background = canvasObject.AddComponent<Image>();
            background.color = DialogBackgroundColor;
            background.raycastTarget = false;
            CreateDialogFrame(canvasObject.transform);

            var dotObject = new GameObject("Red Circle", typeof(RectTransform), typeof(RecordingDot));
            dotObject.transform.SetParent(canvasObject.transform, false);
            var dot = dotObject.GetComponent<RecordingDot>();
            dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            dot.rectTransform.sizeDelta = new Vector2(26, 26);
            dot.rectTransform.anchoredPosition = new Vector2(-115, 20);
            dot.color = new Color(1f, 0.12f, 0.12f, 1f);
            dot.raycastTarget = false;

            var labelObject = new GameObject("Recording Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(canvasObject.transform, false);
            var label = labelObject.GetComponent<Text>();
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(250, 55);
            label.rectTransform.anchoredPosition = new Vector2(45, 20);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 32;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = DialogTextColor;
            label.raycastTarget = false;
            label.text = "Recording";

            var destinationObject = new GameObject("Video Folder", typeof(RectTransform), typeof(Text));
            destinationObject.transform.SetParent(canvasObject.transform, false);
            var destination = destinationObject.GetComponent<Text>();
            destination.rectTransform.sizeDelta = new Vector2(390, 40);
            destination.rectTransform.anchoredPosition = new Vector2(0, -27);
            destination.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            destination.fontSize = 22;
            destination.alignment = TextAnchor.MiddleCenter;
            destination.color = DialogTextColor;
            destination.raycastTarget = false;
            destination.text = "Movies/HoloCube";

            canvasObject.SetActive(false);
            return canvasObject;
        }

        private static Text CreateRecordingFeedback(Transform head, out DialogIcon icon)
        {
            var canvasObject = new GameObject("Recording Feedback", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(head, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0.28f, 1.25f);
            canvasObject.transform.localScale = Vector3.one * 0.001f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 120);
            var background = canvasObject.AddComponent<Image>();
            background.color = DialogBackgroundColor;
            background.raycastTarget = false;
            CreateDialogFrame(canvasObject.transform);
            icon = CreateDialogIcon(canvasObject.transform, DialogIcon.Kind.Saving);
            var labelObject = new GameObject("Save Status", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(canvasObject.transform, false);
            var label = labelObject.GetComponent<Text>();
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(104, 10);
            label.rectTransform.offsetMax = new Vector2(-24, -10);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 24;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = DialogTextColor;
            label.raycastTarget = false;
            canvasObject.SetActive(false);
            return label;
        }

        private static Text CreateStatus(Transform head, out DialogIcon icon)
        {
            var canvasObject = new GameObject("Detection Status", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(head, false);
            canvasObject.transform.localPosition = new Vector3(0, -0.30f, 1.25f);
            canvasObject.transform.localScale = Vector3.one * 0.001f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1120, 340);
            var background = canvasObject.AddComponent<Image>();
            background.color = DialogBackgroundColor;
            background.raycastTarget = false;
            CreateDialogFrame(canvasObject.transform);
            icon = CreateDialogIcon(canvasObject.transform, DialogIcon.Kind.Cube);
            var textObject = new GameObject("Status", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(canvasObject.transform, false);
            var text = textObject.GetComponent<Text>();
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(104, 10);
            text.rectTransform.offsetMax = new Vector2(-24, -10);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = DialogTextColor;
            text.raycastTarget = false;
            text.text = "HoloCube · Loading object detection…";
            return text;
        }

        private static DialogIcon CreateDialogIcon(Transform panel, DialogIcon.Kind kind)
        {
            var iconObject = new GameObject("Dialog Icon", typeof(RectTransform), typeof(DialogIcon));
            iconObject.transform.SetParent(panel, false);
            var icon = iconObject.GetComponent<DialogIcon>();
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(54, 0);
            icon.rectTransform.sizeDelta = new Vector2(48, 48);
            icon.color = DialogTextColor;
            icon.raycastTarget = false;
            icon.SetIcon(kind);
            return icon;
        }

        private static void CreateDialogFrame(Transform panel)
        {
            CreateFrameEdge(panel, "Frame Top", new Vector2(0, 1), Vector2.one,
                new Vector2(-4, 2), new Vector2(0, -1));
            CreateFrameEdge(panel, "Frame Bottom", Vector2.zero, new Vector2(1, 0),
                new Vector2(-4, 2), new Vector2(0, 1));
            CreateFrameEdge(panel, "Frame Left", Vector2.zero, new Vector2(0, 1),
                new Vector2(2, 0), new Vector2(1, 0));
            CreateFrameEdge(panel, "Frame Right", new Vector2(1, 0), Vector2.one,
                new Vector2(2, 0), new Vector2(-1, 0));
        }

        private static void CreateFrameEdge(Transform panel, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 size, Vector2 position)
        {
            var edgeObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            edgeObject.transform.SetParent(panel, false);
            var edge = edgeObject.GetComponent<Image>();
            edge.rectTransform.anchorMin = anchorMin;
            edge.rectTransform.anchorMax = anchorMax;
            edge.rectTransform.sizeDelta = size;
            edge.rectTransform.anchoredPosition = position;
            edge.color = new Color(DialogTextColor.r, DialogTextColor.g, DialogTextColor.b, 0.6f);
            edge.raycastTarget = false;
        }

    }
}
