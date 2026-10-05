using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace HoloCube.QuestYOLO
{
    /// <summary>Displays joystick-adjusted settings and highlights the selected value.</summary>
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
        private int lastMaxBoxes;
        private int lastSelectedIndex;

        public void Refresh(float confidence, int maxBoxes, int selectedIndex)
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
            if (!hasState || maxBoxes != lastMaxBoxes)
            {
                if (BoxCountValue != null)
                {
                    BoxCountValue.text = maxBoxes.ToString(CultureInfo.InvariantCulture);
                    BoxCountValue.color = Maize;
                }
                lastMaxBoxes = maxBoxes;
            }
            if (!hasState || selectedIndex != lastSelectedIndex)
            {
                SetSelected(ConfidenceBox, selectedIndex == 0);
                SetSelected(BoxCountBox, selectedIndex == 1);
                lastSelectedIndex = selectedIndex;
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
