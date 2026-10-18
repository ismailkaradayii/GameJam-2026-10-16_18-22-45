using Itfaiye;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Matches the retargeted stride to navigation without letting animation move the agent.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(30)]
    public sealed class ResidentLocomotion : MonoBehaviour
    {
        public EvacuationResident resident;
        [Min(.1f)] public float walkStrideSpeed = 1.87f;
        [Min(.1f)] public float runStrideSpeed = 4.65f;
        [Header("Idle variation")]
        [Range(0, 4)] public int idleVariant;
        [Range(0f, 1f)] public float idlePhase;
        Animator animator;
        ItfaiyeLegStabilizer legs;
        float legWeight;
        bool supportsIdleVariation;
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int StrideRate = Animator.StringToHash("StrideRate");
        static readonly int IdleVariant = Animator.StringToHash("IdleVariant");

        void Awake() => Initialize();

        void Start()
        {
            if (supportsIdleVariation && (!resident || resident.State == EvacuationResident.ResidentState.Waiting))
                animator.Play("Base Layer.Locomotion", 0, idlePhase);
        }

        public void Initialize()
        {
            if (legs) return;
            animator = GetComponent<Animator>();
            if (!animator || !animator.isHuman || !animator.avatar || !animator.avatar.isValid) return;
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == IdleVariant && parameter.type == AnimatorControllerParameterType.Float)
                    supportsIdleVariation = true;
            if (supportsIdleVariation) animator.SetFloat(IdleVariant, idleVariant);
            legs = GetComponent<ItfaiyeLegStabilizer>();
            if (!legs) legs = gameObject.AddComponent<ItfaiyeLegStabilizer>();
            legs.Initialize();
        }

        public void SetMotion(float worldSpeed, float dt)
        {
            if (!resident || !animator) return;
            float scale = Mathf.Max(.1f, Mathf.Abs(transform.lossyScale.y) * animator.humanScale);
            // Blend toward shorter steps at low speed, instead of slowing a full-sized stride.
            float gait = Mathf.Max(0f, worldSpeed) / scale;
            animator.SetFloat(Speed, gait, .08f, dt);
            // Only Locomotion uses this parameter: fear, burning and shelter poses retain their timing.
            float rate = Mathf.Clamp(gait / Mathf.Max(.1f, runStrideSpeed), 1f, 2.2f);
            animator.SetFloat(StrideRate, rate, .08f, dt);
        }

        public void MatchNavigationToStride()
        {
            if (!resident) return;
            if (!animator) animator = GetComponent<Animator>();
            if (!animator || !animator.isHuman) return;
            float scale = Mathf.Max(.1f, Mathf.Abs(transform.lossyScale.y) * animator.humanScale);
            resident.walkingSpeed = walkStrideSpeed * scale;
            resident.runningSpeed = runStrideSpeed * scale;
            resident.catchUpSpeed = Mathf.Max(resident.catchUpSpeed, resident.runningSpeed * 1.35f);
            // Reach the natural running pace promptly, while retaining NavMesh braking and avoidance.
            var agent = resident.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent) agent.acceleration = Mathf.Max(14f, resident.runningSpeed * 2.5f);
        }

        void LateUpdate()
        {
            if (!resident || !animator || !legs) return;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            bool movingPose = state.IsName("Locomotion") && !resident.IsDead && !resident.IsBurning && !resident.IsResting;
            legWeight = Mathf.MoveTowards(legWeight, movingPose ? 1f : 0f, Time.deltaTime / .12f);
            if (legWeight > 0f && !resident.IsDead) legs.Stabilize(legWeight, Time.deltaTime);
        }

        // Animation sampling uses the same bind-frame solver as the real residents.
        public void StabilizePreview(float dt)
        {
            Initialize();
            if (legs) legs.Stabilize(1f, dt);
        }
    }
}
