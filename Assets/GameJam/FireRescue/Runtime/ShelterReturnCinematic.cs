using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace GameJam.FireRescue
{
    /// <summary>A fixed first-person approach to the real survivors before the existing victory presentation.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(320)]
    public sealed class ShelterReturnCinematic : MonoBehaviour
    {
        [Serializable] public sealed class Thought
        {
            [TextArea(1, 3)] public string text;
            [Min(1f)] public float seconds = 6f;
            public AudioClip voice;
            public float Duration => Mathf.Max(seconds, voice ? voice.length : 0);
            public Thought(string text, float seconds) { this.text = text; this.seconds = seconds; }
        }
        public VolcanoCinematic cinematic;
        public EvacuationMission evacuation;
        public Transform shelterViewpoint, shelterFocus;
        [Tooltip("Optional fixed start. Otherwise the final stretch of the assembly-point-to-shelter route is used.")]
        public Transform returnStart;
        [Min(10f)] public float maximumRouteLength = 100f;
        [Range(.15f, 1.5f)] public float relocationFadeSeconds = .45f;
        [Min(.5f)] public float walkingSpeed = 2.6f;
        [Min(.5f)] public float blendInSeconds = 3f;
        [Min(1f)] public float eyeHeight = 2.15f;
        [Range(40f, 80f)] public float fieldOfView = 58f;
        [Min(2f)] public float observeSeconds = 8f;
        [Range(0f, 1f)] public float voiceVolume = .9f;
        public Thought[] thoughts = {
            new Thought("It's over... I should return to the shelter and make sure everyone is all right.", 6f),
            new Thought("That little wisp of smoke... Taking a warning seriously can save lives.", 6.5f),
            new Thought("From now on, we'll prepare our evacuation plan and emergency kits in advance.", 7f),
            new Thought("Everyone should know the warnings, the assembly point, and the safest routes.", 6.5f),
            new Thought("Keeping each other informed, remembering those who need help... We prepare together.", 7.5f),
            new Thought("We won't rush outside just because the danger seems to be over. We'll wait for the official all-clear.", 7f)
        };
        public Thought arrivalThought = new Thought("There they are... Safe. Being prepared and working together... That's what saved us today.", 8f);
        public bool IsPlaying { get; private set; }
        public bool HasCompleted { get; private set; }
        public bool AtShelter { get; private set; }
        public float Elapsed { get; private set; }
        public float ObservedSeconds { get; private set; }
        public float DistanceTravelled { get; private set; }
        public float RouteLength { get; private set; }
        public float PlannedWalkSeconds { get; private set; }
        public bool IsRelocating { get; private set; }
        public float TransitionOpacity { get; private set; }
        public int ThoughtIndex { get; private set; } = -1;
        public string CurrentLine { get; private set; } = "";
        public Vector3 FinalPosition { get; private set; }
        public Quaternion FinalRotation { get; private set; }
        public IReadOnlyList<Vector3> Route => route;
        public int VisibleSurvivors { get; private set; }
        readonly List<Vector3> route = new List<Vector3>();
        readonly Dictionary<Behaviour, bool> paused = new Dictionary<Behaviour, bool>();
        readonly Dictionary<Renderer, bool> hidden = new Dictionary<Renderer, bool>();
        readonly Dictionary<LavaIgnitionSource, bool> heat = new Dictionary<LavaIgnitionSource, bool>();
        RescueGameFlow flow;
        FireRescuePlayer player;
        Quaternion entryRotation;
        float thoughtTime, thoughtGap, transitionTime, walkTime;
        bool bodyWasEnabled, wasDriving, relocated;
        AudioSource voice;
        bool voicePaused;
        bool VoicePlaying => voice && (voice.isPlaying || voicePaused);

        void Awake() => LoadVoiceClips();

        /// <summary>Preserves assigned recordings; fills empty slots with the seven closing recordings.</summary>
        public void LoadVoiceClips()
        {
            for (int i = 0; i < Mathf.Min(6, thoughts.Length); i++)
                if (thoughts[i] != null && !thoughts[i].voice)
                    thoughts[i].voice = Resources.Load<AudioClip>("ASHFALL/Audio/Ending/ending" + (i + 1).ToString("00"));
            if (arrivalThought != null && !arrivalThought.voice)
                arrivalThought.voice = Resources.Load<AudioClip>("ASHFALL/Audio/Ending/ending07");
        }

        public bool Begin(RescueGameFlow owner)
        {
            if (IsPlaying || HasCompleted || !cinematic || !evacuation || !shelterViewpoint || !owner || !owner.player || !owner.player.viewCamera) return false;
            LoadVoiceClips();
            flow = owner; player = flow.player;
            var view = player.viewCamera;
            entryRotation = view.transform.rotation;
            if (!BuildRoute()) { Debug.LogError("Shelter return requires a connected village NavMesh.", this); return false; }
            if (!cinematic.BeginShelterReturn()) return false;
            cinematic.ReturnFocus = SurvivorFocus();
            IsPlaying = true; Elapsed = ObservedSeconds = DistanceTravelled = thoughtTime = 0;
            AtShelter = false; ThoughtIndex = -1; CurrentLine = "";
            IsRelocating = true; relocated = false;
            TransitionOpacity = transitionTime = walkTime = 0;
            bodyWasEnabled = player.playerBody && player.playerBody.enabled; wasDriving = player.IsDriving;
            player.SetDriving(false);
            if (player.playerBody) player.playerBody.enabled = false;
            if (player.firstPerson && player.firstPerson.bodyAnimator)
                foreach (var renderer in player.firstPerson.bodyAnimator.GetComponentsInChildren<Renderer>(true))
                { hidden[renderer] = renderer.enabled; renderer.enabled = false; }
            // Victory is already earned. Let people animate, but hold hazards and the score stable during the walk.
            Hold(player.mission ? player.mission.sequence : null);
            foreach (var house in FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None)) if (house.gameObject.scene == gameObject.scene) Hold(house);
            foreach (var target in FindObjectsByType<ThermalDamage>(FindObjectsSortMode.None)) if (target.gameObject.scene == gameObject.scene) Hold(target);
            foreach (var source in LavaIgnitionSource.ActiveSources)
                if (source && source.gameObject.scene == gameObject.scene) { heat[source] = source.heatEnabled; source.heatEnabled = false; }
            float speechSeconds = 0; foreach (var thought in thoughts) speechSeconds += thought.Duration;
            PlannedWalkSeconds = Mathf.Max(speechSeconds + .2f * Mathf.Max(0, thoughts.Length - 1),
                RouteLength / Mathf.Max(.5f, walkingSpeed) + blendInSeconds);
            thoughtGap = Mathf.Max(0, PlannedWalkSeconds - speechSeconds) / Mathf.Max(1, thoughts.Length);
            if (!voice)
            {
                voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.spatialBlend = 0;
                voice.priority = 40; voice.dopplerLevel = voice.reverbZoneMix = 0;
            }
            return true;
        }

        bool BuildRoute()
        {
            route.Clear(); RouteLength = 0;
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(shelterViewpoint.position, out var end, 3, NavMesh.AllAreas)) return false;
            Vector3 approach = evacuation.shelterApproach ? evacuation.shelterApproach.position : end.position;
            Vector3 away = Vector3.ProjectOnPlane(approach - end.position, Vector3.up).normalized;
            if (away.sqrMagnitude < .1f) away = -shelterViewpoint.forward;
            // Every candidate belongs to the authored village, never the player's victory position.
            var starts = new List<Vector3>();
            if (returnStart) starts.Add(returnStart.position);
            if (evacuation.assemblyPoint) starts.Add(evacuation.assemblyPoint.position);
            starts.Add(approach + away * maximumRouteLength);
            starts.Add(approach);
            foreach (var start in starts)
            {
                if (!NavMesh.SamplePosition(start, out var from, 8f, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(from.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                route.Clear(); RouteLength = 0;
                foreach (var corner in path.corners) route.Add(corner);
                for (int i = 1; i < route.Count; i++) RouteLength += Vector3.Distance(route[i - 1], route[i]);
                if (route.Count < 2 || RouteLength < .5f) continue;
                // Keep the same final, connected path segment even if the assembly point is very distant.
                float trim = Mathf.Max(0, RouteLength - Mathf.Max(10f, maximumRouteLength));
                while (trim > 0 && route.Count > 1)
                {
                    float length = Vector3.Distance(route[0], route[1]);
                    if (trim >= length) { route.RemoveAt(0); trim -= length; RouteLength -= length; }
                    else { route[0] = Vector3.MoveTowards(route[0], route[1], trim); RouteLength -= trim; trim = 0; }
                }
                return true;
            }
            return false;
        }
        void Hold(Behaviour target) { if (target && !paused.ContainsKey(target)) { paused[target] = target.enabled; target.enabled = false; } }
        void Update()
        {
            if (!IsPlaying || !flow || flow.ReturningToMenu) return;
            PauseVoice(Time.timeScale <= 0f);
            float dt = Mathf.Min(Time.deltaTime, .1f);
            if (dt <= 0 || !cinematic.ShotCamera) return;
            Elapsed += dt;
            if (IsRelocating) { UpdateRelocation(dt); return; }
            walkTime += dt;
            float progress = Mathf.Clamp01(walkTime / Mathf.Max(.1f, PlannedWalkSeconds));
            // Short acceleration/deceleration, with steady walking through the middle of the route.
            float ramp = Mathf.Min(.12f, blendInSeconds / Mathf.Max(.1f, PlannedWalkSeconds));
            float travelled = progress < ramp ? progress * progress / (2f * ramp) :
                progress > 1f - ramp ? 1f - ramp - (1f - progress) * (1f - progress) / (2f * ramp) : progress - ramp * .5f;
            travelled /= 1f - ramp;
            if (ThoughtIndex < thoughts.Length) travelled = Mathf.Min(travelled, .985f);
            float maximumStep = RouteLength / Mathf.Max(.1f, PlannedWalkSeconds * (1f - ramp)) * dt;
            DistanceTravelled = Mathf.MoveTowards(DistanceTravelled, Mathf.Clamp01(travelled) * RouteLength, maximumStep);
            Vector3 foot = AlongRoute(DistanceTravelled, out var direction);
            if (DistanceTravelled >= RouteLength - .001f) { AtShelter = true; foot = route[route.Count - 1]; }
            Vector3 focus = SurvivorFocus();
            cinematic.ReturnFocus = focus;
            float look = AtShelter ? 1f : Mathf.SmoothStep(0, 1, 1f - (RouteLength - DistanceTravelled) / 9f);
            Vector3 desiredPosition = foot + Vector3.up * eyeHeight;
            Vector3 forward = Vector3.Slerp(direction.sqrMagnitude > .001f ? direction.normalized : entryRotation * Vector3.forward,
                (focus - desiredPosition).normalized, look);
            var shot = cinematic.ShotCamera;
            shot.transform.position = desiredPosition;
            Quaternion desiredRotation = Quaternion.LookRotation(forward);
            shot.transform.rotation = Quaternion.Slerp(shot.transform.rotation, desiredRotation, 1 - Mathf.Exp(-dt * 2.5f));
            shot.fieldOfView = fieldOfView;
            if (player.playerBody) player.playerBody.transform.SetPositionAndRotation(foot, Quaternion.Euler(0, shot.transform.eulerAngles.y, 0));
            VisibleSurvivors = CountVisible(shot);
            if (ThoughtIndex < thoughts.Length)
            {
                thoughtTime += dt;
                float duration = thoughts[ThoughtIndex].Duration;
                if (thoughtTime >= duration + thoughtGap && !VoicePlaying) SetThought(ThoughtIndex + 1);
                else if (thoughtTime > duration && !VoicePlaying) CurrentLine = "";
            }
            if (AtShelter && ThoughtIndex >= thoughts.Length)
            {
                if (ObservedSeconds == 0) Speak(arrivalThought);
                ObservedSeconds += dt;
                if (ObservedSeconds >= Mathf.Max(observeSeconds, arrivalThought.Duration) && !VoicePlaying) Complete();
            }
            if (IsPlaying) cinematic.ReturnNarration = CurrentLine;
        }
        void UpdateRelocation(float dt)
        {
            float fade = Mathf.Max(.15f, relocationFadeSeconds);
            transitionTime += dt;
            if (!relocated)
            {
                TransitionOpacity = Mathf.SmoothStep(0, 1, transitionTime / fade);
                if (transitionTime < fade) return;
                // The camera cut happens only under a fully opaque frame, before any closing speech.
                relocated = true; transitionTime = 0;
                Vector3 foot = AlongRoute(0, out var direction);
                Quaternion rotation = Quaternion.LookRotation(direction.normalized);
                cinematic.ShotCamera.transform.SetPositionAndRotation(foot + Vector3.up * eyeHeight, rotation);
                cinematic.ShotCamera.fieldOfView = fieldOfView;
                if (player.playerBody) player.playerBody.transform.SetPositionAndRotation(foot, Quaternion.Euler(0, rotation.eulerAngles.y, 0));
                return;
            }
            TransitionOpacity = 1f - Mathf.SmoothStep(0, 1, transitionTime / fade);
            if (transitionTime < fade) return;
            TransitionOpacity = 0; IsRelocating = false;
            SetThought(0); cinematic.ReturnNarration = CurrentLine;
        }
        void OnGUI()
        {
            if (!IsPlaying || TransitionOpacity <= 0) return;
            int depth = GUI.depth; Color color = GUI.color;
            GUI.depth = -200; GUI.color = new Color(0, 0, 0, TransitionOpacity);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.depth = depth; GUI.color = color;
        }
        void LateUpdate()
        {
            // The existing listener follows the continuous cinematic viewpoint as well.
            if (IsPlaying && cinematic.ShotCamera && player.viewCamera)
                player.viewCamera.transform.SetPositionAndRotation(cinematic.ShotCamera.transform.position, cinematic.ShotCamera.transform.rotation);
        }
        Vector3 AlongRoute(float distance, out Vector3 direction)
        {
            direction = shelterViewpoint.forward;
            for (int i = 1; i < route.Count; i++)
            {
                Vector3 delta = route[i] - route[i - 1]; float length = delta.magnitude;
                if (distance <= length) { direction = delta; return Vector3.Lerp(route[i - 1], route[i], length > .001f ? distance / length : 1); }
                distance -= length;
            }
            return route[route.Count - 1];
        }
        Vector3 SurvivorFocus()
        {
            Vector3 total = Vector3.zero; int count = 0;
            foreach (var resident in evacuation.residents) if (resident && resident.IsSafe) { total += Head(resident); count++; }
            return count > 0 ? total / count : shelterFocus ? shelterFocus.position : shelterViewpoint.position + shelterViewpoint.forward * 8;
        }
        static Vector3 Head(EvacuationResident resident)
        {
            var bone = resident.animator && resident.animator.isHuman ? resident.animator.GetBoneTransform(HumanBodyBones.Head) : null;
            return bone ? bone.position : resident.transform.position + Vector3.up * 1.4f;
        }
        int CountVisible(Camera camera)
        {
            int count = 0;
            foreach (var resident in evacuation.residents)
            {
                if (!resident || !resident.IsSafe) continue;
                Vector3 head = Head(resident), screen = camera.WorldToViewportPoint(head);
                if (screen.z <= 0 || screen.x < .03f || screen.x > .97f || screen.y < .1f || screen.y > .9f) continue;
                Vector3 delta = head - camera.transform.position;
                if (!Physics.Raycast(camera.transform.position, delta.normalized, out var hit, delta.magnitude, ~0, QueryTriggerInteraction.Ignore) || hit.transform.IsChildOf(resident.transform)) count++;
            }
            return count;
        }
        void SetThought(int index) { ThoughtIndex = index; thoughtTime = 0; if (index < thoughts.Length) Speak(thoughts[index]); else CurrentLine = ""; }
        void Speak(Thought thought)
        {
            voice.Stop(); voicePaused = false;
            voice.clip = thought.voice;
            CurrentLine = thought.text;
            if (thought.voice)
            {
                voice.volume = voiceVolume * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f));
                voice.Play();
            }
        }
        void PauseVoice(bool pausedVoice)
        {
            if (!voice || !voice.clip || pausedVoice == voicePaused) return;
            if (pausedVoice && voice.isPlaying) { voice.Pause(); voicePaused = true; }
            else if (!pausedVoice && voicePaused) { voice.UnPause(); voicePaused = false; }
        }
        void Complete()
        {
            FinalPosition = cinematic.ShotCamera.transform.position; FinalRotation = cinematic.ShotCamera.transform.rotation;
            IsPlaying = IsRelocating = false; HasCompleted = true; CurrentLine = ""; TransitionOpacity = 0;
            cinematic.EndShot(true);
            if (player.firstPerson && player.firstPerson.handsCamera) player.firstPerson.handsCamera.enabled = false;
            if (player.pistol) player.pistol.SetAvailable(false);
            flow.CompleteVictoryCinematic(this);
            RestoreWorld();
        }
        void RestoreWorld()
        {
            foreach (var pair in paused) if (pair.Key) pair.Key.enabled = pair.Value; paused.Clear();
            foreach (var pair in heat) if (pair.Key) pair.Key.heatEnabled = pair.Value; heat.Clear();
        }
        public void Cancel()
        {
            if (voice) { voice.Stop(); voice.clip = null; }
            voicePaused = false;
            IsRelocating = false; TransitionOpacity = 0;
            if (IsPlaying)
            {
                IsPlaying = false; CurrentLine = "";
                cinematic.EndShot();
                if (player)
                {
                    player.SetDriving(wasDriving);
                    if (player.playerBody) player.playerBody.enabled = bodyWasEnabled;
                }
            }
            RestoreWorld();
            foreach (var pair in hidden) if (pair.Key) pair.Key.enabled = pair.Value; hidden.Clear();
        }
        void OnDisable() => Cancel();
    }
}
