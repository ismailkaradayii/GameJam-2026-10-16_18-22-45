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
        [Header("Walking and running arm swing")]
        [Tooltip("Camera-space arm swing. Running alternates the arms forward and back below the sightline.")]
        public Vector3 unarmedWalkSwing = new Vector3(.012f, .025f, .045f);
        public Vector3 unarmedSprintSwing = new Vector3(.025f, .06f, .11f);
        [Range(0f, .06f)] public float unarmedMovementLowering = .035f;
        [Range(.05f, .3f)] public float unarmedSprintBlendSeconds = .18f;
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
        [Range(0f, 2f)] public float swayAmount = .55f;
        [Tooltip("Salinimin yumusama suresi (saniye). Kucuk deger daha hizli toparlar.")]
        [Range(.03f, .3f)] public float swaySmoothTime = .14f;
        [Tooltip("Nisan alirken kalan salinim orani.")]
        [Range(0f, 1f)] public float aimSwayMultiplier = .12f;

        [Header("Silah - Yuruyus ve Kosu")]
        [Tooltip("Adimlarla uyumlu yatay/dikey hareketin yuruyusteki siddeti. 0 kapali.")]
        [Range(0f, 3f)] public float walkBobAmount = .55f;
        [Tooltip("Normal yuruyuste hafif namlu egimi (derece). Dururken, nisanda ve ateste sifirlanir.")]
        [Range(0f, 12f)] public float walkLowerAngle = 1.5f;
        [Tooltip("Yuruyuste eklenen hafif alcalma (metre).")]
        [Range(0f, .03f)] public float walkLowerDistance = .004f;
        [Tooltip("Kosudaki adim saliniminin siddeti. Yuruyusten daha belirgin tutulur.")]
        [Range(0f, 3f)] public float runBobAmount = .85f;
        [Tooltip("Kosarken eklenen asagi hareket (metre). Durunca sifirlanir; normal tutusu degistirmez.")]
        [Range(0f, .15f)] public float sprintLowerDistance = .018f;
        [Tooltip("Kosarken namlunun asagi egimi (derece).")]
        [Range(0f, 85f)] public float sprintLowerAngle = 8f;
        [Tooltip("Kosu pozuna giris ve normal tutusa donus yumusakligi (saniye).")]
        [Range(.06f, .3f)] public float locomotionSmoothTime = .22f;
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
        // Seat the palm outside the right grip panel and behind the backstrap.
        // A level palm raises the knuckles without lifting the thumb web into
        // the beavertail; raising the wrist alone makes the frame cut through it.
        public static readonly Vector3 RightGrip = new Vector3(.028f, -.014f, -.150f);
        public static readonly Vector3 LeftGrip = new Vector3(-.031f, -.017f, -.124f);
        public static readonly Quaternion RightGripRotation = Quaternion.LookRotation(Vector3.down, Vector3.left);
        public static readonly Quaternion LeftGripRotation = Quaternion.Euler(5f, 0f, 0f) * Quaternion.LookRotation(Vector3.up, Vector3.left);
        public static readonly Vector3 RearSight = new Vector3(0f, 1.53f, -1.55f);
        public static readonly Vector3 FrontSight = new Vector3(0f, 1.53f, 1.91f);
        Animator animator;
        Transform[] fingers;
        Quaternion[] gripRotations, openRotations, magazineRotations, slideRotations, triggerRotations, relaxedRotations, runningRotations;
        Vector3 animatedMotion;
        Vector3 swayPosition, swayEuler, swayPositionVelocity, swayRotationVelocity;
        public Vector3 CurrentSwayOffset { get; private set; }
        public Vector3 CurrentSwayEuler { get; private set; }
        Vector3 bobPosition, bobEuler, bobPositionVelocity, bobRotationVelocity;
        float gaitWeight, gaitVelocity, sprintWeight, sprintVelocity, gaitPhase;
        float unarmedGaitWeight, unarmedGaitVelocity, unarmedSprintVelocity, unarmedPhase;
        Vector3 leftUnarmedMotion, rightUnarmedMotion, leftUnarmedVelocity, rightUnarmedVelocity;
        float unarmedSwingRotation, unarmedSwingRotationVelocity;
        float sprintLowering, sprintLowerVelocity, walkLowering, walkLowerVelocity, movementPitch;
        public Vector3 CurrentLocomotionOffset { get; private set; }
        public Vector3 CurrentLocomotionEuler { get; private set; }
        public float SprintLowering => sprintLowering;
        public float MovementLowerAngle => movementPitch;
        public bool IsMovementPoseRaised => movementPitch <= .75f;
        bool fingerPoseDirty;
        Vector3 inertiaPosition, inertiaVelocity, previousVelocity;
        float breathPhase;
        ItfaiyeFirstPersonController swimmingPlayer;

        void OnValidate() { fingerPoseDirty = true; }

        void Awake()
        {
            animator = GetComponent<Animator>();
            swimmingPlayer = GetComponentInParent<ItfaiyeFirstPersonController>();
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
                relaxedRotations = CacheFingerPose(handler, original, 5);
                runningRotations = CacheFingerPose(handler, original, 6);
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
                if (poseIndex == 6)
                    value = spread ? (thumb ? .05f : -.2f) : thumb ? (first ? -.55f : second ? -.2f : .05f) : first ? -.55f : second ? -.65f : -.35f;
                else if (poseIndex == 5)
                    value = spread ? (thumb ? .1f : -.15f) : thumb ? (first ? -.4f : second ? .1f : .35f) : first ? -.3f : second ? -.4f : .05f;
                else if (name.StartsWith("Left"))
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
                else if (poseIndex == 1) value = spread ? .05f : thumb ? .25f : .7f;
                else if (poseIndex == 4 && name.Contains("Index") && !spread) value -= first ? .08f : .20f;
                pose.muscles[i] = value;
            }
            handler.SetHumanPose(ref pose);
            if (poseIndex == 0 || poseIndex == 4)
            {
                // Route the firing thumb behind the backstrap before it rests
                // against the frame. The support thumb lies lower and farther forward.
                PoseGripThumb(false, new Vector3(-.028f, .025f, -.080f), new Vector3(-.018f, .010f, -.120f));
                PoseGripThumb(true, new Vector3(-.020f, .022f, -.040f), new Vector3(-.050f, .025f, -.070f));
            }
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

        void PoseGripThumb(bool left, Vector3 distalTarget, Vector3 bendHint)
        {
            var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            var middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);
            var distal = animator.GetBoneTransform(left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal);
            if (!hand || !upper || !middle || !distal) return;
            var grip = left ? LeftGrip : RightGrip;
            var intoHand = Quaternion.Inverse(left ? LeftGripRotation : RightGripRotation);
            Vector3 target = hand.TransformPoint(intoHand * (distalTarget - grip));
            Vector3 hint = hand.TransformPoint(intoHand * (bendHint - grip));
            float first = Vector3.Distance(upper.position, middle.position);
            float second = Vector3.Distance(middle.position, distal.position);
            Vector3 direction = (target - upper.position).normalized;
            float distance = Mathf.Clamp(Vector3.Distance(upper.position, target), .0001f, first + second - .0001f);
            target = upper.position + direction * distance;
            Vector3 bend = Vector3.ProjectOnPlane(hint - upper.position, direction).normalized;
            float along = (first * first - second * second + distance * distance) / (2f * distance);
            Vector3 joint = upper.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, first * first - along * along));
            upper.rotation = Quaternion.FromToRotation(middle.position - upper.position, joint - upper.position) * upper.rotation;
            middle.rotation = Quaternion.FromToRotation(distal.position - middle.position, target - middle.position) * middle.rotation;
            // Keep the fingertip along the barrel instead of curling through the frame.
            Vector3 forward = hand.TransformDirection(intoHand * Vector3.forward);
            distal.rotation = Quaternion.FromToRotation(distal.TransformDirection(left ? Vector3.left : Vector3.right), forward) * distal.rotation;
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
                value = name.Contains("Spread") ? (left ? 0f : -.3f) : first ? (left ? -.8f : .5f) : second ? (left ? -.15f : .1f) : (left ? -.1f : -.6f);
            else if (name.Contains("Middle"))
                value = name.Contains("Spread") ? 0f : first ? (left ? -.85f : -.25f) : second ? (left ? 0f : -.35f) : (left ? -.15f : .15f);
            else if (name.Contains("Ring"))
                value = name.Contains("Spread") ? -.2f : first ? (left ? -.9f : -.3f) : second ? (left ? -.05f : -.35f) : (left ? -.2f : .15f);
            else if (name.Contains("Little"))
                value = name.Contains("Spread") ? -.4f : first ? (left ? -.9f : -.3f) : second ? (left ? -.15f : -.40f) : (left ? -.3f : .15f);
            else return false;
            return true;
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (view == null || animator == null || !animator.isHuman) return;
            if (ButtonSlapActive || (swimmingPlayer && swimmingPlayer.IsSwimming))
            {
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0);
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0);
                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0);
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
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                return;
            }
            // The first-person wrist solver runs after the avatar animation.
            // Humanoid IK goal rotations differ from this mesh's actual wrist axes.
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
        }

        void LateUpdate()
        {
            UpdateSway();
            UpdateLocomotion();
            if (ButtonSlapActive) { PoseButtonSlap(); return; }
            bool unarmed = pistol == null || pistol.DrawAmount <= .001f;
            bool swimmingPresentation = swimmingPlayer && swimmingPlayer.Swimming && swimmingPlayer.Swimming.Blend > .001f;
            if (unarmed && !swimmingPresentation && view && animator && animator.isHuman) PresentUnarmedHands();
            if (swimmingPresentation && view && animator && animator.isHuman)
            {
                PresentSwimmingAnimation();
                if (swimmingPlayer.IsSwimming) return;
            }
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
            var position = Vector3.Lerp(pistolHipPosition, aimPosition, aim) + animatedMotion * (.12f * (1f - aim))
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
                float amount = Mathf.Clamp(swayAmount, 0f, .65f);
                targetPosition = new Vector3(-turn.x * .014f - strafe * .006f, turn.y * .009f, 0f) * amount;
                targetEuler = new Vector3(-turn.y * 1.1f, -turn.x * 2f, turn.x * .8f + strafe * 1.25f) * amount;
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
            var player = pistol != null ? pistol.player : swimmingPlayer;
            float dt = Time.deltaTime;
            if (player == null || animator == null || dt <= 0f) return;
            UpdateUnarmedLocomotion(player, dt);
            if (pistol == null) return;
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
            // Both hands keep their grip while the running stride visibly carries
            // the weapon. Aiming and firing still damp this movement below.
            float amplitude = Mathf.Lerp(Mathf.Clamp(walkBobAmount, 0f, .65f), Mathf.Clamp(runBobAmount, 0f, .95f), sprintWeight) * gaitWeight;
            Vector3 stride = Vector3.Lerp(new Vector3(.01f, .0045f, .002f), new Vector3(.024f, .014f, .009f), sprintWeight);
            Vector3 tilt = Vector3.Lerp(new Vector3(.3f, .2f, .3f), new Vector3(1.6f, .65f, .8f), sprintWeight);
            var targetPosition = new Vector3(side * stride.x, -step * stride.y, Mathf.Sin(gaitPhase * 2f) * stride.z) * amplitude;
            var targetEuler = new Vector3(step * tilt.x, side * tilt.y, -side * tilt.z) * amplitude;
            bobPosition = Vector3.SmoothDamp(bobPosition, targetPosition, ref bobPositionVelocity, .085f, Mathf.Infinity, dt);
            bobEuler = Vector3.SmoothDamp(bobEuler, targetEuler, ref bobRotationVelocity, .085f, Mathf.Infinity, dt);
            float aim = Mathf.SmoothStep(0f, 1f, pistol.AimAmount);
            float visible = weaponSway ? pistol.DrawAmount : 0f;
            float bobWeight = visible * Mathf.Lerp(1f, aimSwayMultiplier, aim);
            if (raisingForShot) bobWeight *= .35f;
            float lowerWeight = visible * sprintLowering * (1f - aim);
            float walkWeight = visible * walkLowering * (1f - aim);
            movementPitch = Mathf.Clamp(sprintLowerAngle, 0f, 10f) * lowerWeight + Mathf.Clamp(walkLowerAngle, 0f, 2f) * walkWeight;
            CurrentLocomotionOffset = bobPosition * bobWeight + new Vector3(0f, -Mathf.Clamp(sprintLowerDistance, 0f, .025f), -.006f) * lowerWeight
                + Vector3.down * (Mathf.Clamp(walkLowerDistance, 0f, .008f) * walkWeight);
            CurrentLocomotionEuler = bobEuler * bobWeight + Vector3.right * movementPitch;
        }

        void UpdateUnarmedLocomotion(ItfaiyeFirstPersonController player, float dt)
        {
            bool moving = (pistol == null || !pistol.Equipped) && player.InputCaptured && player.IsGrounded
                && !player.IsSwimming && !player.IsKneeling && !ButtonSlapActive && player.CurrentSpeed > .08f;
            float gait = moving ? Mathf.Clamp01(player.CurrentSpeed / Mathf.Max(.1f, player.walkSpeed)) : 0f;
            float sprint = moving && player.IsSprinting ? 1f : 0f;
            float blend = Mathf.Max(.12f, unarmedSprintBlendSeconds);
            unarmedGaitWeight = Mathf.SmoothDamp(unarmedGaitWeight, gait, ref unarmedGaitVelocity, blend, Mathf.Infinity, dt);
            UnarmedSprintWeight = Mathf.SmoothDamp(UnarmedSprintWeight, sprint, ref unarmedSprintVelocity, blend, Mathf.Infinity, dt);
            if (moving)
                unarmedPhase = Mathf.Repeat(unarmedPhase + dt * Mathf.PI * 2f * Mathf.Lerp(1.05f, 1.65f, UnarmedSprintWeight), Mathf.PI * 2f);
            Vector3 walk = Vector3.Min(Vector3.Max(unarmedWalkSwing, Vector3.zero), new Vector3(.025f, .04f, .07f));
            Vector3 run = Vector3.Min(Vector3.Max(unarmedSprintSwing, Vector3.zero), new Vector3(.04f, .08f, .14f));
            Vector3 swing = Vector3.Lerp(walk, run, UnarmedSprintWeight) * unarmedGaitWeight;
            float cycle = Mathf.Sin(unarmedPhase);
            Vector3 lower = Vector3.down * (Mathf.Clamp(unarmedMovementLowering, 0f, .06f) * unarmedGaitWeight);
            var left = new Vector3(swing.x * cycle, swing.y * cycle, swing.z * cycle) + lower;
            var right = new Vector3(swing.x * cycle, -swing.y * cycle, -swing.z * cycle) + lower;
            leftUnarmedMotion = Vector3.SmoothDamp(leftUnarmedMotion, left, ref leftUnarmedVelocity, .055f, Mathf.Infinity, dt);
            rightUnarmedMotion = Vector3.SmoothDamp(rightUnarmedMotion, right, ref rightUnarmedVelocity, .055f, Mathf.Infinity, dt);
            unarmedSwingRotation = Mathf.SmoothDamp(unarmedSwingRotation, cycle * unarmedGaitWeight,
                ref unarmedSwingRotationVelocity, .055f, Mathf.Infinity, dt);
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
            // Right-hand winding: little to index gives the palm's outward normal.
            Vector3 localPalm = Vector3.Cross(littleDirection, indexDirection).normalized;
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
            bool swimming = swimmingPlayer && swimmingPlayer.IsSwimming;
            var reach = !ButtonSlapActive && !swimming && left && pistol && pistol.IsReloading ? new Vector3(-.015f, .015f, .035f) * pistol.ReloadPose.weaponWeight : Vector3.zero;
            upper.position = ButtonSlapActive ? ButtonShoulderPosition(left) : view.TransformPoint(new Vector3(side * .17f, -.18f, -.06f) + (pistol && pistol.DrawAmount > .001f && !swimming ? pistol.PoseOffset * .4f : Vector3.zero) + reach);
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

        void PresentSwimmingAnimation()
        {
            var swim = swimmingPlayer.Swimming;
            if (!swim.UsesAuthoredAnimations) return;
            // Retarget the clip's reach into the first-person view. The cut shoulders stay behind
            // the camera; elbow bends, wrists and fingers keep their imported local animation.
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0; float side = left ? -1f : 1f;
                var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (!upper || !hand) continue;
                Vector3 arm = hand.position - upper.position;
                Vector3 native = swimmingPlayer.transform.InverseTransformVector(arm);
                Vector3 direction = new Vector3(native.x * .5f, .1f + native.y * .15f, Mathf.Max(.1f, .4f + native.z * .4f)).normalized;
                Quaternion rotation = Quaternion.FromToRotation(arm, view.TransformDirection(direction)) * upper.rotation;
                Vector3 position = view.TransformPoint(new Vector3(side * .16f, -.19f, -.04f));
                upper.position = Vector3.Lerp(upper.position, position, swim.Blend);
                upper.rotation = Quaternion.Slerp(upper.rotation, rotation, swim.Blend);
            }
        }

        void PresentUnarmedHands()
        {
            if (presentationWeight <= 0f) return;
            if (fingerPoseDirty) CacheGrip();
            // The forward arm rises as the opposite arm pulls back. The wrists
            // follow that swing instead of remaining frozen in a two-handed pose.
            float pump = unarmedSwingRotation * Mathf.Lerp(.35f, 1f, UnarmedSprintWeight);
            PoseArm(true, leftRestPosition + leftUnarmedMotion,
                view.rotation * Quaternion.Euler(18f - pump * 11f, 8f + pump * 5f, -pump * 4f) * LeftGripRotation);
            PoseArm(false, rightRestPosition + rightUnarmedMotion,
                view.rotation * Quaternion.Euler(18f + pump * 11f, -8f + pump * 5f, pump * 4f) * RightGripRotation);
            for (int i = 0; i < fingers.Length; i++)
                if (fingers[i])
                {
                    var pose = Quaternion.Slerp(relaxedRotations[i], runningRotations[i], UnarmedSprintWeight);
                    fingers[i].localRotation = Quaternion.Slerp(fingers[i].localRotation, pose, presentationWeight);
                }
        }
    }
}
