using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>Lit liquid core, aerated breakup and mist. Receives ballistic samples; does no physics.</summary>
    [DisallowMultipleComponent]
    public sealed class PressurizedWaterVisual : MonoBehaviour
    {
        [Min(.01f)] public float nozzleRadius = .055f;
        [Min(.1f)] public float coherentSeconds = .38f;
        [Range(.01f, .6f)] public float spreadPerSecond = .38f;
        [Tooltip("The visible water fan opens continuously from the nozzle.")]
        [Range(0f, 16f)] public float coneHalfAngle = 8.5f;
        public int VisibleDroplets { get; private set; }
        public int CoreSegments { get; private set; }
        public int ImpactCount { get; private set; }
        public ParticleSystem Droplets => droplets;
        public ParticleSystem Mist => mist;
        public ParticleSystem Splashes => splashes;
        const int MaxSamples = 512, Sides = 8;
        readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[MaxSamples * 8];
        readonly List<Vector3> vertices = new List<Vector3>(MaxSamples * Sides * 2);
        readonly List<Vector3> normals = new List<Vector3>(MaxSamples * Sides * 2);
        readonly List<Vector2> uvs = new List<Vector2>(MaxSamples * Sides * 2);
        readonly List<Color> colors = new List<Color>(MaxSamples * Sides * 2);
        readonly List<int> triangles = new List<int>(MaxSamples * Sides * 6);
        Mesh core;
        MeshRenderer coreRenderer;
        ParticleSystem droplets, mist, splashes, impactMist;
        Transform effectsRoot;
        Vector3 previousPosition, previousVelocity;
        float previousAge, previousTime, frameTime, nextMist, nextImpact;
        bool hasPrevious;
        int mistBudget;
        float launchSpeed = 35f;
        public float SprayRadiusAtAge(float age) => nozzleRadius + Mathf.Max(0, age) *
            (launchSpeed * Mathf.Tan(coneHalfAngle * Mathf.Deg2Rad) + spreadPerSecond * .15f);

        void Ensure()
        {
            if (core) return;
            var root = new GameObject("Pressure jet · visuals only");
            root.transform.SetParent(transform, false); effectsRoot = root.transform;
            var filter = root.AddComponent<MeshFilter>(); coreRenderer = root.AddComponent<MeshRenderer>();
            core = new Mesh { name = "Ballistic liquid core" }; core.MarkDynamic(); filter.sharedMesh = core;
            coreRenderer.sharedMaterial = Resources.Load<Material>("WaterRealism/Jet");
            coreRenderer.shadowCastingMode = ShadowCastingMode.Off; coreRenderer.receiveShadows = true;
            droplets = WaterVisualUtility.CreateParticles("Aerated droplets", effectsRoot, "Droplet", particles.Length, true, 0);
            var fade = droplets.colorOverLifetime; fade.enabled = false;
            droplets.Pause();
            mist = WaterVisualUtility.CreateParticles("Fine entrained mist", effectsRoot, "Mist", 240, false, .035f);
            splashes = WaterVisualUtility.CreateParticles("Impact droplets", effectsRoot, "Droplet", 480, true, 1f);
            impactMist = WaterVisualUtility.CreateParticles("Impact atomization", effectsRoot, "Mist", 160, false, .05f);
            var size = mist.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .5f, 1, 2f));
            size = impactMist.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .6f, 1, 2f));
        }

        public void BeginFrame(float time, Vector3 muzzle, Vector3 velocity, bool emitting)
        {
            Ensure(); effectsRoot.gameObject.SetActive(true);
            vertices.Clear(); normals.Clear(); uvs.Clear(); colors.Clear(); triangles.Clear();
            VisibleDroplets = CoreSegments = 0; hasPrevious = emitting;
            previousPosition = muzzle; previousVelocity = velocity; previousAge = 0; previousTime = time;
            frameTime = time;
            launchSpeed = Mathf.Max(1, velocity.magnitude);
            mistBudget = 12;
        }

        // Samples arrive newest to oldest. Missing packets are never joined across an impact or a released trigger.
        public void Sample(Vector3 position, Vector3 previous, Vector3 velocity, float age, float emittedAt, int seed)
        {
            bool connected = hasPrevious && previousTime - emittedAt < .033f &&
                Vector3.Dot(previousVelocity.normalized, velocity.normalized) > .94f &&
                Vector3.SqrMagnitude(previousPosition - position) < 6.25f;
            if (connected && previousAge < coherentSeconds * 1.65f)
                AddCore(previousPosition, position, previousAge, age);
            float breakup = Mathf.SmoothStep(0f, 1f, age / coherentSeconds);
            Vector3 direction = velocity.normalized;
            Vector3 tangent = Vector3.Cross(direction, Vector3.up).normalized;
            if (tangent.sqrMagnitude < .01f) tangent = Vector3.right;
            Vector3 bitangent = Vector3.Cross(direction, tangent);
            int count = 8;
            for (int n = 0; n < count && VisibleDroplets < particles.Length; n++)
            {
                float noise = Mathf.Repeat(seed * .618034f + n * .381966f, 1f);
                float angle = seed * 2.39996f + n * 2.39996f;
                float radius = SprayRadiusAtAge(age) * Mathf.Sqrt(noise);
                Vector3 offset = (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius;
                // Interpolate the actual segment, never beyond its physical head or through its contact surface.
                Vector3 center = connected ? Vector3.Lerp(position, previousPosition, n / 8f) : Vector3.Lerp(previous, position, n / 8f);
                particles[VisibleDroplets++] = new ParticleSystem.Particle {
                    position = center + offset, velocity = velocity * .24f,
                    startLifetime = 1, remainingLifetime = 1,
                    startSize = Mathf.Lerp(.045f, .10f, noise) * Mathf.Lerp(.65f, 1.7f, breakup),
                    startColor = new Color(.85f, .94f, 1f, Mathf.Lerp(.4f, .82f, breakup) * Mathf.Clamp01((8f - age) * 2f)),
                    randomSeed = (uint)(seed * 8 + n + 1)
                };
            }
            if (mistBudget > 0 && age > .08f && frameTime >= nextMist && (seed % 3 == 0))
            {
                mistBudget--;
                float angle = seed * 2.399963f;
                Vector3 radial = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
                float radius = SprayRadiusAtAge(age);
                WaterVisualUtility.Emit(mist, position + radial * radius * .65f,
                    velocity * .13f + radial * .45f + Vector3.up * .2f,
                    .2f + radius * .45f, .48f, .15f);
            }
            previousPosition = position; previousVelocity = velocity; previousAge = age; previousTime = emittedAt; hasPrevious = true;
        }

        void AddCore(Vector3 from, Vector3 to, float ageFrom, float ageTo)
        {
            Vector3 axis = (to - from).normalized;
            if (axis.sqrMagnitude < .01f) return;
            Vector3 side = Vector3.Cross(axis, Vector3.up).normalized;
            if (side.sqrMagnitude < .01f) side = Vector3.right;
            Vector3 up = Vector3.Cross(side, axis);
            int start = vertices.Count;
            for (int ring = 0; ring < 2; ring++)
            {
                float age = ring == 0 ? ageFrom : ageTo;
                float radius = Mathf.Lerp(nozzleRadius, SprayRadiusAtAge(age) * .46f, Mathf.Clamp01(age / .06f));
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coherentSeconds * .5f, coherentSeconds * 1.65f, age));
                for (int n = 0; n < Sides; n++)
                {
                    float angle = n * Mathf.PI * 2 / Sides;
                    Vector3 normal = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    float r = radius * (1f + .12f * Mathf.Sin(age * 185f - frameTime * 65f + n * 1.7f));
                    vertices.Add(effectsRoot.InverseTransformPoint((ring == 0 ? from : to) + normal * r));
                    normals.Add(effectsRoot.InverseTransformDirection(normal));
                    uvs.Add(new Vector2(n / (float)Sides, age * 24f)); colors.Add(new Color(1, 1, 1, alpha * .62f));
                }
            }
            for (int n = 0; n < Sides; n++)
            {
                int a = start + n, b = start + (n + 1) % Sides;
                triangles.Add(a); triangles.Add(b); triangles.Add(a + Sides);
                triangles.Add(b); triangles.Add(b + Sides); triangles.Add(a + Sides);
            }
            CoreSegments++;
        }

        public void EndFrame()
        {
            core.Clear(false); core.SetVertices(vertices); core.SetNormals(normals); core.SetUVs(0, uvs); core.SetColors(colors);
            core.SetTriangles(triangles, 0); core.RecalculateBounds(); coreRenderer.enabled = CoreSegments > 0;
            droplets.SetParticles(particles, VisibleDroplets);
            if (frameTime >= nextMist) nextMist = frameTime + .045f;
        }

        public void Impact(Vector3 point, Vector3 normal, float litres, float time)
        {
            Ensure();
            if (time < nextImpact) return;
            nextImpact = time + .04f; ImpactCount++;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up).normalized;
            if (tangent.sqrMagnitude < .01f) tangent = Vector3.right;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            for (int i = 0; i < 14; i++)
            {
                float angle = i * 2.399963f + time;
                Vector3 radial = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
                Vector3 velocity = normal * Random.Range(1f, 3.8f) + radial * Random.Range(1f, 4.2f);
                float life = normal.y > .5f ? Mathf.Clamp(2f * Vector3.Dot(velocity, normal) / Mathf.Max(.1f, -Vector3.Dot(Physics.gravity, normal)), .12f, .65f) : Random.Range(.22f, .65f);
                WaterVisualUtility.Emit(splashes, point + normal * .045f, velocity, Random.Range(.035f, .09f), life, .82f);
            }
            for (int i = 0; i < 3; i++) WaterVisualUtility.Emit(impactMist, point + normal * .1f,
                normal * Random.Range(.4f, 1.2f) + Random.insideUnitSphere * .45f, Random.Range(.35f, .65f), .65f, .18f);
        }

        public void Clear()
        {
            if (!core) return;
            core.Clear(); VisibleDroplets = CoreSegments = 0;
            droplets.Clear(); mist.Clear(); splashes.Clear(); impactMist.Clear();
        }
        void OnDisable() { Clear(); if (effectsRoot) effectsRoot.gameObject.SetActive(false); }
        void OnDestroy() { if (core) Destroy(core); }
    }
}
