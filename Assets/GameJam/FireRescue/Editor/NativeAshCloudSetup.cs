using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class NativeAshCloudSetup
{
    public const string Folder = "Logs/NativeAshCloud";
    static double nextPoll;
    static NativeAshCloudSetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string path = "Logs/NativeAshCloud.command";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim();
        // Other reviews can share this editor. Leave our request queued until their Play Mode ends.
        if ((command == "checks" || command == "refresh") && EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(path);
        try
        {
            if (command == "audit") Audit();
            else if (command == "apply") Apply();
            else if (command == "checks") NativeAshCloudChecks.Begin();
            else if (command == "refresh") AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            else throw new InvalidOperationException("Unknown native ash cloud command: " + command);
            File.WriteAllText("Logs/NativeAshCloud-result.txt", "OK " + command);
        }
        catch (Exception ex) { File.WriteAllText("Logs/NativeAshCloud-result.txt", ex.ToString()); Debug.LogException(ex); }
    }
    [MenuItem("GameJam/Final Loop/Animate existing ash clouds")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before configuring existing clouds.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity") throw new InvalidOperationException("Open Volcano_Demo first.");
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var atmosphere = Object.FindFirstObjectByType<VolcanoAtmospherePolish>();
        var flow = Object.FindFirstObjectByType<RescueGameFlow>();
        var fx = sequence.effects;
        var all = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int before = all.Length;
        const string nativeFolder = "Assets/BK/PureNature_Volcano/Prefabs/Fx/";
        var clouds = all.Where(p => p != fx.smokePlume && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p) == nativeFolder + "VolcanoSmoke.prefab")
            .OrderBy(p => p.name, StringComparer.Ordinal).ToArray();
        var ashes = all.Single(p => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p) == nativeFolder + "Ashes.prefab");
        if (clouds.Length != 3) throw new InvalidOperationException("Expected the three existing ambient smoke clouds; found " + clouds.Length);
        var town = new Bounds(flow.targetBuildings.First(h => h).transform.position, Vector3.zero);
        foreach (var house in flow.targetBuildings) if (house) town.Encapsulate(house.ignitionBounds ? house.ignitionBounds.bounds : new Bounds(house.transform.position, Vector3.one * 20));
        var townGround = new Vector3(town.center.x, flow.targetBuildings.Where(h => h).Average(h => h.transform.position.y), town.center.z);
        fx.driftingAshClouds = new VolcanoEruptionEffects.DriftingAshCloud[clouds.Length];
        for (int i = 0; i < clouds.Length; i++)
        {
            var cloud = clouds[i]; Undo.RecordObject(cloud, "Animate native ash cloud"); Undo.RecordObject(cloud.transform, "Move native ash cloud");
            ConfigureCloud(cloud);
            var motion = new VolcanoEruptionEffects.DriftingAshCloud {
                particles = cloud, eruptionPosition = fx.transform.position + new Vector3((i - 1) * 95, 60 + i * 35, 20 + i * 15),
                villagePosition = townGround + new Vector3((i - 1) * 90, 46 + i * 20, 50 - i * 60), delay = 5 + i * 6, travelSeconds = 75 + i * 8
            };
            fx.driftingAshClouds[i] = motion; cloud.transform.position = motion.eruptionPosition;
            cloud.gameObject.SetActive(true); Dirty(cloud); Dirty(cloud.transform);
        }
        if (atmosphere.fallingAsh && atmosphere.fallingAsh != ashes)
        { atmosphere.fallingAsh.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); atmosphere.fallingAsh.gameObject.SetActive(false); }
        foreach (var haze in atmosphere.groundHaze) if (haze)
        { haze.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); haze.gameObject.SetActive(false); }
        atmosphere.groundHaze = Array.Empty<ParticleSystem>();
        Undo.SetTransformParent(ashes.transform, atmosphere.transform, "Use native falling ashes");
        ConfigureAshes(ashes, town.size);
        atmosphere.fallingAsh = ashes; atmosphere.anchorAshToVillage = true;
        atmosphere.villageAshOrigin = townGround + Vector3.up * 48; ashes.transform.position = atmosphere.villageAshOrigin;
        ashes.gameObject.SetActive(true); atmosphere.ashParticlesPerSecond = 26;
        sequence.ambientEffects = sequence.ambientEffects.Where(a => a != null && !clouds.Contains(a.particles) && a.particles != ashes).ToArray();
        sequence.suppressedLegacyEffects = sequence.suppressedLegacyEffects.Where(p => !clouds.Contains(p) && p != ashes).ToArray();
        Dirty(ashes); Dirty(ashes.transform); Dirty(atmosphere); Dirty(fx); Dirty(sequence);
        if (before != Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length)
            throw new InvalidOperationException("Cloud animation must reuse the existing particle systems.");
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/setup.txt", $"Reused 3 native VolcanoSmoke instances and the native Ashes instance. Particle systems before={before}, after={before}. No visual assets created. Town={townGround}.\n");
        Audit();
    }
    static void ConfigureCloud(ParticleSystem ps)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = true; main.prewarm = false; main.duration = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Shape;
        main.useUnscaledTime = false; main.maxParticles = 64; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.startLifetime = new ParticleSystem.MinMaxCurve(28, 38); main.startSpeed = 0;
        main.startSize = new ParticleSystem.MinMaxCurve(140, 210); main.startColor = new Color(.62f, .56f, .49f, .28f); main.gravityModifier = 0;
        var emission = ps.emission; emission.enabled = true; emission.rateOverTime = .85f; emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(150, 20, 95); shape.position = shape.rotation = Vector3.zero;
        var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = .2f; velocity.y = -1.4f; velocity.z = -.35f;
        var force = ps.forceOverLifetime; force.enabled = false;
        var size = ps.sizeOverLifetime; size.enabled = true; size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .7f, 1, 1.35f));
        var noise = ps.noise; noise.enabled = true; noise.strength = .7f; noise.frequency = .012f; noise.scrollSpeed = .07f; noise.quality = ParticleSystemNoiseQuality.Low;
        ps.transform.rotation = Quaternion.identity; ps.transform.localScale = Vector3.one;
        PrefabUtility.RecordPrefabInstancePropertyModifications(ps); PrefabUtility.RecordPrefabInstancePropertyModifications(ps.transform);
    }
    static void ConfigureAshes(ParticleSystem ps, Vector3 townSize)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = true; main.prewarm = false;
        main.useUnscaledTime = false; main.maxParticles = 900; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.simulationSpeed = 1; main.startLifetime = new ParticleSystem.MinMaxCurve(28, 32); main.startSpeed = 0;
        main.startSize = new ParticleSystem.MinMaxCurve(.04f, .1f); main.startColor = new Color(.72f, .67f, .61f, .5f);
        main.gravityModifier = 0;
        var emission = ps.emission; emission.rateOverTime = 0; emission.enabled = true;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(townSize.x + 50, 3, townSize.z + 50); shape.position = shape.rotation = Vector3.zero;
        var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = .3f; velocity.y = -1.9f; velocity.z = -.45f; velocity.speedModifier = 1;
        var noise = ps.noise; noise.enabled = true; noise.separateAxes = true;
        noise.strengthX = .65f; noise.strengthY = .12f; noise.strengthZ = .65f;
        noise.frequency = .15f; noise.scrollSpeed = .18f; noise.quality = ParticleSystemNoiseQuality.Low;
        var force = ps.forceOverLifetime; force.enabled = false;
        var collision = ps.collision; collision.enabled = true; collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D; collision.quality = ParticleSystemCollisionQuality.Low;
        collision.enableDynamicColliders = false; collision.bounce = 0; collision.dampen = 1; collision.lifetimeLoss = 1;
        ps.transform.rotation = Quaternion.identity; ps.transform.localScale = Vector3.one;
        PrefabUtility.RecordPrefabInstancePropertyModifications(ps); PrefabUtility.RecordPrefabInstancePropertyModifications(ps.transform);
    }
    static void Dirty(Object value) { if (value) { EditorUtility.SetDirty(value); PrefabUtility.RecordPrefabInstancePropertyModifications(value); } }
    [MenuItem("GameJam/Final Loop/Audit native ash clouds")]
    public static void Audit()
    {
        Directory.CreateDirectory(Folder); var report = new StringBuilder();
        var scene = SceneManager.GetActiveScene();
        report.AppendLine($"Scene={scene.path} dirty={scene.isDirty} play={EditorApplication.isPlaying}");
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        if (sequence) report.AppendLine($"CRATER {sequence.effects.transform.position} stage={sequence.Stage} initialized={sequence.Initialized}");
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"HOUSE {house.name} world={house.transform.position} bounds={house.ignitionBounds?.bounds}");
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(p => Path(p.transform)))
        {
            var main = ps.main; var shape = ps.shape; var velocity = ps.velocityOverLifetime;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var fx = sequence ? sequence.effects : null;
            string owner = !sequence ? "none" : sequence.suppressedLegacyEffects.Contains(ps) ? "suppressed" :
                sequence.ambientEffects.Any(a => a.particles == ps) ? "ambient" :
                fx && (ps == fx.eruptionBurst || ps == fx.smokePlume || ps == fx.ashCrown || ps == fx.fireFountain || ps == fx.lavaBombs) ? "eruption" : "other";
            report.AppendLine($"PARTICLE {Path(ps.transform)} world={ps.transform.position} scale={ps.transform.lossyScale} active={ps.gameObject.activeSelf} owner={owner} source={PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ps)} material={AssetDatabase.GetAssetPath(renderer.sharedMaterial)} " +
                $"life={main.startLifetime.constantMin}-{main.startLifetime.constantMax} size={main.startSize.constantMin}-{main.startSize.constantMax} speed={main.startSpeed.constantMin}-{main.startSpeed.constantMax} rate={ps.emission.rateOverTimeMultiplier} max={main.maxParticles} loop={main.loop} prewarm={main.prewarm} space={main.simulationSpace} shape={shape.shapeType}/{shape.scale} velocity={velocity.enabled}/{velocity.x.constant},{velocity.y.constant},{velocity.z.constant} bounds={renderer.bounds}");
        }
        File.WriteAllText(Folder + "/audit.txt", report.ToString());
    }
    static string Path(Transform transform) => transform.parent ? Path(transform.parent) + "/" + transform.name : transform.name;
}
