using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 R1-C · the tree's edges as ONE Graphic (WP-077 PD §5.8: one mesh, one batch). Each edge is a quad from A to
    /// B in the graphic's local space; a dashed edge (a prerequisite still missing) is 6 px on / 4 px off. Widths and
    /// colours carry the state: hairline, the owned Rim 2, the pinned path 4 px. Rebuilt only when the tree changes
    /// (Commit), never per frame.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiTreeEdges : MaskableGraphic
    {
        private struct Edge
        {
            public Vector2 A;
            public Vector2 B;
            public float Width;
            public bool Dashed;
            public Color32 Colour;
        }

        private const float Dash = 6f;
        private const float Gap = 4f;

        private readonly List<Edge> _edges = new List<Edge>(48);

        /// <summary>Edges waiting for the next mesh (tests).</summary>
        public int Count => _edges.Count;

        public void Clear()
        {
            _edges.Clear();
        }

        public void Add(Vector2 a, Vector2 b, float width, bool dashed, Color colour)
        {
            _edges.Add(new Edge { A = a, B = b, Width = Mathf.Max(1f, width), Dashed = dashed, Colour = colour });
        }

        public void Commit()
        {
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                Vector2 d = e.B - e.A;
                float length = d.magnitude;
                if (length < 0.5f)
                {
                    continue;
                }

                Vector2 dir = d / length;
                if (!e.Dashed)
                {
                    Quad(vh, e.A, e.B, dir, e.Width, e.Colour);
                    continue;
                }

                for (float t = 0f; t < length; t += Dash + Gap)
                {
                    Quad(vh, e.A + dir * t, e.A + dir * Mathf.Min(length, t + Dash), dir, e.Width, e.Colour);
                }
            }
        }

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 dir, float width, Color32 colour)
        {
            Vector2 n = new Vector2(-dir.y, dir.x) * (width * 0.5f);
            int start = vh.currentVertCount;
            vh.AddVert(a - n, colour, Vector2.zero);
            vh.AddVert(a + n, colour, Vector2.zero);
            vh.AddVert(b + n, colour, Vector2.zero);
            vh.AddVert(b - n, colour, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
