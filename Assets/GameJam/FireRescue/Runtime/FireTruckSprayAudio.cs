using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Fades the existing hose spray recording with the cannon's authoritative emission state.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FireTruckWaterCannon))]
    public sealed class FireTruckSprayAudio : MonoBehaviour
    {
        [SerializeField] private FireTruckWaterCannon cannon;
        [SerializeField] private AudioSource spraySource;
        [SerializeField] private AudioClip sprayLoop;
        [SerializeField, Range(0f, 1f)] private float volume = .65f;
        [SerializeField, Min(.01f)] private float fadeInSpeed = 8f;
        [SerializeField, Min(.01f)] private float fadeOutSpeed = 18f;
        private bool paused;

        private void Awake()
        {
            if (!cannon) cannon = GetComponent<FireTruckWaterCannon>();
            if (!spraySource)
            {
                Transform emitter = transform.Find("Water Spray Audio");
                if (!emitter)
                {
                    var child = new GameObject("Water Spray Audio");
                    emitter = child.transform;
                    emitter.SetParent(cannon && cannon.muzzle ? cannon.muzzle : transform, false);
                }
                spraySource = emitter.GetComponent<AudioSource>();
                if (!spraySource) spraySource = emitter.gameObject.AddComponent<AudioSource>();
            }
            if (sprayLoop) spraySource.clip = sprayLoop;
            if (spraySource.clip) spraySource.clip.LoadAudioData();
            spraySource.playOnAwake = false;
            spraySource.loop = true;
            spraySource.spatialBlend = 1f;
            spraySource.dopplerLevel = 0f;
            spraySource.rolloffMode = AudioRolloffMode.Logarithmic;
            spraySource.minDistance = 3f;
            spraySource.maxDistance = 50f;
            spraySource.volume = 0f;
        }

        private void Update()
        {
            if (!spraySource || !spraySource.clip) return;
            if (Time.timeScale <= 0f)
            {
                if (!paused && spraySource.isPlaying) spraySource.Pause();
                paused = true;
                return;
            }
            if (paused) { spraySource.UnPause(); paused = false; }
            bool spraying = cannon && cannon.IsSpraying && Time.timeScale > 0f;
            float target = spraying ? volume * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f)) : 0f;
            if (spraying && !spraySource.isPlaying) spraySource.Play();
            float speed = spraying ? fadeInSpeed : fadeOutSpeed;
            spraySource.volume = Mathf.MoveTowards(spraySource.volume, target, Mathf.Max(.01f, speed) * Time.deltaTime);
            if (!spraying && spraySource.volume <= .001f && spraySource.isPlaying) spraySource.Stop();
        }

        private void OnDisable()
        {
            paused = false;
            if (spraySource) { spraySource.volume = 0f; spraySource.Stop(); }
        }
    }
}
