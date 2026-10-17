using UnityEngine;

namespace Itfaiye
{
    /// <summary>A reticle projected onto the real optic glass from the resolved barrel hit.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(200)]
    public sealed class ItfaiyeReflexSight : MonoBehaviour
    {
        public ItfaiyePistol pistol;
        public MeshRenderer lens;
        [Range(.006f, .035f)] public float dotRadius = .012f;
        public bool ReticleVisible { get; private set; }
        public Vector2 ReticleUV { get; private set; }
        public Vector3 ReticleWorldPoint { get; private set; }
        MaterialPropertyBlock properties;
        static readonly int ReticleId = Shader.PropertyToID("_Reticle");

        void LateUpdate()
        {
            if (!lens || !pistol || !pistol.player || !pistol.player.playerCamera) return;
            var eye = pistol.player.playerCamera.transform.position;
            var ray = new Ray(eye, (pistol.AimPoint - eye).normalized);
            var plane = new Plane(lens.transform.forward, lens.transform.position);
            ReticleVisible = pistol.Ready && pistol.player.InputCaptured && !pistol.MuzzleObstructed;
            if (plane.Raycast(ray, out float distance) && distance > 0f)
            {
                ReticleWorldPoint = ray.GetPoint(distance);
                Vector3 local = lens.transform.InverseTransformPoint(ReticleWorldPoint);
                ReticleUV = new Vector2(local.x + .5f, local.y + .5f);
                ReticleVisible &= Mathf.Abs(local.x) < .48f && Mathf.Abs(local.y) < .48f;
            }
            else ReticleVisible = false;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetVector(ReticleId, new Vector4(ReticleUV.x, ReticleUV.y, dotRadius, ReticleVisible ? 1f : 0f));
            lens.SetPropertyBlock(properties);
        }
        void OnDisable()
        {
            ReticleVisible = false;
            if (lens) lens.SetPropertyBlock(null);
        }
    }
}
