using UnityEngine;

namespace Itfaiye
{
    /// <summary>Preserves the retargeted stride while solving calf roll and ankle orientation in the avatar's own bind frame.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(25)]
    [RequireComponent(typeof(Animator))]
    public sealed class ItfaiyeLegStabilizer : MonoBehaviour
    {
        public ItfaiyeFirstPersonController player;
        Animator animator;
        Leg left, right;
        float weight;

        sealed class Leg
        {
            public Transform thigh, calf, foot, toe;
            public Quaternion thighFrame, calfFrame, footFrame, toeRest;
            public float pitch;
        }

        void Awake() { Initialize(); }

        public void Initialize()
        {
            if (left != null) return;
            animator = GetComponent<Animator>();
            if (!animator || !animator.isHuman || !animator.avatar.isValid) return;
            left = Cache(true); right = Cache(false);
        }

        Leg Cache(bool isLeft)
        {
            var leg = new Leg {
                thigh = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg),
                calf = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg),
                foot = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot),
                toe = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes)
            };
            Quaternion inverse = Quaternion.Inverse(transform.rotation);
            leg.thighFrame = inverse * leg.thigh.rotation;
            leg.calfFrame = inverse * leg.calf.rotation;
            leg.footFrame = inverse * leg.foot.rotation;
            if (leg.toe) leg.toeRest = leg.toe.localRotation;
            return leg;
        }

        void LateUpdate()
        {
            if (!player || !animator || left == null) return;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            bool supported = SupportsPose(state) && (!animator.IsInTransition(0) || SupportsPose(animator.GetNextAnimatorStateInfo(0)));
            // The imported left calf also twists in the jump clips. Keep its bind-frame
            // correction through take-off, flight and landing, including their crossfades.
            bool active = supported && !player.IsSwimming && !player.IsKneeling;
            weight = Mathf.MoveTowards(weight, active ? 1f : 0f, Time.deltaTime / .10f);
            if (weight > 0f) Stabilize(weight, Time.deltaTime);
        }

        static bool SupportsPose(AnimatorStateInfo state) => state.IsName("Pistol Locomotion") || state.IsName("Unarmed") ||
            state.IsName("Locomotion") || state.IsName("Standing Jump") || state.IsName("Moving Jump");

        public void Stabilize(float amount, float dt)
        {
            if (left == null) return;
            Solve(left, -1f, amount, dt); Solve(right, 1f, amount, dt);
        }

        void Solve(Leg leg, float side, float amount, float dt)
        {
            Vector3 ankle = leg.foot.position, hip = leg.thigh.position;
            Quaternion thighBefore = leg.thigh.rotation, calfBefore = leg.calf.rotation, footBefore = leg.foot.rotation;
            Quaternion toeBefore = leg.toe ? leg.toe.localRotation : Quaternion.identity;
            float upperLength = Vector3.Distance(hip, leg.calf.position);
            float lowerLength = Vector3.Distance(leg.calf.position, ankle);
            Vector3 direction = ankle - hip;
            float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(upperLength - lowerLength) + .001f,
                upperLength + lowerLength - .0005f);
            direction.Normalize();
            // Humanoid retargeting can send the left calf through a half turn.
            // A forward knee plane removes that roll without replacing the gait.
            Vector3 hint = transform.forward + transform.right * (side * .08f);
            Vector3 bend = Vector3.ProjectOnPlane(hint, direction).normalized;
            if (bend.sqrMagnitude < .1f) bend = transform.forward;
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            Vector3 knee = hip + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            float desiredPitch = 0f;
            if (leg.toe)
            {
                Vector3 toeDirection = transform.InverseTransformDirection(leg.toe.position - ankle).normalized;
                // Keep heel lift and toe-off, but never inherit the corrupted ankle yaw/roll.
                desiredPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(toeDirection.y, -1f, 1f)) * Mathf.Rad2Deg - 12f, -28f, 38f);
            }
            leg.pitch = Mathf.Lerp(leg.pitch, desiredPitch, 1f - Mathf.Exp(-Mathf.Max(0f, dt) * 28f));
            Quaternion upperFrame = transform.rotation * leg.thighFrame;
            Quaternion lowerFrame = transform.rotation * leg.calfFrame;
            Quaternion thighRotation = Quaternion.FromToRotation(upperFrame * leg.calf.localPosition, knee - hip) * upperFrame;
            leg.thigh.rotation = thighRotation;
            Quaternion calfRotation = Quaternion.FromToRotation(lowerFrame * leg.foot.localPosition, ankle - leg.calf.position) * lowerFrame;
            leg.calf.rotation = calfRotation;
            Quaternion footRotation = transform.rotation * Quaternion.Euler(leg.pitch, side * 5f, 0f) * leg.footFrame;
            // Blend only at posture boundaries. Every frame starts from Animator's fresh pose.
            leg.thigh.rotation = Quaternion.Slerp(thighBefore, thighRotation, amount);
            leg.calf.rotation = Quaternion.Slerp(calfBefore, calfRotation, amount);
            leg.foot.rotation = Quaternion.Slerp(footBefore, footRotation, amount);
            if (leg.toe) leg.toe.localRotation = Quaternion.Slerp(toeBefore, leg.toeRest, amount);
        }
    }
}
