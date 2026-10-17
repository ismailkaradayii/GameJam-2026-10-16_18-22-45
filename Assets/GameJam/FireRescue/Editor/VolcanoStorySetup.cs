using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VolcanoStorySetup
{
    public const string Folder = "Library/VolcanoStoryReview";
    static double nextPoll;
    static VolcanoStorySetup()
    {
        EditorApplication.update += Poll;
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/bridge-ready.txt", DateTime.UtcNow.ToString("O"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        string request = Folder + "/request.txt";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "apply") Apply();
            else if (command == "views") Views();
            else if (command == "run") VolcanoStoryChecks.Run();
            else if (command == "path") AuditPath();
            else if (command == "background-off") { Application.runInBackground = false; PlayerSettings.runInBackground = false; }
            else if (command == "reload")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Reload requires Edit Mode.");
                if (SceneManager.GetActiveScene().isDirty)
                {
                    string backup = Folder + "/Volcano_Demo.unsaved-after-review.unity";
                    if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), backup, true) || !File.Exists(backup))
                        throw new IOException("Could not preserve the editor review state before reloading.");
                }
                EditorSceneManager.OpenScene(FinalLoopSetup.ScenePath); Inspect();
            }
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/error.txt", ex.ToString()); Debug.LogException(ex); }
    }
    public static void Inspect()
    {
        Directory.CreateDirectory(Folder);
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var log = new StringBuilder();
        log.AppendLine("scene=" + SceneManager.GetActiveScene().path + " dirty=" + SceneManager.GetActiveScene().isDirty + " playing=" + EditorApplication.isPlaying + " paused=" + EditorApplication.isPaused);
        if (!mission) { File.WriteAllText(Folder + "/inspect.txt", log.ToString()); return; }
        log.AppendLine("state=" + mission.State + " phase=" + mission.Phase + " time=" + Time.time + " scale=" + Time.timeScale + " cinema=" + mission.cinematic.IsPlaying + " elapsed=" + mission.cinematic.Elapsed);
        if (mission.story) log.AppendLine("STORY stop=" + mission.story.windowStop.position + " focus=" + mission.story.volcanoLookTarget.position + " lean=" + mission.story.windowEyeOffset + " defense=" + mission.story.defensePosition.position + " villagers=" + mission.evacuation.residents.Length + " villageWideQ=" + mission.evacuation.villageWideCall);
        foreach (var t in new[] { mission.standingSpawn, mission.wakePose, mission.houseExit, mission.player.transform, mission.player.viewCamera.transform, mission.sequence.effects.transform })
            if (t) log.AppendLine(t.name + " pos=" + t.position.ToString("F3") + " rot=" + t.eulerAngles.ToString("F3"));
        log.AppendLine("house bounds=" + mission.startingHouse.bounds + " local=" + mission.startingHouse.transform.InverseTransformPoint(mission.standingSpawn.position).ToString("F3"));
        log.AppendLine("weapon=" + mission.weaponPickup.transform.position + " cameraLocal=" + mission.player.transform.InverseTransformPoint(mission.player.viewCamera.transform.position));
        foreach (var route in mission.sequence.routes)
            if (route.flow) log.AppendLine("LAVA ROUTE " + route.flow.name + " pos=" + route.flow.transform.position + " bounds=" + route.flow.GetComponent<Renderer>()?.bounds);
        var house = mission.startingHouse.GetComponentInParent<BurnableBuilding>();
        foreach (var t in house.GetComponentsInChildren<Transform>(true))
        {
            string path = t.name; for (var p = t.parent; p && p != house.transform.parent; p = p.parent) path = p.name + "/" + path;
            var renderer = t.GetComponent<Renderer>();
            log.AppendLine(path + " pos=" + t.position.ToString("F3") + " local=" + t.localPosition.ToString("F3") + " rot=" + t.eulerAngles.ToString("F2") + (renderer ? " bounds=" + renderer.bounds : ""));
        }
        var view = SceneView.lastActiveSceneView;
        if (view && view.camera)
        {
            log.AppendLine("SCENE VIEW pos=" + view.camera.transform.position.ToString("F3") + " rot=" + view.camera.transform.eulerAngles.ToString("F3") + " pivot=" + view.pivot.ToString("F3") + " fov=" + view.camera.fieldOfView);
        }
        var navigation = Object.FindFirstObjectByType<ObjectiveNavigation>();
        if (navigation) foreach (var t in navigation.lakeRoad) if (t) log.AppendLine("ROAD " + t.name + "=" + t.position.ToString("F3"));
        foreach (var p in mission.sequence.effects.GetComponentsInChildren<ParticleSystem>(true).Concat(new[] { mission.sequence.effects.smokePlume, mission.sequence.effects.lavaBombs }).Distinct())
            if (p) log.AppendLine("FX " + p.name + " pos=" + p.transform.position.ToString("F3") + " scale=" + p.transform.lossyScale + " material=" + AssetDatabase.GetAssetPath(p.GetComponent<Renderer>().sharedMaterial));
        File.WriteAllText(Folder + "/inspect.txt", log.ToString());
    }

    [MenuItem("GameJam/Story/Apply window and inner thoughts")]
    public static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Open Volcano_Demo in Edit Mode.");
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        if (!mission || !mission.startingHouse || !mission.sequence.effects || !mission.villageAlarm)
            throw new InvalidOperationException("Expected the existing bedroom, eruption and village alarm.");
        Directory.CreateDirectory(Folder);
        if (!File.Exists(Folder + "/Volcano_Demo.before-story.unity")) File.Copy(scene.path, Folder + "/Volcano_Demo.before-story.unity");
        Undo.RecordObject(mission, "Add volcano story bindings");
        var story = mission.GetComponent<VolcanoStorySequence>();
        if (!story) story = Undo.AddComponent<VolcanoStorySequence>(mission.gameObject);
        Undo.RecordObject(story, "Author volcano inner thoughts");
        var root = GameObject.Find("Volcano Story Bindings");
        if (!root) { root = new GameObject("Volcano Story Bindings"); Undo.RegisterCreatedObjectUndo(root, "Volcano story destinations"); }
        story.mission = mission; mission.story = story;
        story.windowStop = Point(root.transform, "Bedroom volcano window stop", new Vector3(33.3f, 1.24f, -598f));
        story.windowRoute = new[] { Point(root.transform, "Bedroom clear aisle", new Vector3(33.3f, 1.24f, -604f)) };
        story.windowEyeOffset = new Vector3(-2.2f, -2.2f, 0);
        story.volcanoLookTarget = Point(root.transform, "Quiet crater focus", mission.sequence.effects.transform.position + Vector3.up * 12f);
        story.windowStop.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(story.volcanoLookTarget.position - story.windowStop.position, Vector3.up));
        Vector3 vent = VisibleVent(mission, story);
        story.volcanoLookTarget.position = vent + Vector3.up * 8f;
        story.defensePosition = Point(root.transform, "Village volcano approach defense point", Ground(new Vector3(-22f, 0, -545f)));
        story.defensePosition.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(story.volcanoLookTarget.position - story.defensePosition.position, Vector3.up));
        story.omenSmoke = Particles(root.transform, "First small crater smoke", vent,
            mission.sequence.effects.smokePlume, false);
        story.omenSparks = Particles(root.transform, "First few crater sparks", vent + Vector3.up * 2f,
            mission.sequence.effects.lavaBombs, true);
        story.subtitleFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/ASHFALL/UI/Fonts/SourceSans3-Regular.ttf");
        Undo.RecordObject(mission.evacuation, "Let Q reach all alerted village houses");
        mission.evacuation.villageWideCall = true;
        var hud = Object.FindFirstObjectByType<RescueHUD>();
        if (hud && hud.subtitleText)
        {
            Undo.RecordObject(hud.subtitleText, "Readable Turkish inner thoughts");
            hud.subtitleText.font = story.subtitleFont;
            hud.subtitleText.fontSize = 23;
            hud.subtitleText.resizeTextForBestFit = false;
            hud.subtitleText.supportRichText = false;
            hud.subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hud.subtitleText.verticalOverflow = VerticalWrapMode.Truncate;
            EditorUtility.SetDirty(hud.subtitleText);
        }
        foreach (var value in new Object[] { mission, story, mission.evacuation }) EditorUtility.SetDirty(value);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Folder + "/setup.txt", "APPLIED: authored window walk, isolated small crater particles, Turkish inner thoughts, village-wide Q and return defense destination. Existing siren, resident navigation, truck, intake and eruption timing retained.\n");
        Inspect();
    }
    static Transform Point(Transform parent, string name, Vector3 position)
    {
        var point = parent.Find(name);
        if (!point) { var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Volcano story point"); point = go.transform; point.SetParent(parent, false); }
        Undo.RecordObject(point, "Volcano story destination"); point.position = position; return point;
    }
    static Vector3 Ground(Vector3 position)
    {
        foreach (var terrain in Terrain.activeTerrains)
            if (terrain && terrain.terrainData.bounds.Contains(terrain.transform.InverseTransformPoint(position)))
            { position.y = terrain.SampleHeight(position) + terrain.transform.position.y + .12f; break; }
        return position;
    }
    static Vector3 VisibleVent(VolcanoMissionDirector mission, VolcanoStorySequence story)
    {
        // The full eruption's origin is below the front crater rim. A small puff must
        // start at the visible lip to be seen from this particular bedroom window.
        Vector3 localEye = mission.player.transform.InverseTransformPoint(mission.player.viewCamera.transform.position);
        Vector3 eye = story.windowStop.position + story.windowStop.rotation * Vector3.Scale(localEye, mission.player.transform.lossyScale) + story.windowEyeOffset;
        var house = mission.startingHouse.GetComponentInParent<BurnableBuilding>().transform;
        Vector3 vent = mission.sequence.effects.transform.position + Vector3.up * 8f;
        var log = new StringBuilder(); Physics.SyncTransforms();
        for (int step = 0; step < 75; step++, vent.y += 2f)
        {
            Vector3 delta = vent - eye;
            var blockers = Physics.RaycastAll(eye, delta.normalized, delta.magnitude - 2f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => !hit.transform.IsChildOf(house) && !hit.transform.IsChildOf(mission.player.transform)).OrderBy(hit => hit.distance).ToArray();
            log.AppendLine("vent=" + vent.ToString("F2") + " blocker=" + (blockers.Length > 0 ? blockers[0].collider.name + " " + blockers[0].point : "clear"));
            if (blockers.Length == 0) break;
        }
        vent.y += 7f;
        log.AppendLine("AUTHORED " + vent.ToString("F3")); File.WriteAllText(Folder + "/vent-visibility.txt", log.ToString());
        return vent;
    }
    static ParticleSystem Particles(Transform parent, string name, Vector3 position, ParticleSystem source, bool sparks)
    {
        var point = Point(parent, name, position); point.rotation = Quaternion.Euler(-90, 0, 0);
        var particles = point.GetComponent<ParticleSystem>();
        if (!particles) particles = Undo.AddComponent<ParticleSystem>(point.gameObject);
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main; main.playOnAwake = false; main.loop = false; main.prewarm = false;
        main.duration = .8f; main.startDelay = 0; main.maxParticles = 24;
        main.startLifetime = new ParticleSystem.MinMaxCurve(sparks ? 1.5f : 5f, sparks ? 2.4f : 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(sparks ? 14f : 3.5f, sparks ? 22f : 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(sparks ? 3f : 14f, sparks ? 4.2f : 19f);
        main.startColor = sparks ? new Color(4f, 1.1f, .04f, 1f) : new Color(.6f, .57f, .54f, .78f);
        main.gravityModifier = sparks ? .8f : 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = particles.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, sparks ? (short)7 : (short)5) });
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = sparks ? 22 : 10; shape.radius = 2f;
        var size = particles.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1,
            AnimationCurve.Linear(0, sparks ? 1 : .7f, 1, sparks ? .15f : 2f));
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(.65f, .65f), new GradientAlphaKey(0, 1) });
        var color = particles.colorOverLifetime; color.enabled = true; color.color = gradient;
        var originalAnimation = source.textureSheetAnimation;
        var animation = particles.textureSheetAnimation; animation.enabled = originalAnimation.enabled;
        if (originalAnimation.enabled)
        {
            animation.mode = originalAnimation.mode; animation.numTilesX = originalAnimation.numTilesX; animation.numTilesY = originalAnimation.numTilesY;
            animation.animation = originalAnimation.animation; animation.frameOverTime = originalAnimation.frameOverTime;
            animation.startFrame = originalAnimation.startFrame; animation.cycleCount = originalAnimation.cycleCount;
        }
        var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = source.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode = ShadowCastingMode.Off;
        EditorUtility.SetDirty(particles); EditorUtility.SetDirty(renderer);
        return particles;
    }
    public static void Views()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode for reference renders.");
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var story = mission.story;
        var go = new GameObject("Temporary story reference camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.CopyFrom(mission.player.viewCamera);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        camera.GetUniversalAdditionalCameraData().volumeLayerMask = mission.player.viewCamera.GetUniversalAdditionalCameraData().volumeLayerMask;
        camera.enabled = false; camera.useOcclusionCulling = false;
        var localEye = mission.player.transform.InverseTransformPoint(mission.player.viewCamera.transform.position);
        var eye = story.windowStop.position + story.windowStop.rotation * Vector3.Scale(localEye, mission.player.transform.lossyScale) + story.windowEyeOffset;
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(story.volcanoLookTarget.position - eye));
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var active = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.aspect = 1280f / 720f; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(Folder + "/window-reference.png", pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = active; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(go); }
    }
    public static void AuditPath()
    {
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var path = new NavMeshPath(); var log = new StringBuilder();
        if (NavMesh.SamplePosition(mission.standingSpawn.position, out var start, 3, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(mission.story.windowStop.position, out var end, 3, NavMesh.AllAreas))
        {
            log.AppendLine("NAV " + start.position + " -> " + end.position + " complete=" + NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) + " status=" + path.status);
            foreach (var point in path.corners) log.AppendLine("CORNER " + point.ToString("F3"));
        }
        var motor = mission.player.playerBody;
        float radius = motor.radius * Mathf.Max(motor.transform.lossyScale.x, motor.transform.lossyScale.z);
        float height = motor.height * motor.transform.lossyScale.y;
        log.AppendLine("radius=" + radius + " height=" + height);
        foreach (var collider in Physics.OverlapBox(new Vector3(29.5f, 4.5f, -601), new Vector3(8, 3, 9), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (collider.transform.IsChildOf(mission.player.transform)) continue;
            string name = collider.name; for (var p = collider.transform.parent; p; p = p.parent) name = p.name + "/" + name;
            log.AppendLine("COLLIDER " + name + " bounds=" + collider.bounds);
        }
        foreach (var target in new[] { mission.standingSpawn.position, new Vector3(28.5f, 1.24f, -603), new Vector3(30, 1.24f, -600), mission.story.windowStop.position })
        {
            var overlaps = Physics.OverlapCapsule(target + Vector3.up * (radius + .15f), target + Vector3.up * (height - radius), radius * .95f, ~0, QueryTriggerInteraction.Ignore);
            log.AppendLine("CAPSULE " + target + " hits=" + string.Join(",", overlaps.Where(c => !c.transform.IsChildOf(mission.player.transform)).Select(c => c.name)));
        }
        File.WriteAllText(Folder + "/path.txt", log.ToString());
    }
}
