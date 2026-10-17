using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>A single, pooled, world-tracked presentation for existing rescue mechanics.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(300)]
    public sealed class ContextualInteractionUI : MonoBehaviour
    {
        [Serializable]
        public sealed class FloatingNotice
        {
            public RectTransform rect;
            public CanvasGroup group;
            public TMP_Text text;
            [NonSerialized] public float age;
            [NonSerialized] public Vector3 world;
            [NonSerialized] public bool active;
        }

        [Header("Existing scene systems")]
        public FireRescuePlayer player;
        public VolcanoMissionDirector mission;
        public RescueHUD hud;
        public InteractionTextCatalog words;
        public ContextualInteractionTarget[] targets = Array.Empty<ContextualInteractionTarget>();
        [Header("Reusable prefab bindings")]
        public Canvas canvas;
        public RectTransform canvasRect, promptRect;
        public CanvasGroup promptGroup;
        public TMP_Text titleText, keyText, detailText;
        public GameObject progressGroup;
        public Image progressFill;
        public FloatingNotice[] notices = Array.Empty<FloatingNotice>();
        [Tooltip("Existing HUD panels remain intact. The world label slides around visible panels.")]
        public RectTransform[] avoidedHudRegions = Array.Empty<RectTransform>();
        [Header("Presentation")]
        [Min(.01f)] public float fadeInSeconds = .14f, fadeOutSeconds = .12f;
        [Min(.02f)] public float selectionInterval = .05f;
        [Min(.3f)] public float feedbackSeconds = 1.8f;
        public float feedbackRise = 48f;
        public Vector2 screenOffset = new Vector2(60f, 54f);
        public Vector2 screenMargin = new Vector2(24f, 28f);
        [Header("Compact vehicle instructions")]
        public bool compactVehiclePrompts = true;
        [Min(220f)] public float vehiclePromptWidth = 300f;
        [Range(.25f, .75f)] public float vehicleScreenHeight = .47f;

        public ContextualInteractionTarget CurrentTarget => selected.target;
        public InteractionOffer CurrentOffer => selected;
        public bool HasVisiblePrompt => promptGroup && promptGroup.alpha > .01f;
        public int FeedbackCount { get; private set; }
        public int ActiveFeedbackCount { get { int n = 0; foreach (var item in notices) if (item.active) n++; return n; } }
        public bool CanPresent => player && player.isActiveAndEnabled && words && canvas && player.viewCamera &&
            player.viewCamera.enabled && !player.GameplaySuspended && !player.InteractionBusy && !VolcanoCinematic.IsPlayingAny &&
            Time.timeScale > 0f && Cursor.lockState == CursorLockMode.Locked &&
            (!player.gameFlow || !player.gameFlow.BlocksGameplay);

        InteractionOffer selected, displayed;
        readonly RaycastHit[] sightHits = new RaycastHit[48];
        readonly Vector3[] panelCorners = new Vector3[4];
        readonly string[] inputLabels = new string[7];
        Keyboard labelKeyboard;
        Mouse labelMouse;
        Key lastInteract, lastExit, lastRetract, lastPump, lastCall;
        float nextSelection;
        VolcanoMissionDirector.MissionPhase previousPhase;
        int cachedSafeCount = -1, cachedTotalCount = -1;
        string shelterDetail;
        string retractBeforeEntryDetail, enablePumpDetail, disablePumpDetail, drivingDetail;
        bool keysDirty = true;
        bool presentationCached, vehicleLayout;
        Vector2 worldPromptSize;
        float worldTitleSize, worldKeySize, worldDetailSize;

        void Awake()
        {
            HideImmediately();
            if (player) player.contextualInteractions = this;
            if (hud) hud.contextualInteractions = this;
        }
        void OnEnable()
        {
            keysDirty = true;
            InputSystem.onDeviceChange += DeviceChanged;
            if (player) { player.contextualInteractions = this; player.NoticeRaised += OnPlayerNotice; }
            if (hud) hud.contextualInteractions = this;
            if (!mission) return;
            previousPhase = mission.Phase;
            mission.ObjectiveChanged += OnObjectiveChanged;
            if (mission.weaponPickup) mission.weaponPickup.Collected += OnPistolCollected;
            if (mission.evacuation) mission.evacuation.ResidentRescued += OnResidentRescued;
            if (mission.gameFlow)
                foreach (var house in mission.gameFlow.targetBuildings) if (house) house.Rescued += OnFireExtinguished;
        }
        void OnDisable()
        {
            InputSystem.onDeviceChange -= DeviceChanged;
            if (player) { player.NoticeRaised -= OnPlayerNotice; if (player.contextualInteractions == this) player.contextualInteractions = null; }
            if (hud && hud.contextualInteractions == this) hud.contextualInteractions = null;
            if (mission)
            {
                mission.ObjectiveChanged -= OnObjectiveChanged;
                if (mission.weaponPickup) mission.weaponPickup.Collected -= OnPistolCollected;
                if (mission.evacuation) mission.evacuation.ResidentRescued -= OnResidentRescued;
                if (mission.gameFlow)
                    foreach (var house in mission.gameFlow.targetBuildings) if (house) house.Rescued -= OnFireExtinguished;
            }
            HideImmediately();
        }
        void DeviceChanged(InputDevice device, InputDeviceChange change) => keysDirty = true;

        void LateUpdate()
        {
            if (!CanPresent) { HideImmediately(); return; }
            RefreshKeyLabels();
            if (Time.unscaledTime >= nextSelection) RefreshSelection();
            bool same = selected.target && selected.target == displayed.target;
            float dt = Time.unscaledDeltaTime;
            if (!same)
            {
                promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, 0f, dt / Mathf.Max(.01f, fadeOutSeconds));
                if (promptGroup.alpha <= .001f) { displayed = selected; UpdateCopy(); }
            }
            else
            {
                displayed = selected;
                UpdateCopy();
                promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, 1f, dt / Mathf.Max(.01f, fadeInSeconds));
            }
            if (displayed.target && promptGroup.alpha > 0f)
            {
                Vector3 world = displayed.target.FocusPosition(this) + Vector3.up * displayed.target.labelHeight;
                if (player.IsDriving && compactVehiclePrompts) DockVehiclePrompt();
                else if (!Track(promptRect, world, screenOffset)) promptGroup.alpha = 0f;
            }
            TickFeedback(dt);
        }

        public void RefreshSelection()
        {
            nextSelection = Time.unscaledTime + Mathf.Max(.02f, selectionInterval);
            RefreshKeyLabels();
            InteractionOffer best = default;
            float bestScore = float.NegativeInfinity;
            if (CanPresent)
            {
                Transform eye = player.viewCamera.transform;
                foreach (var target in targets)
                {
                    if (!target || !target.isActiveAndEnabled || !target.anchor || !target.anchor.gameObject.activeInHierarchy) continue;
                    Vector3 position = target.FocusPosition(this), delta = position - eye.position;
                    float distance = delta.magnitude;
                    if (distance > target.maximumViewDistance || distance < .04f) continue;
                    float facing = Vector3.Dot(eye.forward, delta / distance);
                    if (facing < Mathf.Cos(target.maximumLookAngle * Mathf.Deg2Rad)) continue;
                    if (!target.TryDescribe(this, out InteractionOffer offer) || !HasLineOfSight(target, position)) continue;
                    float score = facing * 2f + target.priority * .5f - distance / target.maximumViewDistance * .3f;
                    if (target == selected.target) score += .04f;
                    if (score <= bestScore) continue;
                    best = offer; best.target = target; bestScore = score;
                }
            }
            selected = best;
        }

        // The player's existing E coordinator uses the same selection as the visible prompt.
        // Execution remains in FireRescuePlayer / the original mechanic, never in this Canvas.
        public bool TryGetPrimaryTarget(out ContextualInteractionTarget target)
        {
            RefreshSelection();
            target = selected.target;
            return target && selected.input == InteractionInput.Interact && !selected.blocked;
        }

        public bool HasLineOfSight(ContextualInteractionTarget target, Vector3 point)
        {
            if (!player || !player.viewCamera || !target) return false;
            Vector3 origin = player.viewCamera.transform.position, delta = point - origin;
            float distance = delta.magnitude;
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, sightHits, Mathf.Max(0f, distance - .08f), ~0, QueryTriggerInteraction.Ignore);
            if (count == sightHits.Length) return false;
            float nearest = float.PositiveInfinity;
            Transform surface = null;
            for (int i = 0; i < count; i++)
            {
                var hit = sightHits[i];
                if (!hit.collider || (player.playerBody && hit.transform.IsChildOf(player.playerBody.transform))) continue;
                if (player.hose && player.hose.IsCarried && player.hose.nozzle && hit.transform.IsChildOf(player.hose.nozzle)) continue;
                if (player.IsDriving && target.kind != ContextualInteractionKind.TruckExit && player.drive && hit.transform.IsChildOf(player.drive.transform)) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance; surface = hit.transform;
            }
            return !surface || (target.visibilityRoot && surface.IsChildOf(target.visibilityRoot));
        }

        public bool Describe(ContextualInteractionTarget target, out InteractionOffer offer)
        {
            offer = new InteractionOffer { target = target };
            if (!player || !mission || !words) return false;
            var evacuation = mission.evacuation;
            var hose = player.hose;
            switch (target.kind)
            {
                case ContextualInteractionKind.VillageAlarm:
                    if (!target.alarm || target.alarm.Activated || target.alarm.IsPressing || player.IsDriving) return false;
                    offer.title = words.activateAlarm; offer.input = InteractionInput.Interact;
                    offer.blocked = !target.alarm.CanPress;
                    offer.detail = !player.HasPistol ? words.alarmNeedsPistol : player.pistol && player.pistol.IsReloading ? words.alarmReloading :
                        !target.alarm.InReach ? words.alarmGetCloser : "Köy sirenlerini çalıştır";
                    return true;
                case ContextualInteractionKind.Pistol:
                    if (!mission.weaponPickup || !mission.weaponPickup.CanPickUp) return false;
                    offer.title = words.takePistol; offer.detail = words.pistolSlot; offer.input = InteractionInput.Interact; return true;
                case ContextualInteractionKind.Door:
                    if (!target.door || !player.TryGetDoorTarget(out var aimedDoor) || aimedDoor != target.door) return false;
                    offer.title = target.door.WantsOpen ? words.closeDoor : words.openDoor;
                    offer.input = InteractionInput.Interact;
                    offer.detail = target.door.LastBlocked ? words.blockedDoor :
                        mission.Phase == VolcanoMissionDirector.MissionPhase.Weapon && !player.HasPistol ? words.takePistolFirst : "";
                    return true;
                case ContextualInteractionKind.HouseExit:
                    if (player.IsDriving || mission.EruptionTriggered || mission.Phase != VolcanoMissionDirector.MissionPhase.Weapon || mission.LeftStartingHouse) return false;
                    offer.title = words.leaveHouse; offer.detail = player.HasPistol ? words.walkOutside : words.takePistolFirst; return true;
                case ContextualInteractionKind.TruckEntry:
                    if (player.IsDriving || !player.CanReachVehicleEntry()) return false;
                    offer.title = words.enterTruck; offer.input = InteractionInput.Interact;
                    if (hose && hose.IsDeployed) { offer.blocked = true; offer.detail = retractBeforeEntryDetail; }
                    return true;
                case ContextualInteractionKind.TruckExit:
                    if (!player.IsDriving || !player.drive) return false;
                    offer.title = words.exitTruck; offer.input = InteractionInput.ExitVehicle;
                    offer.blocked = Mathf.Abs(player.drive.SpeedKph) > 4f;
                    offer.detail = offer.blocked ? words.stopBeforeExit : !string.IsNullOrEmpty(player.ActiveNotice) ? player.ActiveNotice : drivingDetail;
                    return true;
                case ContextualInteractionKind.Resident:
                    if (player.IsDriving || !evacuation || !evacuation.IsActive || !target.resident || target.resident.IsSafe) return false;
                    if (target.resident.IsCalled)
                    { offer.title = words.leadVillager; offer.detail = words.following; return true; }
                    if (!CanCall(target.resident)) return false;
                    CallOffer(ref offer, evacuation); return true;
                case ContextualInteractionKind.CallHouse:
                    if (player.IsDriving || !evacuation || !evacuation.IsActive || !target.anchor) return false;
                    bool waiting = false;
                    foreach (var resident in evacuation.residents)
                        if (resident && resident.houseExit == target.anchor && !resident.IsCalled && CanCall(resident)) { waiting = true; break; }
                    if (!waiting) return false;
                    CallOffer(ref offer, evacuation); return true;
                case ContextualInteractionKind.Shelter:
                    if (player.IsDriving || !evacuation || !evacuation.IsActive || evacuation.CalledCount == 0) return false;
                    offer.title = words.shelter;
                    if (cachedSafeCount != evacuation.RescuedCount || cachedTotalCount != evacuation.TotalCount)
                    {
                        cachedSafeCount = evacuation.RescuedCount; cachedTotalCount = evacuation.TotalCount;
                        shelterDetail = words.shelterAutomatic + "\n" + cachedSafeCount + " / " + cachedTotalCount + " güvende";
                    }
                    offer.detail = shelterDetail; return true;
                case ContextualInteractionKind.LakeFill:
                    var station = mission.fillStation;
                    if (!player.IsDriving || !station || !station.InRange || !hose) return false;
                    if (station.IsFilling)
                    {
                        offer.title = words.fillingTank; offer.detail = words.disconnect; offer.input = InteractionInput.Interact;
                        offer.showProgress = true; offer.progress = hose.Tank01; return true;
                    }
                    if (hose.Tank01 >= .999f) { offer.title = words.tankFull; offer.detail = words.returnToVillage; return true; }
                    offer.title = words.fillTank; offer.input = InteractionInput.Interact; offer.blocked = !station.CanFill || !hose.fillingUnlocked;
                    offer.detail = !hose.fillingUnlocked ? hose.Status : Mathf.Abs(player.drive.SpeedKph) > station.maximumFillSpeedKph ? words.stopBeforeFill :
                        !station.water || !station.intakePoint || !station.water.ContainsIntake(station.intakePoint.position) ? words.intakeOutOfWater :
                        !hose.CanReach(station.intakePoint.position) ? words.hoseTooFar : "";
                    return true;
                case ContextualInteractionKind.HosePickup:
                    if (player.IsDriving || !hose || hose.IsCarried || !player.IsWithinInteractionReach(hose.IsDeployed ? hose.nozzle : hose.hoseSocket)) return false;
                    offer.title = words.takeHose; offer.blocked = !player.hand || !hose.CanReach(player.hand.position);
                    offer.detail = offer.blocked ? words.hoseTooFar : words.putIntakeInWater; offer.input = InteractionInput.Interact; return true;
                case ContextualInteractionKind.HoseDrop:
                    if (player.IsDriving || !hose || !hose.IsCarried) return false;
                    offer.title = words.dropHose; offer.detail = words.putIntakeInWater; offer.input = InteractionInput.Interact; return true;
                case ContextualInteractionKind.HoseSocket:
                    if (player.IsDriving || !hose || !hose.IsDeployed || !player.IsWithinInteractionReach(hose.hoseSocket)) return false;
                    offer.title = words.retractHose; offer.input = InteractionInput.RetractHose;
                    offer.detail = hose.PumpEnabled ? disablePumpDetail : enablePumpDetail; return true;
                case ContextualInteractionKind.BuildingFire:
                    if (!player.IsDriving || !player.cannon || !target.building || !target.building.IsBurning ||
                        (player.cannon.PredictedTarget != target.building && player.cannon.HitFire != target.building)) return false;
                    offer.title = words.extinguish; offer.detail = words.useWaterCannon; offer.input = InteractionInput.Spray;
                    offer.showProgress = true; offer.progress = 1f - target.building.Heat; return true;
            }
            return false;
        }
        bool CanCall(EvacuationResident resident) => resident && resident.houseExit && mission.evacuation.PlayerTransform &&
            EvacuationResident.FlatDistance(mission.evacuation.PlayerTransform.position, resident.houseExit.position) <= mission.evacuation.shoutRadius;
        void CallOffer(ref InteractionOffer offer, EvacuationMission evacuation)
        {
            offer.title = words.callVillagers; offer.input = InteractionInput.CallVillagers;
            offer.blocked = evacuation.CooldownRemaining > 0f;
            offer.detail = offer.blocked ? words.callCoolingDown : words.leadToShelter;
        }

        void RefreshKeyLabels()
        {
            if (!player) return;
            Key call = mission && mission.evacuation ? mission.evacuation.callKey : Key.Q;
            if (!keysDirty && labelKeyboard == Keyboard.current && labelMouse == Mouse.current &&
                lastInteract == player.interactKey && lastExit == player.exitVehicleKey && lastRetract == player.retractHoseKey &&
                lastPump == player.pumpKey && lastCall == call) return;
            keysDirty = false; labelKeyboard = Keyboard.current; labelMouse = Mouse.current;
            lastInteract = player.interactKey; lastExit = player.exitVehicleKey; lastRetract = player.retractHoseKey; lastPump = player.pumpKey; lastCall = call;
            inputLabels[0] = "";
            inputLabels[1] = KeyLabel(lastInteract); inputLabels[2] = KeyLabel(lastExit); inputLabels[3] = KeyLabel(lastCall);
            inputLabels[4] = KeyLabel(lastRetract); inputLabels[5] = KeyLabel(lastPump);
            inputLabels[6] = labelMouse != null ? "[" + labelMouse.rightButton.displayName + "]" : "";
            retractBeforeEntryDetail = inputLabels[4] + " ile hortumu aracın arkasından topla";
            enablePumpDetail = inputLabels[5] + " · " + (words ? words.enablePump : "");
            disablePumpDetail = inputLabels[5] + " · " + (words ? words.disablePump : "");
            drivingDetail = words ? string.Format(words.drivingHint, inputLabels[6]) : "";
        }
        string KeyLabel(Key key)
        {
            if (labelKeyboard == null || key == Key.None) return "";
            var control = labelKeyboard[key];
            return control == null ? "" : "[" + control.displayName + "]";
        }
        public string Label(InteractionInput input) { RefreshKeyLabels(); return inputLabels[(int)input]; }

        void UpdateCopy()
        {
            bool present = displayed.target;
            if (!present) return;
            SetText(titleText, displayed.title);
            SetText(detailText, displayed.detail);
            string key = Label(displayed.input);
            SetText(keyText, key);
            bool hasKey = !string.IsNullOrEmpty(key);
            if (keyText.gameObject.activeSelf != hasKey) keyText.gameObject.SetActive(hasKey);
            ApplyPromptLayout(player.IsDriving && compactVehiclePrompts, hasKey);
            keyText.color = displayed.blocked ? new Color(.85f, .84f, .8f, 1) : new Color(1, .98f, .92f, 1);
            if (progressGroup.activeSelf != displayed.showProgress) progressGroup.SetActive(displayed.showProgress);
            if (displayed.showProgress) progressFill.fillAmount = Mathf.Clamp01(displayed.progress);
        }
        void ApplyPromptLayout(bool vehicle, bool hasKey)
        {
            bool changed = !presentationCached || vehicleLayout != vehicle || (vehicle && promptRect.sizeDelta.x != vehiclePromptWidth);
            if (!presentationCached)
            {
                worldPromptSize = promptRect.sizeDelta;
                worldTitleSize = titleText.fontSize; worldKeySize = keyText.fontSize; worldDetailSize = detailText.fontSize;
                presentationCached = true;
            }
            vehicleLayout = vehicle;
            float width = vehicle ? vehiclePromptWidth : worldPromptSize.x;
            if (changed)
            {
                promptRect.sizeDelta = vehicle ? new Vector2(width, 154) : worldPromptSize;
                titleText.fontSize = vehicle ? 24 : worldTitleSize;
                keyText.fontSize = vehicle ? 23 : worldKeySize;
                detailText.fontSize = vehicle ? 17 : worldDetailSize;
                titleText.alignment = keyText.alignment = detailText.alignment = vehicle ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
                titleText.rectTransform.sizeDelta = new Vector2(width, vehicle ? 34 : 56);
                keyText.rectTransform.sizeDelta = new Vector2(width, vehicle ? 34 : 82);
                detailText.rectTransform.sizeDelta = new Vector2(width, vehicle ? 48 : 72);
            }
            titleText.rectTransform.anchoredPosition = new Vector2(0, vehicle ? 44 : hasKey ? 76 : 30);
            keyText.rectTransform.anchoredPosition = new Vector2(0, vehicle ? 12 : 10);
            detailText.rectTransform.anchoredPosition = new Vector2(0, vehicle ? -32 : hasKey ? -64 : -24);
            if (progressGroup) ((RectTransform)progressGroup.transform).anchoredPosition = new Vector2(0, vehicle ? -70 : -114);
        }
        void DockVehiclePrompt()
        {
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect safe = Screen.safeArea;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, safe.min, uiCamera, out Vector2 lower);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, safe.max, uiCamera, out Vector2 upper);
            Vector2 half = promptRect.rect.size * .5f;
            Vector2 centre = new Vector2(upper.x - half.x - screenMargin.x - 12,
                Mathf.Lerp(lower.y, upper.y, vehicleScreenHeight));
            centre.y = ClampToFit(centre.y, lower.y + half.y + screenMargin.y, upper.y - half.y - screenMargin.y);
            promptRect.anchoredPosition = AvoidHud(centre, half, lower + screenMargin, upper - screenMargin);
        }
        static void SetText(TMP_Text label, string value) { if (label && label.text != value) label.text = value ?? ""; }

        public bool Track(RectTransform rect, Vector3 world, Vector2 offset)
        {
            Vector3 screen = player.viewCamera.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCamera, out Vector2 local)) return false;
            Rect safe = Screen.safeArea;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, safe.min, uiCamera, out Vector2 lower);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, safe.max, uiCamera, out Vector2 upper);
            Vector2 half = rect.rect.size * .5f;
            local += offset;
            local.x = ClampToFit(local.x, lower.x + half.x + screenMargin.x, upper.x - half.x - screenMargin.x);
            local.y = ClampToFit(local.y, lower.y + half.y + screenMargin.y, upper.y - half.y - screenMargin.y);
            if (rect == promptRect)
                local = AvoidHud(local, half, lower + screenMargin, upper - screenMargin);
            rect.anchoredPosition = local;
            return true;
        }
        Vector2 AvoidHud(Vector2 center, Vector2 half, Vector2 lower, Vector2 upper)
        {
            // A few small HUD rectangles; no allocations or layout searches in the frame loop.
            for (int pass = 0; pass < 2; pass++)
                foreach (var panel in avoidedHudRegions)
                {
                    if (!panel || !panel.gameObject.activeInHierarchy) continue;
                    panel.GetWorldCorners(panelCorners);
                    Vector2 min = canvasRect.InverseTransformPoint(panelCorners[0]);
                    Vector2 max = canvasRect.InverseTransformPoint(panelCorners[2]);
                    if (center.x + half.x <= min.x || center.x - half.x >= max.x || center.y + half.y <= min.y || center.y - half.y >= max.y) continue;
                    Vector2 best = center; float distance = float.PositiveInfinity;
                    for (int side = 0; side < 4; side++)
                    {
                        Vector2 candidate = center;
                        if (side == 0) candidate.y = min.y - half.y - 12;
                        if (side == 1) candidate.y = max.y + half.y + 12;
                        if (side == 2) candidate.x = min.x - half.x - 12;
                        if (side == 3) candidate.x = max.x + half.x + 12;
                        if (candidate.x - half.x < lower.x || candidate.x + half.x > upper.x || candidate.y - half.y < lower.y || candidate.y + half.y > upper.y) continue;
                        float movement = (candidate - center).sqrMagnitude;
                        if (movement < distance) { best = candidate; distance = movement; }
                    }
                    center = best;
                }
            return center;
        }
        public bool ReplacesNavigationLabel(Transform destination)
        {
            if (!destination || !displayed.target || !HasVisiblePrompt) return false;
            var target = displayed.target;
            switch (target.kind)
            {
                case ContextualInteractionKind.Pistol: return destination == mission.weaponPickup.transform;
                case ContextualInteractionKind.VillageAlarm: return target.alarm && destination == target.alarm.contactPoint;
                case ContextualInteractionKind.HouseExit:
                case ContextualInteractionKind.Door: return destination == mission.houseExit;
                case ContextualInteractionKind.TruckEntry: return destination == player.driverDoor;
                case ContextualInteractionKind.LakeFill: return destination == mission.fillStation.transform || destination == mission.fillStation.intakePoint;
                case ContextualInteractionKind.CallHouse: return destination == target.anchor;
                case ContextualInteractionKind.Resident: return target.resident && destination == target.resident.houseExit;
                case ContextualInteractionKind.Shelter: return destination == mission.evacuation.shelterEntrance || destination == mission.evacuation.shelterApproach;
                case ContextualInteractionKind.BuildingFire: return target.building && destination == target.building.transform;
                default: return false;
            }
        }
        static float ClampToFit(float value, float low, float high) => low <= high ? Mathf.Clamp(value, low, high) : (low + high) * .5f;

        void HideImmediately()
        {
            selected = displayed = default;
            if (promptGroup) promptGroup.alpha = 0f;
            foreach (var item in notices) { item.active = false; if (item.group) item.group.alpha = 0; }
            nextSelection = 0;
        }
        public void ShowFeedback(string text, Vector3 world)
        {
            if (!CanPresent || string.IsNullOrEmpty(text) || notices.Length == 0) return;
            int index = 0;
            for (int i = 0; i < notices.Length; i++)
            { if (!notices[i].active) { index = i; break; } if (notices[i].age > notices[index].age) index = i; }
            var item = notices[index]; item.world = world; item.age = 0; item.active = true; item.group.alpha = 0;
            SetText(item.text, text); FeedbackCount++;
        }
        void TickFeedback(float dt)
        {
            foreach (var item in notices)
            {
                if (!item.active) continue;
                item.age += dt;
                if (item.age >= feedbackSeconds) { item.active = false; item.group.alpha = 0; continue; }
                float fraction = item.age / feedbackSeconds;
                float alpha = Mathf.Min(item.age / .12f, (feedbackSeconds - item.age) / .45f);
                bool visible = Track(item.rect, item.world, new Vector2(0, 70f + feedbackRise * fraction));
                item.group.alpha = visible ? Mathf.Clamp01(alpha) : 0f;
            }
        }
        void OnPistolCollected(WeaponPickup pickup) => ShowFeedback(words.pistolCollected, pickup.transform.position + Vector3.up * .5f);
        void OnResidentRescued(EvacuationResident resident) => ShowFeedback(words.residentSaved, resident.transform.position + Vector3.up * 2f);
        void OnFireExtinguished(BurnableBuilding building) => ShowFeedback(words.fireExtinguished, building.ignitionBounds ? building.ignitionBounds.bounds.center : building.transform.position + Vector3.up * 3);
        void OnPlayerNotice(string message) => ShowFeedback(message, player.viewCamera.transform.position + player.viewCamera.transform.forward * 3f);
        void OnObjectiveChanged()
        {
            var previous = previousPhase; previousPhase = mission.Phase;
            if (mission.EruptionTriggered) return;
            Vector3 world = player.transform.position + Vector3.up * 1.7f;
            bool completed = false;
            if (previous == VolcanoMissionDirector.MissionPhase.Weapon && mission.Phase == VolcanoMissionDirector.MissionPhase.WarnVillage)
            { completed = player.HasPistol && (mission.villageAlarm || mission.LeftStartingHouse); if (mission.weaponPickup) world = mission.weaponPickup.transform.position + Vector3.up; }
            if (previous == VolcanoMissionDirector.MissionPhase.WarnVillage && mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate && mission.villageAlarm)
            { completed = mission.WarningCompleted && mission.villageAlarm.Activated; world = mission.villageAlarm.contactPoint.position + Vector3.up * .4f; }
            if (previous == VolcanoMissionDirector.MissionPhase.Evacuate && mission.Phase == VolcanoMissionDirector.MissionPhase.Truck)
            { completed = mission.evacuation && mission.evacuation.IsComplete; if (mission.evacuation.shelterEntrance) world = mission.evacuation.shelterEntrance.position + Vector3.up * 2; }
            if (previous == VolcanoMissionDirector.MissionPhase.Truck && mission.Phase == VolcanoMissionDirector.MissionPhase.Refill)
            { completed = player.IsDriving; world = player.drive.transform.position + Vector3.up * 3; }
            if (previous == VolcanoMissionDirector.MissionPhase.Refill && mission.Phase == VolcanoMissionDirector.MissionPhase.Ready)
            { completed = mission.PreparationComplete; world = mission.fillStation.intakePoint.position + Vector3.up; }
            if (completed) ShowFeedback(words.objectiveComplete, world);
        }
    }
}
