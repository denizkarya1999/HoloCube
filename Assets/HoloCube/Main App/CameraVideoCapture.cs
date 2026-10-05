using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace HoloCube.QuestYOLO
{
    /// <summary>Records the Quest camera texture to a local MP4 while the user holds B.</summary>
    internal sealed class CameraVideoCapture : IDisposable
    {
        private const int MaximumDimension = 640;
        private const int FrameRate = 20;
        private const int BitRate = 1500000;
        private const float MinimumFrameInterval = 1f / FrameRate;
        private const string DefaultHint = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject nativeRecorder;
#endif
        private RenderTexture captureTarget;
        private Texture2D cpuReadbackTexture;
        private readonly object completedFrameLock = new object();
        private byte[] completedFrame;
        private string readbackError;
        private volatile bool readbackPending;
        private volatile bool isRecording;
        private bool disposed;
        private bool hasLastTimestamp;
        private DateTime lastTimestamp;
        private float nextFrameTime;
        private string statusHint = DefaultHint;
        private bool captureInterrupted;

        public bool IsRecording => isRecording;
        public bool CanRetryStart { get; private set; }
        public string StatusHint => statusHint;

        public bool TryStart(Texture source)
        {
            CanRetryStart = false;
            if (disposed || source == null)
            {
                statusHint = "Video recording is unavailable";
                return false;
            }

            try
            {
                // A previous GPU request must finish before its render texture can be reused.
                if (readbackPending)
                {
                    CanRetryStart = true;
                    return false;
                }
#if UNITY_ANDROID && !UNITY_EDITOR
                if (nativeRecorder != null)
                {
                    string previousStatus = nativeRecorder.Call<string>("getStatus");
                    if (previousStatus == "RECORDING" || previousStatus == "SAVING")
                    {
                        CanRetryStart = true;
                        statusHint = "Saving the previous video…";
                        return false;
                    }
                    DisposeNativeRecorder();
                }
#endif
                lock (completedFrameLock)
                {
                    completedFrame = null;
                    readbackError = null;
                }
                captureInterrupted = false;
                ConfigureReadback(source.width, source.height);
#if UNITY_ANDROID && !UNITY_EDITOR
                nativeRecorder = new AndroidJavaObject("com.holocube.capture.QuestCameraVideoRecorder");
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    nativeRecorder.Call<string>("startRecording", activity, captureTarget.width, captureTarget.height, FrameRate, BitRate);
                }
                isRecording = true;
                hasLastTimestamp = false;
                nextFrameTime = 0f;
                statusHint = "RECORDING · release B to stop";
                return true;
#else
                statusHint = "Video recording is available on Quest";
                return false;
#endif
            }
            catch (Exception error)
            {
                isRecording = false;
                statusHint = "Video recording failed";
                Debug.LogWarning("HoloCube video recording could not start: " + error.Message);
#if UNITY_ANDROID && !UNITY_EDITOR
                DisposeNativeRecorder();
#endif
                return false;
            }
        }

        public void CaptureFrame(Texture source, DateTime timestamp)
        {
            if (!isRecording || disposed || source == null || captureTarget == null) return;
            if (hasLastTimestamp && timestamp == lastTimestamp) return;

            lastTimestamp = timestamp;
            hasLastTimestamp = true;
            if (Time.unscaledTime < nextFrameTime || readbackPending) return;
            nextFrameTime = Time.unscaledTime + MinimumFrameInterval;

            Graphics.Blit(source, captureTarget);
            readbackPending = true;

            if (SystemInfo.supportsAsyncGPUReadback)
            {
                try
                {
                    AsyncGPUReadback.Request(captureTarget, 0, TextureFormat.RGBA32, OnReadbackCompleted);
                }
                catch (Exception error)
                {
                    readbackPending = false;
                    SetReadbackError(error.Message);
                }
            }
            else
            {
                ReadBackSynchronously();
            }
        }

        public void Poll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            byte[] frame = null;
            string error = null;
            lock (completedFrameLock)
            {
                frame = completedFrame;
                completedFrame = null;
                error = readbackError;
                readbackError = null;
            }

            if (!string.IsNullOrEmpty(error))
            {
                if (isRecording)
                {
                    captureInterrupted = true;
                    Debug.LogWarning("HoloCube video readback failed: " + error);
                    Stop();
                }
            }
            else if (frame != null && isRecording && nativeRecorder != null)
            {
                try
                {
                    nativeRecorder.Call("enqueueFrame", frame);
                }
                catch (Exception enqueueError)
                {
                    captureInterrupted = true;
                    Debug.LogWarning("HoloCube video frame could not be queued: " + enqueueError.Message);
                    Stop();
                }
            }

            if (nativeRecorder != null)
            {
                try
                {
                    UpdateStatusHint(nativeRecorder.Call<string>("getStatus"));
                }
                catch (Exception errorReadingStatus)
                {
                    statusHint = "Video recording stopped";
                    Debug.LogWarning("HoloCube video status could not be read: " + errorReadingStatus.Message);
                    isRecording = false;
                }
            }
#endif
        }

        public void Stop()
        {
            if (!isRecording) return;
            isRecording = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // ReadButtons can stop a recording before Poll forwards its final completed frame.
                byte[] finalFrame;
                lock (completedFrameLock)
                {
                    finalFrame = completedFrame;
                    completedFrame = null;
                }
                if (finalFrame != null)
                    nativeRecorder?.Call("enqueueFrame", finalFrame);
            }
            catch (Exception error)
            {
                captureInterrupted = true;
                Debug.LogWarning("HoloCube final video frame could not be queued: " + error.Message);
            }
            try
            {
                nativeRecorder?.Call("stopRecording");
                statusHint = captureInterrupted ? "Recording interrupted · saving video…" : "Saving video…";
            }
            catch (Exception error)
            {
                statusHint = "Video recording stopped";
                Debug.LogWarning("HoloCube video recording could not stop cleanly: " + error.Message);
            }
#endif
        }

        public void StopAndWait()
        {
            Stop();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                nativeRecorder?.Call("stopRecordingAndWait", 4500);
                if (nativeRecorder != null)
                    UpdateStatusHint(nativeRecorder.Call<string>("getStatus"));
            }
            catch (Exception error)
            {
                Debug.LogWarning("HoloCube video recording finalization timed out: " + error.Message);
            }
#endif
        }

        public void Dispose()
        {
            if (disposed) return;
            StopAndWait();
            disposed = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            DisposeNativeRecorder();
#endif
            if (!readbackPending) ReleaseReadbackResources();
        }

        private void OnReadbackCompleted(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;
            if (disposed)
            {
                ReleaseReadbackResources();
                return;
            }
            if (!isRecording) return;

            if (request.hasError)
            {
                SetReadbackError("GPU readback failed");
                return;
            }

            try
            {
                byte[] frame = request.GetData<byte>().ToArray();
                lock (completedFrameLock)
                {
                    if (isRecording) completedFrame = frame;
                }
            }
            catch (Exception error)
            {
                SetReadbackError(error.Message);
            }
        }

        private void ReadBackSynchronously()
        {
            var previousTarget = RenderTexture.active;
            try
            {
                RenderTexture.active = captureTarget;
                cpuReadbackTexture.ReadPixels(new Rect(0, 0, captureTarget.width, captureTarget.height), 0, 0, false);
                byte[] frame = cpuReadbackTexture.GetRawTextureData<byte>().ToArray();
                lock (completedFrameLock)
                {
                    if (isRecording) completedFrame = frame;
                }
            }
            catch (Exception error)
            {
                SetReadbackError(error.Message);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                readbackPending = false;
            }
        }

        private void SetReadbackError(string error)
        {
            lock (completedFrameLock)
            {
                readbackError = error;
            }
        }

        private void ConfigureReadback(int sourceWidth, int sourceHeight)
        {
            if (sourceWidth < 2 || sourceHeight < 2)
                throw new InvalidOperationException("The camera image has no usable dimensions.");

            float scale = Mathf.Min(1f, (float)MaximumDimension / Mathf.Max(sourceWidth, sourceHeight));
            int width = Mathf.Max(2, Mathf.RoundToInt(sourceWidth * scale / 2f) * 2);
            int height = Mathf.Max(2, Mathf.RoundToInt(sourceHeight * scale / 2f) * 2);

            if (captureTarget != null && captureTarget.width == width && captureTarget.height == height)
                return;

            ReleaseReadbackResources();
            captureTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            captureTarget.Create();
            cpuReadbackTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        }

        private void UpdateStatusHint(string nativeStatus)
        {
            if (string.IsNullOrEmpty(nativeStatus)) return;

            if (nativeStatus == "RECORDING")
            {
                statusHint = isRecording ? "RECORDING · release B to stop" : "Saving video…";
            }
            else if (nativeStatus == "SAVING")
            {
                statusHint = captureInterrupted ? "Recording interrupted · saving video…" : "Saving video…";
                isRecording = false;
            }
            else if (nativeStatus.StartsWith("SAVED:", StringComparison.Ordinal))
            {
                statusHint = (captureInterrupted ? "Recording interrupted\n" : "") +
                    "Saved to Movies/HoloCube/" + nativeStatus.Substring("SAVED:".Length);
                isRecording = false;
            }
            else if (nativeStatus.StartsWith("ERROR:", StringComparison.Ordinal))
            {
                statusHint = nativeStatus == "ERROR:No camera frames were captured."
                    ? "No video saved · hold B a little longer"
                    : "Video recording failed · no video saved";
                isRecording = false;
            }
            else if (!isRecording && nativeStatus == "IDLE")
            {
                statusHint = DefaultHint;
            }
        }

        private void ReleaseReadbackResources()
        {
            if (captureTarget != null)
            {
                captureTarget.Release();
                UnityEngine.Object.Destroy(captureTarget);
                captureTarget = null;
            }

            if (cpuReadbackTexture != null)
            {
                UnityEngine.Object.Destroy(cpuReadbackTexture);
                cpuReadbackTexture = null;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void DisposeNativeRecorder()
        {
            if (nativeRecorder == null) return;
            try
            {
                nativeRecorder.Call("dispose");
            }
            catch (Exception error)
            {
                Debug.LogWarning("HoloCube video recorder cleanup failed: " + error.Message);
            }
            nativeRecorder.Dispose();
            nativeRecorder = null;
        }
#endif
    }
}
