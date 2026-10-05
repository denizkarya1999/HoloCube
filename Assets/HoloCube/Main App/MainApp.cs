using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR;
using UnityEngine;
using UnityEngine.UI;

namespace HoloCube.QuestYOLO
{
    /// <summary>Start here: camera image → YOLO detections → boxes in the headset.</summary>
    public sealed class MainApp : MonoBehaviour
    {
        private enum MenuPage
        {
            Home,
            About,
            Settings
        }

        private const string ControllerMenu =
            "Press A: YOLO Inference · Hold B: YOLO Data Collection\n" +
            "Press X: About HoloCube · Press Y: Settings";

        [Header("Scene connections")]
        public PassthroughCameraAccess CameraAccess;
        public YOLOModel Model;
        public DetectionOverlay Overlay;
        public Text StatusLabel;

        [Header("Which detections to show")]
        [Tooltip("Minimum confidence: 0.35 means 35%.")]
        [Range(0, 1)] public float Confidence = 0.35f;
        [Min(1)] public int MaxDetections = 24;

        [Header("Headset performance")]
        [Tooltip("Seconds to wait after a detection finishes before capturing again.")]
        [Min(0)] public float DetectionInterval = 0.1f;
        [Tooltip("Hide results older than this many seconds.")]
        [Min(0.1f)] public float MaxResultAge = 0.75f;

        private YOLOInference inference;
        private CameraVideoCapture videoCapture;
        private IEnumerator pendingDetection;
        private DateTime lastCameraTimestamp;
        private float nextCaptureTime;
        private bool userPaused;
        private bool applicationPaused;
        private bool hasFocus = true;
        private bool failed;
        private bool bButtonHeld;
        private bool recordingAttemptedForCurrentHold;
        private MenuPage menuPage;
        private int selectedSettingIndex;
        private float nextSettingsJoystickActionTime;
        private string statusMessage = string.Empty;

        private const float SettingsStickDeadzone = 0.6f;
        private const float SettingsJoystickRepeatInterval = 0.22f;

        private IEnumerator Start()
        {
            videoCapture = new CameraVideoCapture();
            SetStatus("HoloCube · Loading object detection…");
            yield return null; // Let the loading message appear before loading the model.

            try
            {
                if (CameraAccess == null || Model == null || Overlay == null)
                    throw new InvalidOperationException("Assign the camera, model, and overlay in the scene.");

                inference = new YOLOInference(Model);
                RequestPermissions();
                SetStatus("Waiting for camera permission…\nAllow camera access to begin");
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void Update()
        {
            if (failed) return;

            try
            {
                ReadButtons();
                videoCapture?.Poll();
                RefreshStatus();

                if (!CameraIsReady()) return;

                var cameraImage = CameraAccess.GetTexture();
                if (cameraImage == null)
                {
                    StopRecording();
                    CancelDetection();
                    Overlay.Clear();
                    SetStatus("Waiting for the Quest camera…");
                    return;
                }

                UpdateVideoRecording(cameraImage);

                if (inference == null || !inference.IsReady)
                {
                    SetStatus("HoloCube · Loading model…");
                    return;
                }

                if (userPaused)
                {
                    CancelDetection();
                    Overlay.Clear();
                    SetStatus("YOLO inference paused");
                    return;
                }

                // One image at a time; the Android worker runs PyTorch in the background.
                if (pendingDetection == null) CaptureImage(cameraImage);
                ContinueDetection();
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ReadButtons()
        {
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
                userPaused = !userPaused;

            bButtonHeld = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);

            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch))
                menuPage = menuPage == MenuPage.About ? MenuPage.Home : MenuPage.About;

            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
                menuPage = menuPage == MenuPage.Settings ? MenuPage.Home : MenuPage.Settings;

            UpdateSettingsWithJoystick();
        }

        private void UpdateSettingsWithJoystick()
        {
            if (menuPage != MenuPage.Settings || Time.unscaledTime < nextSettingsJoystickActionTime)
                return;

            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            if (Mathf.Abs(stick.y) >= SettingsStickDeadzone)
            {
                selectedSettingIndex = stick.y > 0f ? 0 : 1;
                nextSettingsJoystickActionTime = Time.unscaledTime + SettingsJoystickRepeatInterval;
                return;
            }

            if (Mathf.Abs(stick.x) < SettingsStickDeadzone)
                return;

            int direction = stick.x > 0f ? 1 : -1;
            if (selectedSettingIndex == 0)
            {
                Confidence = Mathf.Clamp(
                    Mathf.Round((Confidence + direction * 0.05f) * 100f) / 100f,
                    0.05f,
                    1f);
            }
            else
            {
                MaxDetections = Mathf.Clamp(MaxDetections + direction, 1, 80);
            }

            nextSettingsJoystickActionTime = Time.unscaledTime + SettingsJoystickRepeatInterval;
        }

        private bool CameraIsReady()
        {
            string waitMessage = null;
            if (applicationPaused || !hasFocus)
                waitMessage = "Camera paused";
            else if (CameraAccess == null)
                waitMessage = "Camera is not configured";
            else if (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess))
                waitMessage = "Camera access is required\nAllow it in app permissions";
            else if (!CameraAccess.IsPlaying)
                waitMessage = "Waiting for the Quest camera…";

            if (waitMessage == null) return true;

            StopRecording();
            CancelDetection();
            if (Overlay != null) Overlay.Clear();
            SetStatus(waitMessage);
            return false;
        }

        private void UpdateVideoRecording(Texture cameraImage)
        {
            if (!bButtonHeld)
            {
                recordingAttemptedForCurrentHold = false;
                StopRecording();
                return;
            }

            if (videoCapture == null) return;

            if (!recordingAttemptedForCurrentHold)
            {
                recordingAttemptedForCurrentHold = true;
                videoCapture.TryStart(cameraImage);
            }

            if (videoCapture.IsRecording)
                videoCapture.CaptureFrame(cameraImage, CameraAccess.Timestamp);
        }

        private void CaptureImage(Texture image)
        {
            if (Time.unscaledTime < nextCaptureTime) return;
            if (CameraAccess.Timestamp == lastCameraTimestamp) return;

            if (image == null) return;

            lastCameraTimestamp = CameraAccess.Timestamp;
            var cameraPose = CameraAccess.GetCameraPose();
            float captureTime = Time.unscaledTime;
            pendingDetection = inference.Detect(
                image, Confidence, MaxDetections,
                detections => ShowDetections(detections, cameraPose, captureTime));
        }

        private void ContinueDetection()
        {
            if (pendingDetection == null) return;

            // The first step copies the image now, before the camera changes it.
            // Later steps poll the background PyTorch worker without blocking rendering.
            if (pendingDetection.MoveNext()) return;

            CancelDetection();
            nextCaptureTime = Time.unscaledTime + DetectionInterval;
        }

        private void ShowDetections(List<Detection> detections, Pose cameraPose, float captureTime)
        {
            float age = Time.unscaledTime - captureTime;
            if (CameraAccess.IsPlaying && !userPaused && age <= MaxResultAge)
                Overlay.Draw(detections, inference.ImageSize, inference.Labels, cameraPose);
            else
                Overlay.Clear();

            SetStatus($"HoloCube · {detections.Count} objects · {age * 1000:0} ms · ~ means approximate depth");
        }

        private void RequestPermissions()
        {
            OVRPermissionsRequester.Request(new[]
            {
                OVRPermissionsRequester.Permission.PassthroughCameraAccess,
                OVRPermissionsRequester.Permission.Scene
            });
        }

        private void SetStatus(string message)
        {
            statusMessage = message;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (StatusLabel == null) return;
            string pageMessage = GetMenuPageMessage();
            string videoStatus = videoCapture != null ? videoCapture.StatusHint : string.Empty;
            StatusLabel.text = string.IsNullOrEmpty(videoStatus)
                ? pageMessage + "\n" + ControllerMenu
                : pageMessage + "\n" + ControllerMenu + "\n" + videoStatus;
        }

        private string GetMenuPageMessage()
        {
            switch (menuPage)
            {
                case MenuPage.About:
                    return "About HoloCube\n" +
                        "Name: HoloCube Research Project\n" +
                        "Version: 1.0\n" +
                        "Developers: Deniz K. Acikbas and Ahmad Jayeb\n" +
                        "Advisor: Xiao Zhang\n" +
                        "Institution: University of Michigan-Dearborn";
                case MenuPage.Settings:
                    return $"Settings\n" +
                        $"{(selectedSettingIndex == 0 ? ">" : " ")} Confidence threshold: {Confidence:0.00}\n" +
                        $"{(selectedSettingIndex == 1 ? ">" : " ")} Maximum number of boxes: {MaxDetections}\n" +
                        "Joystick up/down: select · left/right: adjust";
                default:
                    return string.IsNullOrEmpty(statusMessage) ? "HoloCube" : statusMessage;
            }
        }

        private void StopRecording()
        {
            videoCapture?.Stop();
        }

        private void ShowError(Exception error)
        {
            failed = true;
            Debug.LogException(error, this);
            try
            {
                StopRecording();
                videoCapture?.StopAndWait();
                CancelDetection();
            }
            catch (Exception cleanupError)
            {
                Debug.LogException(cleanupError, this);
            }
            if (Overlay != null) Overlay.Clear();
            SetStatus("Detection stopped\n" + error.Message);
        }

        private void CancelDetection()
        {
            var pending = pendingDetection;
            pendingDetection = null;
            // Disposing the iterator discards its result; native work can finish safely.
            (pending as IDisposable)?.Dispose();
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused) StopRecording();
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            if (!focused) StopRecording();
        }

        private void OnDisable()
        {
            StopRecording();
            videoCapture?.StopAndWait();
            CancelDetection();
            if (Overlay != null) Overlay.Clear();
        }

        private void OnDestroy()
        {
            try
            {
                videoCapture?.Dispose();
            }
            finally
            {
                videoCapture = null;
                inference?.Dispose();
                inference = null;
            }
        }
    }
}
