using System;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VolcanoMissionSetup
{
    public const string Folder = "Library/MissionFlowReview";
    static double next;
    static VolcanoMissionSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        string path = Folder + "/request.txt";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "configure") Configure();
            else if (command == "refresh") AssetDatabase.Refresh();
            else if (command == "play") EditorApplication.EnterPlaymode();
            else if (command == "stop") EditorApplication.ExitPlaymode();
            else if (command == "preview") VolcanoCinematicChecks.Begin();
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/error.txt", ex.ToString()); }
    }
    public static void Inspect()
    {
        Directory.CreateDirectory(Folder);
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        var effects = sequence.effects;
        string data = "playing=" + EditorApplication.isPlaying + "; scene=" + SceneManager.GetActiveScene().path + "; dirty=" + SceneManager.GetActiveScene().isDirty +
            "\neffects=" + effects.transform.position + "; eruption=" + sequence.Stage + "\n";
        foreach (var fx in effects.GetComponentsInChildren<ParticleSystem>())
            data += fx.name + " pos=" + fx.transform.position + " scale=" + fx.transform.lossyScale + " rate=" + fx.emission.rateOverTimeMultiplier +
                " speed=" + fx.main.startSpeedMultiplier + " size=" + fx.main.startSizeMultiplier + " life=" + fx.main.startLifetimeMultiplier + " material=" + AssetDatabase.GetAssetPath(fx.GetComponent<Renderer>().sharedMaterial) + "\n";
        foreach (var source in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsSortMode.None))
            data += "LAVA " + source.name + " pos=" + source.transform.position + " bounds=" + (source.heatCollider ? source.heatCollider.bounds.ToString() : "null") + "\n";
        data += "safe=" + mission.RescuedCount + "/" + mission.TotalCount + " opened=" + mission.ShelterOpened + "\n";
        foreach (var r in mission.residents) data += r.name + " " + r.State + " pos=" + r.transform.position + " slot=" + r.safePlace.position + "\n";
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ps);
            if (!prefab.Contains("PureNature_Volcano/Prefabs/Fx")) continue;
            data += "NATIVE " + ps.name + " prefab=" + prefab + " pos=" + ps.transform.position + " rotation=" + ps.transform.eulerAngles + " scale=" + ps.transform.lossyScale +
                " rate=" + ps.emission.rateOverTimeMultiplier + " speed=" + ps.main.startSpeedMultiplier + " size=" + ps.main.startSizeMultiplier + " life=" + ps.main.startLifetimeMultiplier + "\n";
        }
        File.WriteAllText(Folder + "/status.txt", data);
    }
    [MenuItem("GameJam/Mission/Configure ordered volcano mission")]
    public static void Configure()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != VolcanoTeamIntegrationSetup.VolcanoPath) throw new InvalidOperationException("Open Volcano_Demo in Edit Mode.");
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var player = mission.player;
        Undo.RegisterFullObjectHierarchyUndo(mission.gameObject, "Fix evacuation and mission order");
        Undo.RecordObject(sequence, "Ordered eruption"); Undo.RecordObject(player.hose, "Start with empty water tank");
        var area = mission.shelterSafeArea;
        if (!area)
        {
            var go = new GameObject("Shelter safe interior"); go.transform.SetParent(mission.transform, false);
            go.transform.position = new Vector3(-120.3f, 3.3f, -564.2f);
            area = go.AddComponent<BoxCollider>(); area.size = new Vector3(14, 7, 13); area.isTrigger = true;
            mission.shelterSafeArea = area;
        }
        var director = mission.GetComponent<VolcanoMissionDirector>(); if (!director) director = mission.gameObject.AddComponent<VolcanoMissionDirector>();
        var cinema = sequence.GetComponent<VolcanoCinematic>(); if (!cinema) cinema = sequence.gameObject.AddComponent<VolcanoCinematic>();
        director.evacuation = mission; director.sequence = sequence; director.player = player; director.tank = player.hose; director.cinematic = cinema;
        sequence.missionDirector = director; sequence.cinematic = cinema; sequence.autoErupt = false; sequence.showHud = false;
        sequence.warningSeconds = 3.5f; sequence.lavaReleaseDelay = VolcanoCinematic.Duration;
        cinema.player = player; cinema.crater = sequence.effects.transform;
        cinema.startOffset = new Vector3(500, 480, -900); cinema.endOffset = new Vector3(380, 420, -780);
        cinema.eruptionSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameJam/FireRescue/Audio/VolcanoEruption.wav");
        player.hose.startingTankLitres = 0;
        foreach (var pool in sequence.pools)
        {
            pool.beginsWithEruption = pool.source && !pool.feed &&
                (pool.source.transform.position - sequence.effects.transform.position).sqrMagnitude < 10000;
            if (pool.beginsWithEruption) { pool.startDelay = 0; pool.fillSeconds = 2; }
        }
        if (!player.GetComponent<LavaPlayerContact>()) player.gameObject.AddComponent<LavaPlayerContact>();
        foreach (var source in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(source, "Local lava cooling");
            var cooling = source.GetComponent<LavaCoolingSurface>(); if (!cooling) cooling = source.gameObject.AddComponent<LavaCoolingSurface>();
            cooling.steamMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameJam/FireRescue/Resources/LavaCombat/Smoke.mat");
            cooling.crustMaterial = AssetDatabase.LoadAssetAtPath<Material>(ObsidianReloadSetup.CrustPath);
            cooling.litresPerPatch = 90f;
            var flow = source.GetComponent<ProgressiveLavaFlow>(); if (flow && flow.conformToTerrain) flow.groundClearance = flow.name.StartsWith("Town Branch") ? .24f : .65f;
            if (flow) flow.canBeStoppedByWater = flow.name.StartsWith("Town Branch");
            EditorUtility.SetDirty(source); EditorUtility.SetDirty(cooling); if (flow) EditorUtility.SetDirty(flow);
        }
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None))
        { Undo.RecordObject(house, "Timed house fires"); house.secondsToCollapse = 90; house.extinguishedCooldownSeconds = 18; house.wetProtectionSeconds = 4; EditorUtility.SetDirty(house); }
        PolishEruption(sequence.effects);
        EditorUtility.SetDirty(mission); EditorUtility.SetDirty(director); EditorUtility.SetDirty(cinema); EditorUtility.SetDirty(sequence); EditorUtility.SetDirty(player.hose);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets(); Inspect();
        UpdateHelp();
    }
    static void PolishEruption(VolcanoEruptionEffects fx)
    {
        // Use the scene's original Pure Nature prefab instances and their authored textures/modules.
        var native = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        ParticleSystem Nearest(string prefab) => native.Where(p => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p) ==
            "Assets/BK/PureNature_Volcano/Prefabs/Fx/" + prefab).OrderBy(p => (p.transform.position - fx.transform.position).sqrMagnitude).First();
        var smoke = Nearest("VolcanoSmoke.prefab");
        var eruption = Nearest("VolcanoSmoke2.prefab");
        var sparks = Nearest("Lava_sparkles.prefab");
        Undo.RecordObject(fx, "Use native volcano eruption effects");
        foreach (var component in fx.GetComponents<MonoBehaviour>().Where(c => c && c.GetType().Name == "VolcanoEruptionSpectacle").ToArray())
            Undo.DestroyObjectImmediate(component);
        foreach (var particle in fx.GetComponentsInChildren<ParticleSystem>(true))
            if (particle && particle != smoke && particle != eruption && particle != sparks) Undo.DestroyObjectImmediate(particle.gameObject);
        var extraLight = fx.transform.Find("Sustained crater glow"); if (extraLight) Undo.DestroyObjectImmediate(extraLight.gameObject);
        fx.smokePlume = smoke; fx.eruptionBurst = eruption; fx.lavaBombs = sparks;
        fx.intenseBombSeconds = 10; fx.bombFadeSeconds = 7; fx.sustainedBombFraction = .3f; fx.flashSeconds = 1.4f;
        foreach (var ps in new[] { smoke, eruption, sparks })
        {
            ps.gameObject.SetActive(true);
            var main = ps.main; main.playOnAwake = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ps); EditorUtility.SetDirty(ps);
        }
        var sequence = fx.GetComponentInParent<VolcanoEruptionSequence>();
        sequence.ambientEffects = sequence.ambientEffects.Where(e => e.particles != smoke && e.particles != eruption && e.particles != sparks).ToArray();
        sequence.cinematic.crater = fx.transform;
        EditorUtility.SetDirty(fx); EditorUtility.SetDirty(sequence.cinematic);
    }
    static void UpdateHelp()
    {
        const string path = "Assets/Scenes/MainMenu.unity";
        var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        bool changed = false;
        foreach (var root in scene.GetRootGameObjects()) foreach (var text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            string value = text.text;
            value = value.Replace("Tahliyeden sonra volkan patlar. İtfaiyenin deposunu gölden doldur ve evleri koru.", "Sonra itfaiyenin deposunu gölden doldur. 10 saniyelik patlama sahnesinden sonra alev ruhlarını durdur. Yanan evleri 90 saniye içinde söndür; yaklaşan lavları suyla soğut. Sıcak lava girersen canın azalır.");
            value = value.Replace("Araçtayken sağ tıkla evlere veya lav yaratıklarına su sık.", "Araçtayken sağ tıkla evleri söndür, lavları soğut ve ruhlara su sık.");
            if (value != text.text) { text.text = value; EditorUtility.SetDirty(text); changed = true; }
            if (value.StartsWith("Önce evlere yaklaş"))
            {
                var layout = text.transform.parent.GetComponent<UnityEngine.UI.LayoutElement>();
                if (layout && layout.preferredHeight < 138) { layout.preferredHeight = layout.minHeight = 138; EditorUtility.SetDirty(layout); changed = true; }
            }
        }
        if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }
}
