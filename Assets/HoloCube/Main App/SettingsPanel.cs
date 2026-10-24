using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace HoloCube.QuestYOLO
{
    /// <summary>Displays adjustable confidence and the fixed single-box limit.</summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        public Text ConfidenceValue;
        public Text BoxCountValue;
        public DialogPanel ConfidenceBox;
        public DialogPanel BoxCountBox;

        private static readonly Color Maize = new Color32(255, 203, 5, 255);
        private static readonly Color InactiveBorder = new Color(1f, 203f / 255f, 5f / 255f, 0.35f);
        private static readonly Color DarkBlue = new Color32(0, 27, 54, 255);
        private static readonly Color SelectedBlue = new Color32(0, 55, 103, 255);
        private bool hasState;
        private float lastConfidence;

        public void Refresh(float confidence)
        {
            if (!hasState || !confidence.Equals(lastConfidence))
            {
                if (ConfidenceValue != null)
                {
                    ConfidenceValue.text = confidence.ToString("0.00", CultureInfo.InvariantCulture);
                    ConfidenceValue.color = Maize;
                }
                lastConfidence = confidence;
            }
            if (!hasState)
            {
                if (BoxCountValue != null)
                {
                    BoxCountValue.text = TransmitterRecognitionModel.MaxDetections.ToString(CultureInfo.InvariantCulture);
                    BoxCountValue.color = Maize;
                }
                SetSelected(ConfidenceBox, true);
                SetSelected(BoxCountBox, false);
            }
            hasState = true;
        }

        private static void SetSelected(DialogPanel box, bool selected)
        {
            if (box == null) return;
            Color fill = selected ? SelectedBlue : DarkBlue;
            Color border = selected ? Maize : InactiveBorder;
            float width = selected ? 3f : 1.5f;
            if (box.color == fill && box.BorderColor == border && box.BorderWidth == width) return;
            box.color = fill;
            box.BorderColor = border;
            box.BorderWidth = width;
            box.SetVerticesDirty();
        }
    }
}
