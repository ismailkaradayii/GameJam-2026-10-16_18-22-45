using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>A committed ballistic rise that clears the eaves before crossing onto the roof.</summary>
    public sealed class SpiritRoofFlight
    {
        const float Gravity = 28f;
        readonly RaycastHit[] hits = new RaycastHit[64];
        Vector3 start, end;
        float launchSpeed, crossAt;
        public Vector3 RoofPoint { get; private set; }
        public float Duration { get; private set; }
        public bool HasRoofCollider { get; private set; }

        public void Plan(BurnableBuilding building, Vector3 from, float extraHeight, float bodyRadius)
        {
            var box = building.ignitionBounds;
            Bounds bounds = box.bounds;
            var solids = building.GetComponentsInChildren<Collider>();
            Vector3 local = box.transform.InverseTransformPoint(from) - box.center;
            // Aim inside the near half of the roof, away from the ridge/chimney and eaves.
            local = new Vector3(Mathf.Clamp(local.x, -box.size.x * .22f, box.size.x * .22f),
                box.size.y * .5f, Mathf.Clamp(local.z, -box.size.z * .22f, box.size.z * .22f));
            RoofPoint = box.transform.TransformPoint(box.center + local);
            RoofPoint = new Vector3(RoofPoint.x, bounds.max.y, RoofPoint.z);
            HasRoofCollider = false;
            float best = float.PositiveInfinity, roofCeiling = bounds.max.y;
            for (int sample = 0; sample < 5; sample++)
            {
                Vector3 probe = sample == 0 ? RoofPoint : Vector3.Lerp(RoofPoint, bounds.center, sample * .18f);
                probe.y = bounds.max.y + 8f;
                Ray ray = new Ray(probe, Vector3.down);
                foreach (var solid in solids)
                {
                    if (!solid || !solid.enabled || !solid.gameObject.activeInHierarchy || solid.isTrigger || solid is CharacterController) continue;
                    if (!solid.Raycast(ray, out var hit, bounds.size.y + 16f) || hit.normal.y < .25f ||
                        hit.point.y < bounds.min.y + bounds.size.y * .45f) continue;
                    float score = sample * 20f + hit.distance;
                    if (score >= best) continue;
                    best = score; RoofPoint = hit.point; HasRoofCollider = true;
                    roofCeiling = Mathf.Max(hit.point.y, solid.bounds.max.y);
                }
            }
            start = from;
            // The swept body reaches the surface before this slightly inset endpoint.
            end = RoofPoint + Vector3.up * (bodyRadius - .08f);
            float apex = Mathf.Max(start.y + 4f, roofCeiling + Mathf.Max(2f, extraHeight));
            launchSpeed = Mathf.Sqrt(2f * Gravity * (apex - start.y));
            Duration = launchSpeed / Gravity + Mathf.Sqrt(2f * (apex - end.y) / Gravity);
            float clearance = Mathf.Max(0f, roofCeiling + .8f - start.y);
            crossAt = (launchSpeed - Mathf.Sqrt(Mathf.Max(0f, launchSpeed * launchSpeed - 2f * Gravity * clearance))) / Gravity;
        }

        public Vector3 Position(float seconds)
        {
            float t = Mathf.Clamp(seconds, 0, Duration);
            // Hold outside the wall during the initial vertical thrust, then accelerate across it.
            float across = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(crossAt * .8f, Duration, t));
            Vector3 result = Vector3.Lerp(start, end, across);
            result.y = start.y + launchSpeed * t - .5f * Gravity * t * t;
            return result;
        }

        public Vector3 Velocity(float seconds) =>
            (Position(Mathf.Min(Duration, seconds + .01f)) - Position(Mathf.Max(0, seconds - .01f))).normalized;

        public bool Sweep(Vector3 from, Vector3 to, Transform owner, float radius, out RaycastHit closest)
        {
            closest = default;
            Vector3 travel = to - from;
            if (travel.sqrMagnitude < .000001f) return false;
            int count = Physics.SphereCastNonAlloc(from, radius, travel.normalized,
                hits, travel.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var obstacle = hits[i].collider;
                if (!obstacle || obstacle.transform.IsChildOf(owner) || obstacle.GetComponentInParent<LavaCreatureActor>() ||
                    obstacle.GetComponentInParent<LavaIgnitionSource>() || obstacle.GetComponentInParent<ProgressiveLavaFlow>() ||
                    obstacle.GetComponentInParent<LavaCoolingSurface>()) continue;
                if (hits[i].distance >= distance) continue;
                closest = hits[i]; distance = closest.distance;
            }
            return distance < float.PositiveInfinity;
        }
    }
}
