using UnityEngine;
using UnityEngine.InputSystem;

namespace Itfaiye
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ItfaiyeFirstPersonController : MonoBehaviour
    {
        [Header("References")]
        public Camera playerCamera;
        public Camera handsCamera;
        public Animator bodyAnimator;
        public Animator handsAnimator;
        public ItfaiyePistol pistol;

        [Header("Movement (metres per second)")]
        [Min(.1f)] public float walkSpeed = 1.87f;
        [Min(.1f)] public float runSpeed = 4.57f;
        [Min(.1f)] public float acceleration = 14f;
        [Min(.1f)] public float braking = 20f;
        public float gravity = -25f;
        public float jumpHeight = .9f;

        [Header("First person view")]
        [Range(.01f, .5f)] public float mouseSensitivity = .09f;
        [Range(40f, 100f)] public float fieldOfView = 80f;
        [Range(30f, 89f)] public float maximumPitch = 80f;
        [Tooltip("Return to the starting point after falling off the small test platform.")]
        public bool recoverFromFall = true;

        public float CurrentSpeed { get; private set; }
        public float Pitch => pitch;
        public bool InputCaptured { get; private set; }
        public bool IsGrounded => motor != null && motor.isGrounded;
        public bool IsKneeling { get; private set; }
        public float KneelAmount { get; private set; }
        public Vector2 LocalVelocity { get; private set; }
        // Actual mouse-look speed in degrees/second, before visual recoil.
        public Vector2 LookAngularVelocity { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool CanChangeWeapon => !IsKneeling && KneelAmount < .02f && !PostureTransition;
        public bool PostureTransition => bodyAnimator != null && (bodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Kneel Down") || bodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Stand Up"));

        static readonly int SpeedId = Animator.StringToHash("Speed");
        CharacterController motor;
        Vector3 planarVelocity;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        float verticalVelocity;
        float pitch;
        int ignoreLookFrames;
        float pendingJump = -1f;
        float lastJump = -10f;
        float standingHeight;
        Vector3 cameraRest;

        void Awake()
        {
            motor = GetComponent<CharacterController>();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            standingHeight = motor.height;
            cameraRest = playerCamera.transform.localPosition;
        }

        void Start() { SetInputCaptured(true); }

        public void SetInputCaptured(bool captured)
        {
            if (captured && !InputCaptured) ignoreLookFrames = 2;
            InputCaptured = captured;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }

        void OnDisable() { SetInputCaptured(false); }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) SetInputCaptured(false);
        }

        void Update()
        {
            LookAngularVelocity = Vector2.zero;
            if (playerCamera == null || motor == null) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SetInputCaptured(false);
            else if (!InputCaptured && Application.isFocused && mouse != null && mouse.leftButton.wasPressedThisFrame)
                SetInputCaptured(true);

            var move = Vector2.zero;
            bool sprint = false;
            if (InputCaptured)
            {
                if (ignoreLookFrames > 0) ignoreLookFrames--;
                else if (mouse != null)
                {
                    // Mouse delta already represents this frame: do not multiply it by deltaTime.
                    var delta = mouse.delta.ReadValue() * mouseSensitivity;
                    float previousPitch = pitch;
                    transform.Rotate(0f, delta.x, 0f, Space.World);
                    pitch = Mathf.Clamp(pitch - delta.y, -maximumPitch, maximumPitch);
                    if (Time.deltaTime > 0f) LookAngularVelocity = new Vector2(delta.x, pitch - previousPitch) / Time.deltaTime;
                }
                if (keyboard != null)
                {
                    move.x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                           - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                    move.y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                           - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
                    sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                }
            }
            move = Vector2.ClampMagnitude(move, 1f);
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            bool armed = pistol != null && pistol.Equipped;
            if (InputCaptured && keyboard != null && armed)
            {
                if (keyboard.cKey.wasPressedThisFrame && motor.isGrounded) SetKneeling(!IsKneeling);
                if (move.sqrMagnitude > .01f && IsKneeling) SetKneeling(false);
                if (keyboard.spaceKey.wasPressedThisFrame && motor.isGrounded && pendingJump < 0f && Time.time - lastJump > .6f)
                {
                    if (IsKneeling || KneelAmount > .02f) SetKneeling(false);
                    else if (!PostureTransition)
                    {
                        pendingJump = .10f;
                        lastJump = Time.time;
                        foreach (var anim in new[] { bodyAnimator, handsAnimator })
                        {
                            anim.SetBool("MovingJump", move.sqrMagnitude > .01f);
                            anim.SetTrigger("Jump");
                        }
                    }
                }
            }
            if (IsKneeling || KneelAmount > .05f || PostureTransition) move = Vector2.zero;
            sprint &= armed ? !(pistol.IsAiming || pistol.IsReloading) : true;
            IsSprinting = sprint && move.sqrMagnitude > .01f;
            var target = transform.TransformDirection(new Vector3(move.x, 0f, move.y)) * (sprint ? runSpeed : walkSpeed);
            planarVelocity = Vector3.MoveTowards(planarVelocity, target, (move.sqrMagnitude > 0f ? acceleration : braking) * dt);
            if (motor.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            if (pendingJump >= 0f)
            {
                pendingJump -= dt;
                if (pendingJump < 0f) verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
            }
            verticalVelocity = Mathf.Max(verticalVelocity + gravity * dt, -45f);
            var previous = transform.position;
            var collisions = motor.Move((planarVelocity + Vector3.up * verticalVelocity) * dt);
            if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            if ((collisions & CollisionFlags.Below) != 0) verticalVelocity = -2f;
            CurrentSpeed = Vector3.ProjectOnPlane(transform.position - previous, Vector3.up).magnitude / dt;
            var local = transform.InverseTransformDirection((transform.position - previous) / dt);
            LocalVelocity = new Vector2(local.x, local.z);
            // Use actual displacement, so the feet stop when a wall blocks the player.
            if (bodyAnimator != null) bodyAnimator.SetFloat(SpeedId, CurrentSpeed, .10f, dt);
            if (handsAnimator != null) handsAnimator.SetFloat(SpeedId, CurrentSpeed, .10f, dt);
            if (pistol != null)
            {
                UpdatePistolAnimator(bodyAnimator, dt);
                UpdatePistolAnimator(handsAnimator, dt);
            }
            if (recoverFromFall && transform.position.y < spawnPosition.y - 20f) ReturnToSpawn();
        }

        void LateUpdate()
        {
            if (playerCamera == null) return;
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            // Apply before the hand pose and muzzle ray are evaluated, keeping the
            // rendered view, crosshair and shot on the same camera transform.
            if (pistol != null && pistol.Feedback != null)
                playerCamera.transform.localRotation *= pistol.Feedback.CameraRotation;
            KneelAmount = Mathf.MoveTowards(KneelAmount, IsKneeling ? 1f : 0f, Time.deltaTime / (IsKneeling ? .65f : .9f));
            motor.height = Mathf.Lerp(standingHeight, 1.18f, KneelAmount);
            motor.center = Vector3.up * (motor.height * .5f);
            playerCamera.transform.localPosition = cameraRest + Vector3.down * (.52f * Mathf.SmoothStep(0f, 1f, KneelAmount));
            float hipFov = fieldOfView;
            if (pistol != null && pistol.Hands != null)
                hipFov = Mathf.Max(40f, fieldOfView - pistol.Hands.pistolViewZoom * Mathf.SmoothStep(0f, 1f, pistol.DrawAmount));
            // Keep both projections identical so the muzzle, world hit and crosshair
            // agree. Zoom only the hip view; the existing ADS alignment stays intact.
            float targetFov = pistol != null ? Mathf.Lerp(hipFov, 62f, pistol.AimAmount) : fieldOfView;
            playerCamera.fieldOfView = targetFov;
            if (handsCamera != null) handsCamera.fieldOfView = targetFov;
        }

        void UpdatePistolAnimator(Animator animator, float dt)
        {
            if (animator == null) return;
            animator.SetFloat("MoveX", LocalVelocity.x, .10f, dt);
            animator.SetFloat("MoveY", LocalVelocity.y, .10f, dt);
            animator.SetBool("Equipped", pistol.Equipped);
            animator.SetBool("Kneeling", IsKneeling);
            animator.SetBool("Grounded", motor.isGrounded && pendingJump < 0f && Time.time - lastJump > .25f);
        }

        public bool SetKneeling(bool kneeling)
        {
            if (!kneeling && (IsKneeling || KneelAmount > .02f))
            {
                // Only test the extra headroom: the current capsule must not block itself.
                var hits = Physics.OverlapCapsule(transform.position + Vector3.up * 1.2f,
                    transform.position + Vector3.up * (standingHeight - motor.radius), motor.radius * .94f,
                    Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("FirstPersonHands")), QueryTriggerInteraction.Ignore);
                foreach (var hit in hits) if (!hit.transform.IsChildOf(transform)) return false;
            }
            IsKneeling = kneeling;
            return true;
        }

        void ReturnToSpawn()
        {
            motor.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            motor.enabled = true;
            planarVelocity = Vector3.zero;
            verticalVelocity = -2f;
            CurrentSpeed = 0f;
            LocalVelocity = Vector2.zero;
            pendingJump = -1f;
        }
    }
}
