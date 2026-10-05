// Uses the bundled Ultralytics YOLO26n end-to-end ONNX export.
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace HoloCube.QuestYOLO
{
    /// <summary>Image in → run YOLO → filter results → detections out.</summary>
    public sealed class YOLOInference : IDisposable
    {
        public Vector2Int InputSize { get; }
        public Vector2Int ImageSize { get; private set; }
        public string[] Labels { get; }
        public bool IsBusy { get; private set; }

        private readonly Worker worker;
        private Tensor<float> input;
        private IEnumerator modelSchedule;
        private bool disposed;
        private readonly Shader letterboxShader;
        private Material letterboxMaterial;
        private RenderTexture paddedImage;
        private ImageLetterbox imageLayout;

        public YOLOInference(YOLOModel definition)
        {
            var model = definition.Load();
            letterboxShader = definition.LetterboxShader;
            var shape = model.inputs[0].shape;
            // NCHW means: batch size, color channels, image height, image width.
            InputSize = new Vector2Int(shape.Get(3), shape.Get(2));
            if (shape.Get(1) != 3 || InputSize.x <= 0 || InputSize.y <= 0)
                throw new InvalidOperationException("The model requires a fixed NCHW RGB input.");

            Labels = definition.LoadLabels();
            worker = new Worker(model, definition.Backend);
        }

        // MainApp advances this iterator once per display frame. Each yield gives
        // Unity time to render the headset view before we continue processing.
        public IEnumerator Detect(Texture image, float minimumConfidence,
            int layersPerFrame, int maxDetections, Action<List<Detection>> completed)
        {
            if (disposed) throw new ObjectDisposedException(nameof(YOLOInference));
            if (IsBusy) throw new InvalidOperationException("Only one image may be processed at a time.");
            if (image == null) throw new ArgumentNullException(nameof(image));
            IsBusy = true;

            try
            {
                // 1. Resize the image and turn its RGB pixels into model input numbers.
                PrepareImage(image);

                // 2. Run a few model layers per display frame.
                modelSchedule = worker.ScheduleIterable(input);
                int scheduledLayers = 0;
                while (modelSchedule.MoveNext())
                {
                    scheduledLayers++;
                    if (scheduledLayers % Mathf.Max(1, layersPerFrame) == 0) yield return null;
                }
                (modelSchedule as IDisposable)?.Dispose();
                modelSchedule = null;

                // 3. Read the final rows: [left, top, right, bottom, confidence, class].
                var output = worker.PeekOutput(0) as Tensor<float>;
                if (output == null) throw new InvalidOperationException("Expected a float YOLO26 output.");
                output.ReadbackRequest();
                while (!output.IsReadbackRequestDone()) yield return null;

                // 4. Keep confident results and map boxes back to the camera image.
                using var readableOutput = output.ReadbackAndClone();
                var results = DetectionFilter.ReadYOLO26(readableOutput, imageLayout,
                    Labels.Length, minimumConfidence, maxDetections);
                completed?.Invoke(results);
            }
            finally
            {
                ReleaseImage();
                IsBusy = false;
            }
        }

        private void PrepareImage(Texture image)
        {
            if (letterboxMaterial == null)
            {
                if (letterboxShader == null) throw new InvalidOperationException("Assign the letterbox shader on the model asset.");
                letterboxMaterial = new Material(letterboxShader);
                paddedImage = new RenderTexture(InputSize.x, InputSize.y, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                paddedImage.Create();
            }

            ImageSize = new Vector2Int(image.width, image.height);
            imageLayout = new ImageLetterbox(ImageSize, InputSize);
            letterboxMaterial.SetVector("_ImageRect", imageLayout.ShaderRectangle);
            // Meta marks the camera texture as sRGB in a linear-color project.
            // Undo GPU sampling's color conversion: YOLO expects the original RGB values.
            letterboxMaterial.SetFloat("_SourceIsSRGB", GraphicsFormatUtility.IsSRGBFormat(image.graphicsFormat) ? 1 : 0);
            var previousTarget = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false; // The target contains model numbers, not display colors.
                Graphics.Blit(image, paddedImage, letterboxMaterial);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgbWrite;
            }
            input = new Tensor<float>(new TensorShape(1, 3, InputSize.y, InputSize.x));
            var transform = new TextureTransform().SetDimensions(InputSize.x, InputSize.y, 3)
                .SetCoordOrigin(CoordOrigin.TopLeft);
            TextureConverter.ToTensor(paddedImage, input, transform);
        }

        private void ReleaseImage()
        {
            if (input == null) return;
            // A pause or scene close can interrupt inference. Finish queued model
            // work before releasing the image memory that the GPU may still need.
            try
            {
                if (modelSchedule != null)
                {
                    while (modelSchedule.MoveNext()) { }
                }
                worker.PeekOutput(0)?.CompleteAllPendingOperations();
                input.CompleteAllPendingOperations();
            }
            finally
            {
                (modelSchedule as IDisposable)?.Dispose();
                modelSchedule = null;
                input.Dispose();
                input = null;
            }
        }

        private static void DestroyResource(UnityEngine.Object resource)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                ReleaseImage();
            }
            finally
            {
                worker.Dispose();
                if (paddedImage != null)
                {
                    paddedImage.Release();
                    DestroyResource(paddedImage);
                }
                if (letterboxMaterial != null) DestroyResource(letterboxMaterial);
            }
        }
    }
}
