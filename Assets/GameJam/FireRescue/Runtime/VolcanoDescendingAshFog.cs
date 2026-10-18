using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>A bounded, terrain-following ash front driven by the existing eruption clock.</summary>
    [DisallowMultipleComponent]
    public sealed class VolcanoDescendingAshFog : MonoBehaviour
    {
        [Min(0)] public float startDelay = 6f;
        [Min(10)] public float descentSeconds = 48f;
        [Range(20, 100)] public float nearVisibility = 32f;
        [Range(160, 600)] public float farVisibility = 300f;
        public float FrontProgress { get; private set; }
        public float VillageCoverage { get; private set; }
        public Vector3 FrontPosition { get; private set; }
        public Vector3 VillageCentre { get; private set; }
        public const int MaximumParticles = 192;
        public int LiveParticles
        {
            get { int n = 0; foreach (var bank in banks) if (bank) n += bank.particleCount; return n; }
        }
        public bool IsReady => material && root;
        public bool UsesNativeSmoke { get; private set; }

        readonly ParticleSystem[] banks = new ParticleSystem[12];
        readonly Vector3[] slope = new Vector3[13];
        VolcanoEruptionSequence sequence;
        Material material;
        Transform root;
        Vector3 across, origin;
        float routeLength;
        bool begun;

        public void Initialize(VolcanoEruptionSequence eruption)
        {
            if (IsReady || !eruption || !eruption.effects) return;
            sequence = eruption;
            var source = eruption.effects.smokePlume;
            var renderer = source ? source.GetComponent<ParticleSystemRenderer>() : null;
            var sourceMaterial = renderer ? renderer.sharedMaterial : null;
            if (!sourceMaterial) sourceMaterial = Resources.Load<Material>("LavaCombat/Smoke");
            if (!sourceMaterial) return;
            UsesNativeSmoke = source && renderer && renderer.sharedMaterial == sourceMaterial;
            material = new Material(sourceMaterial) { name = "Runtime descending volcanic ash fog" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_ColorMode")) material.SetFloat("_ColorMode", 0);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_COLORCOLOR_ON"); material.DisableKeyword("_EMISSION");
            material.DisableKeyword("_FLIPBOOKBLENDING_ON"); material.DisableKeyword("_NORMALMAP");
            if (material.HasProperty("_FlipbookBlending")) material.SetFloat("_FlipbookBlending", 0);
            if (material.HasProperty("_SoftParticlesNearFadeDistance")) material.SetFloat("_SoftParticlesNearFadeDistance", 0);
            if (material.HasProperty("_SoftParticlesFarFadeDistance")) material.SetFloat("_SoftParticlesFarFadeDistance", 12);
            root = new GameObject("Descending ash fog (runtime)").transform;
            root.SetParent(transform, false);

            origin = source ? source.transform.position : eruption.effects.transform.position;
            VillageCentre = TownCentre(eruption);
            Vector3 direction = Vector3.ProjectOnPlane(VillageCentre - origin, Vector3.up).normalized;
            across = Vector3.Cross(Vector3.up, direction);
            routeLength = Mathf.Max(1, Vector3.ProjectOnPlane(VillageCentre - origin, Vector3.up).magnitude);
            for (int i = 0; i < slope.Length; i++)
            {
                float t = i / (float)(slope.Length - 1);
                Vector3 point = Vector3.Lerp(origin, VillageCentre, t);
                point.y = Ground(point) + Mathf.Lerp(28, 10, t);
                if (i == 0) point.y = Mathf.Max(point.y, origin.y + 12);
                slope[i] = point;
            }
            FrontPosition = slope[0];
            for (int i = 0; i < banks.Length; i++)
            {
                bool downhill = i < 6;
                banks[i] = CreateBank(i, downhill, source);
                if (!downhill)
                {
                    float a = (i - 6) * Mathf.PI / 3;
                    Vector3 point = VillageCentre + new Vector3(Mathf.Cos(a) * 105, 0, Mathf.Sin(a) * 90);
                    point.y = Ground(point) + 8;
                    banks[i].transform.position = point;
                }
            }
        }

        static Vector3 TownCentre(VolcanoEruptionSequence eruption)
        {
            Vector3 centre = Vector3.zero; int count = 0;
            var flow = eruption.missionDirector ? eruption.missionDirector.gameFlow : null;
            if (flow && flow.targetBuildings != null)
                foreach (var house in flow.targetBuildings) if (house) { centre += house.transform.position; count++; }
            if (count > 0) return centre / count;
            var clouds = eruption.effects.driftingAshClouds;
            foreach (var cloud in clouds) if (cloud != null && cloud.particles) { centre += cloud.villagePosition; count++; }
            return count > 0 ? centre / count : eruption.effects.transform.position + Vector3.back * 650;
        }

        static float Ground(Vector3 position)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                Vector3 local = position - terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (local.x >= 0 && local.z >= 0 && local.x <= size.x && local.z <= size.z)
                    return terrain.SampleHeight(position) + terrain.transform.position.y;
            }
            return position.y;
        }

        ParticleSystem CreateBank(int index, bool downhill, ParticleSystem source)
        {
            var go = new GameObject(downhill ? "Rolling slope bank " + index : "Village low fog " + (index - 6));
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(7419 + index * 113);
            var main = ps.main;
            main.playOnAwake = main.prewarm = false; main.loop = true;
            main.duration = 20; main.startLifetime = new ParticleSystem.MinMaxCurve(16, 22);
            main.maxParticles = 16; main.startSpeed = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
            main.startSize = new ParticleSystem.MinMaxCurve(downhill ? 78 : 95, downhill ? 125 : 145);
            main.startColor = new Color(.62f, .60f, .57f, downhill ? .32f : .20f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var emission = ps.emission; emission.rateOverTime = 0;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(downhill ? 55 : 100, downhill ? 15 : 6, downhill ? 35 : 95);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .45f), new Keyframe(.35f, 1), new Keyframe(1, 1.45f)));
            var colour = ps.colorOverLifetime; colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .18f), new GradientAlphaKey(.65f, .7f), new GradientAlphaKey(0, 1) });
            colour.color = gradient;
            var velocity = ps.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local; velocity.x = .65f; velocity.y = .05f; velocity.z = -.18f;
            if (source && source.textureSheetAnimation.enabled)
            {
                var original = source.textureSheetAnimation; var sheet = ps.textureSheetAnimation;
                sheet.enabled = true; sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = original.numTilesX; sheet.numTilesY = original.numTilesY;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = original.frameOverTime; sheet.startFrame = original.startFrame;
                sheet.cycleCount = original.cycleCount;
            }
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material; renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = .55f;
            return ps;
        }

        Vector3 OnSlope(float progress)
        {
            float f = Mathf.Clamp01(progress) * (slope.Length - 1);
            int index = Mathf.Min(slope.Length - 2, Mathf.FloorToInt(f));
            return Vector3.Lerp(slope[index], slope[index + 1], f - index);
        }

        /// <summary>Uses the sequencer clock, so pause, the spirit freeze, restart and victory holds stay in sync.</summary>
        public void TickFog()
        {
            if (!IsReady || !sequence || !sequence.effects) return;
            float elapsed = sequence.effects.IsErupting ? sequence.effects.Elapsed : 0;
            if (elapsed <= startDelay)
            {
                if (begun) ResetFog();
                return;
            }
            begun = true;
            FrontProgress = Mathf.Clamp01((elapsed - startDelay) / Mathf.Max(10, descentSeconds));
            FrontPosition = OnSlope(FrontProgress);
            VillageCoverage = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, 1, FrontProgress));
            for (int i = 0; i < banks.Length; i++)
            {
                var bank = banks[i]; if (!bank) continue;
                bool downhill = i < 6;
                float coverage = downhill ? Mathf.SmoothStep(0, 1, (elapsed - startDelay - i * .55f) / 8) : VillageCoverage;
                if (downhill)
                {
                    float progress = Mathf.Clamp01((elapsed - startDelay - i * .55f) / Mathf.Max(10, descentSeconds));
                    bank.transform.position = OnSlope(progress) + across * ((i - 2.5f) * Mathf.Lerp(12, 32, progress));
                }
                if (coverage > 0 && !bank.isPlaying)
                {
                    bank.Play(false);
                    // A few soft, initially transparent puffs make the travelling leading edge
                    // readable before the deliberately low sustained emission fills the bank.
                    bank.Emit(downhill ? 3 : 2);
                }
                var emission = bank.emission; emission.rateOverTime = (downhill ? .55f : .4f) * coverage;
            }
        }

        public float CoverageAt(Vector3 position)
        {
            if (!begun) return 0;
            Vector3 along = Vector3.ProjectOnPlane(VillageCentre - origin, Vector3.up).normalized;
            float distance = Vector3.Dot(Vector3.ProjectOnPlane(position - origin, Vector3.up), along);
            float local = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-50, 95, FrontProgress * routeLength - distance));
            return Mathf.Max(local, VillageCoverage);
        }

        public void ResetFog()
        {
            begun = false; FrontProgress = VillageCoverage = 0; FrontPosition = slope[0];
            foreach (var bank in banks) if (bank) bank.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        void OnDisable() => ResetFog();
        void OnDestroy() { if (material) Destroy(material); if (root) Destroy(root.gameObject); }
    }
}
