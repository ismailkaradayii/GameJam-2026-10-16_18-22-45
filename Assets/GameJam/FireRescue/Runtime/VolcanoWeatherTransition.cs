using BKPureNature;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>Keep the sunny opening; restore the pack's volcanic sky and cloud deck after the explosion.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(400)]
    public sealed class VolcanoWeatherTransition : MonoBehaviour
    {
        public VolcanoEruptionSequence sequence;
        public BK_EnvironmentManager environment;
        public Light sun, sunnyFill;
        public Material volcanicSky, volcanicClouds;
        [Min(1f)] public float transitionSeconds = 10f;
        public Vector3 volcanicSunEuler = new Vector3(149.3f, -194.7f, 0);
        public Color volcanicSunColor = Color.white;
        public float volcanicSunIntensity = 1f;
        public Color volcanicFogColor = new Color(.2f,.15f,.12f);
        public Color volcanicAmbientSky = new Color(.23f,.21f,.19f);
        public Color volcanicAmbientEquator = new Color(.114f,.125f,.133f);
        public Color volcanicAmbientGround = new Color(.047f,.043f,.035f);
        public float volcanicFogStart = 115f, volcanicFogEnd = 1550f;
        public float cloudAltitude = 600f, cloudDepth = 500f;
        public int cloudLayers = 50;
        public float Transition01 { get; private set; }
        Material savedSky, savedClouds, fadingSunnySky, runtimeClouds;
        Quaternion savedSunRotation;
        Color savedSunColor, savedSkyAmbient, savedEquator, savedGround, savedFogColor, savedSkyTint, savedSkyGround;
        float savedSunIntensity, savedFillIntensity, savedFogStart, savedFogEnd, savedSkyExposure, savedAltitude, savedDepth, fullCoverage;
        int savedLayers;
        bool skyChanged, started, finalProbe;

        void Awake()
        {
            savedSky = RenderSettings.skybox;
            savedSkyAmbient = RenderSettings.ambientSkyColor; savedEquator = RenderSettings.ambientEquatorColor; savedGround = RenderSettings.ambientGroundColor;
            savedFogColor = RenderSettings.fogColor; savedFogStart = RenderSettings.fogStartDistance; savedFogEnd = RenderSettings.fogEndDistance;
            if (sun) { savedSunColor = sun.color; savedSunIntensity = sun.intensity; savedSunRotation = sun.transform.rotation; }
            if (sunnyFill) savedFillIntensity = sunnyFill.intensity;
            if (environment) { savedClouds = environment.cloudsMaterial; savedAltitude = environment.Altitude; savedDepth = environment.volumeSize; savedLayers = environment.volumeSamples; }
            if (savedSky && savedSky.HasProperty("_SkyTint"))
            {
                fadingSunnySky = new Material(savedSky) { name = "Runtime sunny to volcanic sky" };
                savedSkyTint = savedSky.GetColor("_SkyTint"); savedSkyGround = savedSky.GetColor("_GroundColor"); savedSkyExposure = savedSky.GetFloat("_Exposure");
            }
            if (volcanicClouds)
            {
                runtimeClouds = new Material(volcanicClouds) { name = "Runtime native volcanic cloud deck" };
                fullCoverage = runtimeClouds.HasProperty("_Coverage") ? runtimeClouds.GetFloat("_Coverage") : 1f;
                runtimeClouds.enableInstancing = true;
            }
        }
        void LateUpdate() => TickWeather(Time.deltaTime);
        public void TickWeather(float dt)
        {
            if (!sequence || !float.IsFinite(dt) || dt <= 0 || Time.timeScale <= 0 ||
                sequence.missionDirector && sequence.missionDirector.gameFlow && sequence.missionDirector.gameFlow.BlocksGameplay) return;
            bool erupted = sequence.Stage == VolcanoEruptionSequence.EruptionStage.Erupting || sequence.Stage == VolcanoEruptionSequence.EruptionStage.Flowing;
            Transition01 = Mathf.MoveTowards(Transition01, erupted ? 1 : 0, Mathf.Min(.1f,dt)/Mathf.Max(1,transitionSeconds));
            if (Transition01 <= 0)
            {
                if (started) RestoreSunny();
                started = false; return;
            }
            started = true;
            float blend = Mathf.SmoothStep(0,1,Transition01);
            if (sun)
            {
                sun.intensity = Mathf.Lerp(savedSunIntensity,volcanicSunIntensity,blend);
                sun.color = Color.Lerp(savedSunColor,volcanicSunColor,blend);
                sun.transform.rotation = Quaternion.Slerp(savedSunRotation,Quaternion.Euler(volcanicSunEuler),blend);
            }
            if (sunnyFill) sunnyFill.intensity = Mathf.Lerp(savedFillIntensity,0,blend);
            RenderSettings.ambientSkyColor = Color.Lerp(savedSkyAmbient,volcanicAmbientSky,blend);
            RenderSettings.ambientEquatorColor = Color.Lerp(savedEquator,volcanicAmbientEquator,blend);
            RenderSettings.ambientGroundColor = Color.Lerp(savedGround,volcanicAmbientGround,blend);
            RenderSettings.fogColor = Color.Lerp(savedFogColor,volcanicFogColor,blend);
            RenderSettings.fogStartDistance = Mathf.Lerp(savedFogStart,volcanicFogStart,blend);
            RenderSettings.fogEndDistance = Mathf.Lerp(savedFogEnd,volcanicFogEnd,blend);
            if (environment && runtimeClouds)
            {
                environment.cloudsMaterial = runtimeClouds;
                environment.Altitude = Mathf.Lerp(savedAltitude,cloudAltitude,blend);
                environment.volumeSize = Mathf.Lerp(savedDepth,cloudDepth,blend); environment.volumeSamples = cloudLayers;
                if (runtimeClouds.HasProperty("_Coverage")) runtimeClouds.SetFloat("_Coverage",fullCoverage*blend);
            }
            if (fadingSunnySky && blend < .65f)
            {
                fadingSunnySky.SetColor("_SkyTint",Color.Lerp(savedSkyTint,volcanicFogColor,blend));
                fadingSunnySky.SetColor("_GroundColor",Color.Lerp(savedSkyGround,volcanicFogColor,blend));
                fadingSunnySky.SetFloat("_Exposure",Mathf.Lerp(savedSkyExposure,.45f,blend));
                RenderSettings.skybox = fadingSunnySky;
            }
            else if (volcanicSky) RenderSettings.skybox = volcanicSky;
            if (!skyChanged && blend >= .65f) { skyChanged = true; DynamicGI.UpdateEnvironment(); }
            if (!finalProbe && Transition01 >= .999f) { finalProbe = true; DynamicGI.UpdateEnvironment(); }
        }
        void RestoreSunny()
        {
            RenderSettings.skybox = savedSky; RenderSettings.ambientSkyColor = savedSkyAmbient;
            RenderSettings.ambientEquatorColor = savedEquator; RenderSettings.ambientGroundColor = savedGround;
            RenderSettings.fogColor = savedFogColor; RenderSettings.fogStartDistance = savedFogStart; RenderSettings.fogEndDistance = savedFogEnd;
            if (sun) { sun.color = savedSunColor; sun.intensity = savedSunIntensity; sun.transform.rotation = savedSunRotation; }
            if (sunnyFill) sunnyFill.intensity = savedFillIntensity;
            if (environment) { environment.cloudsMaterial = savedClouds; environment.Altitude = savedAltitude; environment.volumeSize = savedDepth; environment.volumeSamples = savedLayers; }
            skyChanged = finalProbe = false;
        }
        void OnDestroy()
        {
            if (started && gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()) RestoreSunny();
            if (fadingSunnySky) Destroy(fadingSunnySky);
            if (runtimeClouds) Destroy(runtimeClouds);
        }
    }
}
