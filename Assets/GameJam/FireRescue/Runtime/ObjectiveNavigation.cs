using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Glowing circles mark destinations; road arrows guide the truck between them.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(270)]
    public sealed class ObjectiveNavigation : MonoBehaviour
    {
        public VolcanoMissionDirector mission;
        public LineRenderer ring;
        public RectTransform marker;
        public Text label, arrow;
        public Image markerGlow;
        public ObjectiveArrowGraphic directionArrow;
        public Canvas canvas;
        public bool compactLabels;
        string previousLabel, shortLabel;
        [Min(1f)] public float objectiveRadius = 3f;
        [Min(1f)] public float shelterRadius = 8f;
        [Min(10f)] public float edgeMargin = 70f;
        public Color objectiveColor = new Color(.18f, .7f, 1f);
        public Color shelterColor = new Color(.2f, 1f, .45f);
        public Color dangerColor = new Color(1f, .28f, .12f);
        [Min(1f)] public float ringBrightness = 3.2f;
        public Transform[] lakeRoad = System.Array.Empty<Transform>();
        public Transform[] lakeTurnaround = System.Array.Empty<Transform>();
        [Min(10f)] public float drivingMarkerLookAhead = 32f;
        [Tooltip("Away from the road, point to the destination so shortcuts remain optional.")]
        [Min(5f)] public float roadGuidanceDistance = 38f;
        [Min(.01f)] public float drivingGuideSmoothTime = .18f;
        [Min(.01f)] public float markerSmoothTime = .12f;
        [Min(1f)] public float markerMaximumSpeed = 1000f;
        Transform drivingGuide;
        Vector3 guideVelocity, guideForward;
        Vector2 markerVelocity;
        float arrowAngle, arrowAngularVelocity, lastGuideTime = -1f;
        int lastRoadSegment = -1, lastRoadDirection;
        Transform presentedTarget;
        bool presentationVisible;
        Terrain[] terrains;
        public Transform CurrentTarget { get; private set; }
        public string CurrentLabel { get; private set; }
        readonly Vector3[] ringPoints = new Vector3[65];
        const int ArrowEdgeSteps = 8;
        readonly Vector3[] arrowPoints = new Vector3[7 * ArrowEdgeSteps + 1];
        public bool UsesDirectionalArrow => mission && mission.player && mission.player.IsDriving && drivingGuide && CurrentTarget == drivingGuide;
        static readonly Vector2[] arrowOutline = {
            new Vector2(-.23f, -.65f), new Vector2(.23f, -.65f),
            new Vector2(.23f, .02f), new Vector2(.6f, .02f), new Vector2(0, .78f),
            new Vector2(-.6f, .02f), new Vector2(-.23f, .02f), new Vector2(-.23f, -.65f)
        };
        void Awake() => EnsureDirectionArrow();
        public void EnsureDirectionArrow()
        {
            if (markerGlow) markerGlow.enabled = true;
            if (!directionArrow && marker)
            {
                var icon = marker.Find("Destination arrow") as RectTransform;
                if (!icon)
                {
                    icon = new GameObject("Destination arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(ObjectiveArrowGraphic)).GetComponent<RectTransform>();
                    icon.SetParent(marker, false);
                }
                if (arrow)
                {
                    icon.anchorMin = icon.anchorMax = arrow.rectTransform.anchorMin;
                    icon.anchoredPosition = arrow.rectTransform.anchoredPosition +
                        (Vector2.one * .5f - arrow.rectTransform.pivot) * arrow.rectTransform.rect.size;
                }
                icon.sizeDelta = new Vector2(48, 56);
                directionArrow = icon.GetComponent<ObjectiveArrowGraphic>();
            }
            if (arrow) arrow.enabled = false;
            if (directionArrow) { directionArrow.raycastTarget = false; directionArrow.enabled = false; }
        }
        void LateUpdate()
        {
            if (!mission || !mission.player || !mission.player.viewCamera) return;
            Color color = objectiveColor; float radius = objectiveRadius;
            CurrentTarget = SelectTarget(ref color, ref radius);
            bool visible = CurrentTarget && !mission.player.CinematicLocked && !mission.player.InteractionBusy && !VolcanoCinematic.IsPlayingAny && (!mission.gameFlow || !mission.gameFlow.BlocksGameplay);
            if (ring) ring.enabled = visible;
            if (marker) marker.gameObject.SetActive(visible);
            if (!visible) { presentationVisible = false; return; }
            var player = mission.player;
            Vector3 world = CurrentTarget.position;
            bool drivingArrow = UsesDirectionalArrow;
            bool resetPresentation = !presentationVisible || (!player.IsDriving && presentedTarget != CurrentTarget);
            presentationVisible = true; presentedTarget = CurrentTarget;
            terrains = Terrain.activeTerrains;
            if (ring)
            {
                Vector3[] points = drivingArrow ? arrowPoints : ringPoints;
                Vector3 origin = player.IsDriving && player.drive ? player.drive.transform.position : player.transform.position;
                Vector3 forward = Vector3.ProjectOnPlane(world - origin, Vector3.up).normalized;
                if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(player.viewCamera.transform.forward, Vector3.up).normalized;
                if (drivingArrow)
                {
                    guideForward = resetPresentation || guideForward.sqrMagnitude < .01f ? forward :
                        Vector3.Slerp(guideForward, forward, 1f - Mathf.Exp(-10f * Time.deltaTime)).normalized;
                    forward = guideForward;
                }
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                float arrowSize = Mathf.Clamp(radius, .7f, player.IsDriving ? 5f : 3.5f);
                for (int i = 0; i < points.Length; i++)
                {
                    float angle = i * Mathf.PI * 2 / (points.Length - 1);
                    int edge = Mathf.Min(i / ArrowEdgeSteps, arrowOutline.Length - 2);
                    Vector2 outline = Vector2.Lerp(arrowOutline[edge], arrowOutline[edge + 1], (i - edge * ArrowEdgeSteps) / (float)ArrowEdgeSteps);
                    Vector3 p = drivingArrow ? world + (right * outline.x + forward * outline.y) * arrowSize :
                        world + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    if (CurrentTarget != (mission.weaponPickup ? mission.weaponPickup.transform : null) &&
                        CurrentTarget != (mission.story ? mission.story.windowStop : null) &&
                        CurrentTarget != (mission.villageAlarm ? mission.villageAlarm.contactPoint : null))
                        foreach (var terrain in terrains)
                            if (terrain && p.x >= terrain.transform.position.x && p.z >= terrain.transform.position.z &&
                                p.x <= terrain.transform.position.x + terrain.terrainData.size.x && p.z <= terrain.transform.position.z + terrain.terrainData.size.z)
                                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                    // Dense edges follow the road surface instead of cutting through crests between the eight corners.
                    points[i] = p + Vector3.up * (drivingArrow ? .4f : .18f);
                }
                ring.widthMultiplier = radius < 1 ? .08f : .25f;
                ring.positionCount = points.Length; ring.SetPositions(points);
                Color bright = color * (ringBrightness + .25f * Mathf.Sin(Time.time * 3)); bright.a = 1;
                ring.startColor = ring.endColor = bright;
            }
            if (!marker || !canvas) return;
            Vector3 projected = player.viewCamera.WorldToViewportPoint(world + Vector3.up * (player.IsDriving ? 8f : 2f));
            Vector2 direction = new Vector2(projected.x - .5f, projected.y - .5f);
            if (projected.z < 0) direction = -direction;
            if (direction.sqrMagnitude < .001f) direction = Vector2.down;
            Vector2 size = ((RectTransform)canvas.transform).rect.size;
            Vector2 pos = new Vector2((projected.x - .5f) * size.x, (projected.y - .5f) * size.y);
            float maxX = Mathf.Max(1, size.x * .5f - edgeMargin - 60), maxY = Mathf.Max(1, size.y * .5f - edgeMargin - 80);
            bool offscreen = projected.z <= 0 || Mathf.Abs(pos.x) > maxX || Mathf.Abs(pos.y) > maxY;
            if (offscreen)
            {
                direction.Normalize();
                float edge = Mathf.Min(maxX / Mathf.Max(.001f, Mathf.Abs(direction.x)), maxY / Mathf.Max(.001f, Mathf.Abs(direction.y)));
                pos = direction * edge;
            }
            marker.anchoredPosition = resetPresentation || !player.IsDriving ? pos :
                Vector2.SmoothDamp(marker.anchoredPosition, pos, ref markerVelocity, markerSmoothTime, markerMaximumSpeed, Time.deltaTime);
            if (resetPresentation) markerVelocity = Vector2.zero;
            // Keep distant guidance and world rings; avoid a second boxed label on a nearby prompt.
            if (player.contextualInteractions && player.contextualInteractions.isActiveAndEnabled &&
                player.contextualInteractions.ReplacesNavigationLabel(CurrentTarget)) marker.gameObject.SetActive(false);
            if (label)
            {
                if (previousLabel != CurrentLabel)
                {
                    previousLabel = CurrentLabel;
                    int divider = CurrentLabel.IndexOf('|');
                    shortLabel = divider >= 0 ? CurrentLabel.Substring(0, divider).TrimEnd() : CurrentLabel;
                    if (compactLabels) { var culture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR"); shortLabel = culture.TextInfo.ToTitleCase(shortLabel.ToLower(culture)); }
                }
                label.color = compactLabels ? new Color(.94f, .96f, .98f) : color;
                Vector3 destination = world;
                if (drivingArrow)
                {
                    if (lastRoadDirection < 0 && mission.fillStation) destination = mission.fillStation.transform.position;
                    else if (lastRoadDirection > 0 && mission.houseExit) destination = mission.houseExit.position;
                }
                label.text = (compactLabels ? shortLabel : CurrentLabel) + "\n" + Mathf.RoundToInt(Vector3.Distance(player.transform.position, destination)) + " m";
            }
            if (directionArrow)
            {
                directionArrow.enabled = drivingArrow;
                directionArrow.color = Color.Lerp(color, Color.white, .3f);
                float desiredAngle = offscreen ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90 : 180;
                if (resetPresentation) { arrowAngle = desiredAngle; arrowAngularVelocity = 0; }
                else arrowAngle = Mathf.SmoothDampAngle(arrowAngle, desiredAngle, ref arrowAngularVelocity, markerSmoothTime, 300f, Time.deltaTime);
                directionArrow.rectTransform.localRotation = Quaternion.Euler(0, 0, arrowAngle);
                directionArrow.rectTransform.localScale = Vector3.one;
            }
            if (markerGlow)
            {
                markerGlow.enabled = !drivingArrow;
                Color glow = color; glow.a = .62f + .12f * Mathf.Sin(Time.unscaledTime * 2.6f);
                markerGlow.color = glow;
                markerGlow.rectTransform.localScale = Vector3.one * (1f + .07f * Mathf.Sin(Time.unscaledTime * 2.6f));
            }
        }
        Transform SelectTarget(ref Color color, ref float radius)
        {
            var player = mission.player;
            if (!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.Window && mission.story)
            { CurrentLabel = "WINDOW | LOOK AT THE VOLCANO"; radius = .7f; return mission.story.windowStop; }
            if (mission.State == VolcanoMissionDirector.FlowState.Intro) return null;
            if (!player.HasPistol && mission.weaponPickup && !mission.weaponPickup.IsCollected)
            { CurrentLabel = "PISTOL | " + InputLabel(InteractionInput.Interact, "E") + ": TAKE"; radius = .75f; return mission.weaponPickup.transform; }
            if (!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.WarnVillage && mission.villageAlarm && !mission.villageAlarm.Activated)
            { CurrentLabel = "VILLAGE ALARM | " + (player.contextualInteractions ? player.contextualInteractions.Label(InteractionInput.Interact) : "E"); radius = .45f; return mission.villageAlarm.contactPoint; }
            if (!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.Weapon)
            { CurrentLabel = "LEAVE THE HOUSE | " + InputLabel(InteractionInput.Interact, "E") + ": OPEN DOOR"; return mission.houseExit; }
            if (!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate)
            {
                Transform next = null; float distance = float.PositiveInfinity;
                foreach (var resident in mission.evacuation.residents)
                {
                    if (!resident || resident.IsCalled || !resident.houseExit) continue;
                    float d = (resident.transform.position - player.transform.position).sqrMagnitude;
                    if (d < distance) { distance = d; next = resident.transform; }
                }
                if (next) { CurrentLabel = "VILLAGERS | " + InputLabel(InteractionInput.CallVillagers, "Q") + ": CALL OUT"; return next; }
                CurrentLabel = "SHELTER | LEAD THE VILLAGERS"; color = shelterColor; radius = shelterRadius;
                return mission.evacuation.ShelterOpened ? mission.evacuation.shelterEntrance : mission.evacuation.shelterApproach;
            }
            if (!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.Truck)
            { CurrentLabel = "FIRE TRUCK | " + InputLabel(InteractionInput.Interact, "E") + ": ENTER"; radius = 5; return player.driverDoor; }
            if (mission.fillStation && ((!mission.EruptionTriggered && mission.Phase == VolcanoMissionDirector.MissionPhase.Refill) || mission.EruptionTriggered && mission.tank.Tank01 < .05f))
            {
                if (!player.IsDriving) { CurrentLabel = "FIRE TRUCK | " + InputLabel(InteractionInput.Interact, "E") + ": ENTER"; radius = 5; return player.driverDoor; }
                CurrentLabel = "LAKE | REFILL WATER"; radius = mission.fillStation.parkingRadius;
                if (player.drive && !mission.fillStation.InRange &&
                    EvacuationResident.FlatDistance(player.drive.transform.position, mission.fillStation.transform.position) > drivingMarkerLookAhead * (UsesDirectionalArrow ? 1.25f : 1.5f) &&
                    TryGetRoadGuidance(player.drive.transform.position, -1, out var lakeGuide))
                {
                    CurrentLabel = "LAKE | REFILL WATER"; radius = 4;
                    return PlaceDrivingGuide(lakeGuide);
                }
                return mission.fillStation.transform;
            }
            if (player.IsDriving && mission.houseExit && lakeRoad.Length > 1 &&
                (EvacuationResident.FlatDistance(player.drive.transform.position, mission.houseExit.position) > 140 ||
                 !mission.EruptionTriggered && mission.PreparationComplete && lakeRoad[lakeRoad.Length - 1] &&
                 EvacuationResident.FlatDistance(player.drive.transform.position, lakeRoad[lakeRoad.Length - 1].position) > 25))
            {
                CurrentLabel = "VILLAGE | RETURN"; radius = 4;
                if (TryGetRoadGuidance(player.drive.transform.position, 1, out var villageGuide)) return PlaceDrivingGuide(villageGuide);
                return mission.houseExit;
            }
            if (!mission.EruptionTriggered && mission.PreparationComplete && mission.story && mission.story.defensePosition)
            { CurrentLabel = "DEFENSE POINT | LAVA APPROACH"; radius = mission.story.defenseRadius; color = dangerColor; return mission.story.defensePosition; }
            BurnableBuilding urgent = null; float priority = float.PositiveInfinity;
            foreach (var house in BurnableBuilding.ActiveBuildings)
            {
                if (!house || !house.IsBurning) continue;
                float score = house.SecondsUntilCollapse + Vector3.Distance(player.transform.position, house.transform.position) * .06f;
                if (score < priority) { priority = score; urgent = house; }
            }
            if (urgent) { color = dangerColor; radius = 7; CurrentLabel = "HOUSE ON FIRE | EXTINGUISH WITH WATER"; return urgent.transform; }
            if (mission.encounter)
                foreach (var actor in mission.encounter.LivingCreatures) if (actor && !actor.IsDefeated)
                { color = dangerColor; CurrentLabel = "FIRE SPIRIT | STOP IT BEFORE THE HOUSES"; return actor.transform; }
            CurrentLabel = "KASABA | HAZIR BEKLE";
            return mission.houseExit;
        }
        string InputLabel(InteractionInput input, string fallback) => mission.player.contextualInteractions ? mission.player.contextualInteractions.Label(input) : fallback;
        /// <summary>Project onto the road each frame, then advance along it. No checkpoint has to be visited.</summary>
        public bool TryGetRoadGuidance(Vector3 position, int direction, out Vector3 target)
        {
            target = position;
            if (lakeRoad == null || lakeRoad.Length < 2 || direction == 0) return false;
            float nearest = float.PositiveInfinity;
            int segment = -1;
            Vector3 projection = position;
            for (int i = 0; i < lakeRoad.Length - 1; i++)
            {
                if (!lakeRoad[i] || !lakeRoad[i + 1]) continue;
                Vector3 a = lakeRoad[i].position, b = lakeRoad[i + 1].position;
                Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(position.x - a.x, position.z - a.z), ab) / Mathf.Max(.001f, ab.sqrMagnitude));
                Vector3 p = Vector3.Lerp(a, b, t);
                float distance = new Vector2(position.x - p.x, position.z - p.z).sqrMagnitude;
                if (distance < nearest) { nearest = distance; segment = i; projection = p; }
            }
            float allowedDistance = roadGuidanceDistance + (UsesDirectionalArrow ? 6f : 0f);
            if (segment < 0 || nearest > allowedDistance * allowedDistance) { lastRoadSegment = -1; return false; }
            // At adjacent/parallel bends, keep the previous segment while it is still essentially as close.
            // A substantial shortcut immediately reprojects, so no checkpoint becomes mandatory.
            if (lastRoadDirection == direction && lastRoadSegment >= 0 && lastRoadSegment < lakeRoad.Length - 1 &&
                Mathf.Abs(segment - lastRoadSegment) > 1 && lakeRoad[lastRoadSegment] && lakeRoad[lastRoadSegment + 1])
            {
                Vector3 a = lakeRoad[lastRoadSegment].position, b = lakeRoad[lastRoadSegment + 1].position;
                Vector3 ab = Vector3.ProjectOnPlane(b - a, Vector3.up);
                float t = Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(position - a, Vector3.up), ab) / Mathf.Max(.001f, ab.sqrMagnitude));
                Vector3 previous = Vector3.Lerp(a, b, t);
                if (EvacuationResident.FlatDistance(position, previous) <= Mathf.Sqrt(nearest) + 1f)
                { segment = lastRoadSegment; projection = previous; }
            }
            if (lastRoadDirection != direction) lastGuideTime = -1f;
            lastRoadSegment = segment; lastRoadDirection = direction;
            float ahead = drivingMarkerLookAhead;
            Vector3 current = projection;
            int step = direction > 0 ? 1 : -1;
            for (int i = step > 0 ? segment + 1 : segment; i >= 0 && i < lakeRoad.Length; i += step)
            {
                if (!lakeRoad[i]) continue;
                Vector3 next = lakeRoad[i].position;
                float length = EvacuationResident.FlatDistance(current, next);
                if (length >= ahead) { target = Vector3.Lerp(current, next, ahead / Mathf.Max(.001f, length)); return true; }
                ahead -= length; current = next;
            }
            target = current;
            return EvacuationResident.FlatDistance(position, target) > 6f;
        }
        Transform PlaceDrivingGuide(Vector3 position)
        {
            if (!drivingGuide)
                drivingGuide = new GameObject("Live driving guidance") { hideFlags = HideFlags.HideAndDontSave }.transform;
            if (CurrentTarget != drivingGuide || lastGuideTime < 0 || Time.time - lastGuideTime > .25f)
            { drivingGuide.position = position; guideVelocity = Vector3.zero; guideForward = Vector3.zero; }
            else drivingGuide.position = Vector3.SmoothDamp(drivingGuide.position, position, ref guideVelocity,
                drivingGuideSmoothTime, Mathf.Infinity, Time.deltaTime);
            lastGuideTime = Time.time;
            return drivingGuide;
        }
        void OnDestroy()
        {
            if (drivingGuide) Destroy(drivingGuide.gameObject);
        }
    }
}
