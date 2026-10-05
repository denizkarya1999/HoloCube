using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HoloCube.QuestYOLO
{
    /// <summary>Camera image → background PyTorch worker → labelled detections.</summary>
    public sealed class YOLOInference : IDisposable
    {
        public Vector2Int ImageSize => images.ImageSize;
        public string[] Labels { get; }
        public bool IsReady => !disposed && worker.Call<bool>("isReady");
        public bool IsBusy { get; private set; }
        private readonly ImagePreprocessor images;
        private readonly AndroidJavaObject worker;
        private bool disposed;

        public YOLOInference(YOLOModel definition)
        {
            Labels = definition.LoadLabels();
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            worker = new AndroidJavaObject("com.holocube.pytorch.PyTorchWorker", activity, definition.CpuThreads);
            try { images = new ImagePreprocessor(definition.LetterboxShader); }
            catch { worker.Call("close"); worker.Dispose(); throw; }
#else
            throw new PlatformNotSupportedException("The embedded .pt runtime runs on Android. Use Tools/validate-pt.py for desktop model checks.");
#endif
        }

        // MainApp advances this iterator every display frame. Python inference
        // runs on a separate Android thread while Unity keeps rendering.
        public IEnumerator Detect(Texture image, float confidence, int limit, Action<List<Detection>> completed)
        {
            if (disposed) throw new ObjectDisposedException(nameof(YOLOInference));
            if (IsBusy) throw new InvalidOperationException("Only one image may be processed at a time.");
            if (image == null) throw new ArgumentNullException(nameof(image));
            IsBusy = true;
            try
            {
                // Copy this frame immediately, preserving its associated camera pose.
                images.Capture(image);
                while (!images.IsReady) yield return null;
                byte[] pixels = images.ReadPixels(); // Readback data is only valid for one frame.
                while (!worker.Call<bool>("isIdle")) yield return null;
                worker.Call("submit", pixels);
                while (!worker.Call<bool>("isDone")) yield return null;
                var rows = worker.Call<float[]>("take");
                completed?.Invoke(DetectionFilter.ReadYOLO26(rows, images.Layout, Labels.Length, confidence, limit));
            }
            finally
            {
                // A pause hides the result, but does not interrupt native PyTorch.
                // The worker finishes safely before it accepts another frame.
                if (!disposed) worker.Call("discard");
                IsBusy = false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { worker.Call("close"); }
            finally { worker.Dispose(); images.Dispose(); }
        }
    }
}
