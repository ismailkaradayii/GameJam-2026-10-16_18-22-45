using UnityEngine;
using UnityEngine.InputSystem;

namespace Itfaiye
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class ItfaiyePistol : MonoBehaviour
    {
        public ItfaiyeFirstPersonController player;
        public GameObject weapon;
        public Transform muzzle, slide, magazine, trigger;
        public Light muzzleLight;
        public ParticleSystem muzzleFlash, muzzleSmoke;
        public int magazineSize = 12;
        public int reserveAmmo = 48;
        public float shotInterval = .18f;
        [Header("Shot feel")]
        [Range(0f, .15f)] public float triggerBuffer = .10f;
        [Range(1f, 12f)] public float recoilAngle = 7.2f;
        [Range(.01f, .08f)] public float recoilTravel = .042f;
        [Range(.3f, 1f)] public float aimedRecoil = .68f;
        [Tooltip("Kosarken atistan sonra silahin dogrulmus kaldigi sure. Sonra kosu pozuna doner.")]
        [Range(.15f, .6f)] public float sprintShotHoldDuration = .32f;
        public float reloadDuration = 3.2f;
        public bool autoReloadWhenEmpty = true;
        [Tooltip("Let the final shot and slide lock finish before the existing reload animation begins.")]
        [Min(0f)] public float autoReloadDelay = .22f;
        public float range = 100f;
        public bool showHud = true;
        public bool showAmmoHud = true;
        [Tooltip("Read commands from the gameplay coordinator instead of polling keyboard and mouse.")]
        public bool externalControl;
        [Min(0f)] public float creatureDamage = 34f;

        public bool Available { get; private set; } = true;
        public bool Equipped { get; private set; }
        public bool IsReloading { get; private set; }
        public bool IsAiming { get; private set; }
        public float AimAmount { get; private set; }
        public float DrawAmount { get; private set; }
        public float Recoil { get; private set; }
        public int Ammo { get; private set; }
        public int ShotsFired { get; private set; }
        public float ReloadProgress => IsReloading ? Mathf.Clamp01((Time.time - reloadStarted) / reloadDuration) : 0f;
        public bool Ready => Available && Equipped && DrawAmount > .98f && !IsReloading && !holsterRequested;
        public bool SprintShotQueued => sprintShotQueued;
        public bool KeepRaisedForShot => sprintShotQueued || Time.time < raisedUntil;
        public float ShotAge => Time.time - shotStarted;
        public float TriggerPull => Mathf.Clamp01(1f - ShotAge / .13f);
        public float SlideAmount { get; private set; }
        public bool ReloadCommitted { get; private set; }
        public Transform ReplacementMagazine => replacementMagazine;
        public ItfaiyeFirstPersonHands Hands { get; private set; }
        public ItfaiyePistolFeedback Feedback { get; private set; }
        public ItfaiyePistolCasings Casings { get; private set; }
        public ItfaiyePistolReloadPose ReloadPose => ItfaiyePistolReloadPose.Sample(ReloadProgress, MagazineBase, reloadWasEmpty, Hands);
        public Vector3 MagazineBase => Vector3.Scale(magazineRest, weapon.transform.localScale);
        public Vector3 PoseOffset => new Vector3(-.025f * ReloadWeight, -.48f * (1f - DrawAmount) + .055f * ReloadWeight,
            -.12f * (1f - DrawAmount) - .08f * ReloadWeight - Recoil * recoilTravel);
        public Quaternion PoseRotation
        {
            get
            {
                var pose = ReloadPose;
                float weight = IsReloading ? pose.weaponWeight : 0f;
                var reloadRotation = Quaternion.Slerp(Quaternion.Euler(Hands.magazineWeaponEuler),
                    Quaternion.Euler(Hands.slideWeaponEuler), pose.slideTilt);
                return Quaternion.Euler(-Recoil * recoilAngle, Recoil * recoilSide * .65f, Recoil * recoilSide * .55f)
                    * Quaternion.Slerp(Quaternion.identity, reloadRotation, weight);
            }
        }
        float ReloadWeight => IsReloading ? ReloadPose.weaponWeight : 0f;

        public Ray AimRay { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public Collider AimCollider { get; private set; }
        public bool MuzzleObstructed { get; private set; }
        public Ray LastShotRay { get; private set; }
        public Vector3 LastShotPoint { get; private set; }
        public Collider LastShotCollider { get; private set; }
        public RaycastHit LastShotHit { get; private set; }
        public Vector3 CrosshairScreenPosition => player.playerCamera.WorldToScreenPoint(AimPoint);
        public bool FlashVisible => muzzleFlash != null && muzzleFlash.particleCount > 0;

        readonly RaycastHit[] hits = new RaycastHit[32];
        RaycastHit aimHit;
        float nextShot, reloadStarted, shotStarted = -100f, flashUntil, recoilSide = 1f;
        float raisedUntil;
        float queuedShotUntil;
        readonly float[] recoilTimes = { -100f, -100f, -100f, -100f };
        readonly float[] recoilWeights = new float[4];
        int recoilIndex;
        bool sprintShotQueued;
        bool holsterRequested, previouslyCaptured, pendingShot, reloadWasEmpty;
        bool externalCaptured, externalAim, externalFire, externalToggle, externalHolster, externalReload;
        Vector3 slideRest, magazineRest;
        Quaternion magazineRestRotation, triggerRest;
        Transform replacementMagazine;
        GUIStyle label;

        void Awake()
        {
            if (player != null && player.handsAnimator != null)
                Hands = player.handsAnimator.GetComponent<ItfaiyeFirstPersonHands>();
            Feedback = GetComponent<ItfaiyePistolFeedback>();
            Casings = GetComponent<ItfaiyePistolCasings>();
            Ammo = magazineSize;
            if (slide != null) slideRest = slide.localPosition;
            if (trigger != null) triggerRest = trigger.localRotation;
            if (magazine != null)
            {
                magazineRest = magazine.localPosition;
                magazineRestRotation = magazine.localRotation;
                replacementMagazine = Instantiate(magazine, magazine.parent);
                replacementMagazine.name = "Replacement Magazine";
                replacementMagazine.gameObject.SetActive(false);
            }
            if (weapon != null) weapon.SetActive(false);
            if (muzzleLight != null) muzzleLight.enabled = false;
        }

        void Update()
        {
            if (!Available || player == null) return;
            bool captured = player.InputCaptured && (!externalControl || externalCaptured);
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            bool aim = externalControl ? externalAim : mouse != null && mouse.rightButton.isPressed;
            bool fire = externalControl ? externalFire : mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (captured && externalControl)
            {
                if (externalToggle) Toggle();
                if (externalHolster) Holster();
                if (externalReload) Reload();
            }
            else if (captured && keys != null)
            {
                if (keys.digit1Key.wasPressedThisFrame) Toggle();
                if (keys.digit0Key.wasPressedThisFrame) Holster();
                if (keys.rKey.wasPressedThisFrame) Reload();
            }
            externalFire = externalToggle = externalHolster = externalReload = false;
            if (IsReloading)
            {
                if (!ReloadCommitted && ReloadProgress >= ItfaiyePistolReloadPose.InsertedAt)
                {
                    int transfer = Mathf.Min(magazineSize - Ammo, reserveAmmo);
                    Ammo += transfer;
                    reserveAmmo -= transfer;
                    ReloadCommitted = true;
                }
                if (ReloadProgress >= 1f) IsReloading = false;
            }
            // Finish the physical swap before lowering the weapon; another 1 cancels
            // the queued holster without restarting the reload or duplicating ammo.
            if (holsterRequested && player.CanChangeWeapon && !IsReloading)
            {
                Equipped = false;
                holsterRequested = false;
            }
            DrawAmount = Mathf.MoveTowards(DrawAmount, Equipped ? 1f : 0f, Time.deltaTime / .28f);
            if (weapon != null) weapon.SetActive(DrawAmount > .05f);
            // LateUpdate must resolve the last bullet before reload changes the hand pose.
            // Re-check on redraw/resume too; entering a vehicle may cancel an empty reload.
            if (autoReloadWhenEmpty && captured && previouslyCaptured && Time.timeScale > 0f &&
                Ready && Ammo == 0 && reserveAmmo > 0 && !pendingShot && !sprintShotQueued &&
                ShotAge >= Mathf.Max(0f, autoReloadDelay)) Reload();
            if (!captured || !Ready) CancelSprintShot();
            IsAiming = captured && Ready && aim && !player.IsSprinting;
            AimAmount = Mathf.MoveTowards(AimAmount, IsAiming ? 1f : 0f, Time.deltaTime / .18f);
            Recoil = SampleRecoil(1f);
            if (captured && previouslyCaptured && fire) Fire();
            previouslyCaptured = captured;
        }

        void LateUpdate()
        {
            if (!Available || player == null || weapon == null || muzzle == null) return;
            if (sprintShotQueued && Time.time > queuedShotUntil) CancelSprintShot();
            UpdateMechanics();
            if (Equipped)
            {
                // Fast hopping hitboxes must match the current rendered pose when firing.
                Physics.SyncTransforms();
                UpdateAim();
            }
            // Hands have already applied this frame's pose. Do not spend ammo or
            // emit effects until the walking or sprint tilt has cleared.
            if (sprintShotQueued && Ready && player.InputCaptured && Ammo > 0
                && Time.time >= nextShot && (Hands == null || Hands.IsMovementPoseRaised))
                CommitShot();
            if (pendingShot)
            {
                pendingShot = false;
                LastShotRay = AimRay;
                LastShotPoint = AimPoint;
                LastShotCollider = AimCollider;
                LastShotHit = aimHit;
                if (LastShotCollider != null)
                {
                    var creature = LastShotCollider.GetComponentInParent<GameJam.FireRescue.LavaCreatureActor>();
                    if (creature != null) creature.ApplyBulletDamage(creatureDamage, aimHit.point, AimRay.direction);
                }
                if (Feedback != null) Feedback.PlayShot(AimRay, AimPoint, aimHit, AimAmount, MuzzleObstructed);
                if (AimCollider != null && AimCollider.attachedRigidbody != null && !AimCollider.attachedRigidbody.isKinematic)
                    AimCollider.attachedRigidbody.AddForceAtPosition(AimRay.direction * 12f, AimPoint, ForceMode.Impulse);
                // The shot uses this frame's posed barrel. Recoil begins after that ray,
                // rather than moving the gun before deciding where the bullet went.
                shotStarted = Time.time;
                recoilTimes[recoilIndex] = Time.time;
                recoilWeights[recoilIndex] = Mathf.Lerp(1f, aimedRecoil, AimAmount);
                recoilIndex = (recoilIndex + 1) % recoilTimes.Length;
                if (Casings != null) Casings.QueueEjection();
                recoilSide = ShotsFired % 2 == 0 ? -.6f : 1f;
                flashUntil = Time.time + .045f;
                if (muzzleFlash != null) { muzzleFlash.Clear(); muzzleFlash.Play(); muzzleFlash.Emit(2); }
                if (muzzleSmoke != null) { muzzleSmoke.Play(); muzzleSmoke.Emit(2); }
            }
            if (muzzleLight != null) muzzleLight.enabled = Equipped && Time.time < flashUntil;
        }

        void UpdateMechanics()
        {
            float scale = weapon.transform.localScale.x;
            var pose = ReloadPose;
            if (magazine != null)
            {
                magazine.gameObject.SetActive(!IsReloading || pose.originalVisible);
                bool removing = IsReloading && ReloadProgress < .40f;
                magazine.localPosition = magazineRest + (removing ? pose.magazineOffset / scale : Vector3.zero);
                magazine.localRotation = removing ? pose.magazineRotation * magazineRestRotation : magazineRestRotation;
            }
            if (replacementMagazine != null)
            {
                replacementMagazine.gameObject.SetActive(IsReloading && pose.replacementVisible);
                replacementMagazine.localPosition = magazineRest + pose.magazineOffset / scale;
                replacementMagazine.localRotation = pose.magazineRotation * magazineRestRotation;
            }
            float cycling = ShotAge < .023f ? ItfaiyePistolReloadPose.Ease(0f, .023f, ShotAge) : 1f - ItfaiyePistolReloadPose.Ease(.035f, .095f, ShotAge);
            if (IsReloading) SlideAmount = pose.slidePull;
            else SlideAmount = Ammo == 0 && ShotsFired > 0 ? 1f : cycling;
            if (slide != null) slide.localPosition = slideRest + Vector3.back * (SlideAmount * .032f / scale);
            if (trigger != null) trigger.localRotation = triggerRest * Quaternion.Euler(9f * TriggerPull, 0f, 0f);
        }

        void UpdateAim()
        {
            AimRay = new Ray(muzzle.position, muzzle.forward);
            var cameraToMuzzle = muzzle.position - player.playerCamera.transform.position;
            MuzzleObstructed = Raycast(new Ray(player.playerCamera.transform.position, cameraToMuzzle.normalized), cameraToMuzzle.magnitude, out var hit);
            if (MuzzleObstructed || Raycast(AimRay, range, out hit))
            {
                AimPoint = hit.point;
                AimCollider = hit.collider;
                aimHit = hit;
            }
            else
            {
                AimPoint = AimRay.GetPoint(range);
                AimCollider = null;
                aimHit = default;
            }
        }

        bool Raycast(Ray ray, float distance, out RaycastHit closest)
        {
            closest = default;
            int count = Physics.RaycastNonAlloc(ray, hits, distance, Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("FirstPersonHands")), QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(player.transform) || hits[i].distance >= nearest) continue;
                nearest = hits[i].distance; closest = hits[i];
            }
            return nearest < float.PositiveInfinity;
        }

        public void SetExternalInput(bool captured, bool aim, bool fire, bool toggle, bool holster, bool reload)
        {
            bool lostCapture = externalCaptured && !captured;
            externalCaptured = captured && Available;
            externalAim = externalCaptured && aim;
            externalFire = externalCaptured && fire;
            externalToggle = externalCaptured && toggle;
            externalHolster = externalCaptured && holster;
            externalReload = externalCaptured && reload;
            if (!externalCaptured)
            {
                if (lostCapture && Feedback != null) Feedback.ClearViewEffects();
                CancelSprintShot();
                if (lostCapture) pendingShot = false;
                IsAiming = false;
                previouslyCaptured = false;
            }
        }

        /// <summary>Vehicle and hose use require both hands and cancel all weapon actions immediately.</summary>
        public void SetAvailable(bool available)
        {
            if (Available == available) return;
            Available = available;
            if (available) return;
            if (player != null) player.SetKneeling(false);
            Equipped = false;
            holsterRequested = previouslyCaptured = false;
            DrawAmount = AimAmount = Recoil = 0f;
            SetExternalInput(false, false, false, false, false, false);
            ClearActionsAndEffects();
            if (weapon != null) weapon.SetActive(false);
        }

        public void Toggle() { if (!Available) return; if (Equipped && !holsterRequested) Holster(); else Equip(); }
        public void Equip() { if (!Available) return; holsterRequested = false; Equipped = true; }
        public void Holster()
        {
            if (!Equipped) return;
            if (Feedback != null) Feedback.ClearViewEffects();
            CancelSprintShot();
            player.SetKneeling(false);
            holsterRequested = true;
        }
        public bool Reload()
        {
            if (!Ready || pendingShot || reserveAmmo <= 0 || Ammo >= magazineSize) return false;
            CancelSprintShot();
            IsReloading = true;
            ReloadCommitted = false;
            reloadWasEmpty = Ammo == 0 && ShotsFired > 0;
            reloadStarted = Time.time;
            IsAiming = false;
            return true;
        }
        public bool Fire()
        {
            if (!Ready || pendingShot || sprintShotQueued || Ammo <= 0) return false;
            // Keep a single deliberate click just before the next shot is ready.
            // Holding fire still cannot turn this semiautomatic pistol into an automatic.
            if (Time.time < nextShot - triggerBuffer) return false;
            if (Time.time < nextShot || (Hands != null && !Hands.IsMovementPoseRaised))
            {
                sprintShotQueued = true;
                queuedShotUntil = Mathf.Max(Time.time, nextShot) + .22f;
                return true;
            }
            CommitShot();
            return true;
        }

        void CommitShot()
        {
            sprintShotQueued = false;
            raisedUntil = Time.time + sprintShotHoldDuration;
            nextShot = Time.time + shotInterval;
            Ammo--; ShotsFired++; pendingShot = true;
        }

        void CancelSprintShot()
        {
            sprintShotQueued = false;
            raisedUntil = 0f;
            queuedShotUntil = 0f;
        }

        // Difference of two decays: a fast mechanical impulse followed by a
        // damped return. Pulses add, so a second shot never snaps recoil to zero.
        public static float ShotImpulse(float age)
        {
            if (age < 0f || age > .65f) return 0f;
            return (Mathf.Exp(-age / .065f) - Mathf.Exp(-age / .009f)) * 1.60f;
        }

        public float SampleRecoil(float recoveryScale)
        {
            float value = 0f;
            for (int i = 0; i < recoilTimes.Length; i++)
                value += ShotImpulse((Time.time - recoilTimes[i]) / Mathf.Max(.1f, recoveryScale)) * recoilWeights[i];
            return Mathf.Min(value, 1.45f);
        }

        void OnDisable()
        {
            ClearActionsAndEffects();
        }

        void ClearActionsAndEffects()
        {
            if (Feedback != null) Feedback.ClearViewEffects();
            CancelSprintShot();
            IsAiming = false; IsReloading = false; pendingShot = false;
            Recoil = 0f;
            for (int i = 0; i < recoilTimes.Length; i++) { recoilTimes[i] = -100f; recoilWeights[i] = 0f; }
            if (muzzleLight != null) muzzleLight.enabled = false;
            if (muzzleFlash != null) muzzleFlash.Clear();
            if (muzzleSmoke != null) muzzleSmoke.Clear();
            if (replacementMagazine != null) replacementMagazine.gameObject.SetActive(false);
            if (magazine != null)
            {
                magazine.gameObject.SetActive(true);
                magazine.localPosition = magazineRest;
                magazine.localRotation = magazineRestRotation;
            }
            if (slide != null) slide.localPosition = slideRest;
        }
        void OnDestroy() { if (replacementMagazine != null) Destroy(replacementMagazine.gameObject); }

        void OnGUI()
        {
            if (!Available || !showHud || !Equipped) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.LowerRight };
            GUI.color = new Color(1f, 1f, 1f, .85f);
            if (showAmmoHud) GUI.Label(new Rect(Screen.width - 230, Screen.height - 58, 204, 32),
                IsReloading ? "DOLDURULUYOR" : $"A3500X   {Ammo:00} / {reserveAmmo:00}", label);
            if (Ready && player.InputCaptured && !(Hands != null && Hands.pistolSight && AimAmount > .65f) && (!player.IsSprinting
                || (KeepRaisedForShot && (Hands == null || Hands.IsMovementPoseRaised))))
            {
                Vector3 point = CrosshairScreenPosition;
                if (point.z > 0f)
                {
                    float size = AimAmount > .8f ? 2f : 3f;
                    GUI.color = new Color(0f, 0f, 0f, .6f);
                    GUI.DrawTexture(new Rect(point.x - size * .5f - 1f, Screen.height - point.y - size * .5f - 1f, size + 2f, size + 2f), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.DrawTexture(new Rect(point.x - size * .5f, Screen.height - point.y - size * .5f, size, size), Texture2D.whiteTexture);
                }
            }
            GUI.color = Color.white;
        }
    }
}
