using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Sequences the pre-impact explosion sound into post-impact wind ambience.</summary>
    [DisallowMultipleComponent]
    public sealed class WindAudioPulse : MonoBehaviour
    {
        private const string SfxVolumeKey = "Ashfall.SfxVolume";
        public const float ExplosionLeadSeconds = 1f;

        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip explosionSound;
        [SerializeField] private AudioClip windLoop;
        [SerializeField] private bool waitForExplosionSequence = true;
        [SerializeField, Range(0f, 1f)] private float explosionVolume = 0.85f;
        [SerializeField, Range(0f, 1f)] private float minimumVolume = 0.08f;
        [SerializeField, Range(0f, 1f)] private float maximumVolume = 0.42f;
        [SerializeField, Min(0.05f)] private float explosionFadeDuration = 0.8f;
        [SerializeField, Min(0.05f)] private float windFadeDuration = 2f;
        [SerializeField, Min(0f)] private float postExplosionSeconds = 4f;
        [SerializeField, Min(1f)] private float shortestSwellingInterval = 8f;
        [SerializeField, Min(1f)] private float longestSwellingInterval = 15f;
        [SerializeField, Min(0.1f)] private float swellDuration = 4f;
        [SerializeField, Range(0f, 0.5f)] private float volumeVariation = 0.04f;

        private enum PlaybackState { Waiting, Explosion, PostExplosion, FadingExplosion, FadingWind, Wind }
        private PlaybackState state = PlaybackState.Waiting;
        private bool explosionStarted;
        private bool explosionEventReceived;
        private bool warnedAboutExplosionClip;
        private float postExplosionElapsed;
        private float effectivePostExplosionSeconds;
        private float fadeElapsed;
        private float phase;
        private float nextSwellAt;
        private float swellCenter;
        private float swellStrength;
        private float phaseRate;
        private bool sequencedMode;
        private bool pausedForTimeScale;

        private void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.Stop();
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            if (windLoop != null) windLoop.LoadAudioData();
            phaseRate = Random.Range(0.28f, 0.48f);
            state = PlaybackState.Waiting;
            sequencedMode = waitForExplosionSequence;
            if (!sequencedMode) StartWind();
        }

        private void Update()
        {
            if (source != null && Time.timeScale <= 0f)
            {
                if (!pausedForTimeScale && source.isPlaying) source.Pause();
                pausedForTimeScale = true;
                return;
            }
            if (pausedForTimeScale)
            {
                if (source != null) source.UnPause();
                pausedForTimeScale = false;
            }
            if (!sequencedMode) TickSequence(Time.deltaTime);
        }

        /// <summary>Resets the sound timeline when the gameplay countdown is initialized.</summary>
        public void ResetSequence()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (source != null)
            {
                source.Stop();
                source.clip = windLoop;
                source.loop = true;
                source.volume = 0f;
            }
            state = PlaybackState.Waiting;
            sequencedMode = true;
            explosionStarted = false;
            explosionEventReceived = false;
            pausedForTimeScale = false;
            postExplosionElapsed = 0f;
            effectivePostExplosionSeconds = Mathf.Max(0f, postExplosionSeconds);
            fadeElapsed = 0f;
            nextSwellAt = 0f;
        }

        /// <summary>Starts the assigned explosion clip once before the eruption cinematic.</summary>
        public void BeginExplosionSound()
        {
            if (explosionStarted || state != PlaybackState.Waiting) return;
            explosionStarted = true;
            if (source == null) source = GetComponent<AudioSource>();
            if (explosionSound == null)
            {
                if (!warnedAboutExplosionClip)
                {
                    Debug.LogWarning("ASHFALL: Explosion audio is not assigned. Add the existing explode clip to WindAudioAmbience > Wind Audio Pulse.", this);
                    warnedAboutExplosionClip = true;
                }
                return;
            }

            if (source == null) return;
            source.Stop();
            source.clip = explosionSound;
            explosionSound.LoadAudioData();
            // Keep the explosion audible through the cinematic and its transition.
            source.loop = true;
            source.volume = Mathf.Clamp01(explosionVolume * GetSfxVolume());
            source.Play();
            state = PlaybackState.Explosion;
        }

        /// <summary>Called at the authoritative gameplay impact event, not when its warning sound starts.</summary>
        public void NotifyExplosionEvent(float minimumPostSeconds = 0f)
        {
            if (explosionEventReceived) return;
            explosionEventReceived = true;
            postExplosionElapsed = 0f;
            effectivePostExplosionSeconds = Mathf.Max(postExplosionSeconds, minimumPostSeconds);
            if (state == PlaybackState.Explosion || state == PlaybackState.Waiting)
                state = PlaybackState.PostExplosion;
        }

        /// <summary>Called by the eruption sequence so pause and time scale affect the sound timer consistently.</summary>
        public void TickSequence(float deltaTime)
        {
            if (source == null || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            if (state == PlaybackState.Explosion || state == PlaybackState.PostExplosion)
                source.volume = explosionVolume * GetSfxVolume();

            if (state == PlaybackState.PostExplosion)
            {
                postExplosionElapsed += deltaTime;
                if (postExplosionElapsed >= effectivePostExplosionSeconds)
                {
                    fadeElapsed = 0f;
                    state = source.isPlaying ? PlaybackState.FadingExplosion : PlaybackState.FadingWind;
                    if (state == PlaybackState.FadingWind) StartWind();
                }
            }

            if (state == PlaybackState.FadingExplosion)
            {
                fadeElapsed += deltaTime;
                float t = Mathf.Clamp01(fadeElapsed / Mathf.Max(0.05f, explosionFadeDuration));
                source.volume = Mathf.Lerp(explosionVolume * GetSfxVolume(), 0f, t);
                if (t >= 1f)
                {
                    source.Stop();
                    StartWind();
                }
            }
            else if (state == PlaybackState.FadingWind)
            {
                fadeElapsed += deltaTime;
                float t = Mathf.Clamp01(fadeElapsed / Mathf.Max(0.05f, windFadeDuration));
                source.volume = Mathf.Lerp(0f, WindTargetVolume(0f), t);
                if (t >= 1f) state = PlaybackState.Wind;
            }
            else if (state == PlaybackState.Wind)
            {
                if (!source.isPlaying) source.Play();
                if (Time.time >= nextSwellAt) ScheduleSwell(Time.time);

                float halfDuration = swellDuration * 0.5f;
                float distance = Mathf.Abs(Time.time - swellCenter);
                float swell = distance >= halfDuration ? 0f : 0.5f + 0.5f * Mathf.Cos(Mathf.PI * distance / halfDuration);
                phase += deltaTime * phaseRate;
                float variation = Mathf.Sin(phase) * volumeVariation;
                float target = WindTargetVolume(swell + variation);
                source.volume = Mathf.MoveTowards(source.volume, target, deltaTime * 0.14f);
            }
        }

        private void StartWind()
        {
            if (source == null) return;
            source.Stop();
            if (windLoop == null)
            {
                Debug.LogWarning("ASHFALL: Wind ambience clip is missing; post-explosion ambience cannot start.", this);
                state = PlaybackState.Waiting;
                return;
            }
            source.clip = windLoop;
            source.loop = true;
            source.volume = 0f;
            source.Play();
            fadeElapsed = 0f;
            ScheduleSwell(Time.time);
            state = PlaybackState.FadingWind;
        }

        private float WindTargetVolume(float modulation)
        {
            float target = Mathf.Clamp(minimumVolume + swellStrength * Mathf.Clamp01(modulation) + modulation * volumeVariation, 0f, maximumVolume);
            return target * GetSfxVolume();
        }

        private static float GetSfxVolume() => Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 1f));

        private void ScheduleSwell(float now)
        {
            swellCenter = now + swellDuration * 0.5f;
            swellStrength = Mathf.Max(0f, maximumVolume - minimumVolume) * Random.Range(0.68f, 1f);
            nextSwellAt = now + Random.Range(shortestSwellingInterval, Mathf.Max(shortestSwellingInterval, longestSwellingInterval));
        }

        private void OnDisable()
        {
            ResetSequence();
        }
    }
}
