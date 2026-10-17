using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Timing for authored eruption effects. The eruption sequencer supplies the time step.</summary>
    [DisallowMultipleComponent]
    public sealed class VolcanoEruptionEffects : MonoBehaviour
    {
        public ParticleSystem eruptionBurst;
        public ParticleSystem lavaBombs;
        public ParticleSystem smokePlume;
        public Light eruptionLight;
        [Min(0.1f)] public float intenseBombSeconds = 8f;
        [Min(0.1f)] public float bombFadeSeconds = 5f;
        [Range(0f, 1f)] public float sustainedBombFraction = 0.08f;
        [Min(0.05f)] public float flashSeconds = 1.4f;

        public bool IsErupting { get; private set; }
        public float Elapsed { get; private set; }

        private ParticleSystem cachedBombs;
        private ParticleSystem cachedSmoke;
        private Light cachedLight;
        private float bombEmission;
        private float smokeEmission;
        private float lightIntensity;

        private void Awake() => ResetEffects();
        private void OnDisable() => ResetEffects();

        public void BeginEruption()
        {
            CacheSettings();
            IsErupting = true;
            Elapsed = 0f;
            PlayFromStart(eruptionBurst);
            PlayFromStart(lavaBombs);
            PlayFromStart(smokePlume);
            ApplyLevels();
        }

        /// <summary>Call once per frame from the eruption sequencer; this component has no automatic Update.</summary>
        public void TickEffect(float deltaTime)
        {
            if (!IsErupting || deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;
            Elapsed += deltaTime;
            ApplyLevels();
        }

        public void ResetEffects()
        {
            CacheSettings();
            IsErupting = false;
            Elapsed = 0f;
            StopAndClear(eruptionBurst);
            StopAndClear(lavaBombs);
            StopAndClear(smokePlume);
            SetEmission(lavaBombs, bombEmission);
            SetEmission(smokePlume, smokeEmission);
            if (eruptionLight != null)
            {
                eruptionLight.enabled = false;
                eruptionLight.intensity = lightIntensity;
            }
        }

        private void ApplyLevels()
        {
            CacheSettings();
            float fade = Mathf.Clamp01((Elapsed - Mathf.Max(0.1f, intenseBombSeconds)) /
                Mathf.Max(0.1f, bombFadeSeconds));
            float bombFraction = Mathf.Lerp(1f, Mathf.Clamp01(sustainedBombFraction),
                Mathf.SmoothStep(0f, 1f, fade));
            SetEmission(lavaBombs, bombEmission * bombFraction);
            // The ash column remains at its authored rate until the sequence is explicitly reset.
            SetEmission(smokePlume, smokeEmission);
            if (eruptionLight != null)
            {
                float remaining = 1f - Mathf.Clamp01(Elapsed / Mathf.Max(0.05f, flashSeconds));
                eruptionLight.intensity = lightIntensity * remaining * remaining;
                eruptionLight.enabled = remaining > 0f;
            }
        }

        private void CacheSettings()
        {
            if (lavaBombs != cachedBombs)
            {
                cachedBombs = lavaBombs;
                bombEmission = lavaBombs != null ? lavaBombs.emission.rateOverTimeMultiplier : 0f;
            }
            if (smokePlume != cachedSmoke)
            {
                cachedSmoke = smokePlume;
                smokeEmission = smokePlume != null ? smokePlume.emission.rateOverTimeMultiplier : 0f;
            }
            if (eruptionLight != cachedLight)
            {
                cachedLight = eruptionLight;
                lightIntensity = eruptionLight != null ? eruptionLight.intensity : 0f;
            }
        }

        private static void SetEmission(ParticleSystem particles, float rate)
        {
            if (particles == null)
                return;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTimeMultiplier = rate;
        }

        private static void PlayFromStart(ParticleSystem particles)
        {
            if (particles == null)
                return;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play(true);
        }

        private static void StopAndClear(ParticleSystem particles)
        {
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
