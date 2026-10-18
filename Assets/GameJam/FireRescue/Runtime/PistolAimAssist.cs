using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Optional soft camera attraction. Bullets still use the posed barrel and ordinary collisions.</summary>
    [DisallowMultipleComponent]
    public sealed class PistolAimAssist : MonoBehaviour
    {
        public FireRescuePlayer player;
        [Range(1f, 30f)] public float acquisitionCone = 16f;
        [Range(1f, 45f)] public float retentionCone = 23f;
        [Min(1f)] public float maximumDistance = 75f;
        [Min(1f)] public float maximumTurnSpeed = 85f;
        [Min(.1f)] public float acquisitionStrength = 10f;
        [Min(.1f)] public float trackingStrength = 10f;
        public LavaCreatureActor Target { get; private set; }
        public Vector3 TargetPoint { get; private set; }

        readonly Dictionary<LavaCreatureActor, Collider[]> colliders = new Dictionary<LavaCreatureActor, Collider[]>();
        readonly RaycastHit[] hits = new RaycastHit[64];
        LavaCreatureActor[] candidates = System.Array.Empty<LavaCreatureActor>();
        float nextSearch, aimAge, overrideUntil;
        bool wasAiming;

        void Awake() { if (!player) player = GetComponent<FireRescuePlayer>(); }
        void OnDisable() => Release();
        public void Release() { Target = null; wasAiming = false; aimAge = 0; overrideUntil = 0; }

        /// <summary>Called by the input owner after mouse look, before movement/view presentation.</summary>
        public void AdjustLook(ref float yaw, ref float pitch, bool aimHeld, Vector2 mouseDegrees, float deltaTime)
        {
            var pistol = player ? player.pistol : null;
            if (!isActiveAndEnabled || !player || player.IsDriving || player.GameplaySuspended || player.InteractionBusy ||
                !pistol || !pistol.Available || !pistol.Ready || pistol.IsReloading || !aimHeld ||
                !player.viewCamera || (player.firstPerson && (player.firstPerson.IsSprinting || player.firstPerson.IsSwimming || !player.firstPerson.InputCaptured)) ||
                Time.timeScale <= 0 || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            { Release(); return; }

            float mouseSpeed = mouseDegrees.magnitude / Mathf.Max(.001f, deltaTime);
            // A deliberate mouse sweep always wins. Releasing/repressing RMB can acquire another target.
            if (mouseSpeed > 125f)
            { Target = null; overrideUntil = Time.time + .18f; wasAiming = true; return; }
            if (Time.time < overrideUntil) return;
            if (!wasAiming) { Target = null; aimAge = 0; nextSearch = 0; }
            wasAiming = true; aimAge += deltaTime;
            Quaternion view = Quaternion.Euler(pitch, yaw, 0);
            Vector3 origin = player.viewCamera.transform.position;
            if (Time.time >= nextSearch)
            {
                candidates = FindObjectsByType<LavaCreatureActor>(FindObjectsSortMode.None);
                nextSearch = Time.time + .15f;
                // The finite encounter may replace actors on scene reuse; bound this cache to the live roster.
                colliders.Clear();
            }
            if (!TryCandidate(Target, origin, view, retentionCone, out var point))
            {
                Target = null; float best = float.PositiveInfinity;
                foreach (var actor in candidates)
                {
                    if (!TryCandidate(actor, origin, view, acquisitionCone, out var candidate)) continue;
                    Vector3 delta = candidate - origin;
                    float score = Vector3.Angle(view * Vector3.forward, delta) + delta.magnitude * .025f;
                    if (score >= best) continue;
                    best = score; Target = actor; point = candidate;
                }
            }
            if (!Target) return;
            TargetPoint = point;
            Vector3 desired = point - origin;
            Quaternion goal = Quaternion.LookRotation(desired, Vector3.up);
            float strength = aimAge < .3f ? acquisitionStrength : trackingStrength;
            float inputWeight = 1f - Mathf.Clamp01(mouseSpeed / 125f);
            Quaternion softened = Quaternion.Slerp(view, goal, 1f - Mathf.Exp(-strength * inputWeight * deltaTime));
            Quaternion result = Quaternion.RotateTowards(view, softened, maximumTurnSpeed * inputWeight * deltaTime);
            yaw += Mathf.DeltaAngle(yaw, result.eulerAngles.y);
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0, result.eulerAngles.x), -75f, 78f);
        }

        bool TryCandidate(LavaCreatureActor actor, Vector3 origin, Quaternion view, float cone, out Vector3 point)
        {
            point = default;
            if (!actor || !actor.isActiveAndEnabled || actor.IsDefeated || actor.Health <= 0) return false;
            if (!colliders.TryGetValue(actor, out var shapes))
            { shapes = actor.GetComponentsInChildren<Collider>(); colliders[actor] = shapes; }
            Collider torso = null; float largest = 0;
            foreach (var shape in shapes)
            {
                if (!shape || !shape.enabled || shape.isTrigger || !shape.gameObject.activeInHierarchy) continue;
                Vector3 size = shape.bounds.size; float volume = size.x * size.y * size.z;
                if (volume > largest) { largest = volume; torso = shape; }
            }
            if (!torso) return false;
            Bounds bounds = torso.bounds;
            point = bounds.center + Vector3.up * (bounds.extents.y * .15f);
            Vector3 delta = point - origin;
            if (delta.sqrMagnitude < .04f || delta.sqrMagnitude > maximumDistance * maximumDistance ||
                Vector3.Angle(view * Vector3.forward, delta) > cone) return false;
            return HasLineOfSight(origin, point, actor);
        }
        bool HasLineOfSight(Vector3 origin, Vector3 point, LavaCreatureActor actor)
        {
            Vector3 delta = point - origin;
            int mask = Physics.DefaultRaycastLayers;
            int handsLayer = LayerMask.NameToLayer("FirstPersonHands");
            if (handsLayer >= 0) mask &= ~(1 << handsLayer);
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, hits, delta.magnitude + .05f, mask, QueryTriggerInteraction.Ignore);
            RaycastHit[] samples = hits;
            if (count == hits.Length) { samples = Physics.RaycastAll(origin, delta.normalized, delta.magnitude + .05f, mask, QueryTriggerInteraction.Ignore); count = samples.Length; }
            Collider nearest = null; float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = samples[i];
                if (hit.collider.transform.IsChildOf(player.transform) || hit.distance >= distance) continue;
                nearest = hit.collider; distance = hit.distance;
            }
            return nearest && nearest.GetComponentInParent<LavaCreatureActor>() == actor;
        }
    }
}
