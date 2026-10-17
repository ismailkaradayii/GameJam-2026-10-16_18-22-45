using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Drives one continuous engine loop from the truck's measured Rigidbody speed.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FireTruckDrive))]
    public sealed class FireTruckEngineAudio : MonoBehaviour
    {
        private const string SfxVolumeKey = "Ashfall.SfxVolume";

        [SerializeField] private AudioSource engineSource;
        [SerializeField] private AudioClip engineLoop;
        [SerializeField, Min(0f)] private float minimumSpeedKph = 1.5f;
        [SerializeField, Min(1f)] private float fullIntensitySpeedKph = 45f;
        [SerializeField, Range(0f, 1f)] private float minimumVolume = 0.08f;
        [SerializeField, Range(0f, 1f)] private float maximumVolume = 0.55f;
        [SerializeField, Min(0.01f)] private float fadeSpeed = 1.8f;
        [SerializeField, Range(0.5f, 2f)] private float idlePitch = 0.9f;
        [SerializeField, Range(0.5f, 2f)] private float maximumPitch = 1.2f;

        private FireTruckDrive drive;
        private bool paused;

        private void Awake()
        {
            drive = GetComponent<FireTruckDrive>();
            if (engineSource == null) engineSource = GetComponent<AudioSource>();
            if (engineSource == null) engineSource = gameObject.AddComponent<AudioSource>();
            if (engineLoop != null) engineSource.clip = engineLoop;
            if (engineSource.clip != null) engineSource.clip.LoadAudioData();
            engineSource.playOnAwake = false;
            engineSource.loop = true;
            engineSource.spatialBlend = 1f;
            engineSource.dopplerLevel = 0f;
            engineSource.rolloffMode = AudioRolloffMode.Logarithmic;
            engineSource.minDistance = 4f;
            engineSource.maxDistance = 60f;
            engineSource.volume = 0f;
            engineSource.pitch = idlePitch;
            if (engineSource.clip == null)
                Debug.LogWarning("ASHFALL: FireTruckEngineAudio needs the cardrive loop assigned.", this);
        }

        private void Update()
        {
            if (engineSource == null || engineSource.clip == null || drive == null) return;
            if (Time.timeScale <= 0f)
            {
                if (!paused && engineSource.isPlaying) engineSource.Pause();
                paused = true;
                return;
            }
            if (paused) { engineSource.UnPause(); paused = false; }

            // An unattended truck can still roll or settle on its suspension.
            // Only the occupied vehicle should produce the driving loop.
            if (!drive.IsDriven)
            {
                StopEngine();
                return;
            }

            float speedKph = drive.SpeedKph;
            // A spawned truck can inherit a tiny settling velocity before the player takes control.
            // Do not start the loop until the drive controller has actually engaged the vehicle.
            float movement = !drive.IsDriven || speedKph <= minimumSpeedKph ? 0f :
                Mathf.InverseLerp(minimumSpeedKph, Mathf.Max(minimumSpeedKph + 0.1f, fullIntensitySpeedKph), speedKph);
            float sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 1f));
            float targetVolume = movement > 0f
                ? Mathf.Lerp(minimumVolume, maximumVolume, movement) * sfxVolume
                : 0f;

            if (targetVolume > 0f && !engineSource.isPlaying)
                engineSource.Play();

            engineSource.volume = Mathf.MoveTowards(engineSource.volume, targetVolume, fadeSpeed * Time.deltaTime);
            engineSource.pitch = Mathf.Lerp(idlePitch, maximumPitch, movement);
            if (targetVolume == 0f && engineSource.volume <= 0.001f && engineSource.isPlaying)
                engineSource.Stop();
        }

        private void OnDisable() => StopEngine();

        private void StopEngine()
        {
            paused = false;
            if (engineSource != null)
            {
                engineSource.volume = 0f;
                engineSource.Stop();
            }
        }
    }
}
