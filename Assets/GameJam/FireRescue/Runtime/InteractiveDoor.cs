using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>A hinged leaf. Input belongs to FireRescuePlayer, so E has one owner.</summary>
    [DisallowMultipleComponent]
    public sealed class InteractiveDoor : MonoBehaviour
    {
        public BoxCollider leafCollider;
        [Range(-120f, 120f)] public float openAngle = 90f;
        [Min(.1f)] public float travelSeconds = .65f;
        [Tooltip("Small contact tolerance for the fitted leaf at its frame.")]
        [Min(0f)] public float contactTolerance = .008f;
        [SerializeField] Quaternion closedLocalRotation = Quaternion.identity;
        [SerializeField] bool closedPoseConfigured;

        public bool IsOpen => openness >= .999f;
        public bool IsMoving => Mathf.Abs(openness - (targetOpen ? 1f : 0f)) > .0001f;
        public bool LastBlocked { get; private set; }
        public Collider LastBlockingCollider { get; private set; }
        public float LastBlockingDepth { get; private set; }
        public float LastBlockingAngle { get; private set; }
        public float OpenAmount => openness;
        public Quaternion ClosedLocalRotation => closedLocalRotation;
        public string Prompt => (LastBlocked ? "Door blocked. Step back.  |  " : "") + (targetOpen ? "E: Close door" : "E: Open door");
        readonly Collider[] overlaps = new Collider[64];
        bool targetOpen;
        float openness;

        void Awake()
        {
            if (!leafCollider) leafCollider = GetComponent<BoxCollider>();
            if (!closedPoseConfigured) closedLocalRotation = transform.localRotation;
            // Scene-local state: a fresh game always starts in the authored closed pose.
            openness = 0f; targetOpen = false; LastBlocked = false;
            transform.localRotation = closedLocalRotation;
        }

        /// <summary>Explicit authoring entry point; never searches for or changes scene objects.</summary>
        public void ConfigureClosedPose(Quaternion pose, BoxCollider leaf)
        {
            closedLocalRotation = pose; closedPoseConfigured = true; leafCollider = leaf;
            openness = 0f; targetOpen = false; LastBlocked = false;
            transform.localRotation = pose;
        }

        public bool TryToggle()
        {
            if (!isActiveAndEnabled || Time.timeScale <= 0f || !leafCollider || !leafCollider.enabled) return false;
            bool next = !targetOpen;
            Physics.SyncTransforms();
            if (SweepBlocked(openness, next ? 1f : 0f)) { LastBlocked = true; return false; }
            targetOpen = next; LastBlocked = false;
            return true;
        }

        void FixedUpdate() => TickMotion(Time.fixedDeltaTime);

        /// <summary>One scaled motion step, shared with deterministic physics verification.</summary>
        public void TickMotion(float deltaTime)
        {
            if (Time.timeScale <= 0f || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || !IsMoving) return;
            float next = Mathf.MoveTowards(openness, targetOpen ? 1f : 0f, deltaTime / Mathf.Max(.1f, travelSeconds));
            // Recheck every step: a person or object can enter after the initial sweep.
            if (SweepBlocked(openness, next))
            {
                LastBlocked = true;
                // Stop safely and head back to the open side if closing becomes obstructed.
                if (!targetOpen) targetOpen = true;
                return;
            }
            openness = next;
            transform.localRotation = RotationAt(openness);
            LastBlocked = false;
        }

        Quaternion RotationAt(float amount) => closedLocalRotation * Quaternion.AngleAxis(openAngle * amount, Vector3.up);

        bool SweepBlocked(float from, float to)
        {
            LastBlockingCollider = null; LastBlockingDepth = 0f; LastBlockingAngle = 0f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs((to - from) * openAngle) / 2f));
            for (int i = 1; i <= steps; i++)
                if (BlockedAt(Mathf.Lerp(from, to, i / (float)steps))) return true;
            return false;
        }

        bool BlockedAt(float amount)
        {
            if (!leafCollider) return true;
            Quaternion rotation = transform.parent ? transform.parent.rotation * RotationAt(amount) : RotationAt(amount);
            Vector3 scale = transform.lossyScale;
            Vector3 center = transform.position + rotation * Vector3.Scale(leafCollider.center, scale);
            Vector3 half = Vector3.Scale(leafCollider.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            int count = Physics.OverlapBoxNonAlloc(center, half, overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return true; // Crowded areas fail closed, never miss a blocker.
            for (int i = 0; i < count; i++)
            {
                Collider other = overlaps[i];
                if (!other || other.transform.IsChildOf(transform)) continue;
                if (Physics.ComputePenetration(leafCollider, transform.position, rotation,
                    other, other.transform.position, other.transform.rotation, out _, out float distance) && distance > contactTolerance)
                {
                    LastBlockingCollider = other; LastBlockingDepth = distance; LastBlockingAngle = amount * openAngle;
                    return true;
                }
            }
            return false;
        }
    }
}
