using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace HoloCube.QuestYOLO
{
    /// <summary>Read YOLO26's final detections and remove weak or invalid results.</summary>
    public static class DetectionFilter
    {
        // Each row is [left, top, right, bottom, confidence, class ID].
        // The one-to-one model already selects objects: do not apply NMS again.
        public static List<Detection> ReadYOLO26(Tensor<float> output, ImageLetterbox image,
            int classCount, float minimumConfidence, int limit)
        {
            if (output.shape.rank != 3 || output.shape[0] != 1 || output.shape[2] != 6)
                throw new InvalidOperationException("Expected YOLO26 end-to-end output [1, detections, 6].");

            var results = new List<Detection>();
            if (limit <= 0) return results;
            for (int row = 0; row < output.shape[1]; row++)
            {
                float confidence = output[0, row, 4];
                float rawClass = output[0, row, 5];
                if (!Finite(confidence) || confidence < minimumConfidence || confidence > 1) continue;
                if (!Finite(rawClass) || rawClass < 0 || rawClass >= classCount || rawClass != (int)rawClass) continue;

                var box = new Vector4(output[0, row, 0], output[0, row, 1], output[0, row, 2], output[0, row, 3]);
                if (!Finite(box.x) || !Finite(box.y) || !Finite(box.z) || !Finite(box.w)) continue;
                box = image.ToOriginalImage(box);
                if (box.z <= box.x || box.w <= box.y) continue;
                results.Add(new Detection((int)rawClass, confidence, box));
            }

            results.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            if (results.Count > limit) results.RemoveRange(limit, results.Count - limit);
            return results;
        }

        private static bool Finite(float number) => !float.IsNaN(number) && !float.IsInfinity(number);
    }
}
