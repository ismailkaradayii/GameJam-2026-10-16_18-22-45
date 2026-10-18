using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Bedroom foreshadowing and one-shot inner thoughts layered over the existing mission.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-55)]
    public sealed class VolcanoStorySequence : MonoBehaviour
    {
        public enum Thought { Approach, Unease, Omen, Foreseen, Weapon, Shelter, Truck, Lake, Return, Hold }
        public VolcanoMissionDirector mission;
        [Header("Authored bedroom window and village defense position")]
        public Transform windowStop, volcanoLookTarget, defensePosition;
        public Transform[] windowRoute = System.Array.Empty<Transform>();
        [Tooltip("A small lean toward the lower window pane; blended out before returning control.")]
        public Vector3 windowEyeOffset = new Vector3(-2.2f, -2.2f, 0);
        [Range(35f, 80f)] public float windowFieldOfView = 50f;
        [Min(.1f)] public float approachSpeed = 2.8f;
        [Min(1f)] public float approachTimeout = 12f;
        [Min(.1f)] public float arrivalRadius = .28f;
        [Min(1f)] public float defenseRadius = 14f;
        [Header("Independent, small signs of activity; never starts the eruption")]
        public ParticleSystem omenSmoke, omenSparks;
        [Min(0f)] public float omenDelay = 3.2f;
        [Min(0f)] public float recognitionDelay = 6.5f;
        [Min(0f)] public float observationSeconds = 9.8f;
        public Font subtitleFont;
        [Header("Opening voice: approach, unease, omen, recognition, weapon")]
        [Tooltip("Empty entries load talk01 through talk05 from Resources/ASHFALL/Audio/Opening.")]
        public AudioClip[] openingVoiceClips = new AudioClip[5];
        [Range(0f, 1f)] public float voiceVolume = .9f;
        [Min(0f)] public float voiceLineGap = .25f;

        public bool OpeningStarted { get; private set; }
        public bool OpeningComplete { get; private set; }
        public bool IsOpening { get; private set; }
        public bool AtWindow { get; private set; }
        public bool OmenPlayed { get; private set; }
        public bool OwnsMovement => ownsMovement;
        public float ObservationElapsed { get; private set; }
        public string CurrentLine => lineRemaining > 0f ? currentLine : string.Empty;
        public Thought CurrentThought { get; private set; }
        public bool HasSpoken(Thought thought) => spoken.Contains(thought);
        public float OmenAt => Mathf.Max(omenDelay, VoiceDuration(Thought.Unease));
        public float RecognitionAt => Mathf.Max(recognitionDelay, OmenAt + VoiceDuration(Thought.Omen));
        public float ObservationDuration => Mathf.Max(observationSeconds, RecognitionAt + VoiceDuration(Thought.Foreseen));

        struct Line
        {
            public Thought thought;
            public string text;
            public float seconds;
            public AudioClip voice;
        }
        readonly Queue<Line> pending = new Queue<Line>();
        readonly HashSet<Thought> spoken = new HashSet<Thought>();
        string currentLine;
        float lineRemaining, approachElapsed;
        int waypoint;
        bool ownsMovement, stoppedForEruption;
        Vector3 appliedViewOffset;
        Vector3 baseViewLocalPosition;
        Quaternion smoothViewRotation;
        float openingFov;
        AudioSource voiceSource;
        bool voicePaused;
        bool VoicePlaying => voiceSource && (voiceSource.isPlaying || voicePaused);
        FireRescuePlayer Player => mission ? mission.player : null;

        void Awake()
        {
            if (!mission) mission = GetComponent<VolcanoMissionDirector>();
            if (openingVoiceClips == null || openingVoiceClips.Length < 5)
                System.Array.Resize(ref openingVoiceClips, 5);
            for (int i = 0; i < 5; i++)
                if (!openingVoiceClips[i]) openingVoiceClips[i] = Resources.Load<AudioClip>("ASHFALL/Audio/Opening/talk" + (i + 1).ToString("00"));
            StopOmen();
        }

        public bool BeginOpening()
        {
            if (OpeningStarted || !Player || Player.IsDriving || !windowStop || !volcanoLookTarget) return false;
            OpeningStarted = IsOpening = true;
            openingFov = Player.viewCamera.fieldOfView;
            baseViewLocalPosition = Player.viewCamera.transform.localPosition;
            smoothViewRotation = Player.viewCamera.transform.rotation;
            Player.SetCinematicLocked(true); Player.RefreshEquipmentState(); ownsMovement = true;
            Speak(Thought.Approach, "I can't shake this feeling... I'll look out the window.", 6f, true);
            return true;
        }

        void Update()
        {
            RemoveViewOffset();
            if (!mission || !Player) return;
            if (mission.EruptionTriggered)
            {
                if (!stoppedForEruption) { CancelOpening(); stoppedForEruption = true; }
                return;
            }
            bool suspended = Time.timeScale <= 0f || (mission.gameFlow && mission.gameFlow.BlocksGameplay) || VolcanoCinematic.IsPlayingAny;
            PauseVoice(suspended);
            if (suspended) return;
            lineRemaining = Mathf.Max(0, lineRemaining - Time.deltaTime);
            if (lineRemaining <= 0 && pending.Count > 0) Present(pending.Dequeue());
            if (IsOpening) TickOpening();
            else if (OpeningComplete) TickThoughts();
        }

        void LateUpdate()
        {
            if (!IsOpening || !ownsMovement || !Player || !Player.viewCamera || VolcanoCinematic.IsPlayingAny) return;
            // Lean only after the body has stopped, rather than pushing the lens sideways into
            // bedroom geometry while the character is still negotiating the approach.
            float approach = AtWindow ? Mathf.SmoothStep(0, 1, ObservationElapsed / 1.6f) : 0f;
            float leave = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(ObservationDuration - 1.2f, ObservationDuration, ObservationElapsed));
            var view = Player.viewCamera;
            baseViewLocalPosition = view.transform.localPosition;
            appliedViewOffset = windowEyeOffset * (approach * leave);
            Vector3 origin = view.transform.position;
            float length = appliedViewOffset.magnitude;
            if (length > .001f)
                foreach (var hit in Physics.SphereCastAll(origin, .08f, appliedViewOffset / length, length, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.transform.IsChildOf(Player.transform))
                        length = Mathf.Min(length, Mathf.Max(0, hit.distance - .1f));
            appliedViewOffset = appliedViewOffset.normalized * length;
            view.transform.position = origin + appliedViewOffset;
            // Aim from the final lens position, not from the unshifted player's eye.
            if (AtWindow)
            {
                Quaternion focus = Quaternion.LookRotation(volcanoLookTarget.position - view.transform.position);
                view.transform.rotation = Quaternion.Slerp(smoothViewRotation, focus, approach * leave);
            }
            view.fieldOfView = Mathf.Lerp(openingFov, windowFieldOfView, approach * leave);
        }
        void RemoveViewOffset()
        {
            if (Player && Player.viewCamera && appliedViewOffset.sqrMagnitude > 0f)
            {
                Player.viewCamera.transform.localPosition = baseViewLocalPosition;
                Player.viewCamera.fieldOfView = openingFov;
            }
            appliedViewOffset = Vector3.zero;
        }

        void TickOpening()
        {
            float dt = Time.deltaTime;
            if (!AtWindow)
            {
                approachElapsed += dt;
                while (waypoint < windowRoute.Length && (!windowRoute[waypoint] ||
                    EvacuationResident.FlatDistance(Player.transform.position, windowRoute[waypoint].position) <= arrivalRadius)) waypoint++;
                Transform destination = waypoint < windowRoute.Length ? windowRoute[waypoint] : windowStop;
                float distance = EvacuationResident.FlatDistance(Player.transform.position, destination.position);
                if (waypoint >= windowRoute.Length && distance <= arrivalRadius)
                {
                    AtWindow = true;
                    if (!ownsMovement) { Player.SetCinematicLocked(true); Player.RefreshEquipmentState(); ownsMovement = true; }
                }
                else
                {
                    if (ownsMovement)
                    {
                        Vector3 focus = distance < 3f && waypoint >= windowRoute.Length ? volcanoLookTarget.position :
                            destination.position + Vector3.up * (Player.viewCamera.transform.position.y - Player.transform.position.y);
                        Quaternion look = Quaternion.LookRotation(focus - Player.viewCamera.transform.position);
                        smoothViewRotation = Quaternion.Slerp(smoothViewRotation, look, 1f - Mathf.Exp(-2.7f * dt));
                        Player.SimulateCinematicWalk(destination.position,
                            smoothViewRotation, approachSpeed);
                        // A moved prop must never leave the player permanently locked. The same window
                        // becomes a normal guided destination until the player walks into position.
                        if (approachElapsed >= approachTimeout) ReleaseMovement();
                    }
                    return;
                }
            }
            Quaternion facing = Quaternion.LookRotation(volcanoLookTarget.position - Player.viewCamera.transform.position);
            smoothViewRotation = Quaternion.Slerp(smoothViewRotation, facing, 1f - Mathf.Exp(-3f * dt));
            Player.SimulateCinematicWalk(Player.transform.position,
                smoothViewRotation, 0f);
            if (!HasSpoken(Thought.Unease))
            {
                // A short route must not cut off the first line on arrival.
                if (VoiceFor(Thought.Approach) && (lineRemaining > 0f || VoicePlaying)) return;
                Speak(Thought.Unease, "Something is wrong with the volcano... It looks different today.", OmenAt, true);
            }
            ObservationElapsed += dt;
            if (!OmenPlayed && ObservationElapsed >= OmenAt && !VoicePlaying)
            {
                OmenPlayed = true;
                PlayOmen(omenSmoke); PlayOmen(omenSparks);
                Speak(Thought.Omen, "Smoke... and sparks. That's not a good sign.", RecognitionAt - OmenAt, true);
            }
            if (OmenPlayed && ObservationElapsed >= RecognitionAt && !VoicePlaying)
                Speak(Thought.Foreseen, "I knew this day would come.", ObservationDuration - RecognitionAt, true);
            if (HasSpoken(Thought.Foreseen) && ObservationElapsed >= ObservationDuration && !VoicePlaying)
            {
                IsOpening = false; OpeningComplete = true; ReleaseMovement();
                Speak(Thought.Weapon, "I need my weapon first. Then I must sound the siren.", 5f, true);
            }
        }

        void TickThoughts()
        {
            if (mission.WarningCompleted && !Player.InteractionBusy)
                Speak(Thought.Shelter, "I must lead the townspeople to the shelter.", 4.8f);
            if (mission.evacuation && mission.evacuation.IsComplete)
                Speak(Thought.Truck, "Everyone is safe. I should take the fire truck and check its water.", 5.5f);
            if (mission.Phase == VolcanoMissionDirector.MissionPhase.Refill)
                Speak(Thought.Lake, "I need to refill the tank. I should drive the truck to the lake.", 5f);
            if (mission.PreparationComplete)
            {
                Speak(Thought.Return, "I should head back and prepare where the lava will reach the town.", 6.5f, true);
                if (defensePosition && EvacuationResident.FlatDistance(Player.IsDriving && Player.drive ?
                    Player.drive.transform.position : Player.transform.position, defensePosition.position) <= defenseRadius)
                    Speak(Thought.Hold, "I'll wait here. I'm ready to protect the town.", 4.5f);
            }
        }

        void Speak(Thought thought, string text, float seconds, bool interrupt = false)
        {
            if (!spoken.Add(thought)) return;
            var clip = VoiceFor(thought);
            var line = new Line { thought = thought, text = text, voice = clip,
                seconds = clip ? clip.length + voiceLineGap : Mathf.Max(1f, seconds) };
            if (interrupt) { pending.Clear(); Present(line); }
            else if (lineRemaining <= 0) Present(line);
            else pending.Enqueue(line);
        }
        void Present(Line line)
        {
            StopVoice();
            currentLine = line.text; lineRemaining = line.seconds; CurrentThought = line.thought;
            if (!line.voice) return;
            if (!voiceSource)
            {
                var voice = new GameObject("Opening inner monologue"); voice.transform.SetParent(transform, false);
                voiceSource = voice.AddComponent<AudioSource>();
                voiceSource.playOnAwake = false; voiceSource.loop = false; voiceSource.spatialBlend = 0f;
                voiceSource.priority = 40; voiceSource.reverbZoneMix = 0f; voiceSource.dopplerLevel = 0f;
            }
            voiceSource.volume = voiceVolume; voiceSource.clip = line.voice; voiceSource.Play();
        }
        AudioClip VoiceFor(Thought thought)
        {
            int index = (int)thought;
            return openingVoiceClips != null && index >= 0 && index < 5 && index < openingVoiceClips.Length ? openingVoiceClips[index] : null;
        }
        float VoiceDuration(Thought thought) => VoiceFor(thought) ? VoiceFor(thought).length + voiceLineGap : 0f;
        void PauseVoice(bool paused)
        {
            if (!voiceSource) return;
            if (paused && voiceSource.isPlaying) { voiceSource.Pause(); voicePaused = true; }
            else if (!paused && voicePaused) { voiceSource.UnPause(); voicePaused = false; }
        }
        void StopVoice()
        {
            if (voiceSource) { voiceSource.Stop(); voiceSource.clip = null; }
            voicePaused = false;
        }

        public void CancelOpening()
        {
            RemoveViewOffset();
            IsOpening = false;
            pending.Clear(); currentLine = string.Empty; lineRemaining = 0;
            StopVoice(); StopOmen(); ReleaseMovement();
        }
        void ReleaseMovement()
        {
            if (!ownsMovement) return;
            // An interrupting eruption camera owns its own input lock.
            if (Player && (!mission.cinematic || !mission.cinematic.IsPlaying)) Player.SetCinematicLocked(false);
            ownsMovement = false;
        }
        static void PlayOmen(ParticleSystem particles)
        {
            if (!particles) return;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); particles.Play(true);
        }
        void StopOmen()
        {
            foreach (var particles in new[] { omenSmoke, omenSparks })
                if (particles) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        void OnDisable() => CancelOpening();
    }
}
