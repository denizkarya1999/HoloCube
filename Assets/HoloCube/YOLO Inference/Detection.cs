using UnityEngine;

namespace HoloCube.QuestYOLO
{
    /// <summary>One detected object: its class, confidence, and image rectangle.</summary>
    public readonly struct Detection
    {
        // Index into YOLOModel's labels, for example 0 means "person".
        public readonly int ClassId;
        // Confidence from 0 to 1, for example 0.9 means 90%.
        public readonly float Confidence;
        // Pixels in the original camera image, measured from its top-left corner:
        // x = left, y = top, z = right, w = bottom.
        public readonly Vector4 Box;

        public Detection(int classId, float confidence, Vector4 box)
        {
            ClassId = classId;
            Confidence = confidence;
            Box = box;
        }
    }
}
