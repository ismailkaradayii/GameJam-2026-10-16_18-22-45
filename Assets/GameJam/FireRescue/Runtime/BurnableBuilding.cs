using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Persistent building fire, cooled by actual litres delivered by a water hit.</summary>
    [DisallowMultipleComponent]
    public sealed class BurnableBuilding : MonoBehaviour
    {
        public ParticleSystem flames;
        public ParticleSystem smoke;
        public Light fireLight;
        [Tooltip("A fitted trigger box for lava contact; existing solid meshes still block water rays.")]
        public BoxCollider ignitionBounds;
        [Tooltip("Decorative window light meshes authored outside the house hierarchy; hide them when this house collapses.")]
        public Renderer[] windowGlowOverlays = Array.Empty<Renderer>();
        public bool startBurning;
        [Min(1f)] public float litresToExtinguish = 100f;
        [Min(0f)] public float wetProtectionSeconds = 2f;
        [Min(0f)] public float extinguishedCooldownSeconds = 5f;
        [Min(5f)] public float secondsToCollapse = 90f;
        [Header("Building fire presentation")]
        public bool fitFireToBuilding = true;
        [Range(.5f, 2f)] public float flameVisualScale = 1f;
        [Min(.2f)] public float visualFireSpreadSeconds = 3.5f;
        [Min(2f)] public float visualCollapseSeconds = 11f;
        public bool IsBurnedOut { get; private set; }
        public float BurnDamage { get; private set; }
        public float Integrity01 => IsBurnedOut ? 0f : Mathf.Clamp01(1f - BurnDamage / Mathf.Max(1f, secondsToCollapse));
        public float SecondsUntilCollapse => IsBurning ? Mathf.Max(0, secondsToCollapse - BurnDamage) / Mathf.Max(.1f, heat) : float.PositiveInfinity;

        [SerializeField, Range(0f, 1f)] private float heat;
        private float wetSecondsRemaining;
        private float cooldownSecondsRemaining;
        private ParticleSystem cachedFlames;
        private ParticleSystem cachedSmoke;
        private Light cachedLight;
        private float flameEmission;
        private float smokeEmission;
        private float lightIntensity;
        private BuildingFirePresentation presentation;
        private BuildingFireVisual detailedFire;
        private Coroutine roofIgnition;
        private BuildingRuinVisual ruin;
        private static readonly List<BurnableBuilding> Buildings = new List<BurnableBuilding>();
        public static event Action FireStateChanged;

        public float Heat => heat;
        public bool IsBurning => heat > 0f;
        public bool IsRescued { get; private set; }
        public float FoundationCooling01 { get; private set; }
        internal LavaCoolingSurface[] FoundationSurfaces { get; set; }
        public event Action<BurnableBuilding> Rescued;
        public bool MissionFireAllowed { get; internal set; } = true;
        public bool IsDefenseFireTarget { get; internal set; }
        public bool IgnitionBlocked => !MissionFireAllowed || IsRescued || wetSecondsRemaining > 0f || cooldownSecondsRemaining > 0f;
        public static IReadOnlyList<BurnableBuilding> ActiveBuildings => Buildings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBuildings()
        {
            Buildings.Clear();
            FireStateChanged = null;
        }

        private void Awake()
        {
            heat = 0f;
            IsRescued = false;
            FoundationCooling01 = 0f;
            FoundationSurfaces = null;
            wetSecondsRemaining = 0f;
            cooldownSecondsRemaining = 0f;
            if (fitFireToBuilding) presentation = BuildingFirePresentation.Create(this);
            UpdateVisuals();
        }

        private void Start()
        {
            if (startBurning)
                Ignite();
        }

        private void OnEnable()
        {
            if (!Buildings.Contains(this))
                Buildings.Add(this);
            UpdateVisuals();
            FireStateChanged?.Invoke();
        }

        private void OnDisable()
        {
            if (roofIgnition != null) StopCoroutine(roofIgnition);
            roofIgnition = null;
            Buildings.Remove(this);
            FireStateChanged?.Invoke();
            StopParticles(flames, true);
            StopParticles(smoke, true);
            presentation?.Stop();
            if (fireLight != null)
                fireLight.enabled = false;
        }

        private void Update() => TickFire(Time.deltaTime);

        /// <summary>Repeated lava contact never refills an already burning building's heat.</summary>
        public void Ignite()
        {
            if (!isActiveAndEnabled || IsBurnedOut || IsBurning || IgnitionBlocked)
                return;
            heat = 1f;
            UpdateVisuals();
            FireStateChanged?.Invoke();
        }

        public void IgniteFromRoofImpact(Vector3 point)
        {
            if (!isActiveAndEnabled || IsBurnedOut || IsBurning || IgnitionBlocked || roofIgnition != null) return;
            roofIgnition = StartCoroutine(IgniteAfterImpact(point));
        }

        IEnumerator IgniteAfterImpact(Vector3 point)
        {
            // Let the spirit break apart first. The house owns this delay, not the despawned actor.
            yield return new WaitForSeconds(.18f);
            roofIgnition = null;
            if (!isActiveAndEnabled || IsBurnedOut || IsBurning || IgnitionBlocked) yield break;
            presentation?.BeginAt(point);
            Ignite();
        }

        public void ApplyWater(float litres)
        {
            if (!isActiveAndEnabled || IsBurnedOut || litres <= 0f || float.IsNaN(litres) || float.IsInfinity(litres))
                return;
            wetSecondsRemaining = Mathf.Max(wetSecondsRemaining, wetProtectionSeconds);
            bool wasBurning = IsBurning;
            heat = Mathf.Max(0f, heat - litres / Mathf.Max(1f, litresToExtinguish));
            if (heat < 0.00001f)
                heat = 0f;
            bool rescuedNow = wasBurning && !IsBurning && !IsRescued;
            if (rescuedNow)
            {
                IsRescued = true;
                cooldownSecondsRemaining = Mathf.Max(cooldownSecondsRemaining, extinguishedCooldownSeconds);
            }
            FoundationCooling01 = IsRescued ? 1f : Mathf.Clamp01(FoundationCooling01 + litres / Mathf.Max(1f, litresToExtinguish));
            LavaCoolingSurface.CoolBuildingFoundation(this, FoundationCooling01);
            UpdateVisuals();
            if (rescuedNow) Rescued?.Invoke(this);
            if (wasBurning) FireStateChanged?.Invoke();
        }

        /// <summary>Explicit time step for wet protection. Rescued buildings remain permanently safe for this game.</summary>
        public void TickFire(float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;
            wetSecondsRemaining = Mathf.Max(0f, wetSecondsRemaining - deltaTime);
            cooldownSecondsRemaining = Mathf.Max(0f, cooldownSecondsRemaining - deltaTime);
            if (IsBurning && !IsBurnedOut)
            {
                BurnDamage += deltaTime * heat;
                if (!ruin) ruin = GetComponent<BuildingRuinVisual>() ?? gameObject.AddComponent<BuildingRuinVisual>();
                ruin.SetDamage(this);
                if (BurnDamage >= secondsToCollapse)
                {
                    IsBurnedOut = true; heat = 0;
                    FireStateChanged?.Invoke();
                    ruin.Collapse(this);
                }
            }
            UpdateVisuals();
            presentation?.Tick(deltaTime);
        }

        private void UpdateVisuals()
        {
            if (presentation != null)
            {
                presentation.SetState(heat, isActiveAndEnabled && !IsBurnedOut);
                return;
            }
            if (Application.isPlaying)
            {
                if (!detailedFire) detailedFire = BuildingFireVisual.Ensure(this);
                if (detailedFire)
                {
                    detailedFire.SetHeat(isActiveAndEnabled ? heat : 0);
                    if (fireLight) fireLight.enabled = isActiveAndEnabled && IsBurning;
                    return;
                }
            }
            CacheVisualSettings();
            bool burning = isActiveAndEnabled && IsBurning;
            UpdateParticles(flames, flameEmission, burning);
            UpdateParticles(smoke, smokeEmission, burning);
            if (fireLight != null)
            {
                fireLight.enabled = burning;
                fireLight.intensity = lightIntensity * heat;
            }
        }

        private void CacheVisualSettings()
        {
            if (flames != cachedFlames)
            {
                cachedFlames = flames;
                flameEmission = flames != null ? flames.emission.rateOverTimeMultiplier : 0f;
            }
            if (smoke != cachedSmoke)
            {
                cachedSmoke = smoke;
                smokeEmission = smoke != null ? smoke.emission.rateOverTimeMultiplier : 0f;
            }
            if (fireLight != cachedLight)
            {
                cachedLight = fireLight;
                lightIntensity = fireLight != null ? fireLight.intensity : 0f;
            }
        }

        private void UpdateParticles(ParticleSystem particles, float baseEmission, bool burning)
        {
            if (particles == null)
                return;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTimeMultiplier = baseEmission * heat;
            if (burning)
            {
                if (!particles.isPlaying)
                    particles.Play(true);
            }
            else if (particles.isPlaying || particles.isEmitting)
                StopParticles(particles, false);
        }

        private static void StopParticles(ParticleSystem particles, bool clear)
        {
            if (particles != null)
                particles.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear :
                    ParticleSystemStopBehavior.StopEmitting);
        }
        private void OnDestroy() => presentation?.Dispose();
    }
}
