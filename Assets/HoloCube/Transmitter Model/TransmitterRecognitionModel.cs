using System;
using UnityEngine;

namespace HoloCube.QuestYOLO
{
    /// <summary>The trained transmitter checkpoint, label, and CPU settings.</summary>
    [CreateAssetMenu(menuName = "HoloCube/Transmitter Recognition Model")]
    public sealed class TransmitterRecognitionModel : ScriptableObject
    {
        public const string CheckpointFile = "holocube_transmitter.pt";
        public const string CheckpointHash = "8c6e981c7a49ff52c7113cf9cd95763ae6495e3baab3e3d68c17a6919e3d8fef";
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
            if (names.Length != 1 || names[0] != "Transmitter")
                throw new InvalidOperationException("The Transmitter Recognition Model requires class 0: Transmitter.");
            return names;
        }
    }
}
