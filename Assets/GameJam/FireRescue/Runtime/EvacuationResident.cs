using UnityEngine;
using UnityEngine.AI;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent, RequireComponent(typeof(NavMeshAgent))]
    public sealed class EvacuationResident : MonoBehaviour
    {
        public enum ResidentState { Waiting, Panicking, LeavingHouse, Following, EnteringShelter, Sheltered }
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
        public bool IsCalled => State != ResidentState.Waiting;
        public bool IsSafe => State == ResidentState.Sheltered;
        public NavMeshAgent Agent { get; private set; }
        public bool HasNavigationProblem { get; private set; }
        float reactionLeft, repathAt;
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
            lastPosition = transform.position;
        }
        void Start()
        {
            if (Agent.isOnNavMesh) Agent.isStopped = true;
            else HasNavigationProblem = true;
        }
        public bool CallOutside()
        {
            if (IsCalled || !mission || !mission.IsActive) return false;
            State = ResidentState.Panicking;
            reactionLeft = reactionSeconds + (formationIndex % 3) * .16f;
            if (door) door.Open();
            if (animator) animator.SetBool(Panic, true);
            return true;
        }
        void Update()
        {
            if (!mission || !mission.PlayerTransform || !Agent) return;
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
            if (State == ResidentState.LeavingHouse)
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
                    if (distance > 18f) speed = Mathf.Max(speed, 6.6f);
                    if (gap < 1f && distance < 8f) Stop();
                    else GoTo(target, speed, .7f);
                }
            }
            else if (State == ResidentState.EnteringShelter)
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
                HasNavigationProblem = !Agent.SetDestination(hit.position);
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
