using System;
using UnityEngine;

namespace HoloCube.QuestYOLO
{
    /// <summary>The original checkpoint, object names, and CPU settings.</summary>
    [CreateAssetMenu(menuName = "HoloCube/YOLO Model")]
    public sealed class YOLOModel : ScriptableObject
    {
        public const string CheckpointFile = "yolo26n.pt";
        public const string CheckpointHash = "9b09cc8bf347f0fc8a5f7657480587f25db09b34bf33b0652110fb03a8ad4fef";
        public const int InputSize = 320;

        [Tooltip("One object name per line, in class-ID order.")]
        public TextAsset Labels;
        [Tooltip("Background CPU threads used by PyTorch on the headset.")]
        [Range(1, 4)] public int CpuThreads = 2;
        [Tooltip("Fits the camera image into the model input with gray padding.")]
        public Shader LetterboxShader;

        public string[] LoadLabels()
        {
            if (Labels == null) throw new InvalidOperationException("Assign the class names file.");
            var names = Labels.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();
            return names;
        }
    }
}
