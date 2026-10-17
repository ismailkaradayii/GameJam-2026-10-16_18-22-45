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
        public ParticleSystem ashCrown;
        public ParticleSystem fireFountain;
        [Min(0f)] public float ashCrownDelay = 1.1f;
        [Min(.1f)] public float fireFountainSeconds = 6.5f, fireFountainFadeSeconds = 2.5f;
        [Range(0f, .5f)] public float eruptionGlowFraction = .12f;
        public Light eruptionLight;
        [Min(0.1f)] public float intenseBombSeconds = 8f;
        [Min(0.1f)] public float bombFadeSeconds = 5f;
        [Range(0f, 1f)] public float sustainedBombFraction = 0.08f;
        [Min(0.05f)] public float flashSeconds = 1.4f;

        public bool IsErupting { get; private set; }
        public float Elapsed { get; private set; }

        private ParticleSystem cachedBombs;
        private ParticleSystem cachedSmoke;
        private ParticleSystem cachedFountain;
        private Light cachedLight;
        private float bombEmission;
        private float smokeEmission;
        private float lightIntensity;
        private float fountainEmission;

        private void Awake() => ResetEffects();
        private void OnDisable() => ResetEffects();

        public void BeginEruption()
        {
            if (IsErupting) return;
            CacheSettings();
            IsErupting = true;
            Elapsed = 0f;
            PlayFromStart(eruptionBurst);
            PlayFromStart(lavaBombs);
            PlayFromStart(smokePlume);
            PlayFromStart(fireFountain);
            if (ashCrownDelay <= 0f) PlayFromStart(ashCrown);
            ApplyLevels();
        }

        /// <summary>Call once per frame from the eruption sequencer; this component has no automatic Update.</summary>
        public void TickEffect(float deltaTime)
        {
            if (!IsErupting || deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;
            float before = Elapsed;
            Elapsed += deltaTime;
            if (ashCrownDelay > 0f && before < ashCrownDelay && Elapsed >= ashCrownDelay) PlayFromStart(ashCrown);
            // Two smaller pressure pulses give the rising column a rolling, uneven source.
            if (eruptionBurst && ((before < .75f && Elapsed >= .75f) || (before < 1.8f && Elapsed >= 1.8f)))
                eruptionBurst.Emit(10);
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
            StopAndClear(ashCrown);
            StopAndClear(fireFountain);
            SetEmission(lavaBombs, bombEmission);
            SetEmission(smokePlume, smokeEmission);
            SetEmission(fireFountain, fountainEmission);
            if (fireFountain) { var emission = fireFountain.emission; emission.enabled = true; }
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
            SetEmission(smokePlume, smokeEmission * Mathf.Lerp(1f, .55f, fade));
            float fountainFraction = 1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01((Elapsed - fireFountainSeconds) / Mathf.Max(.1f, fireFountainFadeSeconds)));
            SetEmission(fireFountain, fountainEmission * fountainFraction);
            if (fireFountain) { var emission = fireFountain.emission; emission.enabled = fountainFraction > 0f; }
            if (eruptionLight != null)
            {
                float remaining = 1f - Mathf.Clamp01(Elapsed / Mathf.Max(0.05f, flashSeconds));
                float glow = eruptionGlowFraction * fountainFraction * (.85f + .15f * Mathf.Sin(Elapsed * 17f));
                eruptionLight.intensity = lightIntensity * Mathf.Max(remaining * remaining, glow);
                eruptionLight.enabled = eruptionLight.intensity > .01f;
            }
        }

        private void CacheSettings()
        {
            if (fireFountain != cachedFountain)
            {
                cachedFountain = fireFountain;
                fountainEmission = fireFountain ? fireFountain.emission.rateOverTimeMultiplier : 0f;
            }
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
