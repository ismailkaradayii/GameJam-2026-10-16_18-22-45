using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Plays one grounded footstep system from measured first-person movement.</summary>
    [DisallowMultipleComponent]
    public sealed class FireRescueMovementAudio : MonoBehaviour
    {
        private const string SfxVolumeKey = "Ashfall.SfxVolume";

        [Header("References")]
        [SerializeField] private FireRescuePlayer player;
        [SerializeField] private AudioSource footstepSource;
        [SerializeField] private AudioClip footstepClip;
        [SerializeField] private AudioClip walkingClip;
        [SerializeField] private AudioClip runningClip;

        [Header("Movement detection")]
        [SerializeField, Min(0f)] private float minimumHorizontalSpeed = 0.35f;
        [SerializeField, Range(0.1f, 1f)] private float runningSpeedThreshold = 0.65f;
        [Tooltip("Clips this long or longer are treated as a continuous footsteps loop and stop immediately on idle/airborne.")]
        [SerializeField, Min(0.2f)] private float loopClipDurationThreshold = 1.2f;

        [Header("One-shot cadence")]
        [SerializeField, Min(0.1f)] private float walkingStepInterval = 0.48f;
        [SerializeField, Min(0.1f)] private float runningStepInterval = 0.32f;
        [SerializeField, Min(0f)] private float walkingVolume = 0.55f;
        [SerializeField, Min(0f)] private float runningVolume = 0.72f;

        [Header("Loop playback")]
        [SerializeField, Range(0.5f, 2f)] private float runningLoopPitch = 1.3f;

        private float stepTimer;
        private AudioClip currentClip;

        private void Awake()
        {
            if (player == null) player = GetComponent<FireRescuePlayer>();
            if (footstepSource == null) footstepSource = GetComponent<AudioSource>();
            if (footstepSource == null) footstepSource = gameObject.AddComponent<AudioSource>();

            LoadClip(footstepClip);
            LoadClip(walkingClip);
            LoadClip(runningClip);

            footstepSource.playOnAwake = false;
            footstepSource.loop = false;
            footstepSource.spatialBlend = 0.45f;
            footstepSource.dopplerLevel = 0f;
            footstepSource.minDistance = 2f;
            footstepSource.maxDistance = 18f;
            footstepSource.volume = 0f;
        }

        private void Update()
        {
            Itfaiye.ItfaiyeFirstPersonController locomotion = player != null ? player.firstPerson : null;
            if (Time.timeScale <= 0f || player == null || player.GameplaySuspended || player.IsDriving || locomotion == null || !locomotion.IsGrounded ||
                locomotion.CurrentSpeed < minimumHorizontalSpeed)
            {
                StopForInactiveMovement();
                return;
            }

            float runThreshold = Mathf.Lerp(locomotion.walkSpeed, locomotion.runSpeed, runningSpeedThreshold);
            bool running = locomotion.CurrentSpeed >= runThreshold;
            AudioClip selectedClip = SelectClip(running);
            if (selectedClip == null)
            {
                StopForInactiveMovement();
                return;
            }

            float sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 1f));
            float stateVolume = running ? runningVolume : walkingVolume;
            float speedBlend = Mathf.InverseLerp(locomotion.walkSpeed, locomotion.runSpeed, locomotion.CurrentSpeed);

            if (IsContinuousLoop(selectedClip))
            {
                PlayContinuousLoop(selectedClip, running, Mathf.Clamp01(stateVolume * sfxVolume), speedBlend);
                return;
            }

            PrepareOneShot(selectedClip);
            stepTimer -= Time.deltaTime;
            if (stepTimer > 0f || footstepSource.isPlaying) return;

            footstepSource.pitch = Mathf.Lerp(0.96f, 1.12f, speedBlend) * Random.Range(0.97f, 1.03f);
            footstepSource.PlayOneShot(selectedClip, Mathf.Clamp01(stateVolume * sfxVolume));
            stepTimer = running ? runningStepInterval : walkingStepInterval;
        }

        private AudioClip SelectClip(bool running)
        {
            if (running)
                return runningClip != null ? runningClip : footstepClip != null ? footstepClip : walkingClip;
            return walkingClip != null ? walkingClip : footstepClip != null ? footstepClip : runningClip;
        }

        private bool IsContinuousLoop(AudioClip clip) => clip.length >= loopClipDurationThreshold;

        private void PlayContinuousLoop(AudioClip clip, bool running, float volume, float speedBlend)
        {
            if (footstepSource.isPlaying && (!footstepSource.loop || currentClip != clip))
                footstepSource.Stop();

            currentClip = clip;
            footstepSource.clip = clip;
            footstepSource.loop = true;
            footstepSource.pitch = running && clip == walkingClip ? Mathf.Lerp(1f, runningLoopPitch, speedBlend) : 1f;
            footstepSource.volume = volume;
            if (!footstepSource.isPlaying) footstepSource.Play();
        }

        private void PrepareOneShot(AudioClip clip)
        {
            footstepSource.volume = 1f;
            if (currentClip != clip) footstepSource.Stop();
            if (footstepSource.loop)
            {
                footstepSource.Stop();
                footstepSource.loop = false;
                footstepSource.clip = null;
            }

            // Let the current short step finish before scheduling the next one or swapping clips.
            if (currentClip != null && currentClip != clip && footstepSource.isPlaying) return;
            currentClip = clip;
        }

        private void StopForInactiveMovement()
        {
            stepTimer = 0f;
            if (footstepSource == null) return;

            bool activeLongLoop = footstepSource.loop ||
                (currentClip != null && IsContinuousLoop(currentClip));
            if (activeLongLoop)
            {
                footstepSource.Stop();
                footstepSource.loop = false;
                footstepSource.clip = null;
                footstepSource.volume = 0f;
                currentClip = null;
            }
            // A short one-shot already triggered is allowed to finish naturally.
        }

        private static void LoadClip(AudioClip clip)
        {
            if (clip != null) clip.LoadAudioData();
        }

        private void OnDisable()
        {
            StopForInactiveMovement();
            if (footstepSource) footstepSource.Stop();
        }
    }
}
