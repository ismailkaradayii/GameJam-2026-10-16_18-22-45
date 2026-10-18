using System;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Finds standing room immediately beside a cab door, on the same side of every wall.</summary>
    public static class VehicleExitPlacement
    {
        public static bool TryFind(FireRescuePlayer player, out Vector3 feet)
        {
            feet = default;
            if (!player || !player.drive || !player.playerBody) return false;
            Physics.SyncTransforms();
            var truck = player.drive.transform;
            var body = player.playerBody;
            Vector3 scale = body.transform.lossyScale;
            float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(body.height * Mathf.Abs(scale.y), radius * 2f);
            Quaternion facing = Quaternion.Euler(0, truck.eulerAngles.y, 0);
            Vector3 centre = facing * Vector3.Scale(body.center, scale);
            Vector3 bottom = centre - Vector3.up * (height * .5f - radius);
            Vector3 top = centre + Vector3.up * (height * .5f - radius);
            Vector3 doorLocal = player.driverDoor ? truck.InverseTransformPoint(player.driverDoor.position) :
                player.exitPoint ? truck.InverseTransformPoint(player.exitPoint.position) : new Vector3(-3f, 1.8f, 0);
            float preferredSide = doorLocal.x > 0f ? 1f : -1f;
            Vector3 right = facing * Vector3.right, forward = facing * Vector3.forward;
            // The old exit marker can be inside a house. Search only within one body width of a door.
            float[] outward = { radius + .2f, radius + .65f, radius + 1.15f };
            float[] along = { 0f, radius * .5f, -radius * .5f, radius, -radius };
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? preferredSide : -preferredSide;
                Vector3 door = truck.TransformPoint(new Vector3(Mathf.Abs(doorLocal.x) * side, doorLocal.y, doorLocal.z));
                foreach (float distance in outward)
                foreach (float offset in along)
                {
                    Vector3 candidate = door + right * (side * distance) + forward * offset;
                    if (!TryGround(player, candidate, height, out Vector3 ground)) continue;
                    // A door marker can itself extend through a wall when parking tightly.
                    // Sweep from inside the cab, ignoring only the truck, so that wall is still caught.
                    Vector3 cab = truck.TransformPoint(new Vector3(0f, doorLocal.y, doorLocal.z));
                    if (!CanReach(player, cab, ground, bottom, top, radius)) continue;
                    feet = ground;
                    return true;
                }
            }
            // On its side/roof the authored doors can be underground, and the pivot
            // can be several metres above the floor. Search outside the actual chassis
            // envelope, using its lowest point as the ground reference instead.
            var chassis = player.drive.GetComponent<BoxCollider>();
            if (!chassis || Vector3.Dot(truck.up, Vector3.up) >= .65f) return false;
            Bounds bounds = chassis.bounds;
            Vector3 rolledDoor = truck.TransformPoint(doorLocal);
            Vector3 cabCentre = bounds.center + forward * Vector3.Dot(rolledDoor - bounds.center, forward);
            float halfWidth = Vector3.Dot(new Vector3(Mathf.Abs(right.x), 0, Mathf.Abs(right.z)), bounds.extents);
            float[] escapeAlong = { 0f, radius, -radius, height * .5f, -height * .5f };
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? preferredSide : -preferredSide;
                foreach (float distance in outward)
                foreach (float offset in escapeAlong)
                {
                    Vector3 candidate = cabCentre + right * (side * (halfWidth + distance)) + forward * offset;
                    candidate.y = bounds.max.y;
                    if (!TryGround(player, candidate, height, out Vector3 ground, bounds.min.y, bounds.size.y)) continue;
                    if (!CanReach(player, cabCentre, ground, bottom, top, radius)) continue;
                    feet = ground;
                    return true;
                }
            }
            return false;
        }

        static bool CanReach(FireRescuePlayer player, Vector3 cab, Vector3 ground, Vector3 bottom, Vector3 top, float radius)
        {
            if (InsideBuilding(ground, radius + .08f) || Overlaps(player, ground, bottom, top, radius + .05f, false)) return false;
            Vector3 start = new Vector3(cab.x, ground.y, cab.z);
            if (InsideBuilding(start, radius + .08f) || Overlaps(player, start, bottom, top, radius + .05f, true)) return false;
            Vector3 travel = ground - start;
            foreach (var hit in Physics.CapsuleCastAll(start + bottom, start + top, radius + .05f,
                travel.normalized, travel.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!Ignore(player, hit.collider, true)) return false;
            return true;
        }

        static bool TryGround(FireRescuePlayer player, Vector3 besideDoor, float height, out Vector3 feet,
            float? referenceY = null, float extraHeight = 0f)
        {
            feet = default;
            float step = player.playerBody.stepOffset * Mathf.Abs(player.playerBody.transform.lossyScale.y);
            Vector3 origin = besideDoor + Vector3.up * Mathf.Max(1.5f, step);
            var hits = Physics.RaycastAll(origin, Vector3.down, height + 5f + extraHeight, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (Ignore(player, hit.collider, true)) continue;
                // A roof, an unsupported ledge or an unwalkable slope cannot become an exit floor.
                float rise = hit.point.y - (referenceY ?? player.drive.transform.position.y);
                if (rise > Mathf.Max(1f, step) || rise < -Mathf.Max(2.5f, height * .5f) ||
                    hit.normal.y < Mathf.Cos(Mathf.Min(55f, player.playerBody.slopeLimit) * Mathf.Deg2Rad)) return false;
                feet = hit.point + Vector3.up * .12f;
                return true;
            }
            return false;
        }

        static bool InsideBuilding(Vector3 point, float gap)
        {
            foreach (var building in BurnableBuilding.ActiveBuildings)
            {
                if (!building || !building.ignitionBounds) continue;
                var box = building.ignitionBounds;
                Vector3 local = box.transform.InverseTransformPoint(point) - box.center;
                Vector3 scale = box.transform.lossyScale;
                Vector3 half = box.size * .5f;
                if (Mathf.Abs(local.x) < half.x + gap / Mathf.Max(.01f, Mathf.Abs(scale.x)) &&
                    Mathf.Abs(local.z) < half.z + gap / Mathf.Max(.01f, Mathf.Abs(scale.z))) return true;
            }
            return false;
        }

        static bool Overlaps(FireRescuePlayer player, Vector3 point, Vector3 bottom, Vector3 top, float radius, bool ignoreTruck)
        {
            foreach (var collider in Physics.OverlapCapsule(point + bottom, point + top, radius, ~0, QueryTriggerInteraction.Ignore))
                if (!Ignore(player, collider, ignoreTruck)) return true;
            return false;
        }

        static bool Ignore(FireRescuePlayer player, Collider collider, bool ignoreTruck) => !collider ||
            collider.transform.IsChildOf(player.playerBody.transform) ||
            ignoreTruck && collider.transform.IsChildOf(player.drive.transform) ||
            player.hose && player.hose.nozzle && collider.transform.IsChildOf(player.hose.nozzle);
    }
}
