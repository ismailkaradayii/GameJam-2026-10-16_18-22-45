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

        struct Line
        {
            public Thought thought;
            public string text;
            public float seconds;
        }
        readonly Queue<Line> pending = new Queue<Line>();
        readonly HashSet<Thought> spoken = new HashSet<Thought>();
        string currentLine;
        float lineRemaining, approachElapsed;
        int waypoint;
        bool ownsMovement, stoppedForEruption;
        Vector3 appliedViewOffset;
        FireRescuePlayer Player => mission ? mission.player : null;

        void Awake()
        {
            if (!mission) mission = GetComponent<VolcanoMissionDirector>();
            StopOmen();
        }

        public bool BeginOpening()
        {
            if (OpeningStarted || !Player || Player.IsDriving || !windowStop || !volcanoLookTarget) return false;
            OpeningStarted = IsOpening = true;
            Player.SetCinematicLocked(true); Player.RefreshEquipmentState(); ownsMovement = true;
            Speak(Thought.Approach, "I can't shake this feeling... I'll look out the window.", 6f, true);
            return true;
        }

        void Update()
        {
            RemoveViewOffset();
            if (!mission || !Player || Time.timeScale <= 0f || (mission.gameFlow && mission.gameFlow.BlocksGameplay)) return;
            if (mission.EruptionTriggered)
            {
                if (!stoppedForEruption) { CancelOpening(); stoppedForEruption = true; }
                return;
            }
            if (VolcanoCinematic.IsPlayingAny) return;
            lineRemaining = Mathf.Max(0, lineRemaining - Time.deltaTime);
            if (lineRemaining <= 0 && pending.Count > 0) Present(pending.Dequeue());
            if (IsOpening) TickOpening();
            else if (OpeningComplete) TickThoughts();
        }

        void LateUpdate()
        {
            if (!IsOpening || !ownsMovement || !Player || !Player.viewCamera || VolcanoCinematic.IsPlayingAny) return;
            float approach = AtWindow ? 1f : 1f - Mathf.Clamp01(EvacuationResident.FlatDistance(Player.transform.position, windowStop.position) / 4f);
            float leave = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(observationSeconds - 1.2f, observationSeconds, ObservationElapsed));
            appliedViewOffset = windowEyeOffset * (Mathf.SmoothStep(0, 1, approach) * leave);
            Player.viewCamera.transform.position += appliedViewOffset;
        }
        void RemoveViewOffset()
        {
            if (Player && Player.viewCamera) Player.viewCamera.transform.position -= appliedViewOffset;
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
                    Speak(Thought.Unease, "Something is wrong with the volcano... It looks different today.", omenDelay, true);
                }
                else
                {
                    if (ownsMovement)
                    {
                        Vector3 focus = distance < 3f && waypoint >= windowRoute.Length ? volcanoLookTarget.position :
                            destination.position + Vector3.up * (Player.viewCamera.transform.position.y - Player.transform.position.y);
                        Quaternion look = Quaternion.LookRotation(focus - Player.viewCamera.transform.position);
                        Player.SimulateCinematicWalk(destination.position,
                            Quaternion.Slerp(Player.viewCamera.transform.rotation, look, 1f - Mathf.Exp(-3f * dt)), approachSpeed);
                        // A moved prop must never leave the player permanently locked. The same window
                        // becomes a normal guided destination until the player walks into position.
                        if (approachElapsed >= approachTimeout) ReleaseMovement();
                    }
                    return;
                }
            }
            Quaternion facing = Quaternion.LookRotation(volcanoLookTarget.position - Player.viewCamera.transform.position);
            Player.SimulateCinematicWalk(Player.transform.position,
                Quaternion.Slerp(Player.viewCamera.transform.rotation, facing, 1f - Mathf.Exp(-4f * dt)), 0f);
            ObservationElapsed += dt;
            if (!OmenPlayed && ObservationElapsed >= omenDelay)
            {
                OmenPlayed = true;
                PlayOmen(omenSmoke); PlayOmen(omenSparks);
                Speak(Thought.Omen, "Smoke... and sparks. That's not a good sign.", recognitionDelay - omenDelay, true);
            }
            if (ObservationElapsed >= recognitionDelay)
                Speak(Thought.Foreseen, "I knew this day would come.", observationSeconds - recognitionDelay, true);
            if (ObservationElapsed >= observationSeconds)
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
            var line = new Line { thought = thought, text = text, seconds = Mathf.Max(1f, seconds) };
            if (interrupt) { pending.Clear(); Present(line); }
            else if (lineRemaining <= 0) Present(line);
            else pending.Enqueue(line);
        }
        void Present(Line line) { currentLine = line.text; lineRemaining = line.seconds; CurrentThought = line.thought; }

        public void CancelOpening()
        {
            RemoveViewOffset();
            IsOpening = false;
            pending.Clear(); currentLine = string.Empty; lineRemaining = 0;
            StopOmen(); ReleaseMovement();
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
