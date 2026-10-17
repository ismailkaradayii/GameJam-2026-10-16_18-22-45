using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Bounded screen-edge wind ribbons, sharing the locomotion controller's sprint blend.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CanvasRenderer)), DefaultExecutionOrder(240)]
    public sealed class SprintWindLines : MaskableGraphic
    {
        public FireRescuePlayer player;
        [Range(6, 30)] public int linesPerSide = 22;
        [Range(.2f, 3f)] public float travelSpeed = 1.35f;
        [Range(.1f, 1f)] public float opacity = .85f;
        [Range(.5f, 5f)] public float thickness = 3.2f;
        public float Strength { get; private set; }
        public int VisibleStrokeCount { get; private set; }
        float windTime;
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        void LateUpdate()
        {
            var fp = player ? player.firstPerson : null;
            float previous = Strength;
            bool available = fp && !player.IsDriving && !player.GameplaySuspended && !player.CinematicLocked && Time.timeScale > 0 && fp.InputCaptured;
            Strength = available ? fp.SprintViewBlend * (player.pistol ? 1f - player.pistol.AimAmount : 1f) : 0;
            if (Strength > .001f) windTime += Mathf.Min(Time.deltaTime, .1f);
            if (Strength > .001f || previous > .001f) SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); VisibleStrokeCount = 0;
            if (Strength < .001f) return;
            Rect bounds = rectTransform.rect; Vector2 centre = bounds.center;
            float h = bounds.height, w = bounds.width;
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < linesPerSide; i++)
            {
                float seed = Mathf.Repeat(i * .6180339f + (side > 0 ? .27f : 0), 1f);
                float cycle = Mathf.Repeat(windTime * travelSpeed * Mathf.Lerp(.8f, 1.4f, seed) + seed, 1f);
                float radial = Mathf.Lerp(.62f, 1.38f, cycle);
                Vector2 point = centre + new Vector2(side * w * .5f * radial, (seed - .5f) * h * 1.1f * radial);
                Vector2 direction = (point - centre).normalized;
                float length = h * Mathf.Lerp(.07f, .2f, Mathf.Repeat(seed * 5.7f, 1));
                float width = h / 1080f * thickness * Mathf.Lerp(.6f, 1.4f, seed);
                float alpha = Strength * opacity * Mathf.Sin(cycle * Mathf.PI) * Mathf.Lerp(.5f, 1f, seed);
                // All ribbons start in the outer fifth of the frame; the aiming area stays clear.
                Vector2 tail = point - direction * length * .5f;
                Vector2 head = point + direction * length * .5f;
                Vector2 bend = Vector2.Perpendicular(direction) * h * .006f * (seed - .5f);
                Vector2 mid = (tail + head) * .5f + bend;
                Ribbon(mesh, tail, mid, width * .15f, width, 0, alpha);
                Ribbon(mesh, mid, head, width, width * .1f, alpha, 0);
                VisibleStrokeCount++;
            }
        }
        void Ribbon(VertexHelper mesh, Vector2 from, Vector2 to, float fromWidth, float toWidth, float fromAlpha, float toAlpha)
        {
            Vector2 normal = Vector2.Perpendicular(to - from).normalized;
            Color a = color, b = color; a.a *= fromAlpha; b.a *= toAlpha;
            int start = mesh.currentVertCount;
            mesh.AddVert(from - normal * fromWidth, a, Vector2.zero);
            mesh.AddVert(from + normal * fromWidth, a, Vector2.up);
            mesh.AddVert(to + normal * toWidth, b, Vector2.one);
            mesh.AddVert(to - normal * toWidth, b, Vector2.right);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
