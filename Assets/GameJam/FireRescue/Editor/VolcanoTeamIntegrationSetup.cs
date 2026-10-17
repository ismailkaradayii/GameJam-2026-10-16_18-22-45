using System;
using System.IO;
using System.Linq;
using Ashfall.MainMenu;
using GameJam.FireRescue;
using Itfaiye;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit, repeatable authoring of the shared menu and Volcano gameplay scene.</summary>
public static class VolcanoTeamIntegrationSetup
{
    public const string VolcanoPath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    public const string MenuPath = "Assets/Scenes/MainMenu.unity";
    const string PlayerPrefab = "Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab";
    const string CreaturePrefab = "Assets/LavaCreature/Prefabs/LavaCreature.prefab";

    [MenuItem("GameJam/Integration/Connect menu and Volcano gameplay")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring the scenes.");
        EditorSceneManager.SaveOpenScenes();
        Scene scene = EditorSceneManager.OpenScene(VolcanoPath, OpenSceneMode.Single);
        FireRescuePlayer player = IntegratePlayer(scene);
        IntegrateCreatures(scene, player);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        AshfallSettingsSceneBuilder.RefreshGameplayHelp();
        var menu = Object.FindFirstObjectByType<AshfallMenuController>();
        if (!menu) throw new InvalidOperationException("MainMenu has no menu controller.");
        var data = new SerializedObject(menu);
        data.FindProperty("gameplaySceneName").stringValue = "Volcano_Demo";
        data.ApplyModifiedPropertiesWithoutUndo();
        var buttons = data.FindProperty("menuButtons");
        string[] labels = { "YENİ OYUN", "AYARLAR", "NASIL OYNANIR", "ÇIKIŞ" };
        for (int i = 0; i < Mathf.Min(buttons.arraySize, labels.Length); i++)
        {
            var button = buttons.GetArrayElementAtIndex(i).objectReferenceValue as Button;
            if (!button) continue;
            foreach (Text label in button.GetComponentsInChildren<Text>(true)) label.text = labels[i];
        }
        EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
        EditorSceneManager.SaveScene(menu.gameObject.scene);
        // Only the menu and the shared map belong to the playable build flow.
        var extras = EditorBuildSettings.scenes.Where(s => s.path != MenuPath && s.path != VolcanoPath)
            .Select(s => new EditorBuildSettingsScene(s.path, false));
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuPath, true),
            new EditorBuildSettingsScene(VolcanoPath, true) }.Concat(extras).ToArray();
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/VolcanoTeamIntegrationSetup.txt", "MainMenu -> Volcano_Demo\nSingle shared player, original truck/hose/cannon, three lava-gated creatures.\n");
        Debug.Log("VOLCANO_TEAM_INTEGRATION_COMPLETE: MainMenu now opens the shared Volcano_Demo map.");
    }

    static FireRescuePlayer IntegratePlayer(Scene scene)
    {
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        if (!player || !player.drive || !player.hose || !player.viewCamera)
            throw new InvalidOperationException("Volcano's existing fire rescue rig is incomplete.");
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        if (!sequence) throw new InvalidOperationException("Volcano sequence is missing.");
        if (!player.firstPerson)
        {
            var old = player;
            Camera oldCamera = old.viewCamera;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (!prefab) throw new InvalidOperationException("The authored first-person prefab is missing.");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            root.name = "Firefighter";
            root.transform.SetParent(old.transform.parent, false);
            root.transform.position = old.playerBody.transform.position;
            root.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(
                new Vector3(79f, 0f, -601f) - root.transform.position, Vector3.up));
            root.transform.localScale = Vector3.one;
            player = root.AddComponent<FireRescuePlayer>();
            EditorUtility.CopySerialized(old, player);
            player.firstPerson = root.GetComponent<ItfaiyeFirstPersonController>();
            player.pistol = root.GetComponent<ItfaiyePistol>();
            player.playerBody = root.GetComponent<CharacterController>();
            player.viewCamera = player.firstPerson.playerCamera;
            var carry = new GameObject("Hose Carry Point").transform;
            carry.SetParent(player.viewCamera.transform, false);
            carry.localPosition = new Vector3(.35f, -.3f, .65f);
            player.hand = carry;
            var shake = player.viewCamera.gameObject.AddComponent<VolcanoCameraShake>();
            if (sequence.cameraShake) EditorUtility.CopySerialized(sequence.cameraShake, shake);
            sequence.cameraShake = shake;
            Object.DestroyImmediate(old.gameObject);
            if (oldCamera) Object.DestroyImmediate(oldCamera.gameObject);
        }
        player.startDriving = false;
        player.firstPerson.externalControl = true;
        player.pistol.externalControl = true;
        player.firstPerson.recoverFromFall = true;
        player.firstPerson.walkSpeed = 4f;
        player.firstPerson.runSpeed = 6f;
        player.viewCamera.gameObject.tag = "MainCamera";
        player.viewCamera.enabled = true;
        player.viewCamera.farClipPlane = 3000f;
        player.viewCamera.useOcclusionCulling = false;
        player.cannon.aimCamera = player.viewCamera;
        var cameraData = player.viewCamera.GetUniversalAdditionalCameraData();
        cameraData.renderType = CameraRenderType.Base;
        var hands = player.firstPerson.handsCamera;
        hands.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
        if (!cameraData.cameraStack.Contains(hands)) cameraData.cameraStack.Add(hands);
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (camera != player.viewCamera && camera != hands) camera.enabled = false;
        foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            listener.enabled = listener.gameObject == player.viewCamera.gameObject;
        if (!player.viewCamera.GetComponent<AudioListener>()) player.viewCamera.gameObject.AddComponent<AudioListener>();
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.firstPerson);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.pistol);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.cannon);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.viewCamera);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        return player;
    }

    static void IntegrateCreatures(Scene scene, FireRescuePlayer player)
    {
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var terrain = Object.FindFirstObjectByType<Terrain>();
        var flow = sequence.routes.Select(r => r.flow).FirstOrDefault(f => f && f.name == "Town Approach");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CreaturePrefab);
        if (!terrain || !terrain.terrainData || !flow || !flow.source || !flow.source.m_Container || !prefab)
            throw new InvalidOperationException("Town lava, terrain or creature prefab is missing.");
        var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>(FindObjectsInactive.Include);
        if (!encounter)
        {
            var manager = new GameObject("Lava Creature Encounter");
            SceneManager.MoveGameObjectToScene(manager, scene);
            encounter = manager.AddComponent<LavaCreatureEncounter>();
        }
        encounter.sequence = sequence;
        if (encounter.spawns.Length == 0)
        {
            float[] progress = { .28f, .50f, .72f };
            encounter.spawns = new LavaCreatureEncounter.Spawn[progress.Length];
            for (int i = 0; i < progress.Length; i++)
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                root.name = "Town Lava Creature " + (i + 1);
                root.transform.SetParent(encounter.transform, false);
                root.transform.position = PointAlongLava(flow, terrain, progress[i]);
                var solidRenderers = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer) &&
                        !r.name.ToLowerInvariant().Contains("flame") && !r.name.ToLowerInvariant().Contains("ember")).ToArray();
                if (solidRenderers.Length == 0) throw new InvalidOperationException("Creature has no visible body.");
                Bounds bounds = BodyBounds(solidRenderers);
                if (bounds.size.y < .001f) throw new InvalidOperationException("Creature bounds are empty.");
                root.transform.localScale *= 1.8f / bounds.size.y;
                bounds = BodyBounds(solidRenderers);
                // The imported model uses local +Z as up; keep its original axis correction.
                float footOffset = root.transform.position.y - bounds.min.y;
                var jump = root.GetComponent<LavaCreatureAutoJump>();
                jump.groundTerrain = terrain;
                jump.groundClearance = footOffset + .04f;
                root.transform.position += Vector3.up * jump.groundClearance;
                bounds = BodyBounds(solidRenderers);
                var hitbox = root.AddComponent<CapsuleCollider>();
                hitbox.direction = 2;
                hitbox.center = root.transform.InverseTransformPoint(bounds.center);
                float scale = Mathf.Abs(root.transform.lossyScale.x);
                hitbox.radius = Mathf.Min(.48f, Mathf.Max(.22f, Mathf.Max(bounds.size.x, bounds.size.z) * .3f)) / scale;
                hitbox.height = Mathf.Max(hitbox.radius * 2f, bounds.size.y / scale);
                var actor = root.AddComponent<LavaCreatureActor>();
                actor.jump = jump;
                actor.maxHealth = 100f;
                actor.waterDamagePerLitre = 2f;
                ConfigureJump(jump, player.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(jump);
                root.SetActive(false);
                encounter.spawns[i] = new LavaCreatureEncounter.Spawn { creature = actor, feed = flow,
                    feedProgress = progress[i], delayAfterArrival = .25f };
            }
        }
        var placementReport = new CreaturePlacementAudit.Report { scene = scene.path };
        var houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None);
        Physics.SyncTransforms();
        foreach (var spawn in encounter.spawns)
        {
            if (!spawn?.creature) throw new InvalidOperationException("An authored creature entry is missing.");
            ConfigureJump(spawn.creature.jump, player.transform);
            spawn.creature.jump.groundTerrain = terrain;
            MoveHitboxUnderVisual(spawn.creature);
            spawn.creature.gameObject.SetActive(false);
            var placement = CreaturePlacementAudit.Evaluate(spawn.creature, houses);
            placementReport.creatures.Add(placement);
            if (placement.solidObstacles.Length > 0 || placement.overlappingHouseEnvelopes.Length > 0)
            {
                if (!placement.clearCandidateFound)
                    throw new InvalidOperationException(spawn.creature.name + " has no safe placement within 8 metres of its lava arrival point.");
                spawn.creature.transform.position = placement.suggestedPosition;
                PrefabUtility.RecordPrefabInstancePropertyModifications(spawn.creature.transform);
            }
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/CreaturePlacementAdjustments.json", JsonUtility.ToJson(placementReport, true));
        VolcanoCreatureDescentSetup.Configure(encounter);
    }

    static void MoveHitboxUnderVisual(LavaCreatureActor actor)
    {
        Transform visual = actor.transform.Find("SomersaultVisual");
        if (!visual) throw new InvalidOperationException(actor.name + " has no SomersaultVisual pivot.");
        var rootCapsules = actor.GetComponents<CapsuleCollider>();
        var visualCapsules = visual.GetComponentsInChildren<CapsuleCollider>(true);
        if (rootCapsules.Length > 1 || visualCapsules.Length > 1 || (rootCapsules.Length > 0 && visualCapsules.Length > 0))
            throw new InvalidOperationException(actor.name + " has ambiguous capsule hitboxes; no automatic replacement performed.");
        if (rootCapsules.Length == 0)
        {
            if (visualCapsules.Length != 1) throw new InvalidOperationException(actor.name + " has no capsule hitbox.");
            return; // Already migrated: preserve the authored transform and shape.
        }

        CapsuleCollider original = rootCapsules[0];
        var hitbox = new GameObject("Hit Capsule");
        hitbox.layer = actor.gameObject.layer;
        CapsuleCollider migrated = hitbox.AddComponent<CapsuleCollider>();
        // Match the original root-space collider exactly before parenting under the animated
        // pivot. The world-preserving reparent retains its current pose, then follows flips/tuck.
        migrated.transform.SetParent(actor.transform, false);
        migrated.transform.localPosition = Vector3.zero;
        migrated.transform.localRotation = Quaternion.identity;
        migrated.transform.localScale = Vector3.one;
        migrated.transform.SetParent(visual, true);
        EditorUtility.CopySerialized(original, migrated);
        Object.DestroyImmediate(original);
    }

    static void ConfigureJump(LavaCreatureAutoJump jump, Transform target)
    {
        var data = new SerializedObject(jump);
        data.FindProperty("target").objectReferenceValue = target;
        data.FindProperty("emergeOnEnable").boolValue = true;
        data.FindProperty("emergenceDepth").floatValue = 1.2f;
        data.FindProperty("initialRandomDelay").boolValue = false;
        data.FindProperty("minimumWaitTime").floatValue = 1f;
        data.FindProperty("maximumWaitTime").floatValue = 2f;
        data.FindProperty("forwardDistance").floatValue = 3f;
        data.FindProperty("stoppingDistance").floatValue = 2f;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    static Bounds BodyBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    static Vector3 PointAlongLava(ProgressiveLavaFlow flow, Terrain terrain, float fraction)
    {
        var points = new Vector3[201];
        var distances = new float[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float t = i / (float)(points.Length - 1);
            points[i] = (Vector3)flow.source.m_Container.EvaluatePosition(flow.reverse ? 1f - t : t);
            points[i].y = terrain.SampleHeight(points[i]) + terrain.transform.position.y;
            if (i > 0) distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        }
        float distance = distances[distances.Length - 1] * fraction;
        for (int i = 1; i < points.Length; i++)
            if (distances[i] >= distance) return Vector3.Lerp(points[i - 1], points[i],
                Mathf.InverseLerp(distances[i - 1], distances[i], distance));
        return points[points.Length - 1];
    }
}
