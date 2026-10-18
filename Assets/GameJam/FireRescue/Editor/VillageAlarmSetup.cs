using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class VillageAlarmSetup
{
    public const string ReportFolder = "Logs/VillageAlarm";
    [MenuItem("GameJam/Final Loop/Inspect village alarm button")]
    public static void Inspect()
    {
        var report = new StringBuilder();
        var scene = SceneManager.GetActiveScene();
        report.AppendLine("Scene=" + scene.path + "; dirty=" + scene.isDirty + "; playing=" + EditorApplication.isPlaying);
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var player = mission.player;
        report.AppendLine("Player=" + player.transform.position + "; eyeHeight=" + player.eyeHeight + "; interactionRange=" + player.interactionDistance);
        report.AppendLine("View=" + player.viewCamera.transform.position + "; scale=" + player.viewCamera.transform.lossyScale + "; handsCamera=" + player.firstPerson.handsCamera);
        var animator = player.firstPerson.handsAnimator;
        if (animator && animator.isHuman)
        {
            var shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var elbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var wrist = animator.GetBoneTransform(HumanBodyBones.RightHand);
            report.AppendLine("Hands=" + Path(animator.transform) + "; lengths=" + Vector3.Distance(shoulder.position, elbow.position) + "/" + Vector3.Distance(elbow.position, wrist.position) + "; scale=" + animator.transform.lossyScale);
        }
        if (mission.villageAlarm && mission.villageAlarm.contactPoint)
            foreach (var collider in Physics.OverlapSphere(mission.villageAlarm.contactPoint.position, 4f, ~0, QueryTriggerInteraction.Ignore))
                report.AppendLine("NEARBY COLLIDER " + Path(collider.transform) + "; bounds=" + collider.bounds + "; type=" + collider.GetType().Name);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t);
            if (!t.name.Contains("Button", StringComparison.OrdinalIgnoreCase) && !prefab.Contains("emergency-stop-button", StringComparison.OrdinalIgnoreCase)) continue;
            report.AppendLine("OBJECT " + Path(t) + " active=" + t.gameObject.activeInHierarchy + " position=" + t.position + " euler=" + t.eulerAngles + " localScale=" + t.localScale + " prefab=" + prefab);
            foreach (var c in t.GetComponents<Collider>()) report.AppendLine("  COLLIDER " + c.GetType().Name + " bounds=" + c.bounds + "; enabled=" + c.enabled);
            var renderer = t.GetComponent<Renderer>();
            if (renderer)
                report.AppendLine("  MESH bounds=" + renderer.bounds + "; materials=" + string.Join("; ", renderer.sharedMaterials.Select(m => m ? m.name + " / " + m.shader.name : "NULL")));
        }
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.type != LightType.Spot && Vector3.Distance(light.transform.position, mission.startingHouse.bounds.center) > 30) continue;
            report.AppendLine("LIGHT " + Path(light.transform) + " pos=" + light.transform.position + " forward=" + light.transform.forward + " active=" + light.gameObject.activeInHierarchy + " enabled=" + light.enabled +
                " type=" + light.type + " intensity=" + light.intensity + " range=" + light.range + " angle=" + light.spotAngle + " color=" + light.color + " bake=" + light.lightmapBakeType + " mask=" + light.cullingMask + " renderMode=" + light.renderMode);
        }
        Directory.CreateDirectory(ReportFolder); File.WriteAllText(ReportFolder + "/inspection.txt", report.ToString());
    }
    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;

    const string AssetFolder = "Assets/GameJam/FireRescue/UI/VillageAlarm";
    [MenuItem("GameJam/Final Loop/Apply village alarm button")]
    public static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Use the current Volcano_Demo in Edit Mode. Do not discard the placed button.");
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); var player = mission.player;
        if (mission.villageAlarm) throw new InvalidOperationException("Alarm is already connected. Use its Inspector for tuning.");
        var button = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
            .Where(f => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(f).Contains("emergency-stop-button", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Vector3.Distance(f.transform.position, mission.startingHouse.bounds.center)).FirstOrDefault();
        if (!button) throw new InvalidOperationException("The placed emergency-stop-button FBX is not in the active scene.");
        var renderer = button.GetComponent<MeshRenderer>();
        int capIndex = Array.FindIndex(renderer.sharedMaterials, m => m && m.name.Contains("buttn", StringComparison.OrdinalIgnoreCase));
        if (capIndex < 0) throw new InvalidOperationException("Cannot identify the existing red cap submesh.");
        if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets/GameJam/FireRescue/UI", "VillageAlarm");
        var original = button.sharedMesh;
        var triangles = original.GetTriangles(capIndex);
        var vertices = original.vertices;
        var shell = Object.Instantiate(original); shell.name = "Alarm button original housing";
        shell.SetTriangles(Array.Empty<int>(), capIndex);
        var cap = Object.Instantiate(original); cap.name = "Alarm button moving red cap";
        cap.subMeshCount = 1; cap.SetTriangles(triangles, 0);
        var capBounds = new Bounds(vertices[triangles[0]], Vector3.zero);
        foreach (int index in triangles) capBounds.Encapsulate(vertices[index]); cap.bounds = capBounds;
        AssetDatabase.CreateAsset(shell, AssetFolder + "/Alarm housing.asset"); AssetDatabase.CreateAsset(cap, AssetFolder + "/Alarm red cap.asset");
        button.sharedMesh = shell; Dirty(button); PrefabUtility.RecordPrefabInstancePropertyModifications(button);
        var moving = new GameObject("Moving red alarm cap", typeof(MeshFilter), typeof(MeshRenderer));
        moving.transform.SetParent(button.transform, false); moving.GetComponent<MeshFilter>().sharedMesh = cap;
        var capMaterial = new Material(renderer.sharedMaterials[capIndex]) { name = "Village alarm red cap" };
        capMaterial.EnableKeyword("_EMISSION"); capMaterial.SetTexture("_EmissionMap", null); capMaterial.SetColor("_EmissionColor", new Color(1.7f, .02f, .008f));
        capMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        AssetDatabase.CreateAsset(capMaterial, AssetFolder + "/Village alarm red cap.mat");
        var movingRenderer = moving.GetComponent<MeshRenderer>(); movingRenderer.sharedMaterial = capMaterial;
        movingRenderer.shadowCastingMode = renderer.shadowCastingMode;
        var collider = button.GetComponent<BoxCollider>(); if (!collider) collider = button.gameObject.AddComponent<BoxCollider>();
        collider.center = original.bounds.center; collider.size = original.bounds.size; collider.isTrigger = false; Dirty(collider);

        var alarm = button.gameObject.AddComponent<VillageAlarmButton>();
        alarm.mission = mission; alarm.player = player; alarm.movingHead = moving.transform;
        alarm.hands = player.firstPerson.handsAnimator.GetComponent<Itfaiye.ItfaiyeFirstPersonHands>();
        alarm.cameraKick = player.viewCamera.GetComponent<VolcanoCameraShake>();
        alarm.maximumPressDistance = 1.5f;
        var contact = new GameObject("Alarm palm contact").transform; contact.SetParent(button.transform, false);
        Vector3 contactPosition = movingRenderer.bounds.center;
        contactPosition.y = movingRenderer.bounds.max.y + .015f;
        contact.SetPositionAndRotation(contactPosition, Quaternion.FromToRotation(Vector3.up, button.transform.forward));
        alarm.contactPoint = contact;
        var approach = new GameObject("Alarm approach pose").transform; approach.SetParent(mission.transform, false);
        ResolveApproach(alarm, out Vector3 direction, out float approachDistance);
        Vector3 approachPosition = contactPosition + direction * approachDistance; approachPosition.y = mission.standingSpawn.position.y;
        approach.SetPositionAndRotation(approachPosition, Quaternion.LookRotation(-direction)); alarm.approachPose = approach;
        alarm.redSpot = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Spot)
            .OrderBy(l => Vector3.Distance(l.transform.position, contactPosition)).FirstOrDefault();
        if (!alarm.redSpot) throw new InvalidOperationException("The user's red spotlight is missing.");
        var spot = alarm.redSpot;
        spot.enabled = true; spot.lightmapBakeType = LightmapBakeType.Realtime; spot.renderMode = LightRenderMode.ForcePixel;
        spot.range = Mathf.Max(10, Vector3.Distance(spot.transform.position, contactPosition) * 2.2f);
        spot.spotAngle = 34; spot.innerSpotAngle = 19; spot.intensity = Mathf.Max(spot.intensity, 55);
        spot.color = new Color(1, .025f, .006f); spot.cullingMask = ~0; spot.shadows = LightShadows.None;
        spot.transform.rotation = Quaternion.LookRotation(contactPosition - spot.transform.position);
        Dirty(spot); Dirty(spot.transform);
        if (player.firstPerson.handsCamera) { player.firstPerson.handsCamera.farClipPlane = Mathf.Max(8, player.firstPerson.handsCamera.farClipPlane); Dirty(player.firstPerson.handsCamera); }

        var impact = new GameObject("Alarm button impact").AddComponent<AudioSource>(); impact.transform.SetParent(button.transform, false);
        impact.transform.position = contactPosition; impact.playOnAwake = false; impact.spatialBlend = .45f; impact.minDistance = 2; impact.maxDistance = 35; impact.dopplerLevel = 0;
        alarm.impactSource = impact;
        alarm.palmImpact = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Itfaiye/Audio/impact-metal.wav");
        alarm.switchClick = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/soundshelfstudio-ui-click-deep-512211.mp3");
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameJam/FireRescue/Audio/VillageWarningSiren.wav");
        if (!clip) throw new InvalidOperationException("Village siren WAV has not imported yet.");
        var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
        var settings = importer.defaultSampleSettings; settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.loadInBackground = false; importer.SaveAndReimport();
        clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameJam/FireRescue/Audio/VillageWarningSiren.wav");
        var emitters = new GameObject("Village warning siren emitters").transform; emitters.SetParent(mission.transform, false);
        var houses = mission.gameFlow.targetBuildings.Where(h => h).OrderBy(h => h.transform.position.z).ToArray();
        alarm.villageSirens = new AudioSource[3]; alarm.sirenVolume = .5f;
        for (int i = 0; i < 3; i++)
        {
            var source = new GameObject("Village siren " + (i + 1)).AddComponent<AudioSource>(); source.transform.SetParent(emitters, false);
            var house = houses[Mathf.RoundToInt(i * (houses.Length - 1) / 2f)];
            source.transform.position = house.ignitionBounds ? house.ignitionBounds.bounds.center + Vector3.up * 4 : house.transform.position + Vector3.up * 7;
            source.clip = clip; source.playOnAwake = false; source.loop = true; source.volume = 0; source.spatialBlend = .8f;
            source.rolloffMode = AudioRolloffMode.Logarithmic; source.minDistance = 18; source.maxDistance = 200; source.spread = 90; source.dopplerLevel = 0;
            source.pitch = .98f + i * .02f; source.priority = 100; alarm.villageSirens[i] = source;
        }
        mission.villageAlarm = alarm; mission.skipVillageWarning = false;
        var ui = player.contextualInteractions;
        var target = new GameObject("Village alarm button interaction").AddComponent<ContextualInteractionTarget>();
        target.transform.SetParent(ui.targets[0].transform.parent, false);
        target.kind = ContextualInteractionKind.VillageAlarm; target.anchor = contact; target.visibilityRoot = button.transform; target.alarm = alarm;
        target.maximumLookAngle = 38; target.maximumViewDistance = 8; target.priority = 1; target.labelHeight = .25f;
        ui.targets = ui.targets.Concat(new[] { target }).ToArray();
        Dirty(ui); Dirty(ui.words); Dirty(mission); Dirty(alarm); PrefabUtility.RecordPrefabInstancePropertyModifications(ui);
        PrefabUtility.RecordPrefabInstancePropertyModifications(alarm); PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Inspect(); Validate();
    }
    static void Dirty(Object item) { if (item) EditorUtility.SetDirty(item); }
    [MenuItem("GameJam/Final Loop/Finalize village alarm pose")]
    public static void FinalizePose()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Use the authored Volcano_Demo in Edit Mode.");
        var alarm = Object.FindFirstObjectByType<VolcanoMissionDirector>().villageAlarm;
        alarm.hands.buttonLeanInViewMetres = .4f;
        alarm.maximumPressDistance = 1.5f;
        ResolveApproach(alarm, out Vector3 direction, out float approachDistance);
        Vector3 approach = alarm.contactPoint.position + direction * approachDistance;
        approach.y = alarm.mission.standingSpawn.position.y;
        alarm.approachPose.SetPositionAndRotation(approach, Quaternion.LookRotation(-direction));
        alarm.redSpot.transform.rotation = Quaternion.LookRotation(alarm.contactPoint.position - alarm.redSpot.transform.position);
        alarm.redSpot.range = Mathf.Max(alarm.redSpot.range, Vector3.Distance(alarm.redSpot.transform.position, alarm.contactPoint.position) * 2.2f);
        foreach (Object modified in new Object[] { alarm, alarm.approachPose, alarm.redSpot, alarm.redSpot.transform })
        {
            Dirty(modified);
            if (PrefabUtility.IsPartOfPrefabInstance(modified)) PrefabUtility.RecordPrefabInstancePropertyModifications(modified);
        }
        Dirty(alarm.hands); PrefabUtility.RecordPrefabInstancePropertyModifications(alarm.hands);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(alarm.gameObject.scene); EditorSceneManager.SaveScene(alarm.gameObject.scene);
        Validate(); ContextualInteractionSetup.Validate();
    }
    static void ResolveApproach(VillageAlarmButton alarm, out Vector3 direction, out float distance)
    {
        Vector3 contact = alarm.contactPoint.position;
        direction = Vector3.ProjectOnPlane(alarm.mission.standingSpawn.position - contact, Vector3.up).normalized;
        distance = 1.2f;
        // A tabletop button must be approached from its closest exposed edge, not through the table.
        var support = Physics.RaycastAll(contact + Vector3.up * .1f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore)
            .OrderBy(hit => hit.distance)
            .FirstOrDefault(hit => hit.collider is BoxCollider && !hit.transform.IsChildOf(alarm.transform) &&
                !hit.transform.IsChildOf(alarm.player.transform)).collider as BoxCollider;
        if (!support) return;
        Vector3 local = support.transform.InverseTransformPoint(contact), half = support.size * .5f;
        Vector3 min = support.center - half, max = support.center + half;
        var edges = new[] { new Vector3(min.x, local.y, local.z), new Vector3(max.x, local.y, local.z),
            new Vector3(local.x, local.y, min.z), new Vector3(local.x, local.y, max.z) };
        Vector3 offset = edges.Select(edge => Vector3.ProjectOnPlane(support.transform.TransformPoint(edge) - contact, Vector3.up))
            .OrderBy(delta => delta.sqrMagnitude).First();
        if (offset.sqrMagnitude < .0001f) return;
        direction = offset.normalized;
        var body = alarm.player.playerBody;
        float radius = body.radius * Mathf.Max(body.transform.lossyScale.x, body.transform.lossyScale.z);
        distance = offset.magnitude + radius + .1f;
    }
    [MenuItem("GameJam/Final Loop/Validate village alarm button")]
    public static void Validate()
    {
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var alarm = mission ? mission.villageAlarm : null;
        var report = new StringBuilder();
        if (!alarm || !alarm.player || !alarm.hands || !alarm.contactPoint || !alarm.approachPose || !alarm.movingHead || !alarm.redSpot || !alarm.impactSource ||
            !alarm.palmImpact || !alarm.switchClick || !alarm.cameraKick || alarm.villageSirens.Length != 3 || alarm.villageSirens.Any(s => !s || !s.clip || s.playOnAwake || !s.loop))
            throw new InvalidOperationException("Village alarm scene bindings are incomplete.");
        if (mission.skipVillageWarning) throw new InvalidOperationException("The alarm mission must not auto-skip.");
        float distance = Vector3.Distance(alarm.redSpot.transform.position, alarm.contactPoint.position);
        if (distance >= alarm.redSpot.range || Vector3.Dot(alarm.redSpot.transform.forward, (alarm.contactPoint.position - alarm.redSpot.transform.position).normalized) < .99f)
            throw new InvalidOperationException("Red spotlight does not illuminate the button.");
        if (!alarm.player.contextualInteractions.targets.Any(t => t && t.kind == ContextualInteractionKind.VillageAlarm && t.alarm == alarm))
            throw new InvalidOperationException("Central contextual E target is missing.");
        report.AppendLine("PASS existing button, red spotlight, existing hand rig, mission, E target and three spatial sirens.");
        report.AppendLine("Button=" + Path(alarm.transform) + "; position=" + alarm.transform.position + "; scale=" + alarm.transform.localScale);
        report.AppendLine("Spot distance=" + distance + "; range=" + alarm.redSpot.range + "; intensity=" + alarm.redSpot.intensity);
        report.AppendLine("Contact=" + alarm.contactPoint.position + "; approach=" + alarm.approachPose.position + "; slap=" + alarm.slapSeconds + "s; impact=" + alarm.contactFraction);
        Directory.CreateDirectory(ReportFolder); File.WriteAllText(ReportFolder + "/validation.txt", report.ToString());
    }
}
