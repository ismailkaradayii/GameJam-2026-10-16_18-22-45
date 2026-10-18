using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameJam.FireRescue
{
    /// <summary>Scene-local ash, distance haze and restrained lens changes driven by the existing eruption.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(350)]
    public sealed class VolcanoAtmospherePolish : MonoBehaviour
    {
        public VolcanoEruptionSequence sequence;
        public FireRescuePlayer player;
        public Volume atmosphereVolume, cinematicVolume;
        public ParticleSystem fallingAsh;
        public ParticleSystem[] groundHaze = System.Array.Empty<ParticleSystem>();
        public bool anchorAshToVillage;
        public Vector3 villageAshOrigin;
        [Header("Distance haze")]
        public Color clearFogColor = new Color(.20f, .15f, .12f);
        public Color ashFogColor = new Color(.27f, .23f, .20f);
        [Min(10)] public float clearFogStart = 120, clearFogEnd = 1900;
        [Min(10)] public float ashFogStart = 95, ashFogEnd = 1550;
        [Min(1)] public float atmosphereChangeSeconds = 12;
        [Range(0, 30)] public float ashParticlesPerSecond = 13;
        [Header("Readable gameplay lenses")]
        [Range(0, .3f)] public float calmVignette = .14f, defenseVignette = .18f, speedVignette = .04f;
        [Range(0, .1f)] public float chromaticAmount = .025f;
        public float AshBlend { get; private set; }
        public float SpeedAmount { get; private set; }
        [Header("Coordinated ash front and room exposure")]
        public bool descendingFogEnabled = true;
        public VolcanoDescendingAshFog descendingFog;
        [Range(0, .6f)] public float indoorExposureLift = .28f;
        [Range(0, 1)] public float ashSkyBounceIntensity = .34f;
        public float IndoorBlend { get; private set; }
        VolumeProfile runtimeProfile, runtimeCinematic;
        Bloom bloom;
        Vignette vignette;
        ChromaticAberration chromatic;
        ColorAdjustments grade;
        DepthOfField depth;
        float cinematicAperture, cinematicFocalLength;
        WhiteBalance whiteBalance;
        SplitToning splitToning;
        Light windowFill, ashScatterFill;
        Color savedFogColor;
        FogMode savedMode;
        bool savedFog;
        float savedStart, savedEnd, savedDensity, lastHealth = -1, damagePulse;
        CreatureCombatPlayer vitals;

        void Awake()
        {
            savedFog = RenderSettings.fog; savedMode = RenderSettings.fogMode; savedFogColor = RenderSettings.fogColor;
            savedStart = RenderSettings.fogStartDistance; savedEnd = RenderSettings.fogEndDistance; savedDensity = RenderSettings.fogDensity;
            if (atmosphereVolume)
            {
                runtimeProfile = atmosphereVolume.profile;
                runtimeProfile.TryGet(out bloom); runtimeProfile.TryGet(out vignette);
                runtimeProfile.TryGet(out chromatic); runtimeProfile.TryGet(out grade);
                if (!runtimeProfile.TryGet(out whiteBalance)) whiteBalance = runtimeProfile.Add<WhiteBalance>();
                if (!runtimeProfile.TryGet(out splitToning)) splitToning = runtimeProfile.Add<SplitToning>();
                whiteBalance.temperature.Override(2); whiteBalance.tint.Override(0);
                splitToning.shadows.Override(new Color(.46f, .485f, .52f));
                splitToning.highlights.Override(new Color(.525f, .505f, .48f));
                splitToning.balance.Override(-8);
            }
            if (cinematicVolume)
            {
                runtimeCinematic = cinematicVolume.profile; runtimeCinematic.TryGet(out depth); cinematicVolume.weight = 0;
                if (depth) { cinematicAperture = depth.aperture.value; cinematicFocalLength = depth.focalLength.value; }
            }
            if (player) vitals = player.GetComponent<CreatureCombatPlayer>();
            if (fallingAsh) fallingAsh.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (descendingFogEnabled)
            {
                if (!descendingFog) descendingFog = GetComponent<VolcanoDescendingAshFog>();
                if (!descendingFog) descendingFog = gameObject.AddComponent<VolcanoDescendingAshFog>();
                descendingFog.Initialize(sequence);
            }
            CreateWindowFill();
            var scatter = new GameObject("Ash sky bounce (runtime)"); scatter.transform.SetParent(transform, false);
            scatter.transform.rotation = Quaternion.Euler(52, 205, 0);
            ashScatterFill = scatter.AddComponent<Light>(); ashScatterFill.type = LightType.Directional;
            ashScatterFill.color = new Color(.64f, .73f, .82f); ashScatterFill.intensity = 0;
            ashScatterFill.shadows = LightShadows.None; ashScatterFill.bounceIntensity = 0;
        }
        void LateUpdate() => TickAtmosphere(Time.deltaTime);
        public void TickAtmosphere(float deltaTime)
        {
            if (!player || !sequence || !float.IsFinite(deltaTime)) return;
            // A cinematic locks input while the eruption/weather must keep evolving.
            bool paused = Time.timeScale <= 0 || (player.gameFlow && player.gameFlow.BlocksGameplay);
            float dt = paused ? 0 : Mathf.Clamp(deltaTime, 0, .1f);
            bool erupted = sequence.Stage == VolcanoEruptionSequence.EruptionStage.Erupting || sequence.Stage == VolcanoEruptionSequence.EruptionStage.Flowing;
            float ashTarget = erupted ? sequence.effects && sequence.effects.driftingAshClouds.Length > 0 ? sequence.effects.AshArrival01 : 1 : 0;
            if (descendingFogEnabled && descendingFog)
            {
                descendingFog.TickFog();
                ashTarget = Mathf.Max(ashTarget, descendingFog.CoverageAt(player.transform.position));
            }
            AshBlend = Mathf.MoveTowards(AshBlend, ashTarget, dt / Mathf.Max(1, atmosphereChangeSeconds));
            // The volcanic sun falls behind the ridge. A weak, shadow-free sky bounce keeps
            // nearby people, road surfaces and house fronts readable beneath the ash deck.
            if (ashScatterFill) ashScatterFill.intensity = ashSkyBounceIntensity * AshBlend;
            var mission = player.mission;
            bool inRoom = mission && mission.startingHouse && player.viewCamera && mission.startingHouse.bounds.Contains(player.viewCamera.transform.position);
            bool inShelter = mission && mission.evacuation && mission.evacuation.IsInsideShelter(player.transform.position);
            IndoorBlend = Mathf.MoveTowards(IndoorBlend, inRoom || inShelter ? 1 : 0, dt * 2);
            if (windowFill) windowFill.intensity = (inRoom ? .75f : 0) * Mathf.Lerp(1, .55f, AshBlend);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(Color.Lerp(clearFogColor, new Color(.49f, .56f, .61f), .4f), new Color(.34f, .325f, .30f), AshBlend);
            float stormStart = descendingFogEnabled && descendingFog ? descendingFog.nearVisibility : ashFogStart;
            float stormEnd = descendingFogEnabled && descendingFog ? descendingFog.farVisibility : ashFogEnd;
            RenderSettings.fogStartDistance = Mathf.Lerp(clearFogStart, Mathf.Lerp(stormStart, 70, IndoorBlend), AshBlend);
            RenderSettings.fogEndDistance = Mathf.Lerp(clearFogEnd, Mathf.Lerp(stormEnd, 450, IndoorBlend), AshBlend);
            var cinema = sequence.cinematic;
            bool inShot = cinema && cinema.IsPlaying;
            float speed = player.IsDriving && player.drive ? Mathf.InverseLerp(35, 150, player.drive.SpeedKph) :
                player.firstPerson && player.firstPerson.IsSprinting ? Mathf.InverseLerp(2, player.firstPerson.runSpeed, player.firstPerson.CurrentSpeed) : 0;
            SpeedAmount = Mathf.MoveTowards(SpeedAmount, !paused && !inShot ? speed : 0, dt * 3);
            if (vitals)
            {
                if (lastHealth >= 0 && vitals.Health < lastHealth - .1f) damagePulse = .24f;
                lastHealth = vitals.Health;
            }
            damagePulse = Mathf.MoveTowards(damagePulse, 0, dt);
            float hurt = damagePulse / .24f;
            if (vignette) { vignette.intensity.value = Mathf.Lerp(Mathf.Min(calmVignette, .10f), Mathf.Min(defenseVignette, .14f), AshBlend) + SpeedAmount * Mathf.Min(speedVignette, .025f) + hurt * .08f; vignette.color.value = Color.Lerp(Color.black, new Color(.14f, .015f, .008f), hurt); }
            if (chromatic) chromatic.intensity.value = Mathf.Min(chromaticAmount, .006f) + SpeedAmount * .005f;
            if (bloom) { bloom.intensity.value = Mathf.Lerp(.22f, .29f, AshBlend); bloom.threshold.Override(1.35f); bloom.scatter.Override(.5f); }
            if (grade) { grade.postExposure.value = Mathf.Lerp(.72f, .62f, AshBlend) + IndoorBlend * indoorExposureLift; grade.contrast.value = Mathf.Lerp(7, 11, AshBlend) - IndoorBlend * 3; grade.saturation.value = Mathf.Lerp(-3, -10, AshBlend); }
            if (whiteBalance) whiteBalance.temperature.value = Mathf.Lerp(2, -5, AshBlend);
            if (cinematicVolume)
            {
                var story = mission ? mission.story : null;
                float windowFocus = story && !inShot ? story.WindowFocusBlend : 0f;
                cinematicVolume.weight = windowFocus > 0 ? windowFocus :
                    inShot && cinema.Kind != VolcanoCinematic.ShotKind.Intro ? Mathf.MoveTowards(cinematicVolume.weight, 1, Time.unscaledDeltaTime * 5) : 0;
                if (depth)
                {
                    depth.aperture.value = windowFocus > 0 ? story.windowAperture : cinematicAperture;
                    depth.focalLength.value = windowFocus > 0 ? story.windowFocalLength : cinematicFocalLength;
                    if (windowFocus > 0 && player.viewCamera && story.volcanoLookTarget)
                    {
                        // Depth of field focuses on a plane perpendicular to the view.
                        // Follow the actual lens during the lean so the crater stays sharp.
                        var view = player.viewCamera.transform;
                        depth.focusDistance.value = Mathf.Max(5, Vector3.Dot(story.volcanoLookTarget.position - view.position, view.forward));
                    }
                    else if (inShot && cinema.ShotCamera)
                        depth.focusDistance.value = Mathf.Max(5, Vector3.Distance(cinema.ShotCamera.transform.position, cinema.FocusPosition));
                }
            }
            if (fallingAsh)
            {
                fallingAsh.transform.position = anchorAshToVillage ? villageAshOrigin : player.transform.position + Vector3.up * 17;
                var emission = fallingAsh.emission; emission.rateOverTime = ashParticlesPerSecond * AshBlend;
                if (erupted && AshBlend > 0 && !fallingAsh.isPlaying) fallingAsh.Play();
                if (!erupted && fallingAsh.isPlaying) fallingAsh.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (var haze in groundHaze) if (haze) { var emission = haze.emission; emission.rateOverTime = Mathf.Lerp(.35f, .65f, AshBlend); }
            if (player.gameFlow && (player.gameFlow.State == RescueGameFlow.GameState.Won || player.gameFlow.State == RescueGameFlow.GameState.Lost) && sequence.cameraShake)
                sequence.cameraShake.StopShake();
        }
        void CreateWindowFill()
        {
            var mission = player ? player.mission : null;
            if (!mission || !mission.startingHouse || !mission.story || !mission.story.windowStop || !player.viewCamera) return;
            var bounds = mission.startingHouse.bounds;
            var go = new GameObject("Soft bedroom window bounce (runtime)"); go.transform.SetParent(transform, false);
            Vector3 point = Vector3.Lerp(mission.story.windowStop.position, bounds.center, .35f);
            point.y = Mathf.Clamp(player.viewCamera.transform.position.y, bounds.min.y + 1, bounds.max.y - 1);
            go.transform.position = point;
            windowFill = go.AddComponent<Light>(); windowFill.type = LightType.Point;
            windowFill.color = new Color(.72f, .82f, 1); windowFill.intensity = 0;
            windowFill.range = Mathf.Clamp(bounds.size.magnitude * .65f, 12, 24);
            windowFill.shadows = LightShadows.None; windowFill.bounceIntensity = 0;
        }
        void OnDestroy()
        {
            if (gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
            {
                RenderSettings.fog = savedFog; RenderSettings.fogMode = savedMode; RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogStartDistance = savedStart; RenderSettings.fogEndDistance = savedEnd; RenderSettings.fogDensity = savedDensity;
            }
            if (runtimeProfile) Destroy(runtimeProfile);
            if (runtimeCinematic) Destroy(runtimeCinematic);
        }
    }
}
