using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace HoloCube.QuestYOLO
{
    /// <summary>The bundled weights, class names, and choice of GPU or CPU.</summary>
    [CreateAssetMenu(menuName = "HoloCube/YOLO Model")]
    public sealed class YOLOModel : ScriptableObject
    {
        [Tooltip("The YOLO26n end-to-end .onnx file containing the trained network.")]
        public ModelAsset Weights;
        [Tooltip("One object class name per line. The line number is its class ID.")]
        public TextAsset Labels;
        [Tooltip("GPUCompute runs the network on the headset GPU.")]
        public BackendType Backend = BackendType.GPUCompute;
        [Tooltip("Keeps image proportions and adds padding before inference.")]
        public Shader LetterboxShader;

        public Model Load()
        {
            if (Weights == null || Labels == null)
                throw new InvalidOperationException("Assign the bundled YOLO model and labels.");

            var model = ModelLoader.Load(Weights);
            if (model.inputs.Count != 1 || model.outputs.Count != 1)
                throw new InvalidOperationException("Expected YOLO26 with one image input and one end-to-end detection output.");
            return model;
        }

        public string[] LoadLabels()
        {
            if (Labels == null) throw new InvalidOperationException("Assign the class names file.");
            var names = Labels.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();
            return names;
        }
    }
}
