using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Roof monitor: litres travel as pooled ballistic water packets and cool only on impact.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class FireTruckWaterCannon : MonoBehaviour
    {
        public FireHoseSystem tank;
        public Transform yawPivot;
        public Transform pitchPivot;
        public Transform muzzle;
        public Camera aimCamera;
        public Transform ignoredCollisionRoot;
        public LineRenderer waterJet;
        public ParticleSystem sprayParticles;
        public ParticleSystem impactParticles;
        [Min(1f)] public float maxRange = 85f;
        [Min(1f)] public float muzzleSpeed = 35f;
        [Min(0f)] public float flowLitresPerSecond = 35f;
        [Range(0f, 89f)] public float maximumElevation = 70f;
        [Range(0f, 89f)] public float maximumDepression = 25f;
        public LayerMask collisionMask = ~0;

        // IsSpraying describes emission. Already emitted water keeps travelling after release/empty.
        public bool IsSpraying { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public BurnableBuilding HitFire { get; private set; }
        public LavaCoolingSurface HitLava { get; private set; }
        public Vector3 LavaImpactPoint { get; private set; }
        public BurnableBuilding PredictedTarget { get; private set; }
        public bool HasPredictedImpact { get; private set; }
        public bool HasReachableAim { get; private set; }
        public bool IsAimObstructed { get; private set; }
        public Vector3 PredictedImpactNormal { get; private set; }
        public Vector3 LaunchVelocity { get; private set; }
        public int InFlightPacketCount { get; private set; }
        public Vector3 NewestPacketPosition { get; private set; }

        const int Capacity = 512;
        const float Step = .02f;
        const float Lifetime = 8f;
        const float Radius = .045f;
        struct Packet
        {
            public bool active;
            public Vector3 position, previous, velocity, launchDirection;
            public float age, litres, emittedAt;
        }
        readonly Packet[] packets = new Packet[Capacity];
        public PressurizedWaterVisual Visuals { get; private set; }
        readonly RaycastHit[] rayHits = new RaycastHit[64];
        readonly Collider[] overlapHits = new Collider[32];
        bool sprayRequested;
        int nextSlot;
        float simulationTime;
        float simulationRemainder;
        Rigidbody vehicleBody;
        ThermalDamage thermal;
        public float PumpEfficiency
        {
            get
            {
                if (!thermal) thermal = tank ? tank.GetComponent<ThermalDamage>() : GetComponentInParent<ThermalDamage>();
                return thermal ? thermal.PumpEfficiency : 1f;
            }
        }
        public float EffectiveMuzzleSpeed => muzzleSpeed * Mathf.Sqrt(PumpEfficiency);
        public float EffectiveFlow => flowLitresPerSecond * PumpEfficiency;


        void Awake()
        {
            if (!tank) tank = GetComponentInParent<FireHoseSystem>();
            if (!ignoredCollisionRoot) ignoredCollisionRoot = tank ? tank.transform : transform;
            vehicleBody = tank ? tank.GetComponentInParent<Rigidbody>() : GetComponentInParent<Rigidbody>();
            thermal = tank ? tank.GetComponent<ThermalDamage>() : GetComponentInParent<ThermalDamage>();
            ClearWater();
        }
        void LateUpdate() => TickSpray(Time.deltaTime);

        public void SetSpraying(bool spraying)
        {
            sprayRequested = spraying;
            if (!spraying) IsSpraying = false;
        }

        /// <summary>Scaled simulation step; zero updates prediction only. Disable automatic ticking for tests.</summary>
        public void TickSpray(float deltaTime)
        {
            HitFire = null;
            HitLava = null;
            if (!muzzle || !IsFinite(maxRange) || maxRange <= 0f || !IsFinite(muzzleSpeed) || muzzleSpeed <= 0f)
            {
                IsSpraying = false;
                HasPredictedImpact = HasReachableAim = false;
                PredictedTarget = null;
                return;
            }
            UpdateAim();
            // Pause must not consume, advance, or erase water already in flight.
            if (!IsFinite(deltaTime) || deltaTime <= 0f || Time.timeScale <= 0f) return;
            simulationRemainder += Mathf.Min(deltaTime, 10f);
            while (simulationRemainder >= Step - .000001f)
            {
                simulationTime += Step;
                if (sprayRequested && tank && IsFinite(EffectiveFlow) && EffectiveFlow > 0f)
                    EmitWater(Step);
                AdvanceWater(Step);
                simulationRemainder = Mathf.Max(0f, simulationRemainder - Step);
            }
            IsSpraying = sprayRequested && tank && tank.TankLitres > 0f &&
                IsFinite(EffectiveFlow) && EffectiveFlow > 0f && InFlightPacketCount < Capacity;
            DrawWater();
        }

        bool EmitWater(float dt)
        {
            // Never charge water that cannot enter the bounded pool.
            if (InFlightPacketCount >= Capacity) return false;
            float litres = tank.ConsumeWater(EffectiveFlow * dt);
            if (litres <= 0f) return false;
            Collider containing = ContainingObstacle(muzzle.position);
            if (containing)
            {
                Impact(containing, muzzle.position, -muzzle.forward, litres);
                return true;
            }
            while (packets[nextSlot].active) nextSlot = (nextSlot + 1) % Capacity;
            packets[nextSlot] = new Packet
            {
                active = true, position = muzzle.position, previous = muzzle.position,
                velocity = LaunchVelocity, litres = litres, age = 0f,
                launchDirection = muzzle.forward, emittedAt = simulationTime
            };
            nextSlot = (nextSlot + 1) % Capacity;
            InFlightPacketCount++;
            return true;
        }

        void AdvanceWater(float dt)
        {
            Vector3 gravity = Physics.gravity;
            for (int i = 0; i < Capacity; i++)
            {
                if (!packets[i].active) continue;
                Packet packet = packets[i];
                Vector3 next = packet.position + packet.velocity * dt + gravity * (.5f * dt * dt);
                Vector3 movement = next - packet.position;
                Collider containing = ContainingObstacle(packet.position);
                if (containing)
                {
                    Impact(containing, packet.position, -packet.velocity.normalized, packet.litres);
                    Retire(i);
                    continue;
                }
                bool solidHit = NearestHit(packet.position, movement.normalized, movement.magnitude, true, out RaycastHit hit);
                float surfaceDistance = solidHit ? hit.distance : movement.magnitude;
                if (WaterSurfaceInteraction.Raycast(new Ray(packet.position, movement.normalized), surfaceDistance, out var water, out var contact))
                {
                    water.Effects.Cannon(contact, packet.velocity, packet.litres);
                    Retire(i);
                    continue;
                }
                if (solidHit)
                {
                    Impact(hit.collider, hit.point, hit.normal, packet.litres);
                    Retire(i);
                    continue;
                }
                packet.previous = packet.position;
                packet.position = next;
                packet.velocity += gravity * dt;
                packet.age += dt;
                packets[i] = packet;
                if (packet.age >= Lifetime) Retire(i);
            }
        }

        void Retire(int index)
        {
            packets[index].active = false;
            InFlightPacketCount--;
        }

        void Impact(Collider surface, Vector3 point, Vector3 normal, float litres)
        {
            var burnable = surface.GetComponentInParent<ThermalDamage>();
            if (burnable) burnable.ApplyWater(litres);
            BurnableBuilding building = surface.GetComponentInParent<BurnableBuilding>();
            if (building)
            {
                HitFire = building;
                building.ApplyWater(litres);
            }
            LavaCoolingSurface lava = surface.GetComponentInParent<LavaCoolingSurface>();
            if (lava)
            {
                HitLava = lava;
                LavaImpactPoint = point;
                lava.ApplyWater(point, litres);
                // At a junction the same visible liquid may be represented by a trunk
                // and a branch mesh. Cool both contacting layers, so hidden hot lava
                // cannot keep burning through the newly formed obsidian.
                foreach (var adjacent in LavaIgnitionSource.ActiveSources)
                {
                    if (!adjacent || adjacent.gameObject == lava.gameObject || !adjacent.IsHotSurfaceActive) continue;
                    Bounds bounds = adjacent.heatCollider.bounds;
                    if (point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z) continue;
                    if (!adjacent.heatCollider.Raycast(new Ray(point + Vector3.up * .75f, Vector3.down), out var contact, 1.5f)) continue;
                    var connected = adjacent.GetComponent<LavaCoolingSurface>();
                    if (!connected) continue;
                    connected.ApplyWater(contact.point, litres);
                    if (connected.BlocksFlow) { HitLava = connected; LavaImpactPoint = contact.point; }
                }
            }
            LavaCreatureActor creature = surface.GetComponentInParent<LavaCreatureActor>();
            if (creature) creature.ApplyWater(litres);
            EnsureVisuals();
            Visuals.Impact(point, normal, litres, simulationTime);
        }

        void UpdateAim()
        {
            if (!vehicleBody) vehicleBody = tank ? tank.GetComponentInParent<Rigidbody>() : GetComponentInParent<Rigidbody>();
            Vector3 requested = CameraTarget(out Collider requestedCollider);
            bool solved = aimCamera == null;
            // Pivot movement changes muzzle position. Re-solve after each aim adjustment.
            for (int i = 0; i < 3; i++)
            {
                Vector3 inherited = vehicleBody ? vehicleBody.GetPointVelocity(muzzle.position) : Vector3.zero;
                Vector3 launchDirection = muzzle.forward;
                if (aimCamera)
                {
                    solved = SolveLowArc(requested - muzzle.position, inherited, out launchDirection);
                    if (!solved) launchDirection = (requested - muzzle.position).normalized;
                    AimTurret(launchDirection);
                }
                LaunchVelocity = muzzle.forward * EffectiveMuzzleSpeed + inherited;
            }
            PredictImpact(requested, requestedCollider, solved);
        }

        Vector3 CameraTarget(out Collider requestedCollider)
        {
            requestedCollider = null;
            if (!aimCamera) return muzzle.position + muzzle.forward * maxRange;
            Ray ray = aimCamera.ViewportPointToRay(new Vector3(.5f, .5f));
            float cameraRange = maxRange + Vector3.Distance(ray.origin, muzzle.position);
            Vector3 requested;
            if (NearestHit(ray.origin, ray.direction, cameraRange, false, out RaycastHit hit))
            {
                requested = hit.point;
                requestedCollider = hit.collider;
            }
            else requested = ray.origin + ray.direction * cameraRange;
            if (WaterSurfaceInteraction.Raycast(ray, Vector3.Distance(ray.origin, requested) + .002f, out var water, out var waterPoint))
            {
                requested = waterPoint; requestedCollider = water.volume;
            }
            if ((requested - muzzle.position).sqrMagnitude > maxRange * maxRange) requestedCollider = null;
            return muzzle.position + Vector3.ClampMagnitude(requested - muzzle.position, maxRange);
        }

        bool SolveLowArc(Vector3 offset, Vector3 inherited, out Vector3 direction)
        {
            direction = offset.normalized;
            // First positive crossing is the low arc. This also compensates vehicle velocity.
            float previousTime = .001f;
            float previous = RequiredSpeedSquared(offset, inherited, previousTime) - EffectiveMuzzleSpeed * EffectiveMuzzleSpeed;
            for (int i = 1; i <= 240; i++)
            {
                float t = i * (Lifetime / 240f);
                float value = RequiredSpeedSquared(offset, inherited, t) - EffectiveMuzzleSpeed * EffectiveMuzzleSpeed;
                if (previous > 0f && value <= 0f)
                {
                    float low = previousTime, high = t;
                    for (int iteration = 0; iteration < 18; iteration++)
                    {
                        float middle = (low + high) * .5f;
                        if (RequiredSpeedSquared(offset, inherited, middle) > EffectiveMuzzleSpeed * EffectiveMuzzleSpeed) low = middle;
                        else high = middle;
                    }
                    float flight = (low + high) * .5f;
                    direction = (offset / flight - inherited - Physics.gravity * (.5f * flight)).normalized;
                    return true;
                }
                previous = value;
                previousTime = t;
            }
            return false;
        }

        static float RequiredSpeedSquared(Vector3 offset, Vector3 inherited, float time) =>
            (offset / time - inherited - Physics.gravity * (.5f * time)).sqrMagnitude;

        void AimTurret(Vector3 direction)
        {
            if (yawPivot)
            {
                Vector3 up = yawPivot.parent ? yawPivot.parent.up : Vector3.up;
                Vector3 flat = Vector3.ProjectOnPlane(direction, up);
                if (flat.sqrMagnitude > .0001f) yawPivot.rotation = Quaternion.LookRotation(flat.normalized, up);
            }
            if (pitchPivot)
            {
                Vector3 local = pitchPivot.parent ? pitchPivot.parent.InverseTransformDirection(direction) : direction;
                float elevation = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                pitchPivot.localRotation = Quaternion.Euler(-Mathf.Clamp(elevation, -maximumDepression, maximumElevation), 0f, 0f);
            }
        }

        void PredictImpact(Vector3 requested, Collider requestedCollider, bool solved)
        {
            HasPredictedImpact = false;
            PredictedTarget = null;
            PredictedImpactNormal = Vector3.up;
            IsAimObstructed = false;
            Vector3 point = muzzle.position, velocity = LaunchVelocity;
            float closest = (point - requested).sqrMagnitude;
            Collider surface = ContainingObstacle(point);
            if (surface)
            {
                HasPredictedImpact = true;
                AimPoint = point;
                PredictedImpactNormal = -muzzle.forward;
            }
            else for (float time = 0f; time < Lifetime; time += Step)
            {
                Vector3 next = point + velocity * Step + Physics.gravity * (.5f * Step * Step);
                Vector3 movement = next - point;
                bool solidHit = NearestHit(point, movement.normalized, movement.magnitude, true, out RaycastHit hit);
                if (WaterSurfaceInteraction.Raycast(new Ray(point, movement.normalized), solidHit ? hit.distance : movement.magnitude,
                    out var water, out var waterPoint))
                {
                    HasPredictedImpact = true; surface = water.volume; AimPoint = waterPoint; PredictedImpactNormal = Vector3.up;
                    closest = Mathf.Min(closest, (waterPoint - requested).sqrMagnitude); break;
                }
                if (solidHit)
                {
                    HasPredictedImpact = true;
                    surface = hit.collider;
                    AimPoint = hit.point;
                    PredictedImpactNormal = hit.normal;
                    closest = Mathf.Min(closest, (hit.point - requested).sqrMagnitude);
                    break;
                }
                point = next;
                velocity += Physics.gravity * Step;
                closest = Mathf.Min(closest, (point - requested).sqrMagnitude);
                AimPoint = point;
                // The reticle is an aiming aid with an 85 m range, not a full 8-second fall preview.
                if (Vector3.Distance(point, muzzle.position) > maxRange + 2f) break;
            }
            if (surface) PredictedTarget = surface.GetComponentInParent<BurnableBuilding>();
            bool sameTarget = requestedCollider && surface &&
                ((surface == requestedCollider && (AimPoint - requested).sqrMagnitude < 1f) ||
                    (PredictedTarget && requestedCollider.GetComponentInParent<BurnableBuilding>() == PredictedTarget));
            HasReachableAim = solved && (sameTarget || closest < .25f);
            IsAimObstructed = HasPredictedImpact && !HasReachableAim;
        }

        bool NearestHit(Vector3 origin, Vector3 direction, float distance, bool sweep, out RaycastHit nearest)
        {
            nearest = default;
            if (distance <= .000001f) return false;
            int count = sweep
                ? Physics.SphereCastNonAlloc(origin, Radius, direction, rayHits, distance, collisionMask, QueryTriggerInteraction.Collide)
                : Physics.RaycastNonAlloc(origin, direction, rayHits, distance, collisionMask, QueryTriggerInteraction.Collide);
            RaycastHit[] hits = rayHits;
            if (count == rayHits.Length)
            {
                hits = sweep ? Physics.SphereCastAll(origin, Radius, direction, distance, collisionMask, QueryTriggerInteraction.Collide)
                    : Physics.RaycastAll(origin, direction, distance, collisionMask, QueryTriggerInteraction.Collide);
                count = hits.Length;
            }
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (IgnoreCollider(hit.collider) || hit.distance >= nearestDistance) continue;
                nearest = hit;
                nearestDistance = hit.distance;
            }
            return !float.IsPositiveInfinity(nearestDistance);
        }

        Collider ContainingObstacle(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, Radius, overlapHits, collisionMask, QueryTriggerInteraction.Collide);
            Collider[] hits = overlapHits;
            if (count == overlapHits.Length)
            {
                hits = Physics.OverlapSphere(point, Radius, collisionMask, QueryTriggerInteraction.Collide);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i];
                if (IgnoreCollider(collider)) continue;
                bool supported = collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider ||
                    (collider is MeshCollider mesh && mesh.convex);
                if (supported && (collider.ClosestPoint(point) - point).sqrMagnitude <= Radius * Radius) return collider;
            }
            return null;
        }

        bool IgnoreCollider(Collider collider) => !collider || (collider is CharacterController && !collider.GetComponentInParent<ThermalDamage>()) ||
            (collider.isTrigger && !collider.GetComponentInParent<ThermalDamage>()) ||
            WaterSourceVolume.IsSurfaceCollider(collider) ||
            (ignoredCollisionRoot && collider.transform.IsChildOf(ignoredCollisionRoot)) ||
            collider.GetComponentInParent<FireRescuePlayer>() != null;

        void EnsureVisuals()
        {
            if (Visuals) return;
            Visuals = GetComponent<PressurizedWaterVisual>();
            if (!Visuals) Visuals = gameObject.AddComponent<PressurizedWaterVisual>();
            if (waterJet) waterJet.enabled = false;
            if (sprayParticles) sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (impactParticles) impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void DrawWater()
        {
            EnsureVisuals();
            if (waterJet) waterJet.enabled = false;
            Visuals.BeginFrame(simulationTime, muzzle.position, LaunchVelocity, IsSpraying);
            bool newest = true;
            for (int offset = 1; offset <= Capacity; offset++)
            {
                int index = (nextSlot - offset + Capacity) % Capacity;
                Packet packet = packets[index];
                if (!packet.active) continue;
                if (newest) { NewestPacketPosition = packet.position; newest = false; }
                Visuals.Sample(packet.position, packet.previous, packet.velocity, packet.age, packet.emittedAt, index);
            }
            Visuals.EndFrame();
        }

        void ClearWater()
        {
            for (int i = 0; i < Capacity; i++) packets[i].active = false;
            InFlightPacketCount = 0;
            simulationRemainder = 0f;
            IsSpraying = false;
            if (waterJet) waterJet.enabled = false;
            if (sprayParticles) sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (impactParticles) impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (Visuals) Visuals.Clear();
        }
        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        void OnDisable() { sprayRequested = false; HitFire = null; ClearWater(); }
    }
}
