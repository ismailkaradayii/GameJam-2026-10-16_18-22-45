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

        [Tooltip("Animate wheel spin from distance travelled, including when traction assistance supplies the acceleration.")]
        public bool wheelSpinFromTravel = true;

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
        [Tooltip("Extra low-speed acceleration on grounded wheels. Zero preserves unassisted vehicle physics.")]
        [Min(0f)] public float groundAccelerationAssist;
        [Min(1f)] public float accelerationAssistFadeSpeedKph = 45f;

        [Header("Steering")]
        [Range(1f, 50f)] public float maxSteerAngle = 30f;
        [Range(1f, 30f)] public float highSpeedSteerAngle = 10f;
        [Tooltip("Speed at which steering reaches its high-speed angle, independent of the top speed.")]
        [Min(1f)] public float steeringFadeSpeedKph = 70f;
        [Min(1f)] public float steeringSpeed = 80f;
        [Tooltip("Opposite rear-axle steering for low-speed maneuvering. Zero keeps conventional front steering.")]
        [Range(0f, .75f)] public float lowSpeedRearSteerRatio;
        [Min(15f)] public float rearSteerFadeSpeedKph = 45f;

        [Header("Stability")]
        [Min(1f)] public float vehicleMass = 3500f;
        public Vector3 centerOfMassOffset = new Vector3(0f, -0.65f, 0f);
        [Min(0f)] public float antiRollForce = 6500f;
        [Min(0f)] public float speedLimiterStrength = 6f;

        [Header("Arcade driving assistance")]
        public bool arcadeHandling;
        [Tooltip("Full-lock turning radius at low speed, independent of the long truck wheelbase.")]
        [Min(3f)] public float arcadeTurnRadius = 8f;
        [Range(20f, 160f)] public float arcadeLowSpeedYawRate = 110f;
        [Range(15f, 100f)] public float arcadeHighSpeedYawRate = 55f;
        [Min(1f)] public float arcadeSteeringResponse = 12f;
        [Min(0f)] public float arcadeLateralGrip = 10f;
        [Min(0f)] public float arcadeBrakeAcceleration = 24f;
        [Tooltip("Automatically slow for full-lock turns, even while the accelerator is held.")]
        [Min(10f)] public float arcadeCornerSpeedKph = 42f;
        [Min(0f)] public float arcadeUprightStrength = 45f;
        [Tooltip("Lift the powered truck over low curbs and rocks. Buildings and tall obstacles remain solid.")]
        [Min(0f)] public float obstacleStepHeight = 2.4f;
        [Min(1f)] public float obstacleLiftAcceleration = 65f;

        [Header("Progressive truck response")]
        [Tooltip("Give steering, yaw and service brakes a gradual response while retaining the assisted low-speed turning radius.")]
        public bool progressiveHandling;
        [Tooltip("Normalized steering travel per second while turning the wheel.")]
        [Min(.1f)] public float steeringInputResponse = 2.8f;
        [Tooltip("Normalized steering travel per second while letting the wheel return to centre.")]
        [Min(.1f)] public float steeringReturnResponse = 4.5f;
        [Tooltip("Steering response at the steering fade speed. High-speed corrections build more gradually.")]
        [Range(.1f, 1f)] public float highSpeedSteeringResponse = .55f;
        [Tooltip("Limit assisted yaw by cornering acceleration in metres per second squared. Low-speed maneuvering keeps its original radius.")]
        [Min(.1f)] public float maxLateralAcceleration = 8f;
        [Tooltip("Maximum assisted yaw acceleration, in degrees per second squared.")]
        [Min(1f)] public float yawAccelerationDegrees = 150f;
        [Tooltip("Service brake pressure build-up per second. Parking and hill hold remain immediate.")]
        [Min(.1f)] public float serviceBrakeResponse = 4f;

        public bool IsDriven { get; private set; }
        public ThermalDamage Thermal { get; private set; }
        public float MotorEfficiency => Thermal ? Thermal.MotorEfficiency : 1f;
        public float EffectiveForwardSpeedKph => maxForwardSpeedKph * MotorEfficiency;
        public bool IsWrecked => Thermal && Thermal.IsDestroyed;
        public float SpeedKph => body != null ? body.linearVelocity.magnitude * 3.6f : 0f;
        public float RearSteerFactor => lowSpeedRearSteerRatio * (1f - Mathf.InverseLerp(10f, rearSteerFadeSpeedKph, SpeedKph));
        public float AppliedThrottle => appliedThrottle;
        public float AppliedSteering => arcadeSteer;
        public float AppliedBrake => IsDriven ? Mathf.Clamp01(smoothedBrakeTorque / Mathf.Max(1f, brakeTorque)) : 1f;

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
        private readonly float[] visualSpin = new float[4];
        private readonly RaycastHit[] obstacleHits = new RaycastHit[16];
        private float arcadeSteer;

        // Degrees/second at full steering input; useful for tuning and repeatable route checks.
        public float FullSteeringYawRate(float speedKph)
        {
            float cap = Mathf.Lerp(arcadeLowSpeedYawRate, arcadeHighSpeedYawRate,
                Mathf.Clamp01(Mathf.Abs(speedKph) / Mathf.Max(1f, maxForwardSpeedKph)));
            float speed = Mathf.Abs(speedKph) / 3.6f;
            float rate = Mathf.Min(speed / Mathf.Max(3f, arcadeTurnRadius) * Mathf.Rad2Deg, cap);
            // a = speed * yaw rate. At town speeds the existing turning circle still
            // applies; at road speeds it must open out instead of snapping sideways.
            return progressiveHandling ? Mathf.Min(rate,
                Mathf.Max(.1f, maxLateralAcceleration) / Mathf.Max(.5f, speed) * Mathf.Rad2Deg) : rate;
        }

        private void Awake()
        {
            Thermal = ThermalDamage.Ensure(this);
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
            arcadeSteer = 0f;
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
            body.maxAngularVelocity = 3f;
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
            float speedFraction = Mathf.Clamp01(Mathf.Abs(signedSpeed) * 3.6f / Mathf.Max(1f, steeringFadeSpeedKph));
            float steeringLimit = Mathf.Lerp(maxSteerAngle,
                Mathf.Min(highSpeedSteerAngle, maxSteerAngle), speedFraction);
            float requestedSteering = IsDriven ? steeringInput : 0f;
            float steeringResponse = 8f;
            if (progressiveHandling)
            {
                bool centring = Mathf.Abs(requestedSteering) < .001f ||
                    Mathf.Sign(requestedSteering) == Mathf.Sign(arcadeSteer) && Mathf.Abs(requestedSteering) < Mathf.Abs(arcadeSteer);
                steeringResponse = (centring ? steeringReturnResponse : steeringInputResponse) *
                    Mathf.Lerp(1f, highSpeedSteeringResponse, speedFraction);
            }
            arcadeSteer = Mathf.MoveTowards(arcadeSteer, requestedSteering,
                steeringResponse * Time.fixedDeltaTime);
            // The visible front wheels and the yaw assistance follow the same wheel
            // movement, including the slower sweep through centre when countersteering.
            float targetSteer = (progressiveHandling ? arcadeSteer : requestedSteering) * steeringLimit;
            currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, targetSteer,
                steeringSpeed * Time.fixedDeltaTime);

            float requestedThrottle = IsDriven && !IsWrecked ? throttleInput : 0f;
            int requestedDirection = requestedThrottle < 0f ? -1 : 1;
            // A deliberate gear reversal brakes first. Rolling downhill while holding W
            // must not be mistaken for a new reverse request and lock the hill climb.
            bool changingDirection = Mathf.Abs(requestedThrottle) > .05f && Mathf.Abs(signedSpeed) > .8f &&
                Mathf.Sign(requestedThrottle) != Mathf.Sign(signedSpeed) &&
                (!hillHoldAssist || requestedDirection != lastPoweredDirection);
            if (Mathf.Abs(requestedThrottle) > .05f && !changingDirection)
                lastPoweredDirection = requestedDirection;
            // Drain the old pedal under braking before engaging the opposite gear,
            // including when a wall or a near stop keeps speed below the gear threshold.
            bool releasingOppositePedal = progressiveHandling && requestedThrottle * appliedThrottle < -.001f;
            bool braking = !IsDriven || IsWrecked || brakeInput || changingDirection || releasingOppositePedal;
            float pedalTarget = braking ? 0f : requestedThrottle;
            appliedThrottle = smoothPedals ? Mathf.MoveTowards(appliedThrottle, pedalTarget,
                (Mathf.Abs(pedalTarget) > Mathf.Abs(appliedThrottle) ? throttleResponsePerSecond : throttleReleasePerSecond) * Time.fixedDeltaTime) : pedalTarget;
            float throttle = appliedThrottle;
            bool holdingHill = hillHoldAssist && IsDriven && Mathf.Abs(requestedThrottle) < .05f && Mathf.Abs(signedSpeed) < .4f;
            float appliedBrake = !IsDriven ? parkingBrakeTorque :
                braking ? brakeTorque : holdingHill ? parkingBrakeTorque * .5f : Mathf.Abs(throttle) < 0.05f ? coastBrakeTorque : 0f;
            // Release the parking/hill brake immediately when the accelerator is pressed.
            // Otherwise the motor spends its first frames fighting residual brake torque.
            if (progressiveHandling)
                smoothedBrakeTorque = IsDriven && !IsWrecked && !holdingHill && appliedBrake > smoothedBrakeTorque ?
                    Mathf.MoveTowards(smoothedBrakeTorque, appliedBrake, Mathf.Max(1f, brakeTorque) * serviceBrakeResponse * Time.fixedDeltaTime) : appliedBrake;
            else
                smoothedBrakeTorque = smoothPedals && IsDriven && appliedBrake >= smoothedBrakeTorque ? Mathf.MoveTowards(smoothedBrakeTorque, appliedBrake,
                    Mathf.Max(brakeTorque, parkingBrakeTorque) * 5f * Time.fixedDeltaTime) : appliedBrake;

            float targetTopSpeed = (throttle < 0f ? maxReverseSpeedKph : maxForwardSpeedKph) * MotorEfficiency;
            if (arcadeHandling) targetTopSpeed = CornerSpeedLimit(targetTopSpeed);
            float speedInRequestedDirection = Mathf.Max(0f, signedSpeed * Mathf.Sign(throttle)) * 3.6f;
            float powerFactor = 1f - Mathf.InverseLerp(targetTopSpeed * 0.8f,
                targetTopSpeed, speedInRequestedDirection);
            float lowGear = Mathf.Lerp(Mathf.Max(1f, lowSpeedTorqueMultiplier), 1f,
                Mathf.Clamp01(Mathf.Abs(signedSpeed) * 3.6f / Mathf.Max(1f, torqueBoostFadeSpeedKph)));
            float requestedTorque = braking ? 0f : throttle * motorTorque * powerFactor * lowGear * MotorEfficiency;
            bool anyGrounded = false;
            int groundedCount = 0;
            Vector3 supportNormal = Vector3.zero;

            for (int index = 0; index < wheels.Length; index++)
            {
                WheelCollider wheel = wheels[index];
                if (wheel == null)
                    continue;
                bool grounded = wheel.GetGroundHit(out WheelHit contact);
                anyGrounded |= grounded;
                if (grounded) { groundedCount++; supportNormal += contact.normal; }
                wheel.steerAngle = index < 2 ? currentSteerAngle : -currentSteerAngle * RearSteerFactor;
                wheel.brakeTorque = smoothedBrakeTorque;
                // Keep power on contact patches. Airborne wheels receive no powered free spin.
                wheel.motorTorque = (allWheelDrive || index >= 2) && grounded ? requestedTorque : 0f;
                if (wheelSpinFromTravel)
                {
                    Vector3 rollingDirection = Quaternion.AngleAxis(wheel.steerAngle, wheel.transform.up) * wheel.transform.forward;
                    float rollingSpeed = Vector3.Dot(body.GetPointVelocity(wheel.transform.position), rollingDirection);
                    float worldRadius = wheel.radius * Mathf.Abs(wheel.transform.lossyScale.y);
                    float degrees = grounded ? rollingSpeed / Mathf.Max(.05f, worldRadius) * Mathf.Rad2Deg : wheel.rpm * 6f;
                    visualSpin[index] = Mathf.Repeat(visualSpin[index] + degrees * Time.fixedDeltaTime, 360f);
                }
            }

            ApplyAntiRoll(wheels[0], wheels[1]);
            ApplyAntiRoll(wheels[2], wheels[3]);
            if (anyGrounded)
            {
                if (!braking && groundAccelerationAssist > 0 && Mathf.Abs(throttle) > .05f)
                {
                    float assist = 1f - Mathf.Clamp01(Mathf.Abs(signedSpeed) * 3.6f / Mathf.Max(1f, accelerationAssistFadeSpeedKph));
                    Vector3 direction = Vector3.ProjectOnPlane(body.transform.forward, supportNormal.normalized).normalized;
                    body.AddForce(direction * (throttle * groundAccelerationAssist * assist * powerFactor * MotorEfficiency * groundedCount / wheels.Length), ForceMode.Acceleration);
                }
                ApplySpeedLimiter(signedSpeed);
                if (arcadeHandling && IsDriven)
                    ApplyArcadeHandling(signedSpeed, supportNormal.normalized, throttle, braking, holdingHill);
            }
        }

        private void ApplyArcadeHandling(float signedSpeed, Vector3 groundNormal, float throttle, bool braking, bool holdingHill)
        {
            // Keep real suspension and collision response, but remove the wheelbase penalty
            // and sideways sliding that make this oversized rescue truck hard to place.
            Vector3 normal = Vector3.RotateTowards(Vector3.up, groundNormal, 40f * Mathf.Deg2Rad, 0f);
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal).normalized;
            Vector3 right = Vector3.Cross(normal, forward);
            float turnSpeed = Mathf.Abs(signedSpeed);
            float direction = Mathf.Abs(signedSpeed) > .4f ? Mathf.Sign(signedSpeed) : Mathf.Sign(throttle);
            if (!braking && Mathf.Abs(throttle) > .05f) turnSpeed = Mathf.Max(turnSpeed, 1.5f);
            float targetYaw = arcadeSteer * direction * FullSteeringYawRate(turnSpeed * 3.6f) * Mathf.Deg2Rad;
            float yaw = Vector3.Dot(body.angularVelocity, normal);
            float yawAcceleration = progressiveHandling ? Mathf.Max(1f, yawAccelerationDegrees) * Mathf.Deg2Rad : 7f;
            body.AddTorque(normal * Mathf.Clamp((targetYaw - yaw) * arcadeSteeringResponse, -yawAcceleration, yawAcceleration), ForceMode.Acceleration);
            body.AddForce(-right * Mathf.Clamp(Vector3.Dot(body.linearVelocity, right) * arcadeLateralGrip, -55f, 55f), ForceMode.Acceleration);
            float cornerLimit = CornerSpeedLimit((signedSpeed < -.1f ? maxReverseSpeedKph : maxForwardSpeedKph) * MotorEfficiency) / 3.6f;
            float cornerExcess = Mathf.Abs(signedSpeed) - cornerLimit;
            if (cornerExcess > 0f)
                body.AddForce(-forward * Mathf.Sign(signedSpeed) * Mathf.Min(cornerExcess * 6f, arcadeBrakeAcceleration), ForceMode.Acceleration);

            Vector3 tiltVelocity = Vector3.ProjectOnPlane(body.angularVelocity, normal);
            body.AddTorque(Vector3.Cross(transform.up, normal) * arcadeUprightStrength - tiltVelocity * 9f, ForceMode.Acceleration);
            if (Mathf.Abs(throttle) > .05f || braking || holdingHill)
                body.AddForce(-Vector3.ProjectOnPlane(Physics.gravity, normal), ForceMode.Acceleration);
            if (braking || holdingHill)
            {
                Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, normal);
                float pressure = progressiveHandling && !holdingHill && !IsWrecked ? AppliedBrake : 1f;
                body.AddForce(-Vector3.ClampMagnitude(planar * 8f, arcadeBrakeAcceleration * pressure), ForceMode.Acceleration);
            }
            else if (Mathf.Abs(throttle) > .15f)
                AssistLowObstacles(Mathf.Sign(throttle), forward);
        }

        private float CornerSpeedLimit(float topSpeed)
        {
            // A small correction keeps straight-line speed; a held A/D turn is safe without
            // requiring the player to feather W and the brake at the same time.
            float corner = Mathf.InverseLerp(.15f, 1f, Mathf.Abs(arcadeSteer));
            return Mathf.Lerp(topSpeed, Mathf.Min(topSpeed, arcadeCornerSpeedKph), corner);
        }

        private void AssistLowObstacles(float direction, Vector3 forward)
        {
            if (obstacleStepHeight <= 0f) return;
            forward *= direction;
            int first = direction > 0 ? 0 : 2;
            float lift = 0f;
            for (int i = first; i < first + 2; i++)
            {
                WheelCollider wheel = wheels[i];
                if (!wheel || !wheel.GetGroundHit(out WheelHit ground)) continue;
                Vector3 origin = ground.point + Vector3.up * .35f;
                float reach = wheel.radius + Mathf.Clamp(SpeedKph / 3.6f * .2f, .8f, 3f);
                if (!StaticGroundRay(origin, forward, reach, out RaycastHit face) || face.normal.y > .6f) continue;
                Vector3 above = face.point + forward * .6f;
                above.y = ground.point.y + obstacleStepHeight + .5f;
                if (!StaticGroundRay(above, Vector3.down, obstacleStepHeight + .7f, out RaycastHit top) || top.normal.y < .65f) continue;
                float height = top.point.y - ground.point.y;
                if (height > .15f && height <= obstacleStepHeight)
                    lift = Mathf.Max(lift, Mathf.Sqrt(2f * Physics.gravity.magnitude * (height + .25f)));
            }
            if (lift > 0f)
                body.AddForce(Vector3.up * Mathf.Clamp((lift - body.linearVelocity.y) * 14f, 0f, obstacleLiftAcceleration), ForceMode.Acceleration);
        }

        private bool StaticGroundRay(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(origin, direction, obstacleHits, distance, ~0, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = obstacleHits[i];
                if (hit.rigidbody != null || hit.collider is CharacterController || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance >= closest) continue;
                closest = hit.distance; nearest = hit;
            }
            return closest < float.PositiveInfinity;
        }

        private void ApplySpeedLimiter(float signedSpeed)
        {
            // Apply a resistive force rather than rewriting velocity or moving the transform.
            Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            float limit = (signedSpeed < -0.1f ? maxReverseSpeedKph : maxForwardSpeedKph) * MotorEfficiency / 3.6f;
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
                    if (wheelSpinFromTravel) rotation = wheel.transform.rotation * Quaternion.Euler(0f, wheel.steerAngle, 0f) * Quaternion.Euler(visualSpin[index], 0f, 0f);
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
                if (wheelSpinFromTravel)
                    rotation = wheel.transform.rotation * Quaternion.Euler(0f, wheel.steerAngle, 0f) * Quaternion.Euler(visualSpin[index], 0f, 0f);
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
