using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>One spatial voice per live spirit; survives neither defeat nor a pooled disable.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(LavaCreatureActor))]
    public sealed class LavaSpiritAudio : MonoBehaviour
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = .24f;
        AudioSource source;
        LavaCreatureActor actor;
        bool paused;

        void Awake()
        {
            actor = GetComponent<LavaCreatureActor>();
            var emitter = new GameObject("Spirit Audio");
            emitter.transform.SetParent(transform, false);
            source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = true;
            source.spatialBlend = 1f; source.dopplerLevel = 0f;
            source.minDistance = 2f; source.maxDistance = 28f;
            source.volume = 0f;
        }

        void Update()
        {
            if (!source || !clip || !actor || actor.IsDefeated) { StopVoice(); return; }
            if (Time.timeScale <= 0f)
            {
                if (!paused && source.isPlaying) source.Pause();
                paused = true; return;
            }
            if (paused) { source.UnPause(); paused = false; }
            if (source.clip != clip) { source.Stop(); source.clip = clip; }
            source.volume = volume * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f));
            if (!source.isPlaying && source.volume > 0f) source.Play();
        }

        void StopVoice() { paused = false; if (source) { source.Stop(); source.volume = 0f; } }
        void OnDisable() => StopVoice();
    }
}
