using UnityEngine;

namespace Itfaiye
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class ItfaiyeFirstPersonHands : MonoBehaviour
    {
        public Transform view;
        [Range(0f, 1f)] public float presentationWeight = 1f;
        public Vector3 leftRestPosition = new Vector3(-.28f, -.26f, .43f);
        public Vector3 rightRestPosition = new Vector3(.28f, -.26f, .43f);
        public ItfaiyePistol pistol;
        public bool ButtonSlapActive { get; private set; }
        public float ButtonSlapProgress => ButtonSlapActive ? Mathf.Clamp01((Time.time - buttonSlapStarted) / buttonSlapSeconds) : 0f;
        [Range(0f, .5f)] public float buttonLeanInViewMetres = .4f;
        public Vector3 ButtonViewOffset
        {
            get
            {
                if (!ButtonSlapActive) return Vector3.zero;
                float t = ButtonSlapProgress;
                float reach = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, buttonContactFraction, t));
                float recover = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(buttonContactFraction + .14f, 1, t));
                return buttonViewDirection * buttonViewLean * reach * recover;
            }
        }
        float buttonSlapStarted, buttonSlapSeconds, buttonContactFraction;
        float buttonViewLean, buttonDepression;
        Vector3 buttonViewDirection, buttonContactNormal;
        Vector3 buttonWrist;
        Quaternion buttonPalmRotation;
        [Header("Unarmed sprint")]
        [Tooltip("Camera-space arm swing, synchronized to the existing running clip.")]
        public Vector3 unarmedSprintSwing = new Vector3(.04f, .09f, .17f);
        [Range(.05f, .3f)] public float unarmedSprintBlendSeconds = .12f;
        public float UnarmedSprintWeight { get; private set; }
        public static readonly Vector3 DefaultPistolHipPosition = new Vector3(0f, -.15f, .58f);
        [Header("Normal Tutus - Kamera")]
        [Tooltip("Kameraya gore metre: X sag/sol, Y yukari/asagi, Z uzaklik.")]
        public Vector3 pistolHipPosition = DefaultPistolHipPosition;
        [Tooltip("Derece: X yukari/asagi, Y namlunun yonu, Z yana yatis. Duz tutus: 0, 0, 0.")]
        public Vector3 pistolHipEuler = Vector3.zero;
        public float pistolAimDistance = .50f;
        [Tooltip("Optional optic lens centre. ADS uses this instead of the iron rear sight.")]
        public Transform pistolSight;

        [Header("Silah - Kamera Yakinligi")]
        [Tooltip("Silah eldeyken gorus acisini bu kadar daraltir. 0 eski goruntu; 5 hafif yakin. Tutus konumunu ve nisan mesafesini degistirmez.")]
        [Range(0f, 12f)] public float pistolViewZoom = 5f;

        [Header("Silah - Salinim")]
        public bool weaponSway = true;
        [Tooltip("Donuste gecikme ve yana yururken hafif yatis. 0 kapali; 1 varsayilan.")]
        [Range(0f, 2f)] public float swayAmount = 1.35f;
        [Tooltip("Salinimin yumusama suresi (saniye). Kucuk deger daha hizli toparlar.")]
        [Range(.03f, .3f)] public float swaySmoothTime = .09f;
        [Tooltip("Nisan alirken kalan salinim orani.")]
        [Range(0f, 1f)] public float aimSwayMultiplier = .12f;

        [Header("Silah - Yuruyus ve Kosu")]
        [Tooltip("Adimlarla uyumlu yatay/dikey hareketin yuruyusteki siddeti. 0 kapali.")]
        [Range(0f, 3f)] public float walkBobAmount = 1f;
        [Tooltip("Normal yuruyuste hafif namlu egimi (derece). Dururken, nisanda ve ateste sifirlanir.")]
        [Range(0f, 12f)] public float walkLowerAngle = 4f;
        [Tooltip("Yuruyuste eklenen hafif alcalma (metre).")]
        [Range(0f, .03f)] public float walkLowerDistance = .006f;
        [Tooltip("Kosudaki adim saliniminin siddeti. Yuruyusten daha belirgin tutulur.")]
        [Range(0f, 3f)] public float runBobAmount = 1.8f;
        [Tooltip("Kosarken eklenen asagi hareket (metre). Durunca sifirlanir; normal tutusu degistirmez.")]
        [Range(0f, .15f)] public float sprintLowerDistance = .025f;
        [Tooltip("Kosarken namlunun asagi egimi (derece).")]
        [Range(0f, 85f)] public float sprintLowerAngle = 22f;
        [Tooltip("Kosu pozuna giris ve normal tutusa donus yumusakligi (saniye).")]
        [Range(.06f, .3f)] public float locomotionSmoothTime = .16f;
        [Tooltip("Kosarken ates istendiginde silahin dogrulma yumusakligi (saniye). Mermi dogrulduktan sonra cikar.")]
        [Range(.04f, .2f)] public float sprintRaiseSmoothTime = .10f;

        [Header("Normal Tutus - Sol Parmaklar")]
        [Tooltip("Yalnizca sol elin dort parmagini biraz daha kapatir. 0 eski kavrama; basparmak, bilek ve surgu tutusu korunur.")]
        [Range(0f, 1f)] public float leftGripTightness = .35f;

        [Header("Sarjor Degisimi - Silahin Donusu")]
        public Vector3 magazineWeaponEuler = new Vector3(-12f, -12f, 24f);
        [Tooltip("Surgu cekilirken silahin acisi. Negatif Y namluyu sola cevirir; pozitif Z sola yatirir.")]
        public Vector3 slideWeaponEuler = new Vector3(-8f, -28f, 82f);

        [Header("Surgu - Sol El Kavramasi")]
        [Tooltip("Silaha gore bilek konumu (metre). Parmaklar ve bilek bu konumdan surguyle birlikte cekilir.")]
        public Vector3 slideGripPosition = new Vector3(-.092f, .108f, -.065f);
        [Tooltip("Sol elin silaha gore donusu (derece).")]
        public Vector3 slideGripEuler = new Vector3(0f, 175f, -5f);
        public Quaternion SlideGripRotation => Quaternion.Euler(slideGripEuler);

        [Header("Surgu - Parmaklar")]
        [Tooltip("X/Y/Z parmagin uc eklemi: -1 bukulu, +1 acik.")]
        public Vector3 slideThumbCurl = new Vector3(-.9f, -.35f, -.25f);
        public Vector3 slideIndexCurl = new Vector3(-.15f, -.6f, -.35f);
        public Vector3 slideOtherFingersCurl = new Vector3(-.15f, -.6f, -.35f);
        [Range(-1f, 1f)] public float slideThumbSpread = -.3f;
        [Range(-1f, 1f)] public float slideFingerSpread = -.15f;
        [Range(-60f, 60f)] public float slideThumbOpposition = 0f;

        // Metres in the weapon's unscaled reference frame. The model points along +Z.
        public static readonly Vector3 RightGrip = new Vector3(.016f, -.008f, -.121f);
        public static readonly Vector3 LeftGrip = new Vector3(-.029f, -.021f, -.116f);
        public static readonly Quaternion RightGripRotation = Quaternion.Euler(5f, 0f, 0f) * Quaternion.LookRotation(Vector3.down, Vector3.left);
        public static readonly Quaternion LeftGripRotation = Quaternion.Euler(5f, 0f, 0f) * Quaternion.LookRotation(Vector3.up, Vector3.left);
        public static readonly Vector3 RearSight = new Vector3(0f, 1.53f, -1.55f);
        public static readonly Vector3 FrontSight = new Vector3(0f, 1.53f, 1.91f);
        Animator animator;
        Transform[] fingers;
        Quaternion[] gripRotations, openRotations, magazineRotations, slideRotations, triggerRotations;
        Vector3 animatedMotion;
        Vector3 swayPosition, swayEuler, swayPositionVelocity, swayRotationVelocity;
        public Vector3 CurrentSwayOffset { get; private set; }
        public Vector3 CurrentSwayEuler { get; private set; }
        Vector3 bobPosition, bobEuler, bobPositionVelocity, bobRotationVelocity;
        float gaitWeight, gaitVelocity, sprintWeight, sprintVelocity, gaitPhase;
        float sprintLowering, sprintLowerVelocity, walkLowering, walkLowerVelocity, movementPitch;
        public Vector3 CurrentLocomotionOffset { get; private set; }
        public Vector3 CurrentLocomotionEuler { get; private set; }
        public float SprintLowering => sprintLowering;
        public float MovementLowerAngle => movementPitch;
        public bool IsMovementPoseRaised => movementPitch <= .75f;
        bool fingerPoseDirty;
        Vector3 inertiaPosition, inertiaVelocity, previousVelocity;
        float breathPhase;

        void OnValidate() { fingerPoseDirty = true; }

        void Awake()
        {
            animator = GetComponent<Animator>();
            if (!animator.isHuman) return;
            CacheGrip();
        }

        void CacheGrip()
        {
            fingerPoseDirty = false;
            using (var handler = new HumanPoseHandler(animator.avatar, transform))
            {
                var original = new HumanPose(); handler.GetHumanPose(ref original);
                int start = (int)HumanBodyBones.LeftThumbProximal;
                int count = (int)HumanBodyBones.RightLittleDistal - start + 1;
                fingers = new Transform[count];
                for (int i = 0; i < count; i++) fingers[i] = animator.GetBoneTransform((HumanBodyBones)(start + i));
                gripRotations = CacheFingerPose(handler, original, 0);
                openRotations = CacheFingerPose(handler, original, 1);
                magazineRotations = CacheFingerPose(handler, original, 2);
                slideRotations = CacheFingerPose(handler, original, 3);
                triggerRotations = CacheFingerPose(handler, original, 4);
                handler.SetHumanPose(ref original);
            }
        }

        Quaternion[] CacheFingerPose(HumanPoseHandler handler, HumanPose original, int poseIndex)
        {
            var pose = original; pose.muscles = (float[])original.muscles.Clone();
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                string name = HumanTrait.MuscleName[i];
                if (!TryGripMuscle(name, out float value)) continue;
                bool thumb = name.Contains("Thumb"), spread = name.Contains("Spread");
                bool first = name.Contains("1 Stretched"), second = name.Contains("2 Stretched");
                if (name.StartsWith("Left"))
                {
                    if ((poseIndex == 0 || poseIndex == 4) && !thumb && !spread)
                        value = Mathf.Clamp(value - leftGripTightness * (first ? .035f : second ? .22f : .28f), -1f, 1f);
                    if (poseIndex == 1) value = spread ? .05f : thumb ? .25f : .7f;
                    if (poseIndex == 2) value = spread ? (thumb ? .4f : -.1f) : thumb ? (first ? -.6f : .1f) : first ? -.05f : second ? -.3f : -.15f;
                    if (poseIndex == 3)
                    {
                        Vector3 curl = thumb ? slideThumbCurl : name.Contains("Index") ? slideIndexCurl : slideOtherFingersCurl;
                        value = spread ? (thumb ? slideThumbSpread : slideFingerSpread) : first ? curl.x : second ? curl.y : curl.z;
                        value = Mathf.Clamp(value, -1f, 1f);
                    }
                }
                else if (poseIndex == 4 && name.Contains("Index") && !spread) value -= first ? .08f : .20f;
                pose.muscles[i] = value;
            }
            handler.SetHumanPose(ref pose);
            if (poseIndex == 3)
            {
                var thumb = animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
                var axis = Quaternion.Inverse(SlideGripRotation) * Vector3.up;
                thumb.localRotation = Quaternion.AngleAxis(slideThumbOpposition, axis) * thumb.localRotation;
            }
            var rotations = new Quaternion[fingers.Length];
            for (int i = 0; i < rotations.Length; i++) if (fingers[i] != null) rotations[i] = fingers[i].localRotation;
            return rotations;
        }

        public static bool TryGripMuscle(string name, out float value)
        {
            value = 0f;
            if (!name.Contains("Stretched") && !name.Contains("Spread")) return false;
            bool left = name.StartsWith("Left");
            bool first = name.Contains("1 Stretched"), second = name.Contains("2 Stretched");
            if (name.Contains("Thumb"))
                value = name.Contains("Spread") ? (left ? .9f : -1f) : first ? -1f : second ? (left ? .3f : .25f) : .8f;
            else if (name.Contains("Index"))
                value = name.Contains("Spread") ? (left ? 0f : 1f) : first ? (left ? -.8f : -.25f) : second ? (left ? -.15f : -.35f) : (left ? -.1f : .25f);
            else if (name.Contains("Middle"))
                value = name.Contains("Spread") ? 0f : first ? -.85f : second ? (left ? 0f : -.12f) : (left ? -.15f : -.05f);
            else if (name.Contains("Ring"))
                value = name.Contains("Spread") ? -.2f : first ? -.9f : second ? (left ? -.05f : -.16f) : (left ? -.2f : -.1f);
            else if (name.Contains("Little"))
                value = name.Contains("Spread") ? -.4f : first ? -.9f : second ? (left ? -.15f : -.25f) : (left ? -.3f : -.16f);
            else return false;
            return true;
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (view == null || animator == null || !animator.isHuman) return;
            if (ButtonSlapActive)
            {
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0);
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0);
                return;
            }
            if (pistol != null && pistol.DrawAmount > .001f)
            {
                var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var relative = transform.InverseTransformVector(right.position - hips.position);
                animatedMotion = new Vector3(Mathf.Clamp(relative.x - .025f, -.12f, .12f) * .06f,
                    Mathf.Clamp(relative.y - .54f, -.15f, .15f) * .08f,
                    Mathf.Clamp(relative.z - .414f, -.12f, .12f) * .06f);
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                return;
            }
            PresentHand(AvatarIKGoal.LeftHand, HumanBodyBones.LeftHand, leftRestPosition);
            PresentHand(AvatarIKGoal.RightHand, HumanBodyBones.RightHand, rightRestPosition);
        }

        void LateUpdate()
        {
            UpdateSway();
            UpdateLocomotion();
            if (ButtonSlapActive) { PoseButtonSlap(); return; }
            if (view == null || pistol == null || pistol.weapon == null || pistol.DrawAmount <= .001f || animator == null || !animator.isHuman) return;
            if (fingerPoseDirty) CacheGrip();
            float aim = Mathf.SmoothStep(0f, 1f, pistol.AimAmount);
            float scale = pistol.weapon.transform.localScale.x;
            var sight = pistolSight ? pistol.weapon.transform.InverseTransformPoint(pistolSight.position) : RearSight;
            var aimPosition = new Vector3(-sight.x * scale, -sight.y * scale, pistolAimDistance);
            UpdateInertia();
            breathPhase += Mathf.Min(Time.deltaTime, .05f) * 1.65f;
            float breathing = (1f - aim) * (1f - gaitWeight) * (pistol.IsReloading ? 0f : 1f) * pistol.DrawAmount;
            var breath = new Vector3(Mathf.Sin(breathPhase * .5f) * .0006f, Mathf.Sin(breathPhase) * .0012f, 0f) * breathing;
            var position = Vector3.Lerp(pistolHipPosition, aimPosition, aim) + animatedMotion * (.35f * (1f - aim))
                + pistol.PoseOffset + CurrentSwayOffset + CurrentLocomotionOffset + inertiaPosition * Mathf.Lerp(1f, .15f, aim) + breath;
            var heldRotation = pistol.PoseRotation * Quaternion.Slerp(Quaternion.Euler(pistolHipEuler), Quaternion.identity, aim);
            var motionEuler = CurrentSwayEuler + CurrentLocomotionEuler;
            var rotation = Quaternion.Euler(motionEuler) * heldRotation;
            // Tilt around the two-hand grip, not the mesh origin. Otherwise a
            // large downward angle carries the wrists forward and upward.
            var beforeMovementTilt = Quaternion.Euler(motionEuler - Vector3.right * movementPitch) * heldRotation;
            var gripCentre = (RightGrip + LeftGrip) * .5f;
            position += beforeMovementTilt * gripCentre - rotation * gripCentre;
            // Recoil rotates about the wrists as well as translating backwards.
            // Both palms therefore remain planted instead of circling the mesh origin.
            var withoutRecoil = Quaternion.Euler(motionEuler) * Quaternion.Euler(pistol.Recoil * pistol.recoilAngle, 0f, 0f) * heldRotation;
            position += withoutRecoil * gripCentre - rotation * gripCentre;
            pistol.weapon.transform.SetPositionAndRotation(view.TransformPoint(position), view.rotation * rotation);
            PoseArm(false, position + rotation * RightGrip, view.rotation * rotation * RightGripRotation);
            var reload = pistol.ReloadPose;
            Vector3 leftWrist = pistol.IsReloading ? reload.leftWrist : LeftGrip;
            Quaternion leftRotation = pistol.IsReloading ? reload.leftRotation : LeftGripRotation;
            PoseArm(true, position + rotation * leftWrist, view.rotation * rotation * leftRotation);
            for (int i = 0; i < fingers.Length; i++)
            {
                if (fingers[i] == null) continue;
                var target = gripRotations[i];
                bool left = i < (int)HumanBodyBones.RightThumbProximal - (int)HumanBodyBones.LeftThumbProximal;
                if (left && pistol.IsReloading)
                {
                    target = Quaternion.Slerp(target, openRotations[i], reload.openHand);
                    target = Quaternion.Slerp(target, magazineRotations[i], reload.magazineGrip);
                    target = Quaternion.Slerp(target, slideRotations[i], reload.slideGrip);
                }
                else if (!left) target = Quaternion.Slerp(target, triggerRotations[i], pistol.TriggerPull);
                fingers[i].localRotation = Quaternion.Slerp(fingers[i].localRotation, target, pistol.DrawAmount);
            }
        }

        void UpdateSway()
        {
            var player = pistol != null ? pistol.player : null;
            var targetPosition = Vector3.zero;
            var targetEuler = Vector3.zero;
            bool active = weaponSway && player != null && player.InputCaptured && pistol.Equipped && !pistol.IsReloading;
            if (active)
            {
                // Angular velocity keeps the same weight at different frame rates.
                // The clamp prevents a fast flick from throwing the weapon offscreen.
                var turn = Vector2.ClampMagnitude(player.LookAngularVelocity / 160f, 1f);
                float strafe = Mathf.Clamp(player.LocalVelocity.x / Mathf.Max(.1f, player.walkSpeed), -1f, 1f);
                targetPosition = new Vector3(-turn.x * .014f - strafe * .006f, turn.y * .009f, 0f) * swayAmount;
                targetEuler = new Vector3(-turn.y * 1.1f, -turn.x * 2f, turn.x * .8f + strafe * 1.25f) * swayAmount;
            }
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                float smoothing = Mathf.Max(.03f, swaySmoothTime);
                swayPosition = Vector3.SmoothDamp(swayPosition, targetPosition, ref swayPositionVelocity, smoothing, Mathf.Infinity, dt);
                swayEuler = Vector3.SmoothDamp(swayEuler, targetEuler, ref swayRotationVelocity, smoothing, Mathf.Infinity, dt);
            }
            float weight = weaponSway && pistol != null ? pistol.DrawAmount * Mathf.Lerp(1f, aimSwayMultiplier, Mathf.SmoothStep(0f, 1f, pistol.AimAmount)) : 0f;
            if (pistol != null && pistol.KeepRaisedForShot) weight *= .45f;
            CurrentSwayOffset = swayPosition * weight;
            CurrentSwayEuler = swayEuler * weight;
        }

        void UpdateLocomotion()
        {
            var player = pistol != null ? pistol.player : null;
            float dt = Time.deltaTime;
            if (player == null || animator == null || dt <= 0f) return;
            bool unarmedSprint = !pistol.Equipped && player.InputCaptured && player.IsSprinting &&
                player.IsGrounded && player.CurrentSpeed > player.walkSpeed * .65f;
            UnarmedSprintWeight = Mathf.MoveTowards(UnarmedSprintWeight, unarmedSprint ? 1f : 0f,
                dt / Mathf.Max(.05f, unarmedSprintBlendSeconds));
            bool moving = weaponSway && player.InputCaptured && pistol.Equipped && !pistol.IsReloading
                && player.IsGrounded && !player.IsKneeling && player.CurrentSpeed > .08f;
            float speed = player.CurrentSpeed;
            float targetGait = moving ? Mathf.Clamp01(speed / Mathf.Max(.1f, player.walkSpeed)) : 0f;
            float targetSprint = moving && player.IsSprinting && !pistol.IsAiming
                ? Mathf.Clamp01(speed / Mathf.Max(.1f, player.runSpeed)) : 0f;
            float smoothing = Mathf.Max(.06f, locomotionSmoothTime);
            gaitWeight = Mathf.SmoothDamp(gaitWeight, targetGait, ref gaitVelocity, smoothing, Mathf.Infinity, dt);
            sprintWeight = Mathf.SmoothDamp(sprintWeight, targetSprint, ref sprintVelocity, smoothing, Mathf.Infinity, dt);
            // Keep the running rhythm while the gun rises for a shot. Only the
            // lowered pose fades out; neither the saved grip nor run speed changes.
            bool raisingForShot = pistol.KeepRaisedForShot;
            float poseSmoothing = raisingForShot ? Mathf.Max(.04f, sprintRaiseSmoothTime) : smoothing;
            if (raisingForShot)
            {
                sprintLowering = Mathf.MoveTowards(sprintLowering, 0f, dt / Mathf.Clamp(sprintRaiseSmoothTime, .04f, .12f));
                sprintLowerVelocity = 0f;
            }
            else sprintLowering = Mathf.SmoothDamp(sprintLowering, targetSprint,
                ref sprintLowerVelocity, poseSmoothing, Mathf.Infinity, dt);
            float targetWalk = moving && !player.IsSprinting && !pistol.IsAiming ? targetGait : 0f;
            if (raisingForShot)
            {
                walkLowering = Mathf.MoveTowards(walkLowering, 0f, dt / .045f);
                walkLowerVelocity = 0f;
            }
            else walkLowering = Mathf.SmoothDamp(walkLowering, targetWalk,
                ref walkLowerVelocity, poseSmoothing, Mathf.Infinity, dt);
            if (moving)
            {
                // The same locomotion cycle drives the animated feet and this
                // two-step bob. Actual speed gates it, so pushing a wall is still.
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Pistol Locomotion")) gaitPhase = Mathf.Repeat(state.normalizedTime, 1f) * Mathf.PI * 2f;
            }
            float side = Mathf.Sin(gaitPhase);
            float step = Mathf.Cos(gaitPhase * 2f);
            float amplitude = Mathf.Lerp(walkBobAmount, runBobAmount, sprintWeight) * gaitWeight;
            var targetPosition = new Vector3(side * .018f, -step * .011f, Mathf.Sin(gaitPhase * 2f) * .004f) * amplitude;
            var targetEuler = new Vector3(step * .8f, side * .65f, -side * 1.65f) * amplitude;
            bobPosition = Vector3.SmoothDamp(bobPosition, targetPosition, ref bobPositionVelocity, .035f, Mathf.Infinity, dt);
            bobEuler = Vector3.SmoothDamp(bobEuler, targetEuler, ref bobRotationVelocity, .035f, Mathf.Infinity, dt);
            float aim = Mathf.SmoothStep(0f, 1f, pistol.AimAmount);
            float visible = weaponSway ? pistol.DrawAmount : 0f;
            float bobWeight = visible * Mathf.Lerp(1f, aimSwayMultiplier, aim);
            if (raisingForShot) bobWeight *= .35f;
            float lowerWeight = visible * sprintLowering * (1f - aim);
            float walkWeight = visible * walkLowering * (1f - aim);
            movementPitch = sprintLowerAngle * lowerWeight + walkLowerAngle * walkWeight;
            CurrentLocomotionOffset = bobPosition * bobWeight + new Vector3(0f, -sprintLowerDistance, -.02f) * lowerWeight
                + Vector3.down * (walkLowerDistance * walkWeight);
            CurrentLocomotionEuler = bobEuler * bobWeight + Vector3.right * movementPitch;
        }

        void UpdateInertia()
        {
            var player = pistol.player;
            float dt = Time.deltaTime;
            if (player == null || dt <= 0f) return;
            var velocity = new Vector3(player.LocalVelocity.x, 0f, player.LocalVelocity.y);
            var acceleration = Vector3.ClampMagnitude((velocity - previousVelocity) / Mathf.Max(dt, .001f), 20f);
            previousVelocity = velocity;
            var target = weaponSway && player.InputCaptured && !pistol.IsReloading
                ? new Vector3(-acceleration.x * .00035f, 0f, -acceleration.z * .00028f) : Vector3.zero;
            inertiaPosition = Vector3.SmoothDamp(inertiaPosition, target, ref inertiaVelocity, .10f, .3f, dt);
        }

        public bool CanReachButton(Vector3 contact)
        {
            if (!view || !animator || !animator.isHuman) return false;
            var upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            float length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
            Vector3 direction = (contact - view.position).normalized;
            Vector3 shoulder = ButtonShoulderPosition(false) + direction * (buttonLeanInViewMetres * view.lossyScale.y);
            return Vector3.Distance(shoulder, contact) <= length + .12f * view.lossyScale.y;
        }
        public void BeginButtonSlap(Vector3 contact, Vector3 normal, float seconds, float contactFraction)
        {
            if (!view || !animator || !animator.isHuman) return;
            if (fingerPoseDirty) CacheGrip();
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var index = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var little = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            Vector3 indexDirection = hand.InverseTransformVector(index.position - hand.position);
            Vector3 littleDirection = hand.InverseTransformVector(little.position - hand.position);
            Vector3 localForward = (indexDirection + littleDirection).normalized;
            Vector3 localPalm = Vector3.Cross(indexDirection, littleDirection).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, normal).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(view.up, normal).normalized;
            buttonPalmRotation = Quaternion.LookRotation(forward, -normal) * Quaternion.Inverse(Quaternion.LookRotation(localForward, localPalm));
            Vector3 palmOffset = Vector3.Scale((indexDirection + littleDirection) * .5f, hand.lossyScale);
            buttonWrist = contact + normal * .025f - buttonPalmRotation * palmOffset;
            buttonViewDirection = (contact - view.position).normalized;
            buttonViewLean = Mathf.Min(buttonLeanInViewMetres * view.lossyScale.y, Mathf.Max(0, Vector3.Distance(view.position, contact) - .9f));
            buttonContactNormal = normal; buttonDepression = 0;
            buttonSlapStarted = Time.time; buttonSlapSeconds = Mathf.Max(.35f, seconds); buttonContactFraction = Mathf.Clamp(contactFraction, .2f, .7f);
            ButtonSlapActive = true;
        }
        public void CancelButtonSlap() => ButtonSlapActive = false;
        public void SetButtonSlapDepression(float metres) => buttonDepression = Mathf.Max(0, metres);
        Vector3 ButtonShoulderPosition(bool left)
        {
            // The cut shoulders stay behind the first-person camera, including when looking down.
            return view.TransformPoint(new Vector3(left ? -.23f : .23f, -.3f, -.14f));
        }
        void PoseButtonSlap()
        {
            if (!view || !animator || !animator.isHuman) return;
            float progress = ButtonSlapProgress;
            Vector3 rest = new Vector3(.28f, -.28f, .38f), windup = new Vector3(.36f, -.02f, .2f);
            Vector3 contact = view.InverseTransformPoint(buttonWrist - buttonContactNormal * buttonDepression), wrist;
            float windupEnd = buttonContactFraction * .45f;
            if (progress < windupEnd) wrist = Vector3.Lerp(rest, windup, Mathf.SmoothStep(0, 1, progress / windupEnd));
            else if (progress < buttonContactFraction)
            {
                float reach = Mathf.InverseLerp(windupEnd, buttonContactFraction, progress);
                wrist = Vector3.Lerp(windup, contact, reach * reach);
            }
            else wrist = Vector3.Lerp(contact, rest, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(buttonContactFraction + .1f, 1, progress)));
            PoseArm(false, wrist, buttonPalmRotation);
            PoseArm(true, new Vector3(-.3f, -.36f, .32f), view.rotation * LeftGripRotation);
            for (int i = 0; i < fingers.Length; i++)
                if (fingers[i]) fingers[i].localRotation = openRotations[i];
        }

        void PoseArm(bool left, Vector3 wrist, Quaternion wristRotation)
        {
            var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, hand.position);
            float side = left ? -1f : 1f;
            // Camera-space shoulders keep the viewmodel within reach during kneeling and jumps.
            var reach = !ButtonSlapActive && left && pistol.IsReloading ? new Vector3(-.015f, .015f, .035f) * pistol.ReloadPose.weaponWeight : Vector3.zero;
            upper.position = ButtonSlapActive ? ButtonShoulderPosition(left) : view.TransformPoint(new Vector3(side * .17f, -.18f, -.06f) + pistol.PoseOffset * .4f + reach);
            Vector3 target = view.TransformPoint(wrist);
            Vector3 direction = target - upper.position;
            // Let the shoulder follow a small forward reach instead of letting the
            // wrist slip off the gun when stride motion extends the arm fully.
            float viewScale = Mathf.Max(.01f, view.lossyScale.y);
            float shoulderReach = Mathf.Clamp(direction.magnitude - (upperLength + lowerLength - .025f * viewScale), 0f, .12f * viewScale);
            upper.position += direction.normalized * shoulderReach;
            direction = target - upper.position;
            float distance = Mathf.Clamp(direction.magnitude, .01f, upperLength + lowerLength - .001f);
            direction.Normalize();
            Vector3 hint = ButtonSlapActive ? view.right * (side * .45f * viewScale) - Vector3.up * (.2f * viewScale) :
                view.TransformPoint(new Vector3(side * .43f, -.45f, .10f)) - upper.position;
            Vector3 bend = Vector3.ProjectOnPlane(hint, direction).normalized;
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            Vector3 elbow = upper.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            // This avatar's elbow offset is not exactly on local X. Use its measured
            // bone directions so the wrist does not drift a few millimetres off the grip.
            upper.rotation = Quaternion.FromToRotation(wristRotation * lower.localPosition, elbow - upper.position) * wristRotation;
            lower.rotation = Quaternion.FromToRotation(wristRotation * hand.localPosition, target - lower.position) * wristRotation;
            hand.rotation = wristRotation;
        }

        void PresentHand(AvatarIKGoal goal, HumanBodyBones bone, Vector3 rest)
        {
            var hand = animator.GetBoneTransform(bone);
            if (hand == null) return;
            var animated = transform.InverseTransformPoint(hand.position);
            var motion = new Vector3(
                Mathf.Clamp(animated.x - Mathf.Sign(rest.x) * .28f, -.12f, .12f) * .3f,
                Mathf.Clamp(animated.y - .93f, -.22f, .28f) * .45f,
                Mathf.Clamp(animated.z, -.3f, .5f) * .3f);
            float side = Mathf.Sign(rest.x);
            float cycle = animator.GetCurrentAnimatorStateInfo(0).normalizedTime * Mathf.PI * 2f;
            float swing = Mathf.Sin(cycle + (side < 0 ? 0 : Mathf.PI));
            var sprintMotion = new Vector3(-side * unarmedSprintSwing.x * swing,
                unarmedSprintSwing.y * swing, unarmedSprintSwing.z * swing);
            motion = Vector3.Lerp(motion, sprintMotion, UnarmedSprintWeight);
            animator.SetIKPositionWeight(goal, presentationWeight);
            animator.SetIKPosition(goal, view.TransformPoint(rest + motion));
            animator.SetIKRotationWeight(goal, 0f);
            var elbow = goal == AvatarIKGoal.LeftHand ? AvatarIKHint.LeftElbow : AvatarIKHint.RightElbow;
            animator.SetIKHintPositionWeight(elbow, .85f);
            animator.SetIKHintPosition(elbow, view.TransformPoint(new Vector3(Mathf.Sign(rest.x) * .48f, -.5f, .05f)));
        }
    }
}
