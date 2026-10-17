using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Idempotent, explicit authoring in the existing Volcano scene. No asset downloads.</summary>
public static class FinalLoopSetup
{
    public const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string AssetFolder = "Assets/GameJam/FireRescue/Materials";
    static readonly Color Panel = new Color(.035f, .048f, .065f, .91f);
    static readonly Color Blue = new Color(.18f, .7f, 1f);
    static Font font;
    [MenuItem("GameJam/Final Loop/Apply")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring.");
        if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        var scene = SceneManager.GetActiveScene();
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var evacuation = Object.FindFirstObjectByType<EvacuationMission>();
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
        var hud = Object.FindFirstObjectByType<RescueHUD>();
        if (!player || !player.firstPerson || !player.pistol || !player.drive || !mission || !evacuation || !sequence || !encounter || !hud)
            throw new InvalidOperationException("Expected the existing integrated player, truck, evacuation, eruption and HUD.");
        var houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).OrderBy(h => h.name, StringComparer.Ordinal).ToArray();
        var firstHouse = houses.First(h => h.name == "House_Green_Prefab");
        var bed = GameObject.Find("BedSingle01empty");
        var cabinet = GameObject.Find("Cabinet04 (1)");
        if (!bed || !cabinet) throw new InvalidOperationException("The authored bedroom furniture is missing.");
        var root = GameObject.Find("Final Loop Scene Bindings") ?? new GameObject("Final Loop Scene Bindings");
        mission.player = player; mission.evacuation = evacuation; mission.sequence = sequence; mission.tank = player.hose;
        mission.encounter = encounter; mission.gameFlow = hud.flow; mission.cinematic = sequence.cinematic;
        mission.preparationSeconds = 120; mission.skipVillageWarning = true; mission.exitRadius = 9;
        mission.startingHouse = firstHouse.ignitionBounds;
        mission.houseExit = GameObject.Find("House 1 exit").transform; mission.houseExit.rotation = firstHouse.transform.rotation;
        mission.standingSpawn = Point(root.transform, "Bedroom standing spawn", new Vector3(26.8f, 1.04f, -605.9f));
        mission.standingSpawn.rotation = Quaternion.Euler(0, 150, 0);
        mission.wakePose = Point(root.transform, "Wake camera on bed", bed.transform.position + new Vector3(0, 2.6f, .7f));
        mission.wakePose.rotation = Quaternion.Euler(-42, 150, 65);
        player.transform.SetPositionAndRotation(mission.standingSpawn.position, mission.standingSpawn.rotation);
        player.requiresWeaponPickup = true; player.mission = mission; player.startDriving = false; player.showHud = false;
        ConfigureMovementPace(player);
        player.vehicleCameraBlendSeconds = .6f;
        player.pistol.showHud = true; player.pistol.showAmmoHud = false; player.pistol.reserveAmmo = 72;
        player.pistol.reloadDuration = 2.1f;
        player.hose.startingTankLitres = 0; player.hose.fillRateLitresPerSecond = 250;
        player.cannon.flowLitresPerSecond = 45;
        evacuation.startAutomatically = false; evacuation.showHud = false; evacuation.shoutCooldown = 1.5f; evacuation.shoutRadius = 58;
        foreach (var resident in evacuation.residents)
        {
            resident.walkingSpeed = 4.5f; resident.runningSpeed = 9.5f; resident.catchUpSpeed = 28f; resident.reactionSeconds = .65f;
            Dirty(resident);
        }
        sequence.autoErupt = false; sequence.warningSeconds = 1.3f; sequence.lavaReleaseDelay = 2.2f; sequence.showHud = false;
        mission.cinematic.player = player; mission.cinematic.introSeconds = 2.2f; mission.cinematic.eruptionSeconds = 7;
        mission.cinematic.spiritSeconds = 2.6f; mission.cinematic.spiritViewingDistance = 14; mission.spiritRevealDelay = 1;
        mission.cinematic.startOffset = new Vector3(340, 320, -610); mission.cinematic.endOffset = new Vector3(240, 250, -470);
        foreach (var house in houses) { house.startBurning = false; house.secondsToCollapse = 85; Dirty(house); }
        encounter.waveSizes = new[] { 4, 4, 5 }; encounter.secondsBetweenWaves = 20; encounter.secondsBetweenSpawns = 2.2f; encounter.maximumAlive = 9;
        var approach = sequence.routes.Select(r => r.flow).First(f => f && f.name == "Town Approach");
        for (int i = 0; i < encounter.spawns.Length; i++)
        {
            var spawn = encounter.spawns[i]; spawn.feed = approach; spawn.feedProgress = .18f + i * .06f;
            spawn.descendFromLava = true; spawn.downhillFlows = Array.Empty<ProgressiveLavaFlow>(); spawn.descentEndProgress = .8f;
        }
        var survival = Ensure<VolcanoSurvivalObjective>(encounter.gameObject);
        survival.encounter = encounter; survival.player = player; survival.showHud = false;
        var vitals = Ensure<CreatureCombatPlayer>(player.gameObject);
        survival.vitals = vitals; vitals.allowRecovery = false;
        // Use the authored route dependency network, with a short dramatic mountain descent.
        foreach (var route in sequence.routes)
            if (route.flow) { route.flow.speed = route.flow.name.StartsWith("Town Branch") ? 5.5f : route.flow.name == "Town Approach" ? 14f : 45f; Dirty(route.flow); }
        ConfigurePickup(root.transform, mission, cabinet);
        ConfigureTruck(player, firstHouse.transform, root.transform);
        ConfigureLake(root.transform, mission);
        ConfigureHud(hud, mission, root.transform);
        RemoveLegacyShoutAudio(evacuation);
        foreach (var component in new Object[] { player, player.transform, player.firstPerson, player.pistol, player.hose, player.cannon,
            mission, evacuation, sequence, mission.cinematic, encounter, survival, vitals, hud, hud.flow }) Dirty(component);
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Validate();
    }
    [MenuItem("GameJam/Final Loop/Refresh menu help")]
    public static void RefreshMenuHelp()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        string activePath = SceneManager.GetActiveScene().path;
        var menuScene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        var replacements = new Dictionary<string, string>
        {
            { "Önce halkı sığınağa götür; ardından kasabayı yangından koru.", "Hazırlanmak için 120 saniyen var. Ardından köyü savun." },
            { "Aracın arkasından hortumu al; göle bakıp ucunu bırak.", "Araçla MAVİ göl halkasında dur. E bağlantıyı kurar; depo dolunca hortum otomatik toplanır." },
            { "Hortum ucu göldeyken depoyu doldurmak için pompayı aç.", "İstersen yaya halde aracın arkasından hortumu al, ucunu göle bırak ve P ile pompayı aç." },
            { "1 silahı çek/kaldır, 0 silahı kaldır. Hortum taşırken silah kapanır.", "Komodindeki tabancayı E ile al. 1 silahı çek/kaldır, 0 silahı kaldır. Araçtan inince silah geri gelir." }
        };
        foreach (var text in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (text.gameObject.scene != menuScene) continue;
            if (text.text.Contains("250 litre suyla")) text.text = "Yatak odasındaki tabancayı E ile al ve evden çık. Q ile köylüleri çağır; YEŞİL halkalı sığınağa götür. Evin arkasındaki itfaiyeye bin, MAVİ göl işaretinde E ile depoyu doldur. 120 saniye dolunca hazırlığın bitmese de volkan patlar. Üç alev ruhu dalgasını durdur, yanan evleri söndür. En az bir ev ayakta kalırsa kazanırsın; bütün evler yıkılırsa kaybedersin. Sıcak lavdan uzak dur.";
            else if (replacements.TryGetValue(text.text, out string replacement)) text.text = replacement;
            else continue;
            EditorUtility.SetDirty(text);
        }
        EditorSceneManager.MarkSceneDirty(menuScene); EditorSceneManager.SaveScene(menuScene);
        if (!string.IsNullOrEmpty(activePath)) EditorSceneManager.OpenScene(activePath);
    }
    static void ConfigurePickup(Transform root, VolcanoMissionDirector mission, GameObject cabinet)
    {
        var t = Point(root, "Bedroom Pistol Pickup", new Vector3(29.4f, 3.85f, -609.5f));
        var pickup = Ensure<WeaponPickup>(t.gameObject);
        pickup.player = mission.player; pickup.pickupDistance = 7.5f;
        if (!pickup.visual)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/A3500X/Models/A3500X black.fbx");
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(model, t);
            gun.name = "Pickup A3500X"; gun.transform.localRotation = Quaternion.Euler(0, 35, 90);
            var renderers = gun.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            gun.transform.localScale *= 1.35f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            var black = AssetDatabase.LoadAssetAtPath<Material>("Assets/Itfaiye/Materials/A3500X black.mat");
            foreach (var r in renderers) { if (black) r.sharedMaterials = r.sharedMaterials.Select(_ => black).ToArray(); Dirty(r); }
            bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            gun.transform.position += new Vector3(t.position.x - bounds.center.x, t.position.y - bounds.min.y, t.position.z - bounds.center.z);
            Dirty(gun.transform); pickup.visual = gun;
            var glow = t.gameObject.AddComponent<Light>(); glow.type = LightType.Point; glow.color = Blue; glow.range = 4; glow.intensity = 2; glow.shadows = LightShadows.None;
        }
        mission.weaponPickup = pickup; Dirty(pickup);
    }
    static void ConfigureTruck(FireRescuePlayer player, Transform house, Transform root)
    {
        var drive = player.drive;
        // Bake an 8% enlargement into matching geometry and colliders. Rigidbody root remains unit scale.
        if (!root.Find("Truck dimensions authored v1"))
        {
            const float factor = 1.08f;
            foreach (Transform child in drive.transform)
            {
                child.localPosition *= factor;
                var wheel = child.GetComponent<WheelCollider>();
                if (wheel) { wheel.radius *= factor; wheel.center *= factor; wheel.suspensionDistance *= factor; child.localScale = Vector3.one; Dirty(wheel); }
                else child.localScale *= factor;
                Dirty(child);
            }
            foreach (var box in drive.GetComponents<BoxCollider>()) { box.center *= factor; box.size *= factor; Dirty(box); }
            Point(root, "Truck dimensions authored v1", Vector3.zero).gameObject.tag = "EditorOnly";
        }
        drive.transform.localScale = Vector3.one;
        Vector3 parking = Ground(new Vector3(43, 0, -620)) + Vector3.up * .65f;
        drive.transform.SetPositionAndRotation(parking, Quaternion.Euler(0, 0, 0));
        ConfigureTruckPace(drive);
        player.followDistance = 19; player.maxFollowDistance = 24; player.followHeight = 5.5f;
        Dirty(drive); Dirty(drive.transform);
    }
    [MenuItem("GameJam/Final Loop/Apply speed and audio")]
    public static void ApplySpeedAndAudio()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Volcano_Demo first.");
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        var evacuation = Object.FindFirstObjectByType<EvacuationMission>();
        if (!player || !player.firstPerson || !player.drive || !evacuation)
            throw new InvalidOperationException("The existing player, truck and evacuation references are required.");
        ConfigureMovementPace(player);
        ConfigureTruckPace(player.drive);
        foreach (var resident in evacuation.residents)
            if (resident) { resident.catchUpSpeed = 28f; Dirty(resident); }
        RemoveLegacyShoutAudio(evacuation);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Pace/audio updated: walk 12 m/s, sprint 24 m/s, truck 110 km/h, voice and movement loops removed.");
    }
    static void ConfigureMovementPace(FireRescuePlayer player)
    {
        player.walkSpeed = player.firstPerson.walkSpeed = 12f;
        player.sprintSpeed = player.firstPerson.runSpeed = 24f;
        player.firstPerson.acceleration = 60f;
        player.firstPerson.braking = 70f;
        Dirty(player); Dirty(player.firstPerson);
    }
    internal static void ConfigureTruckPace(FireTruckDrive drive)
    {
        drive.motorTorque = 20800; drive.lowSpeedTorqueMultiplier = 2f; drive.torqueBoostFadeSpeedKph = 45.5f;
        drive.maxForwardSpeedKph = 156; drive.maxReverseSpeedKph = 58.5f;
        drive.throttleResponsePerSecond = 18; drive.throttleReleasePerSecond = 20;
        drive.groundAccelerationAssist = 18.2f; drive.accelerationAssistFadeSpeedKph = 104;
        drive.maxSteerAngle = 46; drive.highSpeedSteerAngle = 22; drive.steeringSpeed = 240;
        drive.steeringFadeSpeedKph = 130;
        drive.lowSpeedRearSteerRatio = .6f; drive.rearSteerFadeSpeedKph = 58.5f;
        drive.arcadeHandling = true; drive.arcadeTurnRadius = 8;
        drive.arcadeLowSpeedYawRate = 143; drive.arcadeHighSpeedYawRate = 71.5f;
        drive.arcadeSteeringResponse = 12; drive.arcadeLateralGrip = 13;
        drive.arcadeBrakeAcceleration = 31.2f; drive.arcadeUprightStrength = 45;
        drive.arcadeCornerSpeedKph = 54.6f;
        drive.obstacleStepHeight = 2.4f; drive.obstacleLiftAcceleration = 65;
        drive.wheelSpinFromTravel = true;
        drive.brakeTorque = 46800; drive.parkingBrakeTorque = 40000; drive.coastBrakeTorque = 140;
        drive.allWheelDrive = true; drive.smoothPedals = true; drive.hillHoldAssist = true;
        Dirty(drive);
    }
    static void RemoveLegacyShoutAudio(EvacuationMission evacuation)
    {
        // This source was dedicated to the removed EveryoneOutside voice line.
        var voice = evacuation.GetComponent<AudioSource>();
        if (voice) Object.DestroyImmediate(voice);
        Dirty(evacuation);
    }
    [MenuItem("GameJam/Final Loop/Parking audit")]
    public static void ParkingAudit()
    {
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        var house = GameObject.Find("House_Green_Prefab").transform;
        var box = player.drive.GetComponent<BoxCollider>();
        var lines = new List<string>();
        Physics.SyncTransforms();
        for (float x = -25; x < 60; x += 4) for (float z = -660; z < -605; z += 4)
        {
            var point = Ground(new Vector3(x, 0, z));
            if (point.y > 4 || house.InverseTransformPoint(point).z > -5) continue;
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
            {
                var rotation = Quaternion.Euler(0, yaw, 0);
                float low = point.y, high = point.y;
                foreach (float cx in new[] { -2.5f, 2.5f }) foreach (float cz in new[] { -8f, 8f })
                { float y = Ground(point + rotation * new Vector3(cx, 0, cz)).y; low = Mathf.Min(low, y); high = Mathf.Max(high, y); }
                var overlaps = Physics.OverlapBox(point + Vector3.up * .65f + rotation * box.center, box.size * .5f + Vector3.one * .15f, rotation, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => !(c is TerrainCollider) && !c.transform.IsChildOf(player.drive.transform) && !c.transform.IsChildOf(player.transform)).ToArray();
                if (overlaps.Length == 0 && high - low < 1.5f)
                    lines.Add($"CLEAR point={point} yaw={yaw} heightRange={high-low:F2} rearLocal={house.InverseTransformPoint(point)}");
            }
        }
        File.WriteAllLines("Logs/FinalLoop-parking.txt", lines);
    }
    static void ConfigureLake(Transform root, VolcanoMissionDirector mission)
    {
        var t = Point(root, "Lake refill parking", Ground(new Vector3(348.6f, 0, -604.8f)));
        var station = Ensure<LakeFillStation>(t.gameObject);
        station.player = mission.player; station.water = Object.FindFirstObjectByType<WaterSourceVolume>();
        station.parkingRadius = 14;
        // Select an actual submerged sample in reach of the authored lake parking station.
        Vector3 chosen = Vector3.zero; float best = float.PositiveInfinity;
        for (float x = t.position.x - 38; x <= t.position.x + 38; x += 1)
            for (float z = t.position.z - 38; z <= t.position.z + 38; z += 1)
            {
                Vector3 candidate = new Vector3(x, station.water.SurfaceY - .12f, z);
                float d = (candidate - t.position).sqrMagnitude;
                if (d < best && station.water.ContainsIntake(candidate)) { best = d; chosen = candidate; }
            }
        if (float.IsPositiveInfinity(best)) throw new InvalidOperationException("No reachable submerged intake beside the authored parking.");
        station.intakePoint = Point(root, "Lake submerged intake", chosen);
        mission.fillStation = station; Dirty(station);
    }
    static void ConfigureHud(RescueHUD hud, VolcanoMissionDirector mission, Transform bindings)
    {
        hud.mission = mission;
        Transform canvas = hud.canvas.transform;
        // Retain the old UI objects for reversibility, but give the existing HUD one presentation.
        foreach (Transform child in canvas) child.gameObject.SetActive(false);
        var existing = canvas.Find("Final Loop HUD");
        if (existing) Object.DestroyImmediate(existing.gameObject);
        var root = new GameObject("Final Loop HUD", typeof(RectTransform)).GetComponent<RectTransform>(); root.SetParent(canvas, false); Stretch(root);
        var scaler = hud.canvas.GetComponent<CanvasScaler>(); scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        var objective = Box(root, "Objective", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -30), new Vector2(470, 225));
        hud.objectiveGroup = objective.gameObject.AddComponent<CanvasGroup>();
        Label(objective, "Heading", "OBJECTIVES", 15, new Vector2(20, -12), new Vector2(430, 25)).color = Blue;
        hud.objectiveText = Label(objective, "Title", "", 23, new Vector2(20, -44), new Vector2(430, 65));
        hud.objectiveDetail = Label(objective, "Detail", "", 18, new Vector2(20, -112), new Vector2(430, 62));
        hud.progressText = Label(objective, "Progress", "", 17, new Vector2(20, -180), new Vector2(430, 40));
        var timer = Box(root, "Volcano timer", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -30), new Vector2(660, 68));
        hud.eruptionText = Label(timer, "Timer", "VOLCANO ERUPTION IN: 02:00", 30, new Vector2(10, -10), new Vector2(640, 48), TextAnchor.MiddleCenter);
        var water = Box(root, "Vehicle resources", Vector2.one, Vector2.one, new Vector2(-30, -30), new Vector2(310, 134));
        hud.waterGroup = water.gameObject;
        Label(water, "Label", "WATER TANK", 16, new Vector2(18, -10), new Vector2(270, 25)).color = Blue;
        hud.waterText = Label(water, "Amount", "", 22, new Vector2(18, -38), new Vector2(270, 32));
        hud.waterFill = Fill(water, "Water fill", new Vector2(18, -78), new Vector2(274, 9));
        hud.pumpText = Label(water, "Pump", "", 14, new Vector2(18, -94), new Vector2(274, 34));
        var prompt = Box(root, "Context prompt", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 26), new Vector2(1100, 55));
        hud.promptText = Label(prompt, "Text", "", 20, new Vector2(18, -8), new Vector2(1064, 39), TextAnchor.MiddleCenter);
        var equipment = Box(root, "Weapon slot", Vector2.zero, Vector2.zero, new Vector2(30, 100), new Vector2(440, 50));
        hud.equipmentGroup = equipment.gameObject;
        hud.equipmentText = Label(equipment, "Ammo", "", 20, new Vector2(18, -7), new Vector2(405, 36));
        hud.speedText = Label(root, "Speed", "", 20, Vector2.zero, new Vector2(180, 35), TextAnchor.MiddleRight);
        var speedRect = hud.speedText.rectTransform; speedRect.anchorMin = speedRect.anchorMax = speedRect.pivot = new Vector2(1, 0); speedRect.anchoredPosition = new Vector2(-30, 26);
        hud.healthText = Label(root, "Health", "", 20, Vector2.zero, new Vector2(280, 35));
        var healthRect = hud.healthText.rectTransform; healthRect.anchorMin = healthRect.anchorMax = healthRect.pivot = Vector2.zero; healthRect.anchoredPosition = new Vector2(30, 160);
        var warning = Box(root, "Low water", Vector2.one, Vector2.one, new Vector2(-30, -178), new Vector2(310, 44));
        hud.lowWaterWarning = warning.gameObject; hud.lowWaterBackground = warning.GetComponent<Image>();
        hud.lowWaterText = Label(warning, "Text", "", 15, new Vector2(10, -6), new Vector2(290, 32));
        var toast = Box(root, "Toast", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -112), new Vector2(550, 45));
        toast.GetComponent<Image>().enabled = false;
        hud.toastText = Label(toast, "Text", "", 22, new Vector2(10, -5), new Vector2(530, 35), TextAnchor.MiddleCenter); hud.toastText.color = new Color(.3f, 1, .55f);
        var target = Box(root, "Fire progress", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 100), new Vector2(400, 70));
        hud.targetGroup = target.gameObject;
        hud.targetText = Label(target, "Text", "", 18, new Vector2(12, -8), new Vector2(376, 30), TextAnchor.MiddleCenter);
        hud.targetFill = Fill(target, "Fill", new Vector2(20, -48), new Vector2(360, 8));
        var reticle = Box(root, "Water reticle", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(16, 2));
        hud.impactReticle = reticle.GetComponent<Image>(); hud.impactReticle.color = Blue;
        hud.pausePanel = Modal(root, "Pause", "PAUSED", out var pauseCard);
        hud.resumeButton = Button(pauseCard, "Resume", "RESUME", new Vector2(50, -190));
        hud.pauseMenuButton = Button(pauseCard, "Menu", "MAIN MENU", new Vector2(50, -270));
        hud.victoryPanel = Modal(root, "Result", "", out var resultCard);
        hud.resultTitle = resultCard.Find("Title").GetComponent<Text>();
        hud.resultDetail = Label(resultCard, "Statistics", "", 23, new Vector2(50, -120), new Vector2(540, 115), TextAnchor.MiddleCenter);
        hud.restartButton = Button(resultCard, "Restart", "RESTART", new Vector2(50, -275));
        hud.victoryMenuButton = Button(resultCard, "Menu", "MAIN MENU", new Vector2(50, -355));
        var nav = Ensure<ObjectiveNavigation>(hud.gameObject);
        nav.mission = mission; nav.canvas = hud.canvas;
        var marker = Box(root, "Active destination", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(250, 86));
        marker.GetComponent<Image>().color = new Color(.02f, .035f, .045f, .8f); nav.marker = marker;
        nav.arrow = Label(marker, "Direction", "", 26, new Vector2(108, 0), new Vector2(34, 34), TextAnchor.MiddleCenter);
        nav.arrow.rectTransform.pivot = new Vector2(.5f, .5f); nav.arrow.rectTransform.anchoredPosition = new Vector2(125, -17);
        nav.label = Label(marker, "Destination", "", 17, new Vector2(8, -32), new Vector2(234, 50), TextAnchor.MiddleCenter);
        // Keep navigation behind modals.
        hud.pausePanel.transform.SetAsLastSibling(); hud.victoryPanel.transform.SetAsLastSibling();
        var ringObject = Point(bindings, "Active objective world ring", Vector3.zero);
        nav.ring = Ensure<LineRenderer>(ringObject.gameObject);
        nav.ring.useWorldSpace = true; nav.ring.widthMultiplier = .22f; nav.ring.loop = false;
        string materialPath = AssetFolder + "/ObjectiveRing.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); material.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        nav.ring.sharedMaterial = material; nav.ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        nav.ring.receiveShadows = false;
        // Existing authored road samples are stored as scene waypoints, with no runtime file dependency.
        if (File.Exists("Logs/VolcanoVehiclePolishSetup.json"))
        {
            var road = JsonUtility.FromJson<VolcanoVehiclePolishSetup.Report>(File.ReadAllText("Logs/VolcanoVehiclePolishSetup.json")).roadCentre;
            var route = Point(bindings, "Lake road guidance", Vector3.zero);
            var samples = new List<Transform>();
            for (int i = 0; i < road.Count; i += 8) samples.Add(Point(route, "Road " + samples.Count, road[i]));
            foreach (var point in new[] { new Vector3(90, 0, -596), new Vector3(75, 0, -588), new Vector3(54, 0, -592), new Vector3(43, 0, -603), new Vector3(43, 0, -620) })
                samples.Add(Point(route, "Road " + samples.Count, Ground(point)));
            nav.lakeRoad = samples.ToArray();
        }
        Dirty(nav); Dirty(nav.ring);
    }
    [MenuItem("GameJam/Final Loop/Validate")]
    public static void Validate()
    {
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var hud = Object.FindFirstObjectByType<RescueHUD>();
        var errors = new List<string>();
        if (!mission || !mission.player || !mission.cinematic || !mission.weaponPickup || !mission.houseExit || !mission.standingSpawn || !mission.fillStation) errors.Add("Mission reference missing");
        if (!hud || !hud.restartButton || !hud.resultTitle || !hud.objectiveDetail || !hud.eruptionText) errors.Add("HUD reference missing");
        int missing = 0;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
        if (missing > 0) errors.Add("Missing scripts: " + missing);
        if (mission && mission.fillStation && !mission.fillStation.water.ContainsIntake(mission.fillStation.intakePoint.position)) errors.Add("Intake is not submerged");
        if (mission && mission.player.drive.transform.lossyScale != Vector3.one) errors.Add("Rigidbody root scale is not one");
        File.WriteAllText("Logs/FinalLoop-validation.txt", (errors.Count == 0 ? "PASS" : "FAIL") + "\n" + string.Join("\n", errors) +
            "\nScene: " + SceneManager.GetActiveScene().path + "\nResidents: " + (mission ? mission.evacuation.TotalCount : 0) + "\nMissing scripts: " + missing);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
    }
    static T Ensure<T>(GameObject go) where T : Component { var c = go.GetComponent<T>(); return c ? c : go.AddComponent<T>(); }
    static void Dirty(Object obj) { if (!obj) return; EditorUtility.SetDirty(obj); if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj); }
    static Vector3 Ground(Vector3 p) { foreach (var t in Terrain.activeTerrains) if (t && t.terrainData) { p.y = t.SampleHeight(p) + t.transform.position.y; break; } return p; }
    static Transform Point(Transform parent, string name, Vector3 world) { var t = parent.Find(name); if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); } t.position = world; return t; }
    static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var r = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>(); r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
        r.GetComponent<Image>().color = Panel; r.GetComponent<Image>().raycastTarget = false; return r;
    }
    static Text Label(Transform parent, string name, string value, int size, Vector2 pos, Vector2 dimensions, TextAnchor align = TextAnchor.MiddleLeft)
    {
        var t = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>(); t.transform.SetParent(parent, false);
        var r = t.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = pos; r.sizeDelta = dimensions;
        if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.font = font; t.fontSize = size; t.text = value; t.color = Color.white;
        t.alignment = align; t.raycastTarget = false; return t;
    }
    static Image Fill(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var bg = Box(parent, name + " track", new Vector2(0, 1), new Vector2(0, 1), pos, size); bg.GetComponent<Image>().color = new Color(.15f, .22f, .27f);
        var r = Box(bg, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); Stretch(r);
        var image = r.GetComponent<Image>(); image.color = Blue; image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal; return image;
    }
    static CanvasGroup Modal(Transform parent, string name, string title, out RectTransform card)
    {
        var r = Box(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); Stretch(r); r.GetComponent<Image>().raycastTarget = true;
        var group = r.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.interactable = group.blocksRaycasts = false;
        card = Box(r, "Card", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(640, 470));
        Label(card, "Brand", "ASHFALL", 19, new Vector2(50, -24), new Vector2(540, 30), TextAnchor.MiddleCenter).color = Blue;
        Label(card, "Title", title, 32, new Vector2(35, -65), new Vector2(570, 50), TextAnchor.MiddleCenter); return group;
    }
    static Button Button(Transform parent, string name, string value, Vector2 pos)
    {
        var r = Box(parent, name, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(540, 62));
        var image = r.GetComponent<Image>(); image.color = new Color(.12f, .37f, .52f); image.raycastTarget = true;
        var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = image;
        Label(r, "Label", value, 23, new Vector2(10, -8), new Vector2(520, 46), TextAnchor.MiddleCenter); return b;
    }
    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
}
