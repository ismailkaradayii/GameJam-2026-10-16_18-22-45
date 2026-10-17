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
        VolumeProfile runtimeProfile, runtimeCinematic;
        Bloom bloom;
        Vignette vignette;
        ChromaticAberration chromatic;
        ColorAdjustments grade;
        DepthOfField depth;
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
            }
            if (cinematicVolume) { runtimeCinematic = cinematicVolume.profile; runtimeCinematic.TryGet(out depth); cinematicVolume.weight = 0; }
            if (player) vitals = player.GetComponent<CreatureCombatPlayer>();
            if (fallingAsh) fallingAsh.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
            AshBlend = Mathf.MoveTowards(AshBlend, ashTarget, dt / Mathf.Max(1, atmosphereChangeSeconds));
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(clearFogColor, ashFogColor, AshBlend);
            RenderSettings.fogStartDistance = Mathf.Lerp(clearFogStart, ashFogStart, AshBlend);
            RenderSettings.fogEndDistance = Mathf.Lerp(clearFogEnd, ashFogEnd, AshBlend);
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
            if (vignette) { vignette.intensity.value = Mathf.Lerp(calmVignette, defenseVignette, AshBlend) + SpeedAmount * speedVignette + hurt * .10f; vignette.color.value = Color.Lerp(Color.black, new Color(.14f, .015f, .008f), hurt); }
            if (chromatic) chromatic.intensity.value = chromaticAmount + SpeedAmount * .012f;
            if (bloom) bloom.intensity.value = Mathf.Lerp(.28f, .36f, AshBlend);
            if (grade) { grade.postExposure.value = Mathf.Lerp(.8f, .72f, AshBlend); grade.contrast.value = Mathf.Lerp(12, 15, AshBlend); grade.saturation.value = Mathf.Lerp(-4, -9, AshBlend); }
            if (cinematicVolume)
            {
                cinematicVolume.weight = inShot && cinema.Kind != VolcanoCinematic.ShotKind.Intro ? Mathf.MoveTowards(cinematicVolume.weight, 1, Time.unscaledDeltaTime * 5) : 0;
                if (depth && inShot && cinema.ShotCamera) depth.focusDistance.value = Mathf.Max(5, Vector3.Distance(cinema.ShotCamera.transform.position, cinema.FocusPosition));
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
