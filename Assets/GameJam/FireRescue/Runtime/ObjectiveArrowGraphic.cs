using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>A filled arrow with a dark outline, readable over sky, foliage and fire.</summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveArrowGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            DrawArrow(mesh, rect.center + new Vector2(0, -2), rect.size, new Color(0.015f, .025f, .035f, .94f));
            DrawArrow(mesh, rect.center, rect.size * .78f, color);
        }

        static void DrawArrow(VertexHelper mesh, Vector2 center, Vector2 size, Color tint)
        {
            int first = mesh.currentVertCount;
            Vector2[] shape = {
                new Vector2(-.14f, -.48f), new Vector2(.14f, -.48f),
                new Vector2(.14f, -.06f), new Vector2(-.14f, -.06f),
                new Vector2(-.47f, -.10f), new Vector2(.47f, -.10f), new Vector2(0, .48f)
            };
            foreach (Vector2 point in shape) mesh.AddVert(center + Vector2.Scale(point, size), tint, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first + 2, first + 3, first);
            mesh.AddTriangle(first + 4, first + 5, first + 6);
        }
    }
}
