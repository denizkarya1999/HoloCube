using UnityEngine;
using UnityEngine.UI;

namespace HoloCube.QuestYOLO
{
    /// <summary>A solid recording circle that does not depend on a font glyph.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RecordingDot : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            const int segments = 32;
            mesh.Clear();
            Rect bounds = GetPixelAdjustedRect();
            Vector2 center = bounds.center;
            float radius = Mathf.Min(bounds.width, bounds.height) * 0.5f;
            mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2f * Mathf.PI / segments;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                    color, Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
                mesh.AddTriangle(0, i + 1, (i + 1) % segments + 1);
        }
    }
}
