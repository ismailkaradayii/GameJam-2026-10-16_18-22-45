using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Camera-aimed roof monitor. Input is supplied by the player controller.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class FireTruckWaterCannon : MonoBehaviour
    {
        public FireHoseSystem tank;
        public Transform yawPivot;
        public Transform pitchPivot;
        public Transform muzzle;
        public Camera aimCamera;
        public Transform ignoredCollisionRoot;
        public LineRenderer waterJet;
        public ParticleSystem sprayParticles;
        public ParticleSystem impactParticles;

        [Min(1f)] public float maxRange = 85f;
        [Min(0f)] public float flowLitresPerSecond = 35f;
        [Range(0f, 89f)] public float maximumElevation = 70f;
        [Range(0f, 89f)] public float maximumDepression = 25f;
        public LayerMask collisionMask = ~0;

        public bool IsSpraying { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public BurnableBuilding HitFire { get; private set; }
        public LavaCoolingSurface HitLava { get; private set; }

        private bool sprayRequested;
        private readonly RaycastHit[] rayHits = new RaycastHit[64];
        private readonly Collider[] overlapHits = new Collider[32];

        private void Awake()
        {
            if (tank == null)
                tank = GetComponentInParent<FireHoseSystem>();
            if (ignoredCollisionRoot == null)
                ignoredCollisionRoot = tank != null ? tank.transform : transform;
            StopEffects();
        }

        private void LateUpdate() => TickSpray(Time.deltaTime);

        public void SetSpraying(bool spraying)
        {
            sprayRequested = spraying;
            if (!spraying)
                StopEffects();
        }

        /// <summary>
        /// One aim and water step. For deterministic checks, disable this component and call
        /// this method directly; with no camera or pivots, muzzle.forward defines the aim.
        /// </summary>
        public void TickSpray(float deltaTime)
        {
            HitFire = null;
            HitLava = null;
            if (muzzle == null || !IsFinite(maxRange) || maxRange <= 0f)
            {
                StopEffects();
                return;
            }

            Vector3 target = CameraTarget();
            AimTurret(target);
            Vector3 origin = muzzle.position;
            Vector3 direction = muzzle.forward.normalized;
            Vector3 normal = -direction;
            Collider hitCollider = ContainingObstacle(origin);
            AimPoint = origin + direction * maxRange;

            // Unity rays do not report a collider containing their origin. Treat a muzzle
            // clipped into a wall as blocked, rather than letting its jet emerge beyond it.
            if (hitCollider != null)
            {
                AimPoint = origin;
            }
            else if (NearestHit(origin, direction, maxRange, out RaycastHit hit))
            {
                hitCollider = hit.collider;
                AimPoint = hit.point;
                normal = hit.normal;
            }
            if (hitCollider != null)
            {
                HitFire = hitCollider.GetComponentInParent<BurnableBuilding>();
                HitLava = hitCollider.GetComponentInParent<LavaCoolingSurface>();
            }

            float consumed = 0f;
            if (sprayRequested && tank != null && IsFinite(deltaTime) && deltaTime > 0f &&
                IsFinite(flowLitresPerSecond) && flowLitresPerSecond > 0f)
            {
                consumed = tank.ConsumeWater(flowLitresPerSecond * deltaTime);
                if (consumed > 0f && HitFire != null)
                    HitFire.ApplyWater(consumed);
                if (consumed > 0f && HitLava) HitLava.ApplyWater(AimPoint, consumed);
                if (consumed > 0f && hitCollider != null)
                {
                    var creature = hitCollider.GetComponentInParent<LavaCreatureActor>();
                    if (creature != null) creature.ApplyWater(consumed);
                }
            }

            // Apply the final partial amount, then show the empty-tank state immediately.
            IsSpraying = consumed > 0f && tank.TankLitres > 0f;
            DrawEffects(origin, direction, normal, hitCollider != null);
        }

        private Vector3 CameraTarget()
        {
            if (aimCamera == null)
                return muzzle.position + muzzle.forward * maxRange;

            Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float cameraRange = maxRange + Vector3.Distance(ray.origin, muzzle.position);
            Vector3 target = NearestHit(ray.origin, ray.direction, cameraRange, out RaycastHit hit)
                ? hit.point
                : ray.origin + ray.direction * cameraRange;
            return muzzle.position + Vector3.ClampMagnitude(target - muzzle.position, maxRange);
        }

        private void AimTurret(Vector3 target)
        {
            if (yawPivot != null)
            {
                Vector3 up = yawPivot.parent != null ? yawPivot.parent.up : Vector3.up;
                Vector3 flat = Vector3.ProjectOnPlane(target - yawPivot.position, up);
                if (flat.sqrMagnitude > 0.0001f)
                    yawPivot.rotation = Quaternion.LookRotation(flat.normalized, up);
            }
            if (pitchPivot != null)
            {
                Vector3 localDirection = pitchPivot.parent != null
                    ? pitchPivot.parent.InverseTransformDirection(target - pitchPivot.position)
                    : target - pitchPivot.position;
                float horizontal = new Vector2(localDirection.x, localDirection.z).magnitude;
                if (localDirection.sqrMagnitude > 0.0001f)
                {
                    float elevation = Mathf.Atan2(localDirection.y, horizontal) * Mathf.Rad2Deg;
                    pitchPivot.localRotation = Quaternion.Euler(
                        -Mathf.Clamp(elevation, -maximumDepression, maximumElevation), 0f, 0f);
                }
            }
        }

        private bool NearestHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, rayHits, distance,
                collisionMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = rayHits;
            // NonAlloc results are unsorted, and a full buffer may omit the nearest wall.
            if (count == rayHits.Length)
            {
                hits = Physics.RaycastAll(origin, direction, distance,
                    collisionMask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            nearest = default;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (IgnoreCollider(hit.collider) || hit.distance >= nearestDistance)
                    continue;
                nearest = hit;
                nearestDistance = hit.distance;
            }
            return !float.IsPositiveInfinity(nearestDistance);
        }

        private Collider ContainingObstacle(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, 0.03f, overlapHits,
                collisionMask, QueryTriggerInteraction.Ignore);
            Collider[] hits = overlapHits;
            if (count == overlapHits.Length)
            {
                hits = Physics.OverlapSphere(point, 0.03f, collisionMask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i];
                // ClosestPoint is unsupported for terrain, wheel and non-convex mesh
                // colliders. Those surfaces remain blockers in the regular ray trace.
                bool supportsClosestPoint = collider is BoxCollider || collider is SphereCollider ||
                    collider is CapsuleCollider || (collider is MeshCollider mesh && mesh.convex);
                if (supportsClosestPoint && !IgnoreCollider(collider) &&
                    (collider.ClosestPoint(point) - point).sqrMagnitude < 0.000001f)
                    return collider;
            }
            return null;
        }

        private bool IgnoreCollider(Collider collider)
        {
            return collider == null || collider is CharacterController ||
                (ignoredCollisionRoot != null && collider.transform.IsChildOf(ignoredCollisionRoot)) ||
                collider.GetComponentInParent<FireRescuePlayer>() != null;
        }

        private void DrawEffects(Vector3 origin, Vector3 direction, Vector3 hitNormal, bool hitSurface)
        {
            if (waterJet != null)
            {
                waterJet.enabled = IsSpraying;
                if (IsSpraying)
                {
                    waterJet.useWorldSpace = true;
                    waterJet.positionCount = 2;
                    waterJet.SetPosition(0, origin);
                    waterJet.SetPosition(1, AimPoint);
                }
            }
            if (sprayParticles != null && IsSpraying)
                sprayParticles.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            SetParticles(sprayParticles, IsSpraying);
            if (impactParticles != null && IsSpraying && hitSurface)
                impactParticles.transform.SetPositionAndRotation(AimPoint + hitNormal * 0.03f,
                    Quaternion.LookRotation(hitNormal.sqrMagnitude > 0.0001f ? hitNormal : -direction));
            SetParticles(impactParticles, IsSpraying && hitSurface);
        }

        private static void SetParticles(ParticleSystem particles, bool active)
        {
            if (particles == null)
                return;
            if (active)
            {
                if (!particles.isPlaying)
                    particles.Play(true);
            }
            else if (particles.isPlaying || particles.particleCount > 0)
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void StopEffects()
        {
            IsSpraying = false;
            if (waterJet != null)
                waterJet.enabled = false;
            SetParticles(sprayParticles, false);
            SetParticles(impactParticles, false);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void OnDisable()
        {
            sprayRequested = false;
            HitFire = null;
            StopEffects();
        }
    }
}
