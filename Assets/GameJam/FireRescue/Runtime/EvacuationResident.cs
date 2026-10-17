using UnityEngine;
using UnityEngine.AI;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent, RequireComponent(typeof(NavMeshAgent))]
    public sealed class EvacuationResident : MonoBehaviour
    {
        public enum ResidentState { Waiting, Panicking, LeavingHouse, Following, EnteringShelter, Sheltered, Burning, Dead }
        public EvacuationMission mission;
        public Animator animator;
        public EvacuationDoor door;
        public Transform houseExit;
        public Transform safePlace;
        public int formationIndex;
        public float reactionSeconds = 1.5f;
        public float walkingSpeed = 2f;
        public float runningSpeed = 5.8f;
        public ResidentState State { get; private set; }
        public bool IsCalled => called;
        public bool IsSafe => State == ResidentState.Sheltered;
        public bool IsDead => State == ResidentState.Dead;
        public bool IsBurning => State == ResidentState.Burning;
        public ThermalDamage Thermal { get; private set; }
        bool called;
        ResidentState beforeBurn;
        float diedAt;
        Vector3 deathModelPosition;
        Quaternion deathModelRotation;
        float corpseHeight;
        float corpseWorldHeight;
        bool corpseGroundMeasured;
        Vector3 corpseGroundOffset;
        public NavMeshAgent Agent { get; private set; }
        public bool HasNavigationProblem { get; private set; }
        [Min(1f)] public float catchUpSpeed = 11f;
        [Min(1f)] public float stuckRecoverySeconds = 2.5f;
        float reactionLeft, repathAt, stuckFor;
        Vector3 recoveryPosition;
        int entranceStep;
        Vector3 lastPosition;
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int Panic = Animator.StringToHash("Panic");

        void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
            Agent.acceleration = 14f;
            Agent.angularSpeed = 360f;
            Agent.avoidancePriority = 25 + formationIndex;
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animator) animator.applyRootMotion = false;
            Thermal = ThermalDamage.Ensure(this);
            lastPosition = transform.position;
            recoveryPosition = transform.position;
        }
        void Start()
        {
            if (Agent.isOnNavMesh) Agent.isStopped = true;
            else HasNavigationProblem = true;
        }
        public bool CallOutside()
        {
            if (IsCalled || IsDead || IsBurning || !mission || !mission.IsActive) return false;
            called = true;
            State = ResidentState.Panicking;
            reactionLeft = reactionSeconds + (formationIndex % 3) * .16f;
            if (door) door.Open();
            if (animator) animator.SetBool(Panic, true);
            return true;
        }
        void Update()
        {
            if (IsDead || IsBurning) return;
            if (!mission || !mission.PlayerTransform || !Agent || !Agent.enabled || Time.deltaTime <= 0f) return;
            if (!Agent.isOnNavMesh)
            {
                // Recover a small local displacement only; never warp a resident to the shelter.
                if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out var recovered, 2f, Agent.areaMask)) Agent.Warp(recovered.position);
                else { HasNavigationProblem = true; return; }
            }
            if (IsCalled && !IsSafe && State != ResidentState.Panicking)
            {
                if (FlatDistance(transform.position, recoveryPosition) < .2f) stuckFor += Time.deltaTime;
                else { stuckFor = 0; recoveryPosition = transform.position; }
                if (stuckFor >= stuckRecoverySeconds)
                {
                    if (door) door.Open();
                    Agent.ResetPath(); repathAt = 0;
                    Agent.avoidancePriority = 10 + (formationIndex + Mathf.FloorToInt(Time.time)) % 30;
                    stuckFor = 0;
                }
            }
            // Crossing the actual safe area completes rescue, even if a parking slot is occupied.
            if (IsCalled && !IsSafe && State != ResidentState.Panicking && mission.IsInsideShelter(transform.position))
            {
                State = ResidentState.Sheltered;
                repathAt = 0;
                mission.ResidentArrived(this);
            }
            if (State == ResidentState.Panicking)
            {
                reactionLeft -= Time.deltaTime;
                if (reactionLeft <= 0f)
                {
                    if (animator) animator.SetBool(Panic, false);
                    State = ResidentState.LeavingHouse;
                    repathAt = 0;
                }
            }
            if (State == ResidentState.LeavingHouse && houseExit)
            {
                GoTo(houseExit.position, runningSpeed, .3f);
                if (Arrived(houseExit.position, .8f)) { State = ResidentState.Following; repathAt = 0; }
            }
            else if (State == ResidentState.Following)
            {
                float distance = FlatDistance(transform.position, mission.PlayerTransform.position);
                if (mission.CanEnterShelter(this))
                { State = ResidentState.EnteringShelter; entranceStep = 0; repathAt = 0; }
                else
                {
                    Vector3 target = mission.FollowPoint(formationIndex);
                    float gap = FlatDistance(transform.position, target);
                    float speed = gap > 5f ? runningSpeed : walkingSpeed;
                    // A sprinting player must not permanently outrun the group.
                    if (distance > 18f) speed = Mathf.Max(speed, catchUpSpeed);
                    if (gap < 1f && distance < 8f) Stop();
                    else GoTo(target, speed, .7f);
                }
            }
            else if (State == ResidentState.EnteringShelter && mission.shelterEntrance && safePlace)
            {
                Vector3 destination = entranceStep == 0 ? mission.shelterEntrance.position : safePlace.position;
                GoTo(destination, walkingSpeed + 1.1f, .15f);
                if (Arrived(destination, .9f))
                {
                    if (entranceStep == 0) { entranceStep = 1; repathAt = 0; }
                    else
                    {
                        if (mission.IsInsideShelter(transform.position))
                        { State = ResidentState.Sheltered; mission.ResidentArrived(this); }
                    }
                }
            }
            else if (State == ResidentState.Sheltered && safePlace)
            {
                if (FlatDistance(transform.position, safePlace.position) > 1f) GoTo(safePlace.position, walkingSpeed, .65f);
                else { Stop(); transform.rotation = Quaternion.Slerp(transform.rotation, safePlace.rotation, Time.deltaTime * 3f); }
            }
            float actualSpeed = Time.deltaTime > .0001f ? FlatDistance(transform.position, lastPosition) / Time.deltaTime : 0;
            lastPosition = transform.position;
            if (animator) animator.SetFloat(Speed, Mathf.Min(actualSpeed / Mathf.Max(.1f, transform.lossyScale.y), 7f), .18f, Time.deltaTime);
        }
        public void BeginBurning()
        {
            if (IsDead || IsSafe || IsBurning) return;
            beforeBurn = State; State = ResidentState.Burning;
            Stop();
            if (animator)
            {
                animator.SetBool(Panic, false); animator.SetFloat(Speed, 0);
                if (animator.HasState(0, Animator.StringToHash("Burning"))) animator.CrossFadeInFixedTime("Burning", .12f, 0, (formationIndex % 3) * .12f);
            }
        }
        public void StopBurning()
        {
            if (!IsBurning) return;
            State = beforeBurn;
            if (State == ResidentState.Panicking) reactionLeft = .25f;
            repathAt = 0; stuckFor = 0;
            if (animator)
            {
                animator.SetBool(Panic, State == ResidentState.Panicking);
                animator.CrossFadeInFixedTime(State == ResidentState.Panicking ? "Terrified" : "Locomotion", .2f);
            }
        }
        public void DieFromFire()
        {
            if (IsDead || IsSafe) return;
            State = ResidentState.Dead; diedAt = Time.time;
            Stop();
            var capsule = GetComponent<CapsuleCollider>();
            corpseWorldHeight = capsule ? capsule.bounds.size.y : 2f;
            if (Agent) Agent.enabled = false;
            // A corpse no longer blocks the evacuation queue or the water stream.
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            if (animator)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                deathModelPosition = animator.transform.localPosition; deathModelRotation = animator.transform.localRotation;
                corpseHeight = animator.humanScale;
                if (animator.HasState(0, Animator.StringToHash("BurnDeath"))) animator.CrossFadeInFixedTime("BurnDeath", .08f);
            }
            if (mission) mission.ResidentDied(this);
        }
        void LateUpdate()
        {
            if (!IsDead || !animator) return;
            float t = Mathf.Clamp01((Time.time - diedAt) / 1.15f);
            float fall = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, 1f, t));
            animator.transform.localRotation = deathModelRotation * Quaternion.Euler(86f * fall, 0f, (formationIndex % 2 == 0 ? 8f : -8f) * fall);
            animator.transform.localPosition = deathModelPosition + deathModelRotation * new Vector3(0, corpseHeight * (.06f * fall + .08f * Mathf.Sin(t * Mathf.PI)), corpseHeight * .2f * fall) +
                corpseGroundOffset * Mathf.SmoothStep(0, 1, (Time.time - diedAt - 1.15f) / .25f);
            // Hold the final authored pose instead of looping the dying motion.
            if (t >= 1f)
            {
                animator.speed = 0f;
                if (!corpseGroundMeasured) GroundCorpse();
            }
        }
        void GroundCorpse()
        {
            corpseGroundMeasured = true;
            Vector3 lowest = transform.position; float minY = float.PositiveInfinity;
            if (!animator.isHuman) return;
            // World-space bones remain reliable on retargeted avatars with scaled model roots.
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var id = (HumanBodyBones)i; var bone = animator.GetBoneTransform(id);
                if (!bone) continue;
                float thickness = corpseWorldHeight * (id == HumanBodyBones.Head ? .06f :
                    id == HumanBodyBones.Hips || id == HumanBodyBones.Chest || id == HumanBodyBones.Spine || id == HumanBodyBones.UpperChest ? .07f : .018f);
                Vector3 point = bone.position - Vector3.up * thickness;
                if (point.y < minY) { minY = point.y; lowest = point; }
            }
            if (!float.IsFinite(minY)) return;
            // Sample the floor from the original feet height, so indoor corpses do not snap onto the roof.
            Vector3 origin = new Vector3(lowest.x, transform.position.y + 1.5f, lowest.z);
            if (!Physics.Raycast(origin, Vector3.down, out var floor, 12f, ~0, QueryTriggerInteraction.Ignore)) return;
            Vector3 shift = Vector3.up * (floor.point.y + .035f - minY);
            corpseGroundOffset = animator.transform.parent ? animator.transform.parent.InverseTransformVector(shift) : shift;
        }
        void Stop()
        {
            if (Agent.isOnNavMesh) Agent.isStopped = true;
        }
        void GoTo(Vector3 point, float speed, float stoppingDistance)
        {
            if (!Agent.isOnNavMesh) { HasNavigationProblem = true; return; }
            Agent.speed = speed; Agent.stoppingDistance = stoppingDistance;
            Agent.isStopped = false;
            if (Time.time < repathAt) return;
            repathAt = Time.time + .35f + formationIndex * .015f;
            // Project nearby targets onto walkable ground; never teleport a stuck resident.
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, 3f, Agent.areaMask))
            {
                HasNavigationProblem = !Agent.SetDestination(hit.position) || (!Agent.pathPending && Agent.pathStatus == NavMeshPathStatus.PathInvalid);
            }
            else HasNavigationProblem = true;
        }
        bool Arrived(Vector3 point, float radius) => Agent.isOnNavMesh && !Agent.pathPending &&
            Agent.pathStatus == NavMeshPathStatus.PathComplete &&
            (FlatDistance(transform.position, point) < radius || (Agent.hasPath && Agent.remainingDistance <= Mathf.Max(radius, Agent.stoppingDistance + .15f))) &&
            Mathf.Abs(transform.position.y - point.y) < 1.4f;
        public static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    }
}
