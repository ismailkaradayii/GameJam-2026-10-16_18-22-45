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

            float throttle = IsDriven ? throttleInput : 0f;
            bool changingDirection = Mathf.Abs(throttle) > 0.05f &&
                Mathf.Abs(signedSpeed) > 0.8f && Mathf.Sign(throttle) != Mathf.Sign(signedSpeed);
            bool braking = !IsDriven || brakeInput || changingDirection;
            float appliedBrake = !IsDriven ? parkingBrakeTorque :
                braking ? brakeTorque : Mathf.Abs(throttle) < 0.05f ? coastBrakeTorque : 0f;

            float targetTopSpeed = throttle < 0f ? maxReverseSpeedKph : maxForwardSpeedKph;
            float speedInRequestedDirection = Mathf.Max(0f, signedSpeed * Mathf.Sign(throttle)) * 3.6f;
            float powerFactor = 1f - Mathf.InverseLerp(targetTopSpeed * 0.8f,
                targetTopSpeed, speedInRequestedDirection);
            float requestedTorque = braking ? 0f : throttle * motorTorque * powerFactor;
            bool anyGrounded = false;

            for (int index = 0; index < wheels.Length; index++)
            {
                WheelCollider wheel = wheels[index];
                if (wheel == null)
                    continue;
                bool grounded = wheel.isGrounded;
                anyGrounded |= grounded;
                wheel.steerAngle = index < 2 ? currentSteerAngle : 0f;
                wheel.brakeTorque = appliedBrake;
                // Rear-wheel drive. Airborne wheels receive no powered free spin.
                wheel.motorTorque = index >= 2 && grounded ? requestedTorque : 0f;
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
            ApplyParkingBrake();
        }
    }
}
