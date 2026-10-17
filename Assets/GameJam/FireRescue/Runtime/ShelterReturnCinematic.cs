using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace GameJam.FireRescue
{
    /// <summary>A continuous first-person return to the real survivors before the existing victory presentation.</summary>
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
        [Min(.5f)] public float walkingSpeed = 2.6f;
        [Min(.5f)] public float blendInSeconds = 3f;
        [Min(1f)] public float eyeHeight = 2.15f;
        [Range(40f, 80f)] public float fieldOfView = 58f;
        [Min(2f)] public float observeSeconds = 8f;
        public Thought[] thoughts = {
            new Thought("Bitti... Şimdi sığınağa dönüp herkesin iyi olduğundan emin olmalıyım.", 6f),
            new Thought("O küçük dumanı fark etmek... Bir uyarıyı ciddiye almak hayat kurtarabiliyormuş.", 6.5f),
            new Thought("Bundan sonra tahliye planımızı ve acil durum çantalarımızı önceden hazırlayacağız.", 7f),
            new Thought("Uyarıları takip etmeyi, toplanma yerini ve güvenli yolları herkes bilmeli.", 6.5f),
            new Thought("Birbirimizi haberdar etmek, yardıma ihtiyacı olanları unutmamak... Hazırlık birlikte yapılır.", 7.5f),
            new Thought("Tehlike geçti sanıp acele etmeyeceğiz. Yetkililer güvenli demeden sığınaktan çıkmak yok.", 7f)
        };
        public Thought arrivalThought = new Thought("İşte buradalar... Güvendeler. Önlem almak ve birlikte hareket etmek... Bugün bizi kurtaran buydu.", 8f);
        public bool IsPlaying { get; private set; }
        public bool HasCompleted { get; private set; }
        public bool AtShelter { get; private set; }
        public float Elapsed { get; private set; }
        public float ObservedSeconds { get; private set; }
        public float DistanceTravelled { get; private set; }
        public float RouteLength { get; private set; }
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
        Vector3 entryPosition;
        Quaternion entryRotation;
        float entryFov, thoughtTime, thoughtGap;
        bool bodyWasEnabled, wasDriving;
        AudioSource voice;

        public bool Begin(RescueGameFlow owner)
        {
            if (IsPlaying || HasCompleted || !cinematic || !evacuation || !shelterViewpoint || !owner || !owner.player || !owner.player.viewCamera) return false;
            flow = owner; player = flow.player;
            var view = player.viewCamera;
            entryPosition = view.transform.position; entryRotation = view.transform.rotation; entryFov = view.fieldOfView;
            if (!BuildRoute()) { Debug.LogError("Shelter return requires a connected village NavMesh.", this); return false; }
            if (!cinematic.BeginShelterReturn()) return false;
            cinematic.ReturnFocus = SurvivorFocus();
            IsPlaying = true; Elapsed = ObservedSeconds = DistanceTravelled = thoughtTime = 0;
            AtShelter = false; ThoughtIndex = -1; CurrentLine = "";
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
            thoughtGap = Mathf.Max(0, RouteLength / walkingSpeed + blendInSeconds - speechSeconds) / Mathf.Max(1, thoughts.Length);
            if (!voice) { voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 0; }
            SetThought(0);
            return true;
        }

        bool BuildRoute()
        {
            route.Clear(); RouteLength = 0;
            Vector3 start = entryPosition; start.y = player.transform.position.y;
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(shelterViewpoint.position, out var end, 3, NavMesh.AllAreas)) return false;
            // A driving camera starts behind the cab. Begin there, without cutting to the driver's door.
            foreach (float radius in new[] { 3f, 8f, 20f, 45f })
            {
                if (!NavMesh.SamplePosition(start, out var from, radius, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(from.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                foreach (var corner in path.corners) route.Add(corner);
                for (int i = 1; i < route.Count; i++) RouteLength += Vector3.Distance(route[i - 1], route[i]);
                return route.Count > 0;
            }
            return false;
        }
        void Hold(Behaviour target) { if (target && !paused.ContainsKey(target)) { paused[target] = target.enabled; target.enabled = false; } }
        void Update()
        {
            if (!IsPlaying || !flow || flow.ReturningToMenu) return;
            float dt = Mathf.Min(Time.deltaTime, .1f);
            if (dt <= 0 || !cinematic.ShotCamera) return;
            Elapsed += dt;
            float remaining = RouteLength - DistanceTravelled;
            float speed = walkingSpeed * Mathf.SmoothStep(0, 1, Elapsed / blendInSeconds) * Mathf.Clamp(remaining / 3f, .18f, 1f);
            DistanceTravelled = Mathf.Min(RouteLength, DistanceTravelled + speed * dt);
            Vector3 foot = AlongRoute(DistanceTravelled, out var direction);
            if (remaining < .08f && Elapsed >= blendInSeconds) { AtShelter = true; foot = route[route.Count - 1]; }
            Vector3 focus = SurvivorFocus();
            cinematic.ReturnFocus = focus;
            float look = AtShelter ? 1f : Mathf.SmoothStep(0, 1, 1f - (RouteLength - DistanceTravelled) / 9f);
            Vector3 desiredPosition = foot + Vector3.up * eyeHeight;
            Vector3 forward = Vector3.Slerp(direction.sqrMagnitude > .001f ? direction.normalized : entryRotation * Vector3.forward,
                (focus - desiredPosition).normalized, look);
            float blend = Mathf.SmoothStep(0, 1, Elapsed / blendInSeconds);
            var shot = cinematic.ShotCamera;
            shot.transform.position = Vector3.Lerp(entryPosition, desiredPosition, blend);
            Quaternion desiredRotation = Quaternion.LookRotation(forward);
            shot.transform.rotation = Quaternion.Slerp(shot.transform.rotation, Quaternion.Slerp(entryRotation, desiredRotation, blend), 1 - Mathf.Exp(-dt * 2.5f));
            shot.fieldOfView = Mathf.Lerp(entryFov, fieldOfView, blend);
            if (player.playerBody) player.playerBody.transform.SetPositionAndRotation(foot, Quaternion.Euler(0, shot.transform.eulerAngles.y, 0));
            VisibleSurvivors = CountVisible(shot);
            if (ThoughtIndex < thoughts.Length)
            {
                thoughtTime += dt;
                float duration = thoughts[ThoughtIndex].Duration;
                if (thoughtTime >= duration + thoughtGap) SetThought(ThoughtIndex + 1);
                else if (thoughtTime > duration && !voice.isPlaying) CurrentLine = "";
            }
            if (AtShelter && ThoughtIndex >= thoughts.Length)
            {
                if (ObservedSeconds == 0) Speak(arrivalThought);
                ObservedSeconds += dt;
                if (ObservedSeconds >= Mathf.Max(observeSeconds, arrivalThought.Duration) && !voice.isPlaying) Complete();
            }
            if (IsPlaying) cinematic.ReturnNarration = CurrentLine;
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
            CurrentLine = thought.text;
            if (thought.voice) { voice.clip = thought.voice; voice.volume = Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f)); voice.Play(); }
        }
        void Complete()
        {
            FinalPosition = cinematic.ShotCamera.transform.position; FinalRotation = cinematic.ShotCamera.transform.rotation;
            IsPlaying = false; HasCompleted = true; CurrentLine = "";
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
            if (voice) voice.Stop();
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
