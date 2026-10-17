using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>One current destination: world ring, projected label or a screen-edge arrow.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(270)]
    public sealed class ObjectiveNavigation : MonoBehaviour
    {
        public VolcanoMissionDirector mission;
        public LineRenderer ring;
        public RectTransform marker;
        public Text label, arrow;
        public Canvas canvas;
        [Min(1f)] public float objectiveRadius = 3f;
        [Min(1f)] public float shelterRadius = 8f;
        [Min(10f)] public float edgeMargin = 70f;
        public Color objectiveColor = new Color(.18f, .7f, 1f);
        public Color shelterColor = new Color(.2f, 1f, .45f);
        public Color dangerColor = new Color(1f, .28f, .12f);
        public Transform[] lakeRoad = System.Array.Empty<Transform>();
        public Transform[] lakeTurnaround = System.Array.Empty<Transform>();
        [Min(10f)] public float drivingMarkerLookAhead = 32f;
        int turnWaypoint;
        bool followingTurn, completedTurn;
        public Transform CurrentTarget { get; private set; }
        public string CurrentLabel { get; private set; }
        readonly Vector3[] points = new Vector3[65];
        void LateUpdate()
        {
            if (!mission || !mission.player || !mission.player.viewCamera) return;
            Color color = objectiveColor; float radius = objectiveRadius;
            CurrentTarget = SelectTarget(ref color, ref radius);
            bool visible = CurrentTarget && !mission.player.InteractionBusy && !VolcanoCinematic.IsPlayingAny && (!mission.gameFlow || !mission.gameFlow.BlocksGameplay);
            if (ring) ring.enabled = visible;
            if (marker) marker.gameObject.SetActive(visible);
            if (!visible) return;
            var player = mission.player;
            Vector3 world = CurrentTarget.position;
            if (ring)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    float angle = i * Mathf.PI * 2 / (points.Length - 1);
                    Vector3 p = world + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    if (CurrentTarget != (mission.weaponPickup ? mission.weaponPickup.transform : null) &&
                        CurrentTarget != (mission.villageAlarm ? mission.villageAlarm.contactPoint : null))
                        foreach (var terrain in Terrain.activeTerrains)
                            if (terrain && p.x >= terrain.transform.position.x && p.z >= terrain.transform.position.z &&
                                p.x <= terrain.transform.position.x + terrain.terrainData.size.x && p.z <= terrain.transform.position.z + terrain.terrainData.size.z)
                                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                    points[i] = p + Vector3.up * .18f;
                }
                ring.widthMultiplier = radius < 1 ? .07f : .22f;
                ring.positionCount = points.Length; ring.SetPositions(points);
                ring.startColor = ring.endColor = color * (1.1f + .15f * Mathf.Sin(Time.time * 3));
            }
            if (!marker || !canvas) return;
            Vector3 projected = player.viewCamera.WorldToViewportPoint(world + Vector3.up * (player.IsDriving ? 8f : 2f));
            Vector2 direction = new Vector2(projected.x - .5f, projected.y - .5f);
            if (projected.z < 0) direction = -direction;
            if (direction.sqrMagnitude < .001f) direction = Vector2.down;
            Vector2 size = ((RectTransform)canvas.transform).rect.size;
            Vector2 pos = new Vector2((projected.x - .5f) * size.x, (projected.y - .5f) * size.y);
            float maxX = size.x * .5f - edgeMargin - 60, maxY = size.y * .5f - edgeMargin - 80;
            bool offscreen = projected.z <= 0 || Mathf.Abs(pos.x) > maxX || Mathf.Abs(pos.y) > maxY;
            if (offscreen)
            {
                direction.Normalize();
                float edge = Mathf.Min(maxX / Mathf.Max(.001f, Mathf.Abs(direction.x)), maxY / Mathf.Max(.001f, Mathf.Abs(direction.y)));
                pos = direction * edge;
            }
            marker.anchoredPosition = pos;
            // Keep distant guidance and world rings; avoid a second boxed label on a nearby prompt.
            if (player.contextualInteractions && player.contextualInteractions.isActiveAndEnabled &&
                player.contextualInteractions.ReplacesNavigationLabel(CurrentTarget)) marker.gameObject.SetActive(false);
            if (label) { label.color = color; label.text = CurrentLabel + "\n" + Mathf.RoundToInt(Vector3.Distance(player.transform.position, world)) + " m"; }
            if (arrow)
            {
                arrow.text = offscreen ? "\u25B2" : "\u25C6"; arrow.color = color;
                arrow.rectTransform.localRotation = offscreen ? Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90) : Quaternion.identity;
            }
        }
        Transform SelectTarget(ref Color color, ref float radius)
        {
            var player = mission.player;
            if (mission.tank && mission.tank.Tank01 < .05f) { followingTurn = completedTurn = false; turnWaypoint = 0; }
            if (mission.State == VolcanoMissionDirector.FlowState.Intro) return null;
            if (!player.HasPistol && mission.weaponPickup && !mission.weaponPickup.IsCollected)
            { CurrentLabel = "PISTOL | SLOT 1"; radius = .75f; return mission.weaponPickup.transform; }
            if (!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.WarnVillage && mission.villageAlarm && !mission.villageAlarm.Activated)
            { CurrentLabel = "KÖY ALARMI | " + (player.contextualInteractions ? player.contextualInteractions.Label(InteractionInput.Interact) : "E"); radius = .45f; return mission.villageAlarm.contactPoint; }
            if (!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.Weapon)
            { CurrentLabel = "LEAVE THE HOUSE | E: DOOR"; return mission.houseExit; }
            if (!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate)
            {
                Transform next = null; float distance = float.PositiveInfinity;
                foreach (var resident in mission.evacuation.residents)
                {
                    if (!resident || resident.IsCalled || !resident.houseExit) continue;
                    float d = (resident.houseExit.position - player.transform.position).sqrMagnitude;
                    if (d < distance) { distance = d; next = resident.houseExit; }
                }
                if (next) { CurrentLabel = "VILLAGERS | Q: CALL"; return next; }
                CurrentLabel = "SHELTER | LEAD THE VILLAGERS"; color = shelterColor; radius = shelterRadius;
                return mission.evacuation.ShelterOpened ? mission.evacuation.shelterEntrance : mission.evacuation.shelterApproach;
            }
            if (!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.Truck)
            { CurrentLabel = "FIRE TRUCK | E: ENTER"; radius = 5; return player.driverDoor; }
            if (mission.fillStation && ((!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.Refill) || mission.EruptionTriggered && mission.tank.Tank01 < .05f))
            {
                if (!player.IsDriving) { CurrentLabel = "FIRE TRUCK | E: ENTER"; radius = 5; return player.driverDoor; }
                CurrentLabel = "LAKE | REFILL WATER"; radius = mission.fillStation.parkingRadius;
                if (player.IsDriving && lakeRoad.Length > 1 && !mission.fillStation.InRange)
                {
                    int nearest = 0; float distance = float.PositiveInfinity;
                    for (int i = 0; i < lakeRoad.Length; i++) if (lakeRoad[i])
                    { float d = (lakeRoad[i].position - player.drive.transform.position).sqrMagnitude; if (d < distance) { nearest = i; distance = d; } }
                    // Road is stored from the lake toward the village.
                    if (nearest > 1) { CurrentLabel = "LAKE | FOLLOW THE ROAD"; radius = 5; return RoadAhead(nearest, -1); }
                }
                return mission.fillStation.transform;
            }
            if (player.IsDriving && mission.fillStation && lakeTurnaround.Length > 0)
            {
                if (!followingTurn && !completedTurn && mission.fillStation.InRange && mission.tank && mission.tank.Tank01 >= .999f)
                { followingTurn = true; turnWaypoint = 0; }
                while (followingTurn && turnWaypoint < lakeTurnaround.Length && (!lakeTurnaround[turnWaypoint] ||
                    EvacuationResident.FlatDistance(player.drive.transform.position, lakeTurnaround[turnWaypoint].position) < 12)) turnWaypoint++;
                if (followingTurn && turnWaypoint >= lakeTurnaround.Length) { followingTurn = false; completedTurn = true; }
                if (followingTurn)
                { CurrentLabel = "TURNING AREA | RETURN TO VILLAGE"; radius = 6; return lakeTurnaround[turnWaypoint]; }
            }
            if (player.IsDriving && mission.houseExit && lakeRoad.Length > 1 &&
                (EvacuationResident.FlatDistance(player.drive.transform.position, mission.houseExit.position) > 140 ||
                 !mission.EruptionTriggered && mission.PreparationComplete && lakeRoad[lakeRoad.Length - 1] &&
                 EvacuationResident.FlatDistance(player.drive.transform.position, lakeRoad[lakeRoad.Length - 1].position) > 25))
            {
                int nearest = 0; float distance = float.PositiveInfinity;
                for (int i = 0; i < lakeRoad.Length; i++) if (lakeRoad[i])
                { float d = (lakeRoad[i].position - player.drive.transform.position).sqrMagnitude; if (d < distance) { nearest = i; distance = d; } }
                if (nearest < lakeRoad.Length - 1)
                { CurrentLabel = "VILLAGE | FOLLOW THE ROAD"; radius = 5; return RoadAhead(nearest, 1); }
            }
            BurnableBuilding urgent = null; float priority = float.PositiveInfinity;
            foreach (var house in BurnableBuilding.ActiveBuildings)
            {
                if (!house || !house.IsBurning) continue;
                float score = house.SecondsUntilCollapse + Vector3.Distance(player.transform.position, house.transform.position) * .06f;
                if (score < priority) { priority = score; urgent = house; }
            }
            if (urgent) { color = dangerColor; radius = 7; CurrentLabel = "HOUSE ON FIRE"; return urgent.transform; }
            if (mission.encounter)
                foreach (var actor in mission.encounter.LivingCreatures) if (actor && !actor.IsDefeated)
                { color = dangerColor; CurrentLabel = "FIRE SPIRIT"; return actor.transform; }
            CurrentLabel = "VILLAGE | GET READY";
            return mission.houseExit;
        }
        Transform RoadAhead(int index, int direction)
        {
            float distance = 0;
            Transform target = lakeRoad[index];
            for (int next = index + direction; next >= 0 && next < lakeRoad.Length; next += direction)
            {
                if (!lakeRoad[next]) continue;
                if (target) distance += EvacuationResident.FlatDistance(target.position, lakeRoad[next].position);
                target = lakeRoad[next];
                if (distance >= drivingMarkerLookAhead) break;
            }
            return target;
        }
    }
}
