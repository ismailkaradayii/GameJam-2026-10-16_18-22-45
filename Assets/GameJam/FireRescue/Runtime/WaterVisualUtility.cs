using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    // Rendering only: these systems never own colliders, rigidbodies or damage.
    public static class WaterVisualUtility
    {
        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };

        public static ParticleSystem CreateParticles(string name, Transform parent, string material, int capacity, bool streak, float gravity)
        {
            var root = new GameObject(name); root.transform.SetParent(parent, false);
            var particles = root.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = true; main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.useUnscaledTime = false; main.maxParticles = capacity;
            main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = .06f;
            main.gravityModifier = gravity; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var collision = particles.collision; collision.enabled = false;
            var trails = particles.trails; trails.enabled = false;
            var color = particles.colorOverLifetime; color.enabled = true;
            color.color = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, .06f), new GradientAlphaKey(.55f, .55f), new GradientAlphaKey(0f, 1) }
            };
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("WaterRealism/" + material);
            renderer.renderMode = streak ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = 1.4f; renderer.velocityScale = .035f;
            renderer.normalDirection = .8f; renderer.minParticleSize = 0; renderer.maxParticleSize = .25f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true; renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.SetActiveVertexStreams(Streams);
            particles.Play();
            return particles;
        }

        public static void Emit(ParticleSystem particles, Vector3 point, Vector3 velocity, float size, float life, float alpha)
        {
            if (!particles || !particles.gameObject.activeInHierarchy) return;
            particles.Emit(new ParticleSystem.EmitParams {
                position = point, velocity = velocity, startSize = size, startLifetime = life,
                startColor = new Color(.88f, .95f, 1f, alpha), rotation = Random.Range(0f, 360f)
            }, 1);
        }
    }
}
