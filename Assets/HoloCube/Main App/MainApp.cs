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
        private IEnumerator pendingDetection;
        private DateTime lastCameraTimestamp;
        private float nextCaptureTime;
        private bool userPaused;
        private bool applicationPaused;
        private bool hasFocus = true;
        private bool failed;

        private IEnumerator Start()
        {
            SetStatus("HoloCube · Loading object detection…");
            yield return null; // Let the loading message appear before loading the model.

            try
            {
                if (CameraAccess == null || Model == null || Overlay == null)
                    throw new InvalidOperationException("Assign the camera, model, and overlay in the scene.");

                inference = new YOLOInference(Model);
                RequestPermissions();
                SetStatus("Waiting for camera permission…\nB: retry permission request");
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void Update()
        {
            if (failed || inference == null) return;

            try
            {
                ReadButtons();
                if (!inference.IsReady)
                {
                    SetStatus("HoloCube · Loading .pt model…");
                    return;
                }
                if (!CameraIsReady()) return;

                // One image at a time; the Android worker runs PyTorch in the background.
                if (pendingDetection == null) CaptureImage();
                ContinueDetection();
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ReadButtons()
        {
            if (OVRInput.GetDown(OVRInput.Button.One)) userPaused = !userPaused;
            if (OVRInput.GetDown(OVRInput.Button.Two)) RequestPermissions();
        }

        private bool CameraIsReady()
        {
            string waitMessage = null;
            if (applicationPaused || !hasFocus || userPaused)
                waitMessage = "Detection paused\nA: resume";
            else if (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess))
                waitMessage = "Camera access is required\nAllow it in app permissions · B: retry";
            else if (!CameraAccess.IsPlaying)
                waitMessage = "Waiting for the Quest camera…";

            if (waitMessage == null) return true;

            CancelDetection();
            Overlay.Clear();
            SetStatus(waitMessage);
            return false;
        }

        private void CaptureImage()
        {
            if (Time.unscaledTime < nextCaptureTime) return;
            if (CameraAccess.Timestamp == lastCameraTimestamp) return;

            var image = CameraAccess.GetTexture();
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

            SetStatus($"HoloCube · {detections.Count} objects · {age * 1000:0} ms\nA: pause · ~ means approximate depth");
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
            if (StatusLabel != null) StatusLabel.text = message;
        }

        private void ShowError(Exception error)
        {
            failed = true;
            Debug.LogException(error, this);
            try
            {
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

        private void OnApplicationPause(bool paused) => applicationPaused = paused;
        private void OnApplicationFocus(bool focused) => hasFocus = focused;

        private void OnDisable()
        {
            CancelDetection();
            if (Overlay != null) Overlay.Clear();
        }

        private void OnDestroy()
        {
            try
            {
                CancelDetection();
            }
            finally
            {
                inference?.Dispose();
                inference = null;
            }
        }
    }
}
