using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>A soft, symmetric neon halo for the existing Canvas result title.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Text))]
    public sealed class OutcomeTitleGlow : BaseMeshEffect
    {
        readonly List<UIVertex> source = new List<UIVertex>();
        readonly List<UIVertex> glowing = new List<UIVertex>();
        Color glowColor;

        public static void SetResult(Text label, string title, bool won)
        {
            if (!label) return;
            label.text = title;
            label.color = won ? new Color(.20f, 1f, .34f) : new Color(1f, .20f, .20f);
            var effect = label.GetComponent<OutcomeTitleGlow>();
            if (!effect) effect = label.gameObject.AddComponent<OutcomeTitleGlow>();
            Color glow = won ? new Color(0f, 1f, .12f) : new Color(1f, .025f, .025f);
            if (effect.glowColor == glow) return;
            effect.glowColor = glow;
            label.SetVerticesDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            foreach (var shadow in GetComponents<Shadow>()) shadow.enabled = false;
            if (graphic) graphic.raycastTarget = false;
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount == 0) return;
            source.Clear();
            glowing.Clear();
            mesh.GetUIVertexStream(source);
            // Low-alpha concentric samples produce a halo without a bloom camera or a font change.
            AddRing(6f, 16, .025f);
            AddRing(3f, 12, .07f);
            AddRing(1.25f, 8, .16f);
            glowing.AddRange(source);
            mesh.Clear();
            mesh.AddUIVertexTriangleStream(glowing);
        }

        void AddRing(float radius, int samples, float opacity)
        {
            Color32 tint = glowColor;
            for (int sample = 0; sample < samples; sample++)
            {
                float angle = sample * Mathf.PI * 2f / samples;
                Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                for (int i = 0; i < source.Count; i++)
                {
                    var vertex = source[i];
                    tint.a = (byte)Mathf.RoundToInt(vertex.color.a * opacity);
                    vertex.color = tint;
                    vertex.position += offset;
                    glowing.Add(vertex);
                }
            }
        }
    }
}
