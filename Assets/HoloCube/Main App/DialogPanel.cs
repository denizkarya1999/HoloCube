using UnityEngine;
using UnityEngine.UI;

namespace HoloCube.QuestYOLO
{
    /// <summary>A rounded card with a separate border, built entirely from UI geometry.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DialogPanel : MaskableGraphic
    {
        [Min(0f)] public float CornerRadius = 24f;
        [Min(0f)] public float BorderWidth = 2f;
        public Color BorderColor = new Color(1f, 1f, 1f, 0.16f);

        private const int CornerSegments = 9;
        private const int PerimeterCount = 4 * (CornerSegments + 1);

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect outer = GetPixelAdjustedRect();
            if (outer.width <= 0f || outer.height <= 0f) return;

            float limit = Mathf.Min(outer.width, outer.height) * 0.5f;
            float radius = Mathf.Clamp(CornerRadius, 0f, limit);
            float border = Mathf.Clamp(BorderWidth, 0f, limit);
            if (border <= 0f)
            {
                Fill(mesh, outer, radius);
                return;
            }

            Rect inner = Rect.MinMaxRect(outer.xMin + border, outer.yMin + border,
                outer.xMax - border, outer.yMax - border);
            float innerRadius = Mathf.Max(0f, radius - border);

            // A ring leaves the fill unobstructed, including when either color is translucent.
            for (int i = 0; i < PerimeterCount; i++)
            {
                mesh.AddVert(PerimeterPoint(outer, radius, i), BorderColor, Vector2.zero);
                mesh.AddVert(PerimeterPoint(inner, innerRadius, i), BorderColor, Vector2.zero);
            }
            for (int i = 0; i < PerimeterCount; i++)
            {
                int next = (i + 1) % PerimeterCount;
                mesh.AddTriangle(i * 2, next * 2, next * 2 + 1);
                mesh.AddTriangle(i * 2, next * 2 + 1, i * 2 + 1);
            }
            if (inner.width > 0f && inner.height > 0f) Fill(mesh, inner, innerRadius);
        }

        private void Fill(VertexHelper mesh, Rect bounds, float radius)
        {
            int center = mesh.currentVertCount;
            mesh.AddVert(bounds.center, color, Vector2.zero);
            for (int i = 0; i < PerimeterCount; i++)
                mesh.AddVert(PerimeterPoint(bounds, radius, i), color, Vector2.zero);
            for (int i = 0; i < PerimeterCount; i++)
                mesh.AddTriangle(center, center + 1 + i, center + 1 + (i + 1) % PerimeterCount);
        }

        private static Vector2 PerimeterPoint(Rect bounds, float radius, int index)
        {
            int corner = index / (CornerSegments + 1);
            int segment = index % (CornerSegments + 1);
            Vector2 center;
            switch (corner)
            {
                case 0: center = new Vector2(bounds.xMax - radius, bounds.yMax - radius); break;
                case 1: center = new Vector2(bounds.xMin + radius, bounds.yMax - radius); break;
                case 2: center = new Vector2(bounds.xMin + radius, bounds.yMin + radius); break;
                default: center = new Vector2(bounds.xMax - radius, bounds.yMin + radius); break;
            }
            float angle = (corner + (float)segment / CornerSegments) * Mathf.PI * 0.5f;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            CornerRadius = Mathf.Max(0f, CornerRadius);
            BorderWidth = Mathf.Max(0f, BorderWidth);
            base.OnValidate();
        }
#endif
    }
}
