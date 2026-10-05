using UnityEngine;
using UnityEngine.UI;

namespace HoloCube.QuestYOLO
{
    /// <summary>Small vector icons that stay crisp without a font or texture.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DialogIcon : MaskableGraphic
    {
        public enum Kind { Cube, Controls, Settings, InferenceOff, Saving, Saved, Warning, Confidence, Boxes }

        [SerializeField] private Kind icon;
        private Vector2 origin;
        private float size;
        private const float Stroke = 0.06f;

        public void SetIcon(Kind value)
        {
            if (icon == value) return;
            icon = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect bounds = GetPixelAdjustedRect();
            size = Mathf.Min(bounds.width, bounds.height);
            if (size <= 0f) return;
            origin = bounds.center - Vector2.one * (size * 0.5f);

            switch (icon)
            {
                case Kind.Cube:
                    Path(mesh, true, new Vector2(0.5f, 0.9f), new Vector2(0.85f, 0.7f),
                        new Vector2(0.85f, 0.3f), new Vector2(0.5f, 0.1f),
                        new Vector2(0.15f, 0.3f), new Vector2(0.15f, 0.7f));
                    Line(mesh, new Vector2(0.15f, 0.7f), new Vector2(0.5f, 0.5f));
                    Line(mesh, new Vector2(0.85f, 0.7f), new Vector2(0.5f, 0.5f));
                    Line(mesh, new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.5f));
                    break;
                case Kind.Controls:
                    Path(mesh, true, new Vector2(0.29f, 0.74f), new Vector2(0.71f, 0.74f),
                        new Vector2(0.82f, 0.68f), new Vector2(0.91f, 0.32f),
                        new Vector2(0.88f, 0.23f), new Vector2(0.79f, 0.21f),
                        new Vector2(0.63f, 0.38f), new Vector2(0.37f, 0.38f),
                        new Vector2(0.21f, 0.21f), new Vector2(0.12f, 0.23f),
                        new Vector2(0.09f, 0.32f), new Vector2(0.18f, 0.68f));
                    Line(mesh, new Vector2(0.24f, 0.55f), new Vector2(0.42f, 0.55f));
                    Line(mesh, new Vector2(0.33f, 0.46f), new Vector2(0.33f, 0.64f));
                    Disc(mesh, new Vector2(0.67f, 0.51f), 0.046f);
                    Disc(mesh, new Vector2(0.77f, 0.61f), 0.046f);
                    break;
                case Kind.Settings:
                    Gear(mesh);
                    break;
                case Kind.InferenceOff:
                    Ring(mesh, new Vector2(0.5f, 0.5f), 0.39f);
                    Line(mesh, new Vector2(0.38f, 0.32f), new Vector2(0.38f, 0.68f), 0.105f);
                    Line(mesh, new Vector2(0.62f, 0.32f), new Vector2(0.62f, 0.68f), 0.105f);
                    break;
                case Kind.Saving:
                    Line(mesh, new Vector2(0.5f, 0.85f), new Vector2(0.5f, 0.36f));
                    Path(mesh, false, new Vector2(0.3f, 0.55f), new Vector2(0.5f, 0.35f),
                        new Vector2(0.7f, 0.55f));
                    Path(mesh, false, new Vector2(0.16f, 0.38f), new Vector2(0.16f, 0.17f),
                        new Vector2(0.84f, 0.17f), new Vector2(0.84f, 0.38f));
                    break;
                case Kind.Saved:
                    Ring(mesh, new Vector2(0.5f, 0.5f), 0.39f);
                    Path(mesh, false, new Vector2(0.28f, 0.5f), new Vector2(0.44f, 0.34f),
                        new Vector2(0.72f, 0.66f));
                    break;
                case Kind.Warning:
                    Path(mesh, true, new Vector2(0.5f, 0.88f), new Vector2(0.9f, 0.18f),
                        new Vector2(0.1f, 0.18f));
                    Line(mesh, new Vector2(0.5f, 0.63f), new Vector2(0.5f, 0.43f), 0.075f);
                    Disc(mesh, new Vector2(0.5f, 0.3f), 0.04f);
                    break;
                case Kind.Confidence:
                    Ring(mesh, new Vector2(0.5f, 0.5f), 0.31f);
                    Ring(mesh, new Vector2(0.5f, 0.5f), 0.15f, 0.05f);
                    Disc(mesh, new Vector2(0.5f, 0.5f), 0.035f);
                    Line(mesh, new Vector2(0.5f, 0.81f), new Vector2(0.5f, 0.9f));
                    Line(mesh, new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.19f));
                    Line(mesh, new Vector2(0.1f, 0.5f), new Vector2(0.19f, 0.5f));
                    Line(mesh, new Vector2(0.81f, 0.5f), new Vector2(0.9f, 0.5f));
                    break;
                case Kind.Boxes:
                    Path(mesh, true, new Vector2(0.12f, 0.18f), new Vector2(0.65f, 0.18f),
                        new Vector2(0.65f, 0.66f), new Vector2(0.12f, 0.66f));
                    Path(mesh, false, new Vector2(0.35f, 0.78f), new Vector2(0.35f, 0.88f),
                        new Vector2(0.88f, 0.88f), new Vector2(0.88f, 0.4f),
                        new Vector2(0.77f, 0.4f));
                    break;
            }
        }

        private void Gear(VertexHelper mesh)
        {
            const int Teeth = 8;
            Vector2 previous = Vector2.zero;
            Vector2 first = Vector2.zero;
            for (int i = 0; i < Teeth * 4; i++)
            {
                float angle = i * Mathf.PI * 2f / (Teeth * 4);
                float radius = i % 4 < 2 ? 0.4f : 0.31f;
                Vector2 point = new Vector2(0.5f + Mathf.Cos(angle) * radius,
                    0.5f + Mathf.Sin(angle) * radius);
                if (i == 0) first = point;
                else Line(mesh, previous, point, 0.052f);
                previous = point;
            }
            Line(mesh, previous, first, 0.052f);
            Ring(mesh, new Vector2(0.5f, 0.5f), 0.135f, 0.052f);
        }

        private void Path(VertexHelper mesh, bool closed, params Vector2[] points)
        {
            for (int i = 1; i < points.Length; i++) Line(mesh, points[i - 1], points[i]);
            if (closed) Line(mesh, points[points.Length - 1], points[0]);
        }

        private void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width = Stroke)
        {
            Vector2 direction = (to - from).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
            int start = mesh.currentVertCount;
            Vertex(mesh, from - normal);
            Vertex(mesh, from + normal);
            Vertex(mesh, to + normal);
            Vertex(mesh, to - normal);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
            Disc(mesh, from, width * 0.5f);
            Disc(mesh, to, width * 0.5f);
        }

        private void Ring(VertexHelper mesh, Vector2 center, float radius, float width = Stroke)
        {
            const int Segments = 40;
            int start = mesh.currentVertCount;
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vertex(mesh, center + radial * (radius - width * 0.5f));
                Vertex(mesh, center + radial * (radius + width * 0.5f));
                if (i == 0) continue;
                int next = start + i * 2;
                mesh.AddTriangle(next - 2, next - 1, next + 1);
                mesh.AddTriangle(next - 2, next + 1, next);
            }
        }

        private void Disc(VertexHelper mesh, Vector2 center, float radius)
        {
            const int Segments = 12;
            int start = mesh.currentVertCount;
            Vertex(mesh, center);
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                Vertex(mesh, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            for (int i = 0; i < Segments; i++)
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % Segments);
        }

        private void Vertex(VertexHelper mesh, Vector2 position)
        {
            mesh.AddVert(origin + position * size, color, Vector2.zero);
        }
    }
}
