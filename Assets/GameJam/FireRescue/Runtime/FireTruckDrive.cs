using System;
using UnityEngine;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FireTruckDrive : MonoBehaviour
    {
        [Header("Vehicle references")]
        public Rigidbody body;

        [Tooltip("Front left, front right, rear left, rear right.")]
        public WheelCollider[] wheels = new WheelCollider[4];

        [Tooltip("Wheel meshes in the same order as the colliders. Entries can be empty.")]
        public Transform[] wheelVisuals = new Transform[4];

        [Tooltip("Preserve each wheel mesh's authored orientation when following its collider.")]
        public bool preserveAuthoredWheelRotations = true;

        [Tooltip("Optional additional local rotation for each visual, in degrees.")]
        public Vector3[] wheelVisualEulerOffsets = new Vector3[4];

        [Header("Power and brakes")]
        [Min(0f)] public float motorTorque = 1700f;
        [Min(1f)] public float maxForwardSpeedKph = 70f;
        [Min(1f)] public float maxReverseSpeedKph = 20f;
        [Min(0f)] public float brakeTorque = 8000f;
        [Min(0f)] public float parkingBrakeTorque = 12000f;
        [Min(0f)] public float coastBrakeTorque = 50f;
        [Tooltip("Distribute the configured per-wheel torque to all grounded wheels.")]
        public bool allWheelDrive;
        [Min(1f)] public float lowSpeedTorqueMultiplier = 1f;
        [Min(1f)] public float torqueBoostFadeSpeedKph = 20f;
        public bool smoothPedals;
        [Min(.1f)] public float throttleResponsePerSecond = 3f;
        [Min(.1f)] public float throttleReleasePerSecond = 6f;
        [Tooltip("Hold a nearly stopped truck when the pedals are released; never change its velocity.")]
        public bool hillHoldAssist;

        [Header("Steering")]
        [Range(1f, 50f)] public float maxSteerAngle = 30f;
        [Range(1f, 30f)] public float highSpeedSteerAngle = 10f;
        [Min(1f)] public float steeringSpeed = 80f;

        [Header("Stability")]
        [Min(1f)] public float vehicleMass = 3500f;
        public Vector3 centerOfMassOffset = new Vector3(0f, -0.65f, 0f);
        [Min(0f)] public float antiRollForce = 6500f;
        [Min(0f)] public float speedLimiterStrength = 6f;

        public bool IsDriven { get; private set; }
        public float SpeedKph => body != null ? body.linearVelocity.magnitude * 3.6f : 0f;

        private float throttleInput;
        private float steeringInput;
        private bool brakeInput;
        private float currentSteerAngle;
        private float appliedThrottle;
        private float smoothedBrakeTorque;
        private int lastPoweredDirection = 1;
        private readonly Quaternion[] authoredVisualOffsets = new Quaternion[4];
        private readonly Transform[] cachedVisuals = new Transform[4];
        private readonly WheelCollider[] cachedColliders = new WheelCollider[4];

        private void Awake()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            ConfigureBody();
            CaptureVisualOffsets();
            ApplyParkingBrake();
        }

        public void Configure(Rigidbody vehicleBody, WheelCollider[] wheelColliders, Transform[] visuals)
        {
            if (vehicleBody == null)
                throw new ArgumentNullException(nameof(vehicleBody));
            if (wheelColliders == null || wheelColliders.Length != 4)
                throw new ArgumentException("Supply four colliders in FL, FR, RL, RR order.", nameof(wheelColliders));
            for (int index = 0; index < 4; index++)
            {
                if (wheelColliders[index] == null || wheelColliders[index].attachedRigidbody != vehicleBody)
                    throw new ArgumentException("Every wheel must belong to the supplied rigidbody.", nameof(wheelColliders));
            }

            body = vehicleBody;
            wheels = wheelColliders;
            wheelVisuals = visuals ?? new Transform[4];
            ConfigureBody();
            CaptureVisualOffsets();
            if (!IsDriven)
                ApplyParkingBrake();
        }

        public void SetDriven(bool driven)
        {
            IsDriven = driven;
            throttleInput = 0f;
            steeringInput = 0f;
            brakeInput = false;
            appliedThrottle = 0f;
            if (!driven)
                ApplyParkingBrake();
            else if (body != null)
                body.WakeUp();
        }

        public void SetInput(float throttle, float steering, bool brake)
        {
            throttleInput = float.IsNaN(throttle) ? 0f : Mathf.Clamp(throttle, -1f, 1f);
            steeringInput = float.IsNaN(steering) ? 0f : Mathf.Clamp(steering, -1f, 1f);
            brakeInput = brake;
        }

        private void ConfigureBody()
        {
            if (body == null)
                return;
            body.mass = vehicleMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 0.04f;
            body.angularDamping = 0.75f;
            if (wheels != null)
                foreach (WheelCollider wheel in wheels)
                    if (wheel != null) wheel.ConfigureVehicleSubsteps(5f, 8, 4);
            // Recalculate before applying the offset so repeated Configure calls cannot accumulate it.
            body.ResetCenterOfMass();
            body.centerOfMass += centerOfMassOffset;
        }

        private void FixedUpdate()
        {
            if (body == null || wheels == null || wheels.Length != 4)
            {
                ApplyParkingBrake();
                return;
            }

            float signedSpeed = Vector3.Dot(body.linearVelocity, body.transform.forward);
            float speedFraction = Mathf.Clamp01(Mathf.Abs(signedSpeed) * 3.6f / maxForwardSpeedKph);
            float steeringLimit = Mathf.Lerp(maxSteerAngle,
                Mathf.Min(highSpeedSteerAngle, maxSteerAngle), speedFraction);
            float targetSteer = IsDriven ? steeringInput * steeringLimit : 0f;
            currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, targetSteer,
                steeringSpeed * Time.fixedDeltaTime);

            float requestedThrottle = IsDriven ? throttleInput : 0f;
            int requestedDirection = requestedThrottle < 0f ? -1 : 1;
            // A deliberate gear reversal brakes first. Rolling downhill while holding W
            // must not be mistaken for a new reverse request and lock the hill climb.
            bool changingDirection = Mathf.Abs(requestedThrottle) > .05f && Mathf.Abs(signedSpeed) > .8f &&
                Mathf.Sign(requestedThrottle) != Mathf.Sign(signedSpeed) &&
                (!hillHoldAssist || requestedDirection != lastPoweredDirection);
            if (Mathf.Abs(requestedThrottle) > .05f && !changingDirection)
                lastPoweredDirection = requestedDirection;
            bool braking = !IsDriven || brakeInput || changingDirection;
            float pedalTarget = braking ? 0f : requestedThrottle;
            appliedThrottle = smoothPedals ? Mathf.MoveTowards(appliedThrottle, pedalTarget,
                (Mathf.Abs(pedalTarget) > Mathf.Abs(appliedThrottle) ? throttleResponsePerSecond : throttleReleasePerSecond) * Time.fixedDeltaTime) : pedalTarget;
            float throttle = appliedThrottle;
            bool holdingHill = hillHoldAssist && IsDriven && Mathf.Abs(requestedThrottle) < .05f && Mathf.Abs(signedSpeed) < .4f;
            float appliedBrake = !IsDriven ? parkingBrakeTorque :
                braking ? brakeTorque : holdingHill ? parkingBrakeTorque * .5f : Mathf.Abs(throttle) < 0.05f ? coastBrakeTorque : 0f;
            smoothedBrakeTorque = smoothPedals && IsDriven ? Mathf.MoveTowards(smoothedBrakeTorque, appliedBrake,
                Mathf.Max(brakeTorque, parkingBrakeTorque) * 5f * Time.fixedDeltaTime) : appliedBrake;

            float targetTopSpeed = throttle < 0f ? maxReverseSpeedKph : maxForwardSpeedKph;
            float speedInRequestedDirection = Mathf.Max(0f, signedSpeed * Mathf.Sign(throttle)) * 3.6f;
            float powerFactor = 1f - Mathf.InverseLerp(targetTopSpeed * 0.8f,
                targetTopSpeed, speedInRequestedDirection);
            float lowGear = Mathf.Lerp(Mathf.Max(1f, lowSpeedTorqueMultiplier), 1f,
                Mathf.Clamp01(Mathf.Abs(signedSpeed) * 3.6f / Mathf.Max(1f, torqueBoostFadeSpeedKph)));
            float requestedTorque = braking ? 0f : throttle * motorTorque * powerFactor * lowGear;
            bool anyGrounded = false;

            for (int index = 0; index < wheels.Length; index++)
            {
                WheelCollider wheel = wheels[index];
                if (wheel == null)
                    continue;
                bool grounded = wheel.isGrounded;
                anyGrounded |= grounded;
                wheel.steerAngle = index < 2 ? currentSteerAngle : 0f;
                wheel.brakeTorque = smoothedBrakeTorque;
                // Keep power on contact patches. Airborne wheels receive no powered free spin.
                wheel.motorTorque = (allWheelDrive || index >= 2) && grounded ? requestedTorque : 0f;
            }

            ApplyAntiRoll(wheels[0], wheels[1]);
            ApplyAntiRoll(wheels[2], wheels[3]);
            if (anyGrounded)
                ApplySpeedLimiter(signedSpeed);
        }

        private void ApplySpeedLimiter(float signedSpeed)
        {
            // Apply a resistive force rather than rewriting velocity or moving the transform.
            Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            float limit = (signedSpeed < -0.1f ? maxReverseSpeedKph : maxForwardSpeedKph) / 3.6f;
            float excessSpeed = planarVelocity.magnitude - limit;
            if (excessSpeed > 0f)
                body.AddForce(-planarVelocity.normalized * excessSpeed * speedLimiterStrength,
                    ForceMode.Acceleration);
        }

        private void ApplyAntiRoll(WheelCollider left, WheelCollider right)
        {
            if (left == null || right == null || antiRollForce <= 0f)
                return;
            bool leftGrounded = left.GetGroundHit(out WheelHit leftHit);
            bool rightGrounded = right.GetGroundHit(out WheelHit rightHit);
            float leftTravel = leftGrounded ? SuspensionTravel(left, leftHit) : 1f;
            float rightTravel = rightGrounded ? SuspensionTravel(right, rightHit) : 1f;
            float force = (leftTravel - rightTravel) * antiRollForce;
            if (leftGrounded)
                body.AddForceAtPosition(-left.transform.up * force, left.transform.TransformPoint(left.center));
            if (rightGrounded)
                body.AddForceAtPosition(right.transform.up * force, right.transform.TransformPoint(right.center));
        }

        private static float SuspensionTravel(WheelCollider wheel, WheelHit hit)
        {
            Vector3 localHit = wheel.transform.InverseTransformPoint(hit.point) - wheel.center;
            return Mathf.Clamp01((-localHit.y - wheel.radius) / Mathf.Max(wheel.suspensionDistance, 0.001f));
        }

        private void CaptureVisualOffsets()
        {
            for (int index = 0; index < 4; index++)
            {
                WheelCollider wheel = wheels != null && index < wheels.Length ? wheels[index] : null;
                Transform visual = wheelVisuals != null && index < wheelVisuals.Length ? wheelVisuals[index] : null;
                cachedColliders[index] = wheel;
                cachedVisuals[index] = visual;
                authoredVisualOffsets[index] = Quaternion.identity;
                if (wheel != null && visual != null && preserveAuthoredWheelRotations)
                {
                    wheel.GetWorldPose(out _, out Quaternion rotation);
                    authoredVisualOffsets[index] = Quaternion.Inverse(rotation) * visual.rotation;
                }
            }
        }

        private void LateUpdate()
        {
            for (int index = 0; index < 4; index++)
            {
                WheelCollider wheel = wheels != null && index < wheels.Length ? wheels[index] : null;
                Transform visual = wheelVisuals != null && index < wheelVisuals.Length ? wheelVisuals[index] : null;
                if (wheel == null || visual == null || visual == wheel.transform)
                    continue;
                if (cachedColliders[index] != wheel || cachedVisuals[index] != visual)
                    CaptureVisualOffsets();
                wheel.GetWorldPose(out Vector3 position, out Quaternion rotation);
                Quaternion manualOffset = wheelVisualEulerOffsets != null && index < wheelVisualEulerOffsets.Length
                    ? Quaternion.Euler(wheelVisualEulerOffsets[index]) : Quaternion.identity;
                visual.SetPositionAndRotation(position, rotation * authoredVisualOffsets[index] * manualOffset);
            }
        }

        private void ApplyParkingBrake()
        {
            if (wheels == null)
                return;
            foreach (WheelCollider wheel in wheels)
            {
                if (wheel == null)
                    continue;
                wheel.motorTorque = 0f;
                wheel.brakeTorque = parkingBrakeTorque;
            }
        }

        private void OnDisable()
        {
            IsDriven = false;
            throttleInput = 0f;
            steeringInput = 0f;
            brakeInput = false;
            appliedThrottle = 0f;
            ApplyParkingBrake();
        }
    }
}
