using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Persistent fire damage shared by villagers and the rescue vehicle.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(120)]
    public sealed class ThermalDamage : MonoBehaviour
    {
        public EvacuationResident resident;
        public FireTruckDrive truck;
        [Min(1f)] public float maximumHealth = 100f;
        [Min(0f)] public float fireDamagePerSecond = 16f;
        [Min(0f)] public float lavaDamagePerSecond = 48f;
        [Min(1f)] public float waterToExtinguish = 12f;
        [Header("Rescue truck protection")]
        [Min(0f)] public float truckFireGraceSeconds = 3f;
        [Min(0f)] public float automaticSuppressionDelay = 2f;
        [Min(0f)] public float automaticSuppressionLitresPerSecond = 18f;
        [Min(0f)] public float truckWetProtectionSeconds = 10f;
        public float Health { get; private set; } = 100f;
        public float Integrity01 => Mathf.Clamp01(Health / Mathf.Max(1f, maximumHealth));
        public float Heat { get; private set; }
        public bool IsBurning => Heat > .001f;
        public bool IsDestroyed => Health <= 0f;
        public bool InHotLava { get; private set; }
        public bool CanIgnite => isActiveAndEnabled && !IsDestroyed && (!resident || (!resident.IsDead && !resident.IsSafe));
        public float MotorEfficiency => IsDestroyed ? 0f : truck ? Mathf.Lerp(.55f, 1f, Mathf.InverseLerp(0f, .6f, Integrity01)) : Mathf.Lerp(.2f, 1f, Integrity01);
        public float PumpEfficiency => IsDestroyed ? 0f : truck ? Mathf.Lerp(.6f, 1f, Mathf.InverseLerp(0f, .6f, Integrity01)) : Mathf.Lerp(.15f, 1f, Integrity01);
        public bool AutomaticSuppressionActive { get; private set; }
        public int Ignitions { get; private set; }
        public event System.Action<ThermalDamage> Destroyed;
        static readonly List<ThermalDamage> targets = new List<ThermalDamage>();
        public static IReadOnlyList<ThermalDamage> ActiveTargets => targets;
        Collider[] contacts;
        ThermalBurnVisual visual;
        float wetSeconds, corpseFireSeconds, spiritCooldown, burningSeconds;
        FireHoseSystem onboardTank;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTargets() => targets.Clear();

        public static ThermalDamage Ensure(Component owner)
        {
            var damage = owner.GetComponent<ThermalDamage>();
            return damage ? damage : owner.gameObject.AddComponent<ThermalDamage>();
        }
        void Awake()
        {
            resident = GetComponent<EvacuationResident>(); truck = GetComponent<FireTruckDrive>();
            // Migrate old scenes and dynamically spawned trucks; authored tuning remains editable.
            if (truck && maximumHealth <= 100f && fireDamagePerSecond >= 3.2f) ConfigureRescueTruck();
            onboardTank = truck ? GetComponent<FireHoseSystem>() : null;
            Health = Mathf.Max(1f, maximumHealth);
            contacts = GetComponentsInChildren<Collider>(true);
        }
        void OnEnable() { if (!targets.Contains(this)) targets.Add(this); }
        void OnDisable() { targets.Remove(this); InHotLava = false; }
        void Update() => TickHeat(Time.deltaTime);

        public void ConfigureRescueTruck()
        {
            truck = GetComponent<FireTruckDrive>();
            maximumHealth = 240f; fireDamagePerSecond = 1.2f; lavaDamagePerSecond = 6f; waterToExtinguish = 40f;
            truckFireGraceSeconds = 3f; automaticSuppressionDelay = 2f;
            automaticSuppressionLitresPerSecond = 18f; truckWetProtectionSeconds = 10f;
        }

        public Vector3 ClosestPoint(Vector3 from)
        {
            Vector3 best = transform.position + Vector3.up;
            float distance = float.PositiveInfinity;
            if (contacts == null) contacts = GetComponentsInChildren<Collider>(true);
            foreach (var c in contacts)
            {
                if (!Usable(c)) continue;
                Vector3 p = c.ClosestPoint(from);
                float d = (p - from).sqrMagnitude;
                if (d < distance) { best = p; distance = d; }
            }
            return best;
        }
        static bool Usable(Collider c) => c && c.enabled && c.gameObject.activeInHierarchy &&
            (c is BoxCollider || c is CapsuleCollider || c is SphereCollider || c is MeshCollider m && m.convex);

        public bool CheckLavaContact()
        {
            if (!CanIgnite) return false;
            if (contacts == null) contacts = GetComponentsInChildren<Collider>(true);
            foreach (var source in LavaIgnitionSource.ActiveSources)
            {
                if (!source || !source.IsHotSurfaceActive) continue;
                foreach (var contact in contacts)
                {
                    if (!Usable(contact)) continue;
                    if (source.TouchesHotCollider(contact)) return true;
                    // NavMesh feet can hover a few centimetres above a liquid mesh.
                    // Probe the real surface below the feet, including gaps and cooled patches.
                    if (!resident) continue;
                    Bounds b = contact.bounds, lava = source.heatCollider.bounds;
                    Vector3 feet = new Vector3(b.center.x, b.min.y, b.center.z);
                    if (feet.x < lava.min.x || feet.x > lava.max.x || feet.z < lava.min.z || feet.z > lava.max.z) continue;
                    if (source.heatCollider.Raycast(new Ray(new Vector3(feet.x, lava.max.y + .5f, feet.z), Vector3.down), out var surface, lava.size.y + 1f) &&
                        feet.y <= surface.point.y + .18f && feet.y >= surface.point.y - .25f && source.IsHotAt(surface.point)) return true;
                }
                // The chassis sits above the road: the tyres must also detect shallow lava.
                if (truck && truck.wheels != null)
                    foreach (var wheel in truck.wheels)
                    {
                        if (!wheel || !wheel.enabled) continue;
                        wheel.GetWorldPose(out var p, out _);
                        float r = wheel.radius * Mathf.Max(.01f, wheel.transform.lossyScale.y);
                        if (source.heatCollider.Raycast(new Ray(p + Vector3.up * r, Vector3.down), out var hit, r * 2f + .15f) &&
                            source.IsHotAt(hit.point)) return true;
                    }
            }
            return false;
        }

        public bool Ignite(float intensity = 1f)
        {
            if (!CanIgnite || wetSeconds > 0f || !float.IsFinite(intensity) || intensity <= 0f) return false;
            bool first = !IsBurning;
            Heat = Mathf.Max(Heat, Mathf.Clamp01(intensity));
            if (!visual) visual = gameObject.GetComponent<ThermalBurnVisual>() ?? gameObject.AddComponent<ThermalBurnVisual>();
            if (first) { burningSeconds = 0f; Ignitions++; if (resident) resident.BeginBurning(); visual.OnIgnited(); }
            return true;
        }
        public bool IgniteFromSpirit(float impactDamage = 12f)
        {
            if (!CanIgnite || wetSeconds > 0f || spiritCooldown > 0f) return false;
            spiritCooldown = truck ? 4f : .8f;
            if (!Ignite()) return false;
            Damage(impactDamage * (truck ? .5f : 1f));
            return true;
        }
        public void ApplyWater(float litres)
        {
            if (!float.IsFinite(litres) || litres <= 0f) return;
            Heat = Mathf.Max(0f, Heat - litres / Mathf.Max(1f, waterToExtinguish));
            if (!IsBurning)
            {
                Heat = 0f; burningSeconds = 0f; wetSeconds = truck ? truckWetProtectionSeconds : 2f;
                AutomaticSuppressionActive = false;
                if (resident && !resident.IsDead) resident.StopBurning();
            }
        }
        public float SuppressFromTank(FireHoseSystem tank, float dt)
        {
            if (!truck || !tank || !IsBurning || IsDestroyed || !float.IsFinite(dt) || dt <= 0f || Time.timeScale <= 0f) return 0f;
            float litres = tank.ConsumeWater(45f * Mathf.Min(dt, .25f));
            if (litres > 0f) ApplyWater(litres);
            return litres;
        }
        public void TickHeat(float dt)
        {
            if (!isActiveAndEnabled || !float.IsFinite(dt) || dt <= 0f || Time.timeScale <= 0f) return;
            dt = Mathf.Min(dt, .25f);
            AutomaticSuppressionActive = false;
            spiritCooldown = Mathf.Max(0f, spiritCooldown - dt);
            wetSeconds = Mathf.Max(0f, wetSeconds - dt);
            if (resident && resident.IsSafe) { Heat = 0f; InHotLava = false; return; }
            InHotLava = CheckLavaContact();
            if (InHotLava) { wetSeconds = 0f; Ignite(); }
            else if (CanIgnite && wetSeconds <= 0f)
            {
                foreach (var building in BurnableBuilding.ActiveBuildings)
                {
                    if (!building || !building.IsBurning || !building.ignitionBounds || !building.ignitionBounds.enabled) continue;
                    foreach (var contact in contacts)
                    {
                        if (!Usable(contact) || !contact.bounds.Intersects(building.ignitionBounds.bounds)) continue;
                        if (Physics.ComputePenetration(contact, contact.transform.position, contact.transform.rotation,
                            building.ignitionBounds, building.ignitionBounds.transform.position, building.ignitionBounds.transform.rotation, out _, out _))
                        { Ignite(Mathf.Max(.6f, building.Heat)); break; }
                    }
                    if (IsBurning) break;
                }
            }
            if (!IsBurning) return;
            if (IsDestroyed)
            {
                corpseFireSeconds -= dt;
                if (corpseFireSeconds <= 0f) Heat = 0f;
                return;
            }
            float previousBurningSeconds = burningSeconds;
            burningSeconds += dt;
            // Short lava crossings leave time to escape. Staying in lava still destroys the vehicle.
            float damageSeconds = truck ? Mathf.Clamp(burningSeconds - truckFireGraceSeconds, 0f, dt) : dt;
            Damage((fireDamagePerSecond * Heat + (InHotLava ? lavaDamagePerSecond : 0f)) * damageSeconds);
            if (truck && !IsDestroyed && !InHotLava && onboardTank && burningSeconds > automaticSuppressionDelay)
            {
                float suppressionSeconds = Mathf.Clamp(burningSeconds - Mathf.Max(previousBurningSeconds, automaticSuppressionDelay), 0f, dt);
                float litres = onboardTank.ConsumeWater(automaticSuppressionLitresPerSecond * suppressionSeconds);
                if (litres > 0f) { ApplyWater(litres); AutomaticSuppressionActive = IsBurning; }
            }
        }
        void Damage(float amount)
        {
            if (IsDestroyed || !float.IsFinite(amount) || amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            if (!IsDestroyed) return;
            corpseFireSeconds = truck ? 18f : 5f;
            if (resident) resident.DieFromFire();
            if (visual) visual.OnDeath();
            Destroyed?.Invoke(this);
        }
    }
}
