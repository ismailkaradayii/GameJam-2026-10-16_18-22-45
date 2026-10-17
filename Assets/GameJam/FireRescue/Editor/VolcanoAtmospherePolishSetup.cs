using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class VolcanoAtmospherePolishSetup
{
    public const string Folder = "Logs/VolcanoPolish";
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string Materials = "Assets/GameJam/FireRescue/Materials/";

    [MenuItem("GameJam/Final Loop/Apply eruption atmosphere polish")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Volcano_Demo before applying polish.");
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var fx = sequence.effects; var cinema = sequence.cinematic; var player = cinema.player;
        sequence.cameraShake = Ensure<VolcanoCameraShake>(player.viewCamera.gameObject);
        cinema.eruptionSeconds = 8; cinema.startOffset = new Vector3(390, 300, -670); cinema.endOffset = new Vector3(290, 240, -535);
        cinema.eruptionFocusOffset = new Vector3(0, 130, 0); cinema.aftermathShake = sequence.cameraShake;
        cinema.aftershockSeconds = 8; cinema.aftershockStrength = .12f;
        sequence.cameraShake.noiseFrequency = 11; sequence.cameraShake.aimedStrengthMultiplier = .35f;
        fx.intenseBombSeconds = 7; fx.bombFadeSeconds = 7; fx.sustainedBombFraction = .08f; fx.flashSeconds = 1.2f;
        fx.eruptionLight.intensity = 1100; fx.eruptionLight.range = 220;
        fx.smokePlume.transform.position = cinema.crater.position + Vector3.up * 8;
        fx.eruptionBurst.transform.position = cinema.crater.position + Vector3.up * 12;
        fx.lavaBombs.transform.position = cinema.crater.position + Vector3.up * 4;
        ConfigureColumn(fx.smokePlume, false);
        if (!fx.ashCrown)
        {
            var clone = Object.Instantiate(fx.smokePlume.gameObject, fx.transform);
            clone.name = "Upper rolling ash crown"; fx.ashCrown = clone.GetComponent<ParticleSystem>();
        }
        fx.ashCrown.transform.position = cinema.crater.position + Vector3.up * 115;
        ConfigureColumn(fx.ashCrown, true);
        // The pack's Color blend preserves bright texture luminance. Ash needs multiply tint.
        var ashMaterial = Material("Volcanic ash smoke.mat", fx.smokePlume.GetComponent<ParticleSystemRenderer>().sharedMaterial);
        ashMaterial.name = "Volcanic ash smoke"; ashMaterial.SetFloat("_ColorMode", 0);
        ashMaterial.DisableKeyword("_COLORCOLOR_ON"); ashMaterial.DisableKeyword("_COLOROVERLAY_ON"); ashMaterial.DisableKeyword("_COLORADDSUBDIFF_ON");
        ashMaterial.SetColor("_BaseColor", new Color(.48f,.47f,.46f,1));
        ashMaterial.SetColor("_EmissionColor", Color.black); ashMaterial.DisableKeyword("_EMISSION");
        fx.smokePlume.GetComponent<ParticleSystemRenderer>().sharedMaterial = ashMaterial;
        fx.ashCrown.GetComponent<ParticleSystemRenderer>().sharedMaterial = ashMaterial; Dirty(ashMaterial);
        ConfigurePressureCloud(fx.eruptionBurst);
        ConfigureBombs(fx.lavaBombs);

        var atmosphereObject = GameObject.Find("Volcano atmosphere polish") ?? new GameObject("Volcano atmosphere polish");
        var atmosphere = Ensure<VolcanoAtmospherePolish>(atmosphereObject);
        atmosphere.sequence = sequence; atmosphere.player = player;
        var profile = Profile("Ashfall Atmosphere.asset");
        var bloom = Component<Bloom>(profile); bloom.intensity.Override(.28f); bloom.threshold.Override(1.2f); bloom.scatter.Override(.58f);
        var grade = Component<ColorAdjustments>(profile); grade.postExposure.Override(.8f); grade.contrast.Override(12); grade.saturation.Override(-4);
        var vignette = Component<Vignette>(profile); vignette.intensity.Override(.14f); vignette.smoothness.Override(.72f); vignette.color.Override(Color.black);
        Component<ChromaticAberration>(profile).intensity.Override(.025f);
        atmosphere.atmosphereVolume = Volume(atmosphereObject.transform, "Atmosphere grading", profile, 20, 1);
        var cinematicProfile = Profile("Ashfall Cinematic Lens.asset");
        var depth = Component<DepthOfField>(cinematicProfile); depth.mode.Override(DepthOfFieldMode.Bokeh);
        depth.focusDistance.Override(600); depth.aperture.Override(10); depth.focalLength.Override(42);
        atmosphere.cinematicVolume = Volume(atmosphereObject.transform, "Cinematic lens only", cinematicProfile, 30, 0);
        var environment = Object.FindFirstObjectByType<BKPureNature.BK_EnvironmentManager>();
        environment.overrideFogColor = false; Dirty(environment);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = atmosphere.clearFogColor;
        RenderSettings.fogStartDistance = atmosphere.clearFogStart; RenderSettings.fogEndDistance = atmosphere.clearFogEnd;
        var dustMaterial = Material("Ground ash dust.mat", Resources.Load<Material>("LavaCombat/Smoke"));
        var emberMaterial = Resources.Load<Material>("LavaCombat/Flame");
        atmosphere.fallingAsh = Particles(atmosphereObject.transform, "Airborne ash flecks", dustMaterial, 160, true);
        ConfigureAsh(atmosphere.fallingAsh);
        atmosphere.groundHaze = new Vector3[] { new Vector3(-30, 0, -575), new Vector3(70, 0, -582), new Vector3(180, 0, -610) }
            .Select((position,index) =>
            {
                var haze = Particles(atmosphereObject.transform, "Low ground haze " + index, dustMaterial, 24, true);
                foreach (var terrain in Terrain.activeTerrains) { position.y = terrain.SampleHeight(position) + terrain.transform.position.y + .5f; break; }
                haze.transform.position = position; ConfigureGroundHaze(haze); return haze;
            }).ToArray();
        var feel = Ensure<PlayerPolishEffects>(player.gameObject); feel.player = player;
        feel.movementDust = Particles(player.transform, "Footfall dust", dustMaterial, 64, false);
        feel.pickupSparkles = Particles(player.transform, "Pickup sparkles", emberMaterial, 32, false);
        var pickupAudio = player.transform.Find("Pickup confirmation audio");
        if (!pickupAudio) { pickupAudio = new GameObject("Pickup confirmation audio").transform; pickupAudio.SetParent(player.transform, false); }
        feel.pickupAudio = Ensure<AudioSource>(pickupAudio.gameObject); feel.pickupAudio.playOnAwake = false;
        feel.pickupAudio.spatialBlend = 0; feel.pickupAudio.dopplerLevel = 0; feel.pickupSound = Resources.Load<AudioClip>("LavaCombat/SpiritConfirm");
        Dirty(feel); Dirty(feel.pickupAudio); Dirty(atmosphere); Dirty(fx); Dirty(cinema); Dirty(sequence); Dirty(sequence.cameraShake); Dirty(fx.eruptionLight);
        AddFade(player.gameFlow);
        EditorUtility.SetDirty(profile); EditorUtility.SetDirty(cinematicProfile);
        foreach (var component in profile.components.Concat(cinematicProfile.components)) EditorUtility.SetDirty(component);
        foreach (var ps in new[] { fx.eruptionBurst, fx.smokePlume, fx.lavaBombs, fx.ashCrown, atmosphere.fallingAsh, feel.movementDust, feel.pickupSparkles }.Concat(atmosphere.groundHaze))
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off;
            Dirty(ps); Dirty(ps.transform); Dirty(renderer);
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        var menu = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity"); AddFade(null);
        EditorSceneManager.MarkSceneDirty(menu); EditorSceneManager.SaveScene(menu);
        EditorSceneManager.OpenScene(ScenePath); Audit();
        File.WriteAllText(Folder + "/setup.txt", "APPLIED: native eruption layers, post-cinematic aftershock, scene atmosphere/lens profiles, ash/haze, foot/pickup FX and scene fades.\n");
    }

    static void ConfigureColumn(ParticleSystem ps, bool crown)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = true; main.prewarm = false; main.duration = 12;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startSize3D = main.startRotation3D = false;
        main.maxParticles = crown ? 128 : 256; main.startLifetime = new ParticleSystem.MinMaxCurve(crown ? 16 : 12, crown ? 23 : 16);
        main.startSpeed = new ParticleSystem.MinMaxCurve(crown ? 30 : 50, crown ? 45 : 75);
        main.startSize = new ParticleSystem.MinMaxCurve(crown ? 160 : 90, crown ? 220 : 135);
        main.startColor = new Color(.23f, .22f, .21f, crown ? .68f : .86f); main.gravityModifier = 0;
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = crown ? 52 : 10; shape.radius = crown ? 25 : 12;
        ps.transform.rotation = Quaternion.Euler(-90, 0, 0); ps.transform.localScale = Vector3.one;
        var emission = ps.emission; emission.rateOverTime = crown ? 7 : 26; emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
        var size = ps.sizeOverLifetime; size.enabled = true; size.separateAxes = false; size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0,.6f),new Keyframe(.25f,1.4f),new Keyframe(1,2.3f)));
        var velocity = ps.velocityOverLifetime; velocity.enabled = false;
        var force = ps.forceOverLifetime; force.enabled = false;
        var limit = ps.limitVelocityOverLifetime; limit.enabled = false;
        var noise = ps.noise; noise.enabled = true; noise.strength = crown ? 6 : 3; noise.frequency = .025f; noise.scrollSpeed = .12f; noise.quality = ParticleSystemNoiseQuality.Low;
        var columnGradient = new Gradient();
        columnGradient.SetKeys(new[] {new GradientColorKey(new Color(1,.96f,.9f),0),new GradientColorKey(new Color(.45f,.43f,.42f),1)},
            new[] {new GradientAlphaKey(0,0),new GradientAlphaKey(.9f,.04f),new GradientAlphaKey(.7f,.7f),new GradientAlphaKey(0,1)});
        var color = ps.colorOverLifetime; color.enabled = true; color.color = columnGradient;
        ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
    }
    static void ConfigurePressureCloud(ParticleSystem ps)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = false; main.prewarm = false; main.duration = .7f; main.maxParticles = 96;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.2f, 5.5f); main.startSpeed = new ParticleSystem.MinMaxCurve(35, 75); main.startSize = new ParticleSystem.MinMaxCurve(24, 48);
        main.startColor = new Color(.65f,.43f,.27f,.7f); main.gravityModifier = 0;
        var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 42) });
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55; shape.radius = 16;
        ps.transform.rotation = Quaternion.Euler(-90,0,0); ps.transform.localScale = Vector3.one;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0,.4f,1,3));
        var color = ps.colorOverLifetime; color.enabled = true; color.color = FadeGradient(new Color(1,.7f,.4f),new Color(.22f,.21f,.2f),.8f);
    }
    static void ConfigureBombs(ParticleSystem ps)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = true; main.prewarm = false; main.duration = 10; main.maxParticles = 200;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.8f,7); main.startSpeed = new ParticleSystem.MinMaxCurve(45,95);
        main.startSize = new ParticleSystem.MinMaxCurve(.55f,1.5f); main.startColor = new Color(3.5f,1.15f,.16f); main.gravityModifier = 1.35f;
        var emission = ps.emission; emission.rateOverTime = 20; emission.SetBursts(new[] {new ParticleSystem.Burst(0,50)});
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 38; shape.radius = 10;
        ps.transform.rotation = Quaternion.Euler(-90,0,0); ps.transform.localScale = Vector3.one;
        var trails = ps.trails; trails.enabled = true; trails.ratio = .35f; trails.lifetime = .2f; trails.minVertexDistance = .8f;
        ps.GetComponent<ParticleSystemRenderer>().trailMaterial = Resources.Load<Material>("LavaCombat/Flame");
    }
    static ParticleSystem Particles(Transform parent, string name, Material material, int maximum, bool loop)
    {
        var existing = parent.Find(name);
        var go = existing ? existing.gameObject : new GameObject(name);
        go.transform.SetParent(parent, false); var ps = Ensure<ParticleSystem>(go);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = loop; main.loop = loop; main.duration = 12; main.maxParticles = maximum;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.startSpeed = 0;
        var emission = ps.emission; emission.enabled = loop; emission.rateOverTime = 0;
        var shape = ps.shape; shape.enabled = false;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0,1,1,1.7f));
        var color = ps.colorOverLifetime; color.enabled = true; color.color = FadeGradient(Color.white,Color.white,.75f);
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return ps;
    }
    static void ConfigureAsh(ParticleSystem ps)
    {
        var main = ps.main; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(5,9); main.startSize = new ParticleSystem.MinMaxCurve(.025f,.065f); main.startColor = new Color(.65f,.59f,.53f,.38f);
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(48,3,48);
        var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World; velocity.x = .8f; velocity.y = -2.4f; velocity.z = .3f;
        var noise = ps.noise; noise.enabled = true; noise.strength = .5f; noise.frequency = .08f; noise.quality = ParticleSystemNoiseQuality.Low;
        var size = ps.sizeOverLifetime; size.enabled = false;
    }
    static void ConfigureGroundHaze(ParticleSystem ps)
    {
        var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(10,16); main.startSize = new ParticleSystem.MinMaxCurve(9,17); main.startColor = new Color(.30f,.26f,.22f,.08f); main.prewarm = true;
        var emission = ps.emission; emission.rateOverTime = .35f;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(34,.4f,25);
        var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World; velocity.x = .4f; velocity.y = .015f; velocity.z = .12f;
    }
    static Gradient FadeGradient(Color start, Color end, float opacity)
    {
        var g = new Gradient(); g.SetKeys(new[] {new GradientColorKey(start,0),new GradientColorKey(end,1)},new[] {new GradientAlphaKey(0,0),new GradientAlphaKey(opacity,.12f),new GradientAlphaKey(opacity*.6f,.65f),new GradientAlphaKey(0,1)}); return g;
    }
    static void AddFade(RescueGameFlow flow)
    {
        var go = GameObject.Find("Ashfall scene curtain") ?? new GameObject("Ashfall scene curtain", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
        Ensure<GraphicRaycaster>(go);
        var scaler = Ensure<CanvasScaler>(go); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080);
        var group = go.GetComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
        var child = go.transform.Find("Black curtain");
        var image = child ? child.GetComponent<Image>() : new GameObject("Black curtain", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(go.transform, false); image.color = Color.black;
        image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one; image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        var fade = Ensure<AshfallSceneFade>(go); fade.curtain = group; fade.canvas = canvas; fade.flow = flow; Dirty(fade);
    }
    static Volume Volume(Transform parent, string name, VolumeProfile profile, int priority, float weight)
    {
        var child = parent.Find(name); if (!child) { child = new GameObject(name).transform; child.SetParent(parent, false); }
        var volume = Ensure<Volume>(child.gameObject); volume.isGlobal = true; volume.priority = priority; volume.weight = weight; volume.sharedProfile = profile; Dirty(volume); return volume;
    }
    static VolumeProfile Profile(string name)
    {
        var asset = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Materials + name);
        if (!asset) { asset = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(asset, Materials + name); } return asset;
    }
    static T Component<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var value)) return value;
        value = profile.Add<T>(); AssetDatabase.AddObjectToAsset(value,profile); return value;
    }
    static Material Material(string name, Material source)
    {
        var asset = AssetDatabase.LoadAssetAtPath<Material>(Materials + name);
        if (!asset) { asset = new Material(source); AssetDatabase.CreateAsset(asset, Materials + name); } return asset;
    }
    static T Ensure<T>(GameObject go) where T : UnityEngine.Component
    {
        var component = go.GetComponent<T>();
        return component ? component : go.AddComponent<T>();
    }
    static void Dirty(Object value) { EditorUtility.SetDirty(value); if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
    [MenuItem("GameJam/Final Loop/Open volcano polish scene")]
    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before opening the polish review.");
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity");
        Audit();
    }
    [MenuItem("GameJam/Final Loop/Audit eruption polish")]
    public static void Audit()
    {
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder();
        report.AppendLine($"Scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path} play={EditorApplication.isPlaying}");
        report.AppendLine($"Fog={RenderSettings.fog} mode={RenderSettings.fogMode} density={RenderSettings.fogDensity} start={RenderSettings.fogStartDistance} end={RenderSettings.fogEndDistance} color={RenderSettings.fogColor}");
        report.AppendLine($"Ambient={RenderSettings.ambientMode} {RenderSettings.ambientIntensity} exposure={RenderSettings.reflectionIntensity} sky={RenderSettings.skybox}");
        foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            report.AppendLine($"VOLUME {volume.name} enabled={volume.enabled} active={volume.gameObject.activeSelf} global={volume.isGlobal} priority={volume.priority} weight={volume.weight} profile={AssetDatabase.GetAssetPath(volume.sharedProfile)}");
            if (volume.sharedProfile) foreach (var component in volume.sharedProfile.components)
                report.AppendLine(component.GetType().Name + " " + EditorJsonUtility.ToJson(component));
        }
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        report.AppendLine($"SEQUENCE routes={sequence.routes.Length} warning={sequence.warningSeconds} lavaRelease={sequence.lavaReleaseDelay}");
        if (sequence)
        {
            report.AppendLine("CINEMATIC " + EditorJsonUtility.ToJson(sequence.cinematic));
            report.AppendLine("EFFECTS " + EditorJsonUtility.ToJson(sequence.effects));
            report.AppendLine($"Crater={sequence.cinematic.crater.position} player={sequence.cinematic.player.transform.position}");
            foreach (var particles in new[] { sequence.effects.eruptionBurst, sequence.effects.lavaBombs, sequence.effects.smokePlume, sequence.effects.ashCrown }.Where(p => p).SelectMany(p => p.GetComponentsInChildren<ParticleSystem>(true)).Distinct())
            {
                var main = particles.main; var emission = particles.emission; var renderer = particles.GetComponent<ParticleSystemRenderer>();
                report.AppendLine($"PARTICLES {particles.name} world={particles.transform.position} scale={particles.transform.lossyScale} loop={main.loop} duration={main.duration} max={main.maxParticles} life={main.startLifetime.constantMin}-{main.startLifetime.constantMax} speed={main.startSpeed.constantMin}-{main.startSpeed.constantMax} size={main.startSize.constantMin}-{main.startSize.constantMax} rate={emission.rateOverTimeMultiplier} bursts={emission.burstCount} material={renderer.sharedMaterial} shader={renderer.sharedMaterial?.shader.name} source={PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(particles)}");
            }
        }
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"CAMERA {camera.name} active={camera.enabled} post={camera.GetUniversalAdditionalCameraData().renderPostProcessing} layers={camera.GetUniversalAdditionalCameraData().volumeLayerMask.value}");
        File.WriteAllText(Folder + "/audit.txt", report.ToString());
    }
}
