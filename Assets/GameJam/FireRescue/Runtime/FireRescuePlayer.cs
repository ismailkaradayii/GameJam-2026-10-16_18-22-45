using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameJam.FireRescue
{
    /// <summary>Keyboard/mouse controls, player movement, cameras and a compact gameplay HUD.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-90)]
    public sealed class FireRescuePlayer : MonoBehaviour
    {
        [Header("Scene references")]
        public FireTruckDrive drive;
        public FireHoseSystem hose;
        public FireTruckWaterCannon cannon;
        public CharacterController playerBody;
        public Camera viewCamera;
        public Transform hand;
        public Transform driverDoor;
        public Transform exitPoint;
        public RescueGameFlow gameFlow;
        [Header("First-person character (optional)")]
        public Itfaiye.ItfaiyeFirstPersonController firstPerson;
        public Itfaiye.ItfaiyePistol pistol;

        [Header("Player")]
        public bool startDriving = true;
        public bool lockCursorOnStart = true;
        public bool showHud = true;
        public float walkSpeed = 4f;
        public float sprintSpeed = 6f;
        public float eyeHeight = 1.65f;
        public float interactionDistance = 3f;
        public float mouseSensitivity = 0.12f;

        [Header("Driving camera")]
        public float followDistance = 8.5f;
        public float followHeight = 1.8f;
        [Min(1f)] public float minFollowDistance = 5f;
        [Min(1f)] public float maxFollowDistance = 14f;
        [Min(0f)] public float cannonCameraLift = 2.8f;

        public bool IsDriving { get; private set; }
        public bool GameplaySuspended => CinematicLocked || gameplaySuspended || (gameFlow && gameFlow.BlocksGameplay);
        public string InteractionPrompt => GetPrompt();
        public string WaterStatus => GetWaterStatus();
        public bool CinematicLocked { get; private set; }
        public void SetCinematicLocked(bool locked)
        {
            CinematicLocked = locked;
            SetCannonActive(false);
            if (drive) drive.SetInput(0, 0, true);
            if (firstPerson) firstPerson.SuspendExternalMovement();
            if (pistol) { pistol.SetExternalInput(false, false, false, false, false, false); pistol.SetAvailable(!locked && !IsDriving && (!hose || !hose.IsCarried)); }
            equipmentStateKnown = false;
            if (!locked) RefreshEquipmentState();
        }

        float lookYaw;
        float lookPitch = 16f;
        float orbitYaw;
        float verticalVelocity;
        bool snapCamera = true;
        bool focused = true;
        bool gameplaySuspended;
        bool pistolHudBeforeSuspend;
        bool waitForMouseRelease;
        int resumeAfterFrame = -1;
        bool aimingCannon;
        Vector2 lookAngularVelocity;
        Renderer[] bodyRenderers;
        bool[] bodyRendererDefaults;
        bool equipmentStateKnown, previousDriving, previousCarrying;
        Func<Vector3, Vector3> hoseTravelConstraint;
        string notice;
        float noticeUntil;
        GUIStyle titleStyle;
        GUIStyle textStyle;
        GUIStyle promptStyle;
        readonly RaycastHit[] doorHits = new RaycastHit[32];
        LavaPlayerContact lavaContact;

        void Awake()
        {
            lavaContact = GetComponent<LavaPlayerContact>();
            if (!firstPerson) firstPerson = GetComponent<Itfaiye.ItfaiyeFirstPersonController>();
            if (firstPerson)
            {
                firstPerson.externalControl = true;
                if (!playerBody) playerBody = firstPerson.GetComponent<CharacterController>();
                if (!viewCamera) viewCamera = firstPerson.playerCamera;
                if (!pistol) pistol = firstPerson.pistol;
                if (firstPerson.bodyAnimator)
                {
                    bodyRenderers = firstPerson.bodyAnimator.GetComponentsInChildren<Renderer>(true);
                    bodyRendererDefaults = new bool[bodyRenderers.Length];
                    for (int i = 0; i < bodyRenderers.Length; i++) bodyRendererDefaults[i] = bodyRenderers[i].enabled;
                }
            }
            if (pistol) pistol.externalControl = true;
            if (!playerBody) playerBody = GetComponent<CharacterController>();
            if (!viewCamera) viewCamera = Camera.main;
            if (!cannon && drive) cannon = drive.GetComponent<FireTruckWaterCannon>();
            if (cannon && viewCamera) cannon.aimCamera = viewCamera;
            if (playerBody) lookYaw = playerBody.transform.eulerAngles.y;
            hoseTravelConstraint = ConstrainHoseTravel;
        }

        void Start()
        {
            SetDriving(startDriving && drive);
            if (lockCursorOnStart) SetCursor(true);
        }

        void Update()
        {
            if (CinematicLocked) return;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (GameplaySuspended)
            {
                StopGameplayInput();
                return;
            }
            if (waitForMouseRelease)
            {
                StopGameplayInput();
                if (Time.frameCount > resumeAfterFrame &&
                    (mouse == null || (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed)))
                    waitForMouseRelease = false;
                return;
            }
            if (!gameFlow && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursor(false);
            if (focused && Cursor.lockState != CursorLockMode.Locked && mouse != null && mouse.leftButton.wasPressedThisFrame)
                SetCursor(true);

            bool inputEnabled = focused && Cursor.lockState == CursorLockMode.Locked && keyboard != null;
            RefreshEquipmentState();
            lookAngularVelocity = Vector2.zero;
            if (!inputEnabled)
            {
                if (pistol) pistol.SetExternalInput(false, false, false, false, false, false);
                SetCannonActive(false);
                if (IsDriving && drive) drive.SetInput(0f, 0f, true);
                if (!IsDriving) MoveOnFoot(Vector2.zero, false, captured: false);
                return;
            }

            Vector2 mouseDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            if (IsDriving)
            {
                orbitYaw += mouseDelta.x * mouseSensitivity;
                lookPitch = Mathf.Clamp(lookPitch - mouseDelta.y * mouseSensitivity, -10f, 65f);
                if (mouse != null)
                    followDistance = Mathf.Clamp(followDistance - mouse.scroll.ReadValue().y * 0.01f,
                        minFollowDistance, Mathf.Max(minFollowDistance, maxFollowDistance));
            }
            else
            {
                float previousYaw = lookYaw;
                float previousPitch = lookPitch;
                lookYaw += mouseDelta.x * mouseSensitivity;
                lookPitch = Mathf.Clamp(lookPitch - mouseDelta.y * mouseSensitivity, -75f, 78f);
                if (Time.deltaTime > 0f)
                    lookAngularVelocity = new Vector2(lookYaw - previousYaw, lookPitch - previousPitch) / Time.deltaTime;
            }

            if (keyboard.fKey.wasPressedThisFrame)
            {
                if (IsDriving) ExitVehicle();
                else EnterVehicle();
            }

            Vector2 move = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            if (IsDriving)
            {
                if (pistol) pistol.SetExternalInput(false, false, false, false, false, false);
                if (drive) drive.SetInput(move.y, move.x, keyboard.spaceKey.isPressed);
                SetCannonActive(mouse != null && mouse.rightButton.isPressed);
                return;
            }

            SetCannonActive(false);
            MoveOnFoot(Vector2.ClampMagnitude(move, 1f), keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed,
                keyboard.spaceKey.wasPressedThisFrame, keyboard.cKey.wasPressedThisFrame);
            // Update the first-person camera before ray-based interactions this frame.
            UpdateFootCamera();
            if (keyboard.eKey.wasPressedThisFrame) Interact();
            if (hose && keyboard.pKey.wasPressedThisFrame) hose.TogglePump();
            RefreshEquipmentState();
            if (keyboard.rKey.wasPressedThisFrame) ReloadOrRetract();
            if (pistol)
                pistol.SetExternalInput(true, mouse != null && mouse.rightButton.isPressed,
                    mouse != null && mouse.leftButton.wasPressedThisFrame,
                    keyboard.digit1Key.wasPressedThisFrame, keyboard.digit0Key.wasPressedThisFrame, false);
        }

        void LateUpdate()
        {
            if (GameplaySuspended || CinematicLocked) return;
            // Keep the unified character/creature target with the vehicle while its motor is disabled.
            if (IsDriving && firstPerson && drive && playerBody)
                playerBody.transform.position = drive.transform.position;
            if (!viewCamera) return;
            if (IsDriving && drive) UpdateDrivingCamera();
            else UpdateFootCamera();
        }

        public bool EnterVehicle()
        {
            if (GameplaySuspended) return false;
            if (!drive || IsDriving) return false;
            if (!Near(driverDoor ? driverDoor : drive.transform, interactionDistance))
            {
                ShowNotice("Binmek için sürücü kapısına yaklaş.");
                return false;
            }
            if (hose && hose.IsDeployed)
            {
                ShowNotice("Yola çıkmadan önce hortumu aracın arkasında R ile topla.");
                return false;
            }
            SetDriving(true);
            return true;
        }

        public bool ExitVehicle()
        {
            if (GameplaySuspended) return false;
            if (!IsDriving || !drive || !playerBody) return false;
            if (Mathf.Abs(drive.SpeedKph) > 4f)
            {
                ShowNotice("İnmeden önce aracı durdur.");
                return false;
            }
            Vector3 destination = exitPoint ? exitPoint.position : drive.transform.position - drive.transform.right * 3f;
            destination = FindGround(destination);
            if (!IsClearExit(destination))
            {
                destination = FindGround(drive.transform.position + drive.transform.right * 3f);
                if (!IsClearExit(destination))
                {
                    ShowNotice("Kapı önü kapalı. İnmek için aracı açık bir yere park et.");
                    return false;
                }
            }
            playerBody.enabled = false;
            playerBody.transform.position = destination;
            lookYaw = drive.transform.eulerAngles.y;
            lookPitch = 8f;
            verticalVelocity = 0f;
            SetDriving(false);
            return true;
        }

        /// <summary>Initial setup / test entry point; interactive entry also checks the door and hose.</summary>
        public void SetDriving(bool driving)
        {
            SetCannonActive(false);
            IsDriving = driving && drive;
            if (firstPerson) firstPerson.SuspendExternalMovement();
            if (playerBody) playerBody.enabled = !IsDriving;
            if (drive)
            {
                drive.SetInput(0f, 0f, true);
                drive.SetDriven(IsDriving);
            }
            if (IsDriving)
            {
                orbitYaw = 0f;
                lookPitch = 18f;
                if (firstPerson && viewCamera) viewCamera.fieldOfView = firstPerson.fieldOfView;
            }
            RefreshEquipmentState();
            snapCamera = true;
        }

        /// <summary>Synchronizes both hands after external pickup/vehicle actions as well as normal input.</summary>
        public void RefreshEquipmentState()
        {
            bool carrying = hose && hose.IsCarried;
            bool available = !IsDriving && !carrying;
            if (pistol) pistol.SetAvailable(available);
            if (equipmentStateKnown && previousDriving == IsDriving && previousCarrying == carrying) return;
            equipmentStateKnown = true;
            previousDriving = IsDriving;
            previousCarrying = carrying;
            if (firstPerson && firstPerson.handsCamera) firstPerson.handsCamera.enabled = available;
            if (bodyRenderers != null)
                for (int i = 0; i < bodyRenderers.Length; i++)
                    if (bodyRenderers[i]) bodyRenderers[i].enabled = !IsDriving && bodyRendererDefaults[i];
        }

        /// <summary>The deployed hose owns R at the rear socket; elsewhere R belongs to the pistol.</summary>
        public bool ReloadOrRetract()
        {
            if (GameplaySuspended) return false;
            if (IsDriving) return false;
            if (hose && hose.IsDeployed && Near(hose.hoseSocket, interactionDistance))
            {
                hose.Retract();
                RefreshEquipmentState();
                return true;
            }
            if (pistol && pistol.Available) return pistol.Reload();
            if (hose && hose.IsDeployed) ShowNotice("Hortumu toplamak için aracın arkasına yaklaş.");
            return false;
        }

        void MoveOnFoot(Vector2 input, bool sprint, bool jump = false, bool kneel = false, bool captured = true)
        {
            if (!playerBody || !playerBody.enabled) return;
            if (lavaContact) input *= lavaContact.MovementMultiplier;
            playerBody.transform.rotation = Quaternion.Euler(0f, lookYaw, 0f);
            UpdateFootCamera();
            if (firstPerson)
            {
                firstPerson.SimulateExternalMovement(input, sprint, jump, kneel, captured, lookPitch, lookAngularVelocity,
                    hose && hose.IsCarried && hand && hose.hoseSocket ? hoseTravelConstraint : null);
                return;
            }
            Vector3 travel = playerBody.transform.TransformDirection(new Vector3(input.x, 0f, input.y));
            travel *= (sprint ? sprintSpeed : walkSpeed) * Time.deltaTime;
            if (hose && hose.IsCarried && hand && hose.hoseSocket)
                travel = ConstrainHoseTravel(travel);

            if (playerBody.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity = Mathf.Max(verticalVelocity - 22f * Time.deltaTime, -35f);
            CollisionFlags flags = playerBody.Move(travel + Vector3.up * verticalVelocity * Time.deltaTime);
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
        }

        Vector3 ConstrainHoseTravel(Vector3 travel)
        {
            float safeLength = Mathf.Max(0.1f, hose.maxHoseLength - 0.35f);
            Vector3 origin = hose.hoseSocket.position;
            Vector3 intake = hand.position;
            float oldDistance = Vector3.Distance(intake, origin);
            float newDistance = Vector3.Distance(intake + travel, origin);
            if (newDistance <= safeLength || newDistance < oldDistance) return travel;
            if (oldDistance >= safeLength) return Vector3.zero;

            // Shorten this frame's movement at the reach boundary instead of teleporting the player.
            float low = 0f;
            float high = 1f;
            for (int i = 0; i < 10; i++)
            {
                float middle = (low + high) * 0.5f;
                if (Vector3.Distance(intake + travel * middle, origin) <= safeLength) low = middle;
                else high = middle;
            }
            return travel * low;
        }

        /// <summary>E has one action: carried intake, visible door, then nearby intake pickup.</summary>
        public bool Interact()
        {
            if (GameplaySuspended || IsDriving || waitForMouseRelease) return false;
            if (hose && hose.IsCarried) { InteractWithHose(); return true; }
            if (TryGetDoorTarget(out InteractiveDoor door))
            {
                bool changed = door.TryToggle();
                if (!changed && door.LastBlocked) ShowNotice("Kapının önünde engel var. Biraz geri çekil.");
                return changed;
            }
            if (hose && hand && Near(hose.IsDeployed ? hose.nozzle : hose.hoseSocket, interactionDistance))
            {
                bool pickedUp = hose.TryPickup(hand);
                RefreshEquipmentState();
                return pickedUp;
            }
            return false;
        }

        /// <summary>The first solid surface owns the ray; a door behind a wall cannot be used.</summary>
        public bool TryGetDoorTarget(out InteractiveDoor door)
        {
            door = null;
            if (GameplaySuspended || IsDriving || !viewCamera || (hose && hose.IsCarried)) return false;
            int count = Physics.RaycastNonAlloc(viewCamera.transform.position, viewCamera.transform.forward, doorHits,
                Mathf.Max(0f, interactionDistance), ~0, QueryTriggerInteraction.Ignore);
            if (count == doorHits.Length) return false;
            float nearest = float.PositiveInfinity;
            Collider surface = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = doorHits[i];
                // The truck remains a real blocker for doors; only our own body/nozzle is ignored.
                if (playerBody && hit.transform.IsChildOf(playerBody.transform)) continue;
                if (hose && hose.nozzle && hit.transform.IsChildOf(hose.nozzle)) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance; surface = hit.collider;
            }
            door = surface ? surface.GetComponentInParent<InteractiveDoor>() : null;
            return door && door.isActiveAndEnabled;
        }

        void InteractWithHose()
        {
            if (hose.IsCarried)
            {
                Vector3 point = GetDropPoint();
                if (!hose.CanReach(point))
                {
                    ShowNotice("Hortum bu noktaya yetişmiyor. Biraz yaklaş.");
                    return;
                }
                hose.Drop(point);
                return;
            }
            Transform pickupPoint = hose.IsDeployed ? hose.nozzle : hose.hoseSocket;
            if (hand && Near(pickupPoint, interactionDistance)) hose.TryPickup(hand);
            else ShowNotice(hose.IsDeployed ? "Hortum ucuna yaklaş." : "Hortumu almak için aracın arkasına yaklaş.");
        }

        Vector3 GetDropPoint()
        {
            if (!viewCamera) return hand ? hand.position : transform.position;
            Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            RaycastHit[] hits = Physics.RaycastAll(ray, 8f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (IsOwnCollider(hit.collider)) continue;
                // The visible water can have its own solid collider in front of the source trigger.
                // Resolve either surface hit to a submerged intake; dry banks and higher rocks remain solid.
                foreach (WaterSourceVolume source in WaterSourceVolume.ActiveSources)
                {
                    if (!source || Mathf.Abs(hit.point.y - source.SurfaceY) > 0.3f) continue;
                    Vector3 intake = new Vector3(hit.point.x, source.SurfaceY - 0.12f, hit.point.z);
                    if (source.ContainsIntake(intake)) return intake;
                }
                if (!hit.collider.isTrigger) return hit.point + hit.normal * 0.08f;
            }
            Vector3 ahead = viewCamera.transform.position + Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized * 1.5f;
            return FindGround(ahead) + Vector3.up * 0.08f;
        }

        Vector3 FindGround(Vector3 point)
        {
            RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 15f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
                if (!IsOwnCollider(hit.collider)) return hit.point + Vector3.up * 0.12f;
            return point;
        }

        bool IsClearExit(Vector3 point)
        {
            float size = Mathf.Abs(playerBody.transform.lossyScale.y);
            float radius = playerBody.radius * size;
            float height = Mathf.Max(playerBody.height * size, radius * 2f);
            Vector3 bottom = point + Vector3.up * (radius + 0.04f);
            Vector3 top = point + Vector3.up * (height - radius);
            Collider[] overlaps = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider overlap in overlaps)
                if (overlap && !overlap.transform.IsChildOf(playerBody.transform)) return false;
            return true;
        }

        bool IsOwnCollider(Collider other)
        {
            if (!other) return true;
            if (playerBody && other.transform.IsChildOf(playerBody.transform)) return true;
            if (drive && other.transform.IsChildOf(drive.transform)) return true;
            if (hose && hose.nozzle && other.transform.IsChildOf(hose.nozzle)) return true;
            return false;
        }

        bool Near(Transform target, float distance)
        {
            if (!target || !playerBody) return false;
            Vector3 centre = playerBody.transform.position + Vector3.up * 0.9f;
            return Vector3.Distance(centre, target.position) <= distance;
        }

        void UpdateFootCamera()
        {
            if (!viewCamera || !playerBody) return;
            if (firstPerson)
            {
                firstPerson.ApplyExternalView();
                return;
            }
            viewCamera.transform.SetPositionAndRotation(playerBody.transform.position + Vector3.up * eyeHeight,
                Quaternion.Euler(lookPitch, lookYaw, 0f));
        }

        void UpdateDrivingCamera()
        {
            Vector3 target = drive.transform.position + Vector3.up * (followHeight + (aimingCannon ? cannonCameraLift : 0f));
            Quaternion rotation = Quaternion.Euler(lookPitch, drive.transform.eulerAngles.y + orbitYaw, 0f);
            Vector3 desired = target + rotation * Vector3.back * followDistance;
            Vector3 direction = desired - target;
            float distance = direction.magnitude;
            RaycastHit[] hits = Physics.SphereCastAll(target, 0.25f, direction.normalized, distance, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
                if (!IsOwnCollider(hit.collider)) distance = Mathf.Min(distance, Mathf.Max(0.5f, hit.distance - 0.15f));
            desired = target + direction.normalized * distance;
            float blend = snapCamera ? 1f : 1f - Mathf.Exp(-12f * Time.deltaTime);
            viewCamera.transform.position = Vector3.Lerp(viewCamera.transform.position, desired, blend);
            Vector3 lookDirection = target - viewCamera.transform.position;
            if (lookDirection.sqrMagnitude > 0.01f)
                viewCamera.transform.rotation = Quaternion.Slerp(viewCamera.transform.rotation, Quaternion.LookRotation(lookDirection), blend);
            snapCamera = false;
        }

        void ShowNotice(string text)
        {
            notice = text;
            noticeUntil = Time.unscaledTime + 3f;
        }

        void SetCannonActive(bool active)
        {
            aimingCannon = active && !GameplaySuspended && IsDriving && cannon;
            if (cannon) cannon.SetSpraying(aimingCannon);
        }

        void SetCursor(bool locked)
        {
            if (!locked)
            {
                SetCannonActive(false);
                if (firstPerson) firstPerson.SetInputCaptured(false);
                if (pistol) pistol.SetExternalInput(false, false, false, false, false, false);
            }
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Pause gameplay without changing the held equipment or the vehicle/hose state.</summary>
        public void SetGameplaySuspended(bool suspended)
        {
            if (gameplaySuspended == suspended) return;
            gameplaySuspended = suspended;
            StopGameplayInput();
            if (suspended)
            {
                if (pistol)
                {
                    pistolHudBeforeSuspend = pistol.showHud;
                    pistol.showHud = false;
                }
                waitForMouseRelease = false;
                SetCursor(false);
            }
            else
            {
                if (pistol) pistol.showHud = pistolHudBeforeSuspend;
                // A Resume button click must not become a shot or a water command.
                resumeAfterFrame = Time.frameCount;
                waitForMouseRelease = true;
                SetCursor(true);
            }
        }

        void StopGameplayInput()
        {
            lookAngularVelocity = Vector2.zero;
            SetCannonActive(false);
            if (drive) drive.SetInput(0f, 0f, true);
            if (pistol) pistol.SetExternalInput(false, false, false, false, false, false);
            if (firstPerson) firstPerson.SetInputCaptured(false);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (!hasFocus) SetCursor(false);
        }

        void OnDisable()
        {
            SetCannonActive(false);
            if (drive) drive.SetInput(0f, 0f, true);
            SetCursor(false);
            if (pistol) pistol.SetAvailable(false);
            if (firstPerson) firstPerson.SuspendExternalMovement();
        }

        void OnGUI()
        {
            if (!showHud || GameplaySuspended || CinematicLocked) return;
            if (textStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                promptStyle = new GUIStyle(textStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
                titleStyle.normal.textColor = textStyle.normal.textColor = promptStyle.normal.textColor = Color.white;
            }
            float scale = Mathf.Clamp(Screen.height / 900f, 0.75f, 1.5f);
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            GUI.color = new Color(0.035f, 0.05f, 0.065f, 0.86f);
            GUI.DrawTexture(new Rect(18f, 18f, 348f, 178f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(32f, 27f, 315f, 28f), IsDriving ? "İTFAİYE  ·  " + Mathf.Abs(drive ? drive.SpeedKph : 0f).ToString("0") + " km/sa" : "İTFAİYE  ·  YAYA", titleStyle);
            float amount = hose ? hose.Tank01 : 0f;
            GUI.Label(new Rect(32f, 59f, 315f, 24f), "SU DEPOSU  " + (hose ? hose.TankLitres.ToString("0") : "0") + " L  ·  %" + (amount * 100f).ToString("0"), textStyle);
            GUI.color = new Color(0.16f, 0.21f, 0.26f);
            GUI.DrawTexture(new Rect(32f, 86f, 318f, 9f), Texture2D.whiteTexture);
            GUI.color = new Color(0.15f, 0.72f, 0.83f);
            GUI.DrawTexture(new Rect(32f, 86f, 318f * Mathf.Clamp01(amount), 9f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(32f, 102f, 315f, 33f), GetWaterStatus(), textStyle);
            GUI.Label(new Rect(32f, 137f, 315f, 54f), IsDriving ? "WASD Sürüş  ·  Boşluk Fren  ·  F İn\nSağ tık Su  ·  Fare Nişan  ·  Esc İmleç" :
                "WASD Yürü  ·  Shift Koş  ·  F Bin\nE Hortum  ·  P Pompa  ·  R " + (pistol && pistol.Equipped ? "Doldur" : "Topla"), textStyle);

            string prompt = GetPrompt();
            if (Cursor.lockState != CursorLockMode.Locked) prompt = "Devam etmek için oyun ekranına tıkla.";
            if (!string.IsNullOrEmpty(prompt))
            {
                float panelWidth = Mathf.Min(740f, width - 36f);
                GUI.color = new Color(0.035f, 0.05f, 0.065f, 0.82f);
                Rect panel = new Rect((width - panelWidth) * 0.5f, height - 74f, panelWidth, 48f);
                GUI.DrawTexture(panel, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(panel.x + 10f, panel.y, panel.width - 20f, panel.height), prompt, promptStyle);
            }
            if (Cursor.lockState == CursorLockMode.Locked && (IsDriving || !pistol || !pistol.Equipped))
            {
                float x = width * 0.5f;
                float y = height * 0.5f;
                GUI.color = IsDriving && cannon && cannon.IsSpraying ? new Color(0.35f, 0.85f, 1f) : Color.white;
                GUI.DrawTexture(new Rect(x - 2f, y - 2f, 4f, 4f), Texture2D.whiteTexture);
                if (IsDriving && cannon)
                {
                    GUI.DrawTexture(new Rect(x - 13f, y - 1f, 7f, 2f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(x + 6f, y - 1f, 7f, 2f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(x - 1f, y - 13f, 2f, 7f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(x - 1f, y + 6f, 2f, 7f), Texture2D.whiteTexture);
                }
            }
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        string GetPrompt()
        {
            if (Time.unscaledTime < noticeUntil) return notice;
            if (IsDriving)
            {
                if (cannon)
                {
                    if (hose && hose.TankLitres <= 0f) return "Depo boş, gölden doldur. Dur, F ile in ve arka hortumu al.";
                    return "Fareyle yanan eve nişan al.  ·  Sağ tık basılı: Su püskürt  ·  F Araçtan in";
                }
                return drive && Mathf.Abs(drive.SpeedKph) > 4f ? "Göle yaklaş, dur ve hortumu almak için araçtan in." : "F  Araçtan in";
            }
            if (hose && hose.IsCarried) return "Suya bak ve E ile hortum ucunu bırak.  ·  P Pompayı aç/kapat";
            if (TryGetDoorTarget(out InteractiveDoor door)) return door.Prompt;
            if (hose && hose.IsDeployed && Near(hose.nozzle, interactionDistance))
                return Near(hose.hoseSocket, interactionDistance)
                    ? "E  Hortum ucunu al  ·  R Hortumu topla  ·  P Pompayı aç/kapat"
                    : "E  Hortum ucunu al  ·  P Pompayı aç/kapat";
            if (hose && Near(hose.hoseSocket, interactionDistance))
                return hose.IsDeployed ? "R  Hortumu topla  ·  P Pompayı aç/kapat" : "E  Hortumu al";
            if (Near(driverDoor ? driverDoor : (drive ? drive.transform : null), interactionDistance)) return "F  Araca bin";
            if (pistol) return pistol.Equipped ? "Sol tık Ateş  ·  Sağ tık Nişan  ·  R Doldur  ·  C Çök  ·  Boşluk Zıpla  ·  0 Kaldır" : "1  Silahı çıkar  ·  Hortum için aracın arkasına git.";
            if (!hose) return "";
            return hose.IsDeployed ? "P  Pompayı aç/kapat  ·  Hortumu toplamak için aracın arkasına dön." : "Hortumu almak için aracın arkasına git.";
        }

        string GetWaterStatus()
        {
            if (IsDriving && cannon)
            {
                if (hose && hose.TankLitres <= 0f) return "Depo boş, gölden doldur.";
                if (cannon.IsSpraying)
                {
                    if (cannon.HitFire)
                        return "Su " + cannon.flowLitresPerSecond.ToString("0") + " L/sn · Yangın %" + (cannon.HitFire.Heat * 100f).ToString("0");
                    return "Su püskürtülüyor · " + cannon.flowLitresPerSecond.ToString("0") + " L/sn";
                }
                return "Sağ tık basılı: Su püskürt";
            }
            return hose ? hose.Status : "";
        }
    }
}
