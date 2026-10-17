using Itfaiye;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>One physical palm strike commits the village warning and starts the authored siren emitters.</summary>
    [DisallowMultipleComponent]
    public sealed class VillageAlarmButton : MonoBehaviour
    {
        public VolcanoMissionDirector mission;
        public FireRescuePlayer player;
        public ItfaiyeFirstPersonHands hands;
        public Transform contactPoint, approachPose, movingHead;
        public Light redSpot;
        public VolcanoCameraShake cameraKick;
        public AudioSource impactSource;
        public AudioClip palmImpact, switchClick;
        public AudioSource[] villageSirens = System.Array.Empty<AudioSource>();
        [Header("Palm strike")]
        [Min(.5f)] public float maximumPressDistance = 3.5f;
        [Min(.35f)] public float slapSeconds = .72f;
        [Range(.2f, .7f)] public float contactFraction = .42f;
        [Min(.001f)] public float buttonTravelMetres = .045f;
        [Min(0f)] public float impactShake = .065f;
        [Header("Village alarm")]
        [Min(3f)] public float sirenSeconds = 32f;
        [Min(.05f)] public float sirenFadeIn = 1.4f, sirenFadeOut = 3f;
        [Range(0f, 1f)] public float sirenVolume = .65f;

        public bool Activated { get; private set; }
        public bool IsPressing { get; private set; }
        public int StrikeCount { get; private set; }
        public int AlarmStartCount { get; private set; }
        public bool SirenRunning { get; private set; }
        public float PressProgress => IsPressing ? Mathf.Clamp01((Time.time - pressStarted) / Mathf.Max(.35f, slapSeconds)) : 0f;
        public bool InReach => player && player.viewCamera && contactPoint &&
            Vector3.Distance(player.viewCamera.transform.position, contactPoint.position) <= maximumPressDistance &&
            (!hands || hands.CanReachButton(contactPoint.position));
        public bool CanPress => isActiveAndEnabled && !Activated && !IsPressing && player && player.HasPistol &&
            !player.IsDriving && !player.GameplaySuspended && (!player.hose || !player.hose.IsCarried) &&
            (!player.pistol || !player.pistol.IsReloading) && InReach;

        readonly RaycastHit[] hits = new RaycastHit[32];
        Vector3 headRest;
        float pressStarted, alarmStarted, spotIntensity;
        bool reEquip, restoreEquipment, audioPaused, announceAlarm;

        void Awake()
        {
            if (movingHead) headRest = movingHead.localPosition;
            if (redSpot) spotIntensity = redSpot.intensity;
            foreach (var source in villageSirens) if (source) { source.playOnAwake = false; source.Stop(); source.volume = 0; }
        }
        public bool TryPress()
        {
            if (!CanPress || !VisibleContact()) return false;
            reEquip = player.pistol && player.pistol.Equipped;
            IsPressing = true; pressStarted = Time.time;
            player.RefreshEquipmentState();
            if (hands) hands.BeginButtonSlap(contactPoint.position, contactPoint.up, slapSeconds, contactFraction);
            return true;
        }
        bool VisibleContact()
        {
            Vector3 delta = contactPoint.position - player.viewCamera.transform.position;
            if (Vector3.Dot(player.viewCamera.transform.forward, delta.normalized) < .72f) return false;
            int count = Physics.RaycastNonAlloc(player.viewCamera.transform.position, delta.normalized, hits,
                Mathf.Max(0, delta.magnitude - .1f), ~0, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
                if (!hits[i].transform.IsChildOf(transform) && !hits[i].transform.IsChildOf(player.transform)) return false;
            return true;
        }
        void Update()
        {
            if (IsPressing)
            {
                if (!player || player.CinematicLocked || (player.gameFlow && player.gameFlow.State != RescueGameFlow.GameState.Playing && player.gameFlow.State != RescueGameFlow.GameState.Paused))
                    EndPress();
                else if (Time.timeScale > 0)
                {
                    float progress = PressProgress;
                    if (!Activated && progress >= contactFraction) Strike();
                    if (movingHead)
                    {
                        float depth = progress < contactFraction ? 0 : progress < contactFraction + .08f ?
                            Mathf.SmoothStep(0, 1, (progress - contactFraction) / .08f) :
                            1f - Mathf.SmoothStep(0, 1, (progress - contactFraction - .08f) / .3f);
                        movingHead.localPosition = headRest + movingHead.parent.InverseTransformVector(-contactPoint.up * buttonTravelMetres * depth);
                        if (hands) hands.SetButtonSlapDepression(buttonTravelMetres * depth);
                    }
                    if (progress >= 1) EndPress();
                }
            }
            if (restoreEquipment && player && !player.GameplaySuspended && !player.IsDriving)
            {
                player.RefreshEquipmentState();
                if (reEquip && player.pistol && player.pistol.Available) player.pistol.Equip();
                restoreEquipment = false;
            }
            if (announceAlarm && !IsPressing && player && !player.GameplaySuspended && player.contextualInteractions && player.contextualInteractions.CanPresent)
            {
                player.contextualInteractions.ShowFeedback(player.contextualInteractions.words.alarmActivated, contactPoint.position + Vector3.up * .35f);
                announceAlarm = false;
            }
            TickSiren();
            if (redSpot)
                redSpot.intensity = spotIntensity * (Activated ? .2f : player && player.HasPistol ? .85f + .15f * Mathf.Sin(Time.time * 5) : .7f);
        }
        void Strike()
        {
            if (Activated) return;
            Activated = true; announceAlarm = true; StrikeCount++;
            if (impactSource)
            {
                float sfx = Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1));
                if (palmImpact) impactSource.PlayOneShot(palmImpact, .8f * sfx);
                if (switchClick) impactSource.PlayOneShot(switchClick, .75f * sfx);
            }
            if (cameraKick) cameraKick.Shake(.18f, impactShake);
            alarmStarted = Time.time; SirenRunning = true; AlarmStartCount++;
            for (int i = 0; i < villageSirens.Length; i++)
            {
                var source = villageSirens[i];
                if (!source || !source.clip) continue;
                source.volume = 0; source.Play(); source.time = i * .31f % source.clip.length;
            }
            if (mission) mission.CompleteVillageWarning();
        }
        void EndPress()
        {
            IsPressing = false;
            if (hands) hands.CancelButtonSlap();
            if (movingHead) movingHead.localPosition = headRest;
            restoreEquipment = true;
        }
        void TickSiren()
        {
            if (!SirenRunning) return;
            if (player && player.gameFlow && (player.gameFlow.State == RescueGameFlow.GameState.Won ||
                player.gameFlow.State == RescueGameFlow.GameState.Lost || player.gameFlow.ReturningToMenu)) { StopSirens(); return; }
            bool paused = Time.timeScale <= 0f || (player && player.gameFlow && player.gameFlow.State == RescueGameFlow.GameState.Paused);
            if (paused)
            {
                if (!audioPaused) foreach (var source in villageSirens) if (source) source.Pause();
                audioPaused = true; return;
            }
            if (audioPaused) { foreach (var source in villageSirens) if (source) source.UnPause(); audioPaused = false; }
            float age = Time.time - alarmStarted;
            if (age >= sirenSeconds || (player && player.gameFlow && player.gameFlow.BlocksGameplay)) { StopSirens(); return; }
            float envelope = Mathf.Min(Mathf.Clamp01(age / Mathf.Max(.05f, sirenFadeIn)), Mathf.Clamp01((sirenSeconds - age) / Mathf.Max(.05f, sirenFadeOut)));
            float volume = sirenVolume * envelope * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1));
            if (VolcanoCinematic.IsPlayingAny) volume *= .35f;
            foreach (var source in villageSirens) if (source) source.volume = volume;
        }
        void StopSirens()
        {
            foreach (var source in villageSirens) if (source) { source.Stop(); source.volume = 0; }
            SirenRunning = audioPaused = false;
        }
        void OnDisable()
        {
            StopSirens();
            if (IsPressing) EndPress();
            if (redSpot) redSpot.intensity = spotIntensity;
            if (restoreEquipment && player && !player.GameplaySuspended)
            {
                player.RefreshEquipmentState();
                if (reEquip && player.pistol && player.pistol.Available) player.pistol.Equip();
                restoreEquipment = false;
            }
        }
    }
}
