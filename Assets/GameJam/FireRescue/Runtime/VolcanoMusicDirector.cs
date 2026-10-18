using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Scene-local score. Only its own two sources are mixed; gameplay audio keeps its authored levels.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(400)]
    public sealed class VolcanoMusicDirector : MonoBehaviour
    {
        public VolcanoMissionDirector mission;
        public AudioClip passage, horizon;
        [Header("Score levels, before the player's music setting")]
        [Range(0f, 1f)] public float passageVolume = .26f, horizonVolume = .31f;
        [Min(.1f)] public float fadeInSeconds = 3f, crossfadeSeconds = 2.8f;
        [Header("Foreground sound keeps priority; strongest reduction wins")]
        [Range(0f, 1f)] public float speechDuck = .18f, actionDuck = .42f, drivingDuck = .6f;
        [Range(0f, 1f)] public float waterDuck = .5f, alarmDuck = .22f, explosionDuck = .12f;
        [Min(.01f)] public float duckAttackSeconds = .08f, duckReleaseSeconds = 1.2f;
        [Min(.1f)] public float shotHoldSeconds = .45f;

        public AudioSource PassageSource { get; private set; }
        public AudioSource HorizonSource { get; private set; }
        public bool IsHorizon { get; private set; }
        public float CurrentDuck { get; private set; } = 1f;
        public float TargetDuck { get; private set; } = 1f;
        public string MixReason { get; private set; } = "Calm";

        AudioSource[] foregroundSources = System.Array.Empty<AudioSource>();
        WindAudioPulse eruptionAudio;
        FireRescueMovementAudio movementAudio;
        float fadeIn, transition, shotHold, foregroundHold, cacheAt;
        int previousShots;
        bool paused, started;

        bool SpiritBriefing => mission && mission.cinematic && mission.cinematic.IsPlaying &&
            mission.cinematic.Kind == VolcanoCinematic.ShotKind.Spirits && mission.cinematic.SpiritFrozen;

        void Awake()
        {
            if (!mission) mission = GetComponent<VolcanoMissionDirector>();
            PassageSource = CreateSource("Passage - preparation score", passage);
            HorizonSource = CreateSource("Horizon - eruption score", horizon);
        }

        AudioSource CreateSource(string label, AudioClip clip)
        {
            var emitter = new GameObject(label);
            emitter.transform.SetParent(transform, false);
            var source = emitter.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.reverbZoneMix = 0f;
            source.priority = 210;
            source.volume = 0f;
            return source;
        }

        void Start() => StartScore();
        void OnEnable() { if (started) StartScore(); }
        void LateUpdate() => TickMusic(Time.unscaledDeltaTime);

        void StartScore()
        {
            started = true;
            paused = false;
            fadeIn = transition = shotHold = foregroundHold = 0f;
            CurrentDuck = TargetDuck = 1f;
            IsHorizon = false;
            cacheAt = -1f;
            if (PassageSource) { PassageSource.volume = 0f; if (passage) PassageSource.Play(); }
            if (HorizonSource) { HorizonSource.volume = 0f; HorizonSource.Stop(); }
            previousShots = mission && mission.player && mission.player.pistol ? mission.player.pistol.ShotsFired : 0;
        }

        public void TickMusic(float deltaTime)
        {
            if (!started || !isActiveAndEnabled || !mission || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            var flow = mission.gameFlow;
            bool shouldPause = flow && flow.State == RescueGameFlow.GameState.Paused ||
                Time.timeScale <= 0f && (!flow || !flow.HasResult) && !SpiritBriefing;
            if (shouldPause)
            {
                if (!paused) { PassageSource.Pause(); HorizonSource.Pause(); paused = true; }
                return;
            }
            if (paused) { PassageSource.UnPause(); HorizonSource.UnPause(); paused = false; }

            // The warning sound leads the explosion. Change the score at the visible burst itself.
            bool erupted = mission.sequence ? mission.sequence.Stage == VolcanoEruptionSequence.EruptionStage.Erupting ||
                mission.sequence.Stage == VolcanoEruptionSequence.EruptionStage.Flowing : mission.EruptionTriggered;
            if (!IsHorizon && erupted && horizon)
            {
                IsHorizon = true;
                HorizonSource.volume = 0f;
                HorizonSource.Play();
            }
            fadeIn = Mathf.MoveTowards(fadeIn, 1f, deltaTime / Mathf.Max(.1f, fadeInSeconds));
            if (IsHorizon) transition = Mathf.MoveTowards(transition, 1f, deltaTime / Mathf.Max(.1f, crossfadeSeconds));

            if (Time.unscaledTime >= cacheAt)
            {
                // One-shots created by the existing gameplay systems are cached, never searched per frame.
                foregroundSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (!eruptionAudio) eruptionAudio = FindFirstObjectByType<WindAudioPulse>();
                if (!movementAudio && mission.player) movementAudio = mission.player.GetComponent<FireRescueMovementAudio>();
                cacheAt = Time.unscaledTime + .75f;
            }
            SelectForeground(deltaTime);
            float smoothing = TargetDuck < CurrentDuck ? duckAttackSeconds : duckReleaseSeconds;
            CurrentDuck = Mathf.MoveTowards(CurrentDuck, TargetDuck, deltaTime / Mathf.Max(.01f, smoothing));
            float musicSetting = Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.MusicVolume", .75f));
            float resultGain = flow && flow.HasResult ? .45f : flow && flow.IsVictoryCinematic ? .65f : 1f;
            float gain = musicSetting * fadeIn * CurrentDuck * resultGain;
            // Equal-power blend avoids a dip between the two different scores.
            PassageSource.volume = passageVolume * Mathf.Cos(transition * Mathf.PI * .5f) * gain;
            HorizonSource.volume = horizonVolume * Mathf.Sin(transition * Mathf.PI * .5f) * gain;
            if (transition >= 1f && PassageSource.isPlaying) PassageSource.Stop();
        }

        void SelectForeground(float deltaTime)
        {
            TargetDuck = 1f; MixReason = "Calm";
            // Only the world is frozen for this reading card. Keep the score moving at its regular level.
            if (SpiritBriefing)
            {
                shotHold = foregroundHold = 0f;
                MixReason = "Spirit briefing";
                return;
            }
            var player = mission.player;
            var ending = mission.gameFlow ? mission.gameFlow.victoryCinematic : null;
            bool speaking = mission.story && !string.IsNullOrEmpty(mission.story.CurrentLine) ||
                mission.evacuation && mission.evacuation.dialogue && mission.evacuation.dialogue.IsShowingLine ||
                ending && ending.IsPlaying && !string.IsNullOrEmpty(ending.CurrentLine);
            if (speaking) Duck(speechDuck, "Dialogue");

            shotHold = Mathf.Max(0f, shotHold - deltaTime);
            foregroundHold = Mathf.Max(0f, foregroundHold - deltaTime);
            bool sfxAudible = PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f) > .001f;
            if (player && player.pistol)
            {
                if (player.pistol.ShotsFired != previousShots && sfxAudible)
                {
                    shotHold = shotHoldSeconds;
                    // The attack of a shot gets room on the very frame the actual shot is committed.
                    CurrentDuck = Mathf.Min(CurrentDuck, actionDuck);
                }
                previousShots = player.pistol.ShotsFired;
            }
            if (!sfxAudible) return;
            if (shotHold > 0f || player && player.pistol && player.pistol.IsReloading) Duck(actionDuck, "Weapon");
            if (player && player.drive && player.drive.IsDriven && player.drive.SpeedKph > 1.5f)
                Duck(Mathf.Lerp(.8f, drivingDuck, Mathf.InverseLerp(1.5f, 45f, player.drive.SpeedKph)), "Engine");
            if (player && (player.cannon && player.cannon.IsSpraying || player.hose && player.hose.IsPumping)) Duck(waterDuck, "Water");
            if (mission.villageAlarm && mission.villageAlarm.SirenRunning) Duck(alarmDuck, "Siren");
            if (eruptionAudio && eruptionAudio.IsExplosionAudible) Duck(explosionDuck, "Explosion");
            if (AudibleOneShot()) foregroundHold = .25f;
            if (foregroundHold > 0f) Duck(actionDuck, "Foreground effect");
        }

        bool AudibleOneShot()
        {
            var listener = mission.player ? mission.player.viewCamera : null;
            if (!listener) return false;
            foreach (var source in foregroundSources)
            {
                if (!source || source == PassageSource || source == HorizonSource || !source.enabled ||
                    !source.isPlaying || source.loop || source.mute || source.volume <= .025f) continue;
                // Ambient footsteps/fire/wind must not hold the score down throughout the entire mission.
                if (movementAudio && source == movementAudio.FootstepSource) continue;
                float distance = Vector3.Distance(source.transform.position, listener.transform.position);
                float attenuation = distance >= source.maxDistance ? 0f : source.rolloffMode == AudioRolloffMode.Linear ?
                    1f - Mathf.InverseLerp(source.minDistance, source.maxDistance, distance) :
                    Mathf.Min(1f, source.minDistance / Mathf.Max(source.minDistance, distance));
                if (source.volume * Mathf.Lerp(1f, attenuation, source.spatialBlend) > .025f) return true;
            }
            return false;
        }

        void Duck(float amount, string reason)
        {
            if (amount >= TargetDuck) return;
            TargetDuck = Mathf.Clamp01(amount); MixReason = reason;
        }

        void OnDisable()
        {
            if (PassageSource) PassageSource.Stop();
            if (HorizonSource) HorizonSource.Stop();
            paused = false;
        }
    }
}
