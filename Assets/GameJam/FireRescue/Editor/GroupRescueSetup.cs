using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class GroupRescueSetup
{
    public const string Folder = "Logs/GroupRescue";
    public const string Delivery = "C:/Users/omanc/Documents/ChatGPT/game jam/Artifacts/GroupRescue";
    const string AssetsFolder = "Assets/GameJam/FireRescue/GroupRescue";
    static double nextPoll;
    static GroupRescueSetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string request = "Logs/GroupRescue.command";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try
        {
            if (command == "apply") Apply();
            else if (command == "audit") Audit();
            else if (command == "checks") GroupRescueChecks.Begin();
            else if (command == "refresh") AssetDatabase.Refresh();
            else throw new InvalidOperationException(command);
            File.WriteAllText("Logs/GroupRescue-result.txt", "OK " + command);
        }
        catch (Exception ex) { File.WriteAllText("Logs/GroupRescue-result.txt", ex.ToString()); Debug.LogException(ex); }
    }

    [MenuItem("GameJam/Final Loop/Apply small group rescue")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        if (!mission || mission.gameObject.scene.path != FinalLoopSetup.ScenePath) throw new InvalidOperationException("Open Volcano_Demo.");
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(Delivery + "/Before");
        string snapshot = Delivery + "/Before/Scene-in-editor.unity";
        if (!File.Exists(snapshot)) EditorSceneManager.SaveScene(mission.gameObject.scene, snapshot, true);
        Undo.SetCurrentGroupName("Voiced group rescue and next group marker");
        Undo.RecordObject(mission, "Configure staged evacuation");
        mission.groupRescue = true; mission.alarmTriggersPanic = true;
        mission.callAllAlarmedVillagers = false; mission.villageWideCall = false;
        mission.callKey = UnityEngine.InputSystem.Key.None;
        mission.groupInteractionDistance = 6f; mission.assemblyInteractionDistance = 10f; mission.assemblyGroupDistance = 17f;
        var root = mission.transform.Find("Group rescue destinations");
        if (!root) { root = new GameObject("Group rescue destinations").transform; root.SetParent(mission.transform, false); Undo.RegisterCreatedObjectUndo(root.gameObject, "Rescue destinations"); }
        var anchors = new[] { Point(root, "Panicked group 1 - pair", new Vector3(62, 0, -600)),
            Point(root, "Panicked group 2 - pair", new Vector3(79, 0, -543)),
            Point(root, "Panicked group 3 - three", new Vector3(107, 0, -625)) };
        mission.assemblyPoint = Point(root, "Village assembly point", new Vector3(58, 0, -578));
        mission.assemblySlots = new Transform[7];
        int[] groups = { 0, 1, 0, 1, 2, 2, 2 };
        int[] member = new int[3];
        var residents = mission.residents.OrderBy(r => r.formationIndex).ToArray();
        if (residents.Length != 7) throw new InvalidOperationException("Expected the existing seven villagers.");
        for (int i = 0; i < residents.Length; i++)
        {
            var r = residents[i]; Undo.RecordObject(r, "Assign rescue group");
            int g = groups[i], m = member[g]++;
            r.rescueGroup = g; r.panicGroupAnchor = anchors[g];
            r.panicGroupOffset = new Vector3((m - (g == 2 ? 1f : .5f)) * 2.8f, 0, 0);
            r.reactionSeconds = .45f + i * .09f;
            var offset = i < 4 ? new Vector3(-4.8f + i * 3.2f, 0, -2.4f) : new Vector3(-3.2f + (i - 4) * 3.2f, 0, 1.6f);
            mission.assemblySlots[r.formationIndex] = Point(root, "Assembly waiting place " + (i + 1), mission.assemblyPoint.position + offset);
            mission.assemblySlots[r.formationIndex].rotation = Quaternion.LookRotation(Vector3.left);
            Dirty(r);
        }
        ConfigureDialogue(mission);
        var ui = mission.player.contextualInteractions;
        if (!ui) throw new InvalidOperationException("Existing interaction UI is missing.");
        Undo.RecordObject(ui, "Connect assembly interaction");
        var targets = ui.targets.Where(t => t).ToList();
        var assembly = targets.FirstOrDefault(t => t.kind == ContextualInteractionKind.AssemblyPoint);
        if (!assembly)
        {
            var go = new GameObject("Assembly point interaction"); go.transform.SetParent(root, false);
            assembly = Undo.AddComponent<ContextualInteractionTarget>(go); targets.Add(assembly);
        }
        assembly.kind = ContextualInteractionKind.AssemblyPoint;
        assembly.anchor = mission.assemblyPoint; assembly.localPoint = Vector3.up * 3f;
        assembly.maximumViewDistance = 24; assembly.maximumLookAngle = 70; assembly.priority = 1f;
        foreach (var target in targets.Where(t => t.kind == ContextualInteractionKind.Resident))
        { Undo.RecordObject(target, "Configure local group interaction"); target.maximumViewDistance = 10; target.maximumLookAngle = 65; target.priority = .7f; Dirty(target); }
        ui.targets = targets.ToArray(); Dirty(assembly); Dirty(ui);
        if (ui.words)
        {
            Undo.RecordObject(ui.words, "Update rescue instructions");
            ui.words.callVillagers = "Grubu yanına al"; ui.words.leadToShelter = "Grubu sarı toplanma alanına getir";
            Dirty(ui.words);
        }
        BuildAssemblyMarker(mission, root);
        BuildGroupMarker(mission, root);
        var director = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        if (director)
        {
            Undo.RecordObject(director, "Restore two-minute eruption countdown");
            director.preparationSeconds = 120f; Dirty(director);
        }
        Dirty(mission);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(mission.gameObject.scene); EditorSceneManager.SaveScene(mission.gameObject.scene);
        UpdateMenuHelp();
        if (mission.gameObject.scene.isDirty) EditorSceneManager.SaveScene(mission.gameObject.scene);
        Audit();
    }

    static void ConfigureDialogue(EvacuationMission mission)
    {
        var dialogue = mission.GetComponent<RescueDialogue>();
        if (!dialogue) dialogue = Undo.AddComponent<RescueDialogue>(mission.gameObject);
        Undo.RecordObject(dialogue, "Configure recorded rescue dialogue");
        dialogue.mission = mission;
        if (dialogue.lines == null || dialogue.lines.Length == 0) dialogue.lines = RescueDialogue.CreateDefaultLines();
        dialogue.LoadVoiceClips();
        mission.dialogue = dialogue; Dirty(dialogue);
        var crowdClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/crowdpanic.mp3");
        if (!crowdClip) throw new InvalidOperationException("Original crowd panic ambience is missing.");
        var crowd = mission.transform.Find("Crowd Panic Ambience");
        if (!crowd) { crowd = new GameObject("Crowd Panic Ambience").transform; crowd.SetParent(mission.transform, false); }
        var crowdSource = crowd.GetComponent<AudioSource>();
        if (!crowdSource) crowdSource = Undo.AddComponent<AudioSource>(crowd.gameObject);
        Undo.RecordObject(crowdSource, "Restore crowd panic ambience");
        crowdSource.enabled = true; crowdSource.clip = crowdClip; crowdSource.loop = true; crowdSource.playOnAwake = false;
        Dirty(crowdSource);
        var serialized = new SerializedObject(mission);
        serialized.FindProperty("crowdPanicLoop").objectReferenceValue = crowdClip;
        serialized.FindProperty("crowdPanicVolume").floatValue = .45f;
        serialized.FindProperty("crowdFadeSpeed").floatValue = 1.2f;
        serialized.FindProperty("crowdSource").objectReferenceValue = crowdSource;
        serialized.ApplyModifiedProperties();
        foreach (var ending in Object.FindObjectsByType<ShelterReturnCinematic>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(ending, "Preserve recorded shelter return narration");
            ending.LoadVoiceClips();
            Dirty(ending);
        }
    }


    static void BuildGroupMarker(EvacuationMission mission, Transform parent)
    {
        var root = parent.Find("Next rescue group marker");
        if (!root) { root = new GameObject("Next rescue group marker").transform; root.SetParent(parent, false); Undo.RegisterCreatedObjectUndo(root.gameObject, "Next rescue group marker"); }
        var marker = root.GetComponent<RescueGroupMarker>();
        if (!marker) marker = Undo.AddComponent<RescueGroupMarker>(root.gameObject);
        marker.mission = mission;
        root.position = mission.residents[0].panicGroupAnchor.position + Vector3.up * marker.height;
        var symbol = root.GetComponentInChildren<TextMesh>(true);
        if (!symbol) { var go = new GameObject("Exclamation"); go.transform.SetParent(root, false); symbol = go.AddComponent<TextMesh>(); }
        symbol.transform.localPosition = Vector3.zero; symbol.transform.localRotation = Quaternion.identity;
        symbol.text = "!"; symbol.anchor = TextAnchor.MiddleCenter; symbol.alignment = TextAlignment.Center;
        symbol.fontSize = 64; symbol.characterSize = .32f; symbol.color = new Color(1f, .8f, .12f);
        symbol.font = Ashfall.UI.AshfallTypography.Semibold;
        symbol.GetComponent<MeshRenderer>().sharedMaterial = symbol.font.material;
        marker.symbol = symbol;
        string path = AssetsFolder + "/Group marker dark.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.color = new Color(.025f, .035f, .04f); AssetDatabase.CreateAsset(material, path); }
        var plate = root.Find("Marker background");
        if (!plate)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Marker background"; go.transform.SetParent(root, false);
            Object.DestroyImmediate(go.GetComponent<Collider>()); plate = go.transform;
        }
        plate.localPosition = new Vector3(0, 0, .05f); plate.localRotation = Quaternion.identity; plate.localScale = new Vector3(1.35f, 2.1f, 1);
        marker.backdrop = plate.GetComponent<Renderer>(); marker.backdrop.sharedMaterial = material;
        marker.backdrop.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        symbol.gameObject.SetActive(false); marker.backdrop.enabled = false;
        Dirty(marker); Dirty(symbol); Dirty(marker.backdrop);
    }

    static void UpdateMenuHelp()
    {
        const string path = "Assets/Scenes/MainMenu.unity";
        var menu = SceneManager.GetSceneByPath(path);
        bool opened = !menu.IsValid() || !menu.isLoaded;
        if (opened) menu = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        bool changed = false;
        foreach (var root in menu.GetRootGameObjects())
            foreach (var text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                string before = text.text, after = before;
                after = after.Replace("Call villagers with Q and lead them to the GREEN shelter ring.", "Use E near each group of 2–3 villagers. Lead them to the AMBER assembly area and press E to tell them to wait. Once all seven are there, press E and lead everyone to the GREEN shelter.")
                    .Replace("Call out near the houses and lead the villagers to shelter.", "E near a group: follow. E at the amber assembly area: wait. All seven assembled: E to depart for the shelter.")
                    .Replace("Q ile köylüleri çağır; YEŞİL halkalı sığınağa götür.", "E ile 2–3 kişilik grupları al, SARI toplanma alanında E ile beklet. Herkes gelince E ile hep birlikte YEŞİL sığınağa götür.")
                    .Replace("Approach the frightened groups of 2, 2 and 3 outside. Press E to earn their trust and lead each group to the AMBER assembly area. Press E there to tell them to wait. Once all seven have assembled, press E to lead everyone to the GREEN shelter.", "Follow the ! marker to collect groups of 2, 2 and 3 with E. Bring each group to the AMBER assembly area and press E to leave them there. The ! then moves to the next group. Once all seven are assembled, press E at the assembly area and lead everyone to the GREEN shelter.")
                    .Replace("Use E near each group of 2–3 villagers. Lead them to the AMBER assembly area and press E to tell them to wait. Once all seven are there, press E and lead everyone to the GREEN shelter.", "Follow ! and press E to collect the next group. Bring them to the AMBER assembly area and press E to leave them there. Repeat for all seven, then press E and lead everyone to the GREEN shelter.")
                    .Replace("Collect a nearby group. E at the amber assembly area: wait. When everyone is there, E: depart for the shelter.", "Follow ! and press E to collect the next group. E at the amber assembly area leaves them there. All seven assembled: E to lead everyone to shelter.")
                    .Replace("E ile 2–3 kişilik grupları al, SARI toplanma alanında E ile beklet. Herkes gelince E ile hep birlikte YEŞİL sığınağa götür.", "! işaretli grubu E ile yanına al. SARI toplanma alanına getirip E ile bırak. Ünlem sıradaki gruba geçer. Herkes toplanınca E ile yanına alıp YEŞİL sığınağa götür.")
                    .Replace("120 seconds", "240 seconds").Replace("120 saniye", "240 saniye");
                if (text.gameObject.name.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0 && before.Trim() == "Q") after = "E";
                if (after == before) continue;
                Undo.RecordObject(text, "Update group rescue help"); text.text = after; Dirty(text); changed = true;
            }
        if (changed) { EditorSceneManager.MarkSceneDirty(menu); EditorSceneManager.SaveScene(menu); }
        if (opened) EditorSceneManager.CloseScene(menu, true);
    }

    static void BuildAssemblyMarker(EvacuationMission mission, Transform parent)
    {
        if (!AssetDatabase.IsValidFolder(AssetsFolder)) AssetDatabase.CreateFolder("Assets/GameJam/FireRescue", "GroupRescue");
        string path = AssetsFolder + "/Assembly amber.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); mat.name = "Assembly amber"; mat.color = new Color(1, .66f, .12f); AssetDatabase.CreateAsset(mat, path); }
        var markerRoot = parent.Find("Assembly area visual");
        if (!markerRoot) { markerRoot = new GameObject("Assembly area visual").transform; markerRoot.SetParent(parent, false); Undo.RegisterCreatedObjectUndo(markerRoot.gameObject, "Assembly area marker"); }
        var marker = markerRoot.GetComponent<AssemblyPointMarker>();
        if (!marker) marker = Undo.AddComponent<AssemblyPointMarker>(markerRoot.gameObject);
        marker.mission = mission;
        var line = markerRoot.GetComponent<LineRenderer>();
        if (!line) line = Undo.AddComponent<LineRenderer>(markerRoot.gameObject);
        line.useWorldSpace = true; line.loop = true; line.widthMultiplier = .2f; line.sharedMaterial = mat;
        line.positionCount = 96; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 96; i++)
        {
            float a = i * Mathf.PI * 2f / 96;
            Vector3 p = mission.assemblyPoint.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 8.4f;
            p.y = Ground(p) + .2f; line.SetPosition(i, p);
        }
        marker.boundary = line;
        var label = markerRoot.GetComponentInChildren<TextMesh>();
        if (!label)
        { var go = new GameObject("Assembly point sign"); go.transform.SetParent(markerRoot, false); label = go.AddComponent<TextMesh>(); }
        label.transform.position = mission.assemblyPoint.position + Vector3.up * 8f;
        label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
        label.fontSize = 64; label.characterSize = .3f;
        label.font = Ashfall.UI.AshfallTypography.Semibold;
        label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
        label.text = "TOPLANMA ALANI\n0 / 7"; label.color = new Color(1, .75f, .22f); marker.label = label;
        marker.slotMarkers = new Renderer[mission.assemblySlots.Length];
        for (int i = 0; i < marker.slotMarkers.Length; i++)
        {
            var t = markerRoot.Find("Waiting place marker " + i);
            if (!t)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = "Waiting place marker " + i;
                go.transform.SetParent(markerRoot, false); Object.DestroyImmediate(go.GetComponent<Collider>()); t = go.transform;
            }
            t.position = mission.assemblySlots[i].position + Vector3.up * .07f;
            t.localScale = new Vector3(1.2f, .025f, 1.2f);
            var renderer = t.GetComponent<Renderer>(); renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.slotMarkers[i] = renderer;
        }
        Dirty(marker); Dirty(line); Dirty(label);
    }

    [MenuItem("GameJam/Final Loop/Audit small group rescue")]
    public static void Audit()
    {
        Directory.CreateDirectory(Folder);
        var m = Object.FindFirstObjectByType<EvacuationMission>();
        var log = new StringBuilder();
        log.AppendLine("scene=" + SceneManager.GetActiveScene().path + " play=" + EditorApplication.isPlaying + " dirty=" + SceneManager.GetActiveScene().isDirty);
        log.AppendLine("groupRescue=" + m.groupRescue + " Q=" + m.callKey + " villageWide=" + m.villageWideCall + " allAlarmed=" + m.callAllAlarmedVillagers);
        log.AppendLine("assembly=" + (m.assemblyPoint ? m.assemblyPoint.position.ToString("F3") : "MISSING") + " slots=" + m.assemblySlots.Length);
        foreach (var r in m.residents.OrderBy(r => r.formationIndex))
        {
            log.AppendLine(r.name + " group=" + r.rescueGroup + " anchor=" + (r.panicGroupAnchor ? r.panicGroupAnchor.position.ToString("F2") : "MISSING"));
            if (!r.panicGroupAnchor || !m.assemblyPoint) continue;
            Route(log, "house->exit", r.transform.position, r.houseExit.position);
            Route(log, "exit->group", r.houseExit.position, r.panicGroupAnchor.position);
            Route(log, "group->assembly", r.panicGroupAnchor.position, m.AssemblySlot(r).position);
            Route(log, "assembly->shelter", m.AssemblySlot(r).position, m.shelterApproach.position);
        }
        log.AppendLine("dialogueTextLines=" + (m.dialogue ? m.dialogue.lines.Length : 0) + " speechAssets=" + AssetDatabase.IsValidFolder("Assets/Resources/ASHFALL/Audio/Rescue") +
            " crowdPanic=" + (new SerializedObject(m).FindProperty("crowdPanicLoop").objectReferenceValue != null) + " nextGroupMarker=" + (Object.FindFirstObjectByType<RescueGroupMarker>() != null));
        File.WriteAllText(Folder + "/audit.txt", log.ToString());
    }
    static void Route(StringBuilder log, string name, Vector3 a, Vector3 b)
    {
        var path = new NavMeshPath(); bool found = NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path);
        log.AppendLine("  " + name + "=" + found + "/" + path.status);
    }
    static Transform Point(Transform parent, string name, Vector3 position)
    {
        var t = parent.Find(name);
        if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); Undo.RegisterCreatedObjectUndo(t.gameObject, "Rescue point"); }
        Undo.RecordObject(t, "Place rescue point");
        position.y = Ground(position);
        if (!NavMesh.SamplePosition(position, out var hit, 5f, NavMesh.AllAreas)) throw new InvalidOperationException("No navigation at " + name);
        t.position = hit.position; return t;
    }
    static float Ground(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            Vector3 local = p - t.transform.position;
            if (local.x >= 0 && local.z >= 0 && local.x <= t.terrainData.size.x && local.z <= t.terrainData.size.z)
                return t.SampleHeight(p) + t.transform.position.y + .1f;
        }
        return p.y;
    }
    static void Dirty(Object obj)
    {
        EditorUtility.SetDirty(obj);
        if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
    }
}
