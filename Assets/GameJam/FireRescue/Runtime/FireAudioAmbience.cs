using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>One shared fire loop whose level follows all currently burning buildings.</summary>
    [DisallowMultipleComponent]
    public sealed class FireAudioAmbience : MonoBehaviour
    {
        private const string SfxVolumeKey = "Ashfall.SfxVolume";
        private static FireAudioAmbience activeInstance;

        [SerializeField] private AudioSource ambienceSource;
        [SerializeField] private AudioClip fireLoop;
        [SerializeField, Range(0f, 1f)] private float maximumVolume = 0.65f;
        [SerializeField, Min(0.01f)] private float fadeSpeed = 1f;
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 0f;

        private float targetVolume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => activeInstance = null;

        private void Awake()
        {
            if (activeInstance != null && activeInstance != this)
            {
                Destroy(gameObject);
                return;
            }
            activeInstance = this;

            if (ambienceSource == null) ambienceSource = GetComponent<AudioSource>();
            if (ambienceSource == null) ambienceSource = gameObject.AddComponent<AudioSource>();
            if (fireLoop != null) ambienceSource.clip = fireLoop;
            if (ambienceSource.clip != null) ambienceSource.clip.LoadAudioData();
            ambienceSource.playOnAwake = false;
            ambienceSource.loop = true;
            ambienceSource.spatialBlend = spatialBlend;
            ambienceSource.volume = 0f;
            ambienceSource.dopplerLevel = 0f;
            if (ambienceSource.clip == null)
                Debug.LogWarning("ASHFALL: FireAudioAmbience needs the fire loop assigned.", this);
        }

        private void OnEnable()
        {
            BurnableBuilding.FireStateChanged += RefreshTargetVolume;
            RefreshTargetVolume();
        }

        private void Update()
        {
            if (ambienceSource == null || ambienceSource.clip == null) return;
            float sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 1f));
            float effectiveTarget = targetVolume * sfxVolume;
            if (effectiveTarget > 0f && !ambienceSource.isPlaying)
                ambienceSource.Play();

            ambienceSource.volume = Mathf.MoveTowards(ambienceSource.volume, effectiveTarget, fadeSpeed * Time.deltaTime);
            if (effectiveTarget == 0f && ambienceSource.volume <= 0.001f && ambienceSource.isPlaying)
                ambienceSource.Stop();
        }

        private void RefreshTargetVolume()
        {
            int burningCount = 0;
            foreach (BurnableBuilding building in BurnableBuilding.ActiveBuildings)
                if (building != null && building.isActiveAndEnabled && building.IsBurning) burningCount++;

            float countBlend = Mathf.Clamp01((burningCount - 1) / 3f);
            targetVolume = burningCount == 0 ? 0f : maximumVolume * Mathf.Lerp(0.55f, 1f, countBlend);
        }

        private void OnDisable()
        {
            BurnableBuilding.FireStateChanged -= RefreshTargetVolume;
            if (ambienceSource != null) ambienceSource.Stop();
        }

        private void OnDestroy()
        {
            if (activeInstance == this) activeInstance = null;
        }
    }
}
