using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace GameJam.FireRescue
{
    // Routes use the oriented house footprints, including doorways and missing mesh colliders.
    // The small village graph is rebuilt only when the destination changes or a route expires.
    public sealed class SpiritOutdoorNavigation
    {
        public const float ArrivalDistance = .4f;
        readonly List<Vector3> route = new List<Vector3>();
        readonly List<Vector3> nodes = new List<Vector3>();
        NavMeshPath navPath;
        Vector3 destination;
        BoxCollider vehicle;
        Vector3 vehiclePosition;
        Quaternion vehicleRotation;
        float refreshAt;
        int corner;

        public void Invalidate() { refreshAt = 0; route.Clear(); corner = 0; }

        public Vector3 Next(Vector3 from, Vector3 goal, float clearance, BoxCollider truck = null)
        {
            if (vehicle != truck || (truck && (Vector3.Distance(vehiclePosition, truck.transform.position) > .5f ||
                Quaternion.Angle(vehicleRotation, truck.transform.rotation) > 5f))) Invalidate();
            vehicle = truck;
            goal = Outside(goal, from, clearance + .15f, vehicle);
            if (route.Count == 0 || Time.time >= refreshAt || Flat(goal, destination) > 2.5f)
                Build(from, goal, clearance);
            while (corner < route.Count - 1 && Flat(from, route[corner]) < ArrivalDistance) corner++;
            // Skip already passed corners only when the shortcut cannot cross a house.
            while (corner + 1 < route.Count && !Blocked(from, route[corner + 1], clearance, vehicle)) corner++;
            return corner < route.Count ? route[corner] : goal;
        }

        void Build(Vector3 from, Vector3 goal, float clearance)
        {
            route.Clear(); corner = 0; destination = goal; refreshAt = Time.time + 2.2f;
            if (vehicle) { vehiclePosition = vehicle.transform.position; vehicleRotation = vehicle.transform.rotation; }
            if (!Blocked(from, goal, clearance, vehicle)) { route.Add(goal); return; }
            if (navPath == null) navPath = new NavMeshPath();
            if (NavMesh.SamplePosition(from, out var a, 3f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(goal, out var b, 3f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, navPath) &&
                navPath.status == NavMeshPathStatus.PathComplete)
            {
                Vector3 previous = from;
                bool valid = true;
                var corners = navPath.corners;
                for (int i = 1; i < corners.Length; i++)
                {
                    if (Blocked(previous, corners[i], clearance, vehicle)) { valid = false; break; }
                    previous = corners[i];
                }
                if (valid && !Blocked(previous, goal, clearance, vehicle))
                { for (int i = 1; i < corners.Length; i++) route.Add(corners[i]); route.Add(goal); return; }
            }
            nodes.Clear(); nodes.Add(from); nodes.Add(goal);
            foreach (var house in BurnableBuilding.ActiveBuildings)
            {
                if (!house || !house.ignitionBounds) continue;
                AddCorners(house.ignitionBounds);
            }
            if (vehicle) AddCorners(vehicle);
            int count = nodes.Count;
            var cost = new float[count]; var parent = new int[count]; var visited = new bool[count];
            for (int i = 0; i < count; i++) { cost[i] = float.PositiveInfinity; parent[i] = -1; }
            cost[0] = 0;
            for (int step = 0; step < count; step++)
            {
                int current = -1; float best = float.PositiveInfinity;
                for (int i = 0; i < count; i++) if (!visited[i] && cost[i] < best) { current = i; best = cost[i]; }
                if (current < 0 || current == 1) break;
                visited[current] = true;
                for (int i = 1; i < count; i++)
                {
                    if (visited[i] || i == current || Blocked(nodes[current], nodes[i], clearance, vehicle)) continue;
                    float score = cost[current] + Flat(nodes[current], nodes[i]);
                    if (score < cost[i]) { cost[i] = score; parent[i] = current; }
                }
            }
            if (parent[1] >= 0)
            {
                for (int i = 1; i > 0; i = parent[i]) route.Add(nodes[i]);
                route.Reverse();
            }
            // Stop outside an unreachable destination; local steering can retry after props move.
            else route.Add(from);

            void AddCorners(BoxCollider box)
            {
                Vector3 half = ExpandedHalf(box, clearance + .35f);
                for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 p = box.transform.TransformPoint(box.center + new Vector3(x * half.x, 0, z * half.z));
                    p.y = from.y;
                    if (!Contains(p, clearance, vehicle)) nodes.Add(p);
                }
            }
        }

        public static Vector3 Exterior(BoxCollider box, Vector3 from, float gap)
        {
            Vector3 local = box.transform.InverseTransformPoint(from) - box.center;
            Vector3 half = ExpandedHalf(box, gap);
            local.x = Mathf.Clamp(local.x, -half.x, half.x);
            local.z = Mathf.Clamp(local.z, -half.z, half.z);
            float dx = half.x - Mathf.Abs(local.x), dz = half.z - Mathf.Abs(local.z);
            if (dx < dz) local.x = (local.x >= 0 ? 1 : -1) * half.x;
            else local.z = (local.z >= 0 ? 1 : -1) * half.z;
            Vector3 result = box.transform.TransformPoint(box.center + local); result.y = from.y;
            return result;
        }

        public static Vector3 Outside(Vector3 point, Vector3 observer, float gap, BoxCollider vehicle = null)
        {
            if (!Contains(point, gap, vehicle)) return point;
            Vector3 result = point; float best = float.PositiveInfinity;
            foreach (var house in BurnableBuilding.ActiveBuildings)
            {
                if (!house || !house.ignitionBounds) continue;
                ConsiderBox(house.ignitionBounds);
            }
            if (vehicle) ConsiderBox(vehicle);
            return result;

            void ConsiderBox(BoxCollider box)
            {
                Vector3 closest = Exterior(box, observer, gap + .15f);
                Consider(closest);
                Vector3 half = ExpandedHalf(box, gap + .2f);
                for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = box.transform.TransformPoint(box.center + new Vector3(x * half.x, 0, z * half.z));
                    corner.y = point.y; Consider(corner);
                }
            }
            void Consider(Vector3 candidate)
            {
                if (Contains(candidate, gap, vehicle)) return;
                float cost = Flat(candidate, point) + Flat(candidate, observer) * .3f;
                if (cost < best) { best = cost; result = candidate; }
            }
        }

        public static bool Contains(Vector3 point, float gap, BoxCollider vehicle = null)
        {
            foreach (var house in BurnableBuilding.ActiveBuildings)
                if (house && house.ignitionBounds && Inside(house.ignitionBounds, point, gap)) return true;
            return vehicle && Inside(vehicle, point, gap);
        }

        public static bool Inside(BoxCollider box, Vector3 point, float gap)
        {
            Vector3 p = box.transform.InverseTransformPoint(point) - box.center, h = ExpandedHalf(box, gap);
            return Mathf.Abs(p.x) < h.x && Mathf.Abs(p.z) < h.z;
        }

        public static bool Blocked(Vector3 from, Vector3 to, float gap, BoxCollider vehicle = null)
        {
            foreach (var house in BurnableBuilding.ActiveBuildings)
            {
                if (!house || !house.ignitionBounds) continue;
                if (Crosses(house.ignitionBounds, false)) return true;
            }
            return vehicle && Crosses(vehicle, true);

            bool Crosses(BoxCollider box, bool allowEscape)
            {
                Vector3 a = box.transform.InverseTransformPoint(from) - box.center;
                Vector3 b = box.transform.InverseTransformPoint(to) - box.center;
                Vector3 half = ExpandedHalf(box, gap);
                bool insideMargin = Mathf.Abs(a.x) < half.x && Mathf.Abs(a.z) < half.z;
                Vector3 solid = box.size * .5f;
                bool outsideSolid = Mathf.Abs(a.x) >= solid.x || Mathf.Abs(a.z) >= solid.z;
                // A spawn or separation step can start inside the clearance margin. Let it
                // escape outwards; rejecting every direction traps it against an invisible wall.
                if (insideMargin && (outsideSolid || allowEscape))
                {
                    float before = Mathf.Min(half.x - Mathf.Abs(a.x), half.z - Mathf.Abs(a.z));
                    float after = Mathf.Min(half.x - Mathf.Abs(b.x), half.z - Mathf.Abs(b.z));
                    if (after < before - .0001f && (allowEscape || a.x * (b.x - a.x) + a.z * (b.z - a.z) > 0f)) return false;
                }
                float enter = 0, exit = 1;
                if (Slab(a.x, b.x - a.x, half.x, ref enter, ref exit) &&
                    Slab(a.z, b.z - a.z, half.z, ref enter, ref exit)) return true;
                return false;
            }
        }

        static bool Slab(float start, float delta, float half, ref float enter, ref float exit)
        {
            if (Mathf.Abs(delta) < .0001f) return Mathf.Abs(start) < half;
            float a = (-half - start) / delta, b = (half - start) / delta;
            enter = Mathf.Max(enter, Mathf.Min(a, b)); exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter < exit && exit > .0001f && enter < .9999f;
        }

        static Vector3 ExpandedHalf(BoxCollider box, float gap)
        {
            Vector3 scale = box.transform.lossyScale;
            return box.size * .5f + new Vector3(gap / Mathf.Max(.01f, Mathf.Abs(scale.x)), 0,
                gap / Mathf.Max(.01f, Mathf.Abs(scale.z)));
        }
        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
