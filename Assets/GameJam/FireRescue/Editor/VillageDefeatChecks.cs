using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Exercises village losses and the authored result Canvas in an unsaved Play Mode scene.</summary>
[InitializeOnLoad]
public static class VillageDefeatChecks
{
    const string Folder = "Logs/VillageDefeat", Pending = "VillageDefeatChecks.Pending";
    [Serializable] class Check { public string name, evidence; public bool passed; }
    [Serializable] class Report { public string status = "RUNNING"; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
    static Report report;
    static GameObject interfaceTemplate, fixture;
    static RescueGameFlow flow;
    static RescueHUD hud;
    static EvacuationMission evacuation;
    static VolcanoSurvivalObjective survival;
    static Camera camera;
    static NavMeshDataInstance navigation;
    static bool background;
    static float oldScale;

    static VillageDefeatChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Run; }
        };
    }

    [MenuItem("GameJam/Final Loop/Check village defeat conditions")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/report.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }

    static void Run()
    {
        report = new Report(); background = Application.runInBackground; oldScale = Time.timeScale;
        Application.runInBackground = true; Application.logMessageReceived += Log;
        try
        {
            interfaceTemplate = Object.FindFirstObjectByType<RescueHUD>().gameObject;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            SceneManager.SetActiveScene(SceneManager.CreateScene("Temporary village defeat review"));
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(100, .2f, 100),
                    transform = Matrix4x4.TRS(new Vector3(0, -.1f, 0), Quaternion.identity, Vector3.one) }
            }, new Bounds(Vector3.zero, new Vector3(110, 20, 110)), Vector3.zero, Quaternion.identity);
            navigation = NavMesh.AddNavMeshData(data);
            camera = new GameObject("Result review camera").AddComponent<Camera>();
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .025f, .02f);

            CreateFixture(true);
            CheckThat("A healthy village keeps playing before the eruption", flow.State == RescueGameFlow.GameState.Playing);
            foreach (var resident in evacuation.residents) resident.Thermal.Ignite();
            TickFlow();
            CheckThat("Burning villagers do not count as dead", evacuation.residents.All(r => r.Thermal.IsBurning) && evacuation.DeadCount == 0 && flow.State == RescueGameFlow.GameState.Playing);
            for (int i = 0; i < 6; i++) BurnToDeath(evacuation.residents[i]);
            TickFlow();
            CheckThat("Six deaths with one living villager keep playing", evacuation.DeadCount == 6 && flow.State == RescueGameFlow.GameState.Playing);
            BurnToDeath(evacuation.residents[6]); TickFlow();
            CheckThat("The final fire death automatically ends the game", flow.State == RescueGameFlow.GameState.Lost && flow.DefeatReason == RescueGameFlow.LossReason.AllVillagersLost);
            CheckThat("Loss freezes gameplay and releases the cursor", Time.timeScale == 0 && flow.player.GameplaySuspended && Cursor.lockState == CursorLockMode.None && Cursor.visible);
            survival.TickObjective();
            CheckThat("The survival objective agrees with the village loss", survival.State == VolcanoSurvivalObjective.MissionState.Lost);
            CheckOutcome("BÜTÜN KÖYLÜLER ÖLDÜ", "Köylülerin tamamı yangında hayatını kaybetti.");
            Capture("01-all-villagers-lost");

            CreateFixture(false);
            var safe = evacuation.residents[6];
            typeof(EvacuationResident).GetProperty("State").SetValue(safe, EvacuationResident.ResidentState.Sheltered);
            for (int i = 0; i < 6; i++) BurnToDeath(evacuation.residents[i]);
            TickFlow();
            CheckThat("A sheltered survivor prevents total-villager defeat", safe.IsSafe && evacuation.DeadCount == 6 && flow.State == RescueGameFlow.GameState.Playing);
            flow.FinishMission(true);
            CheckThat("A surviving villager still allows a normal victory", flow.State == RescueGameFlow.GameState.Won && flow.DefeatReason == RescueGameFlow.LossReason.None);

            CreateFixture(true);
            for (int i = 0; i < 4; i++) Collapse(flow.targetBuildings[i]);
            var lastHouse = flow.targetBuildings[4]; lastHouse.Ignite(); lastHouse.TickFire(4.9f); TickFlow();
            CheckThat("The final house must fully collapse before village defeat", flow.targetBuildings.Count(h => h.IsBurnedOut) == 4 && lastHouse.IsBurning && flow.State == RescueGameFlow.GameState.Playing);
            // Decorative buildings outside the objective must not mask the loss of every village house.
            House("Unrelated scenery house", new Vector3(35, 0, 35), fixture.transform);
            lastHouse.TickFire(.2f); TickFlow();
            CheckThat("All five collapsed village houses automatically end the game", flow.State == RescueGameFlow.GameState.Lost && flow.DefeatReason == RescueGameFlow.LossReason.VillageBurned);
            CheckOutcome("KÖY YANDI", "Bütün evler tamamen yandı. Köyü kurtaramadın.");
            Capture("02-village-burned");
            flow.Resume(); flow.FinishMission(true);
            CheckThat("Resume and late victory cannot overwrite defeat", flow.State == RescueGameFlow.GameState.Lost && flow.DefeatReason == RescueGameFlow.LossReason.VillageBurned && Time.timeScale == 0);

            CreateFixture(false);
            foreach (var house in flow.targetBuildings) { house.Ignite(); house.ApplyWater(house.litresToExtinguish); }
            foreach (var resident in evacuation.residents) BurnToDeath(resident);
            TickFlow();
            CheckThat("Total deaths beat a pending house rescue victory", flow.State == RescueGameFlow.GameState.Lost && flow.DefeatReason == RescueGameFlow.LossReason.AllVillagersLost);
            CheckOutcome("BÜTÜN KÖYLÜLER ÖLDÜ", "Köylülerin tamamı yangında hayatını kaybetti.");

            CreateFixture(false);
            foreach (var resident in evacuation.residents) BurnToDeath(resident);
            flow.FinishMission(true);
            CheckThat("A same-frame victory request cannot skip total deaths", flow.State == RescueGameFlow.GameState.Lost && flow.DefeatReason == RescueGameFlow.LossReason.AllVillagersLost);

            CreateFixture(false);
            evacuation.residents = Array.Empty<EvacuationResident>(); TickFlow();
            CheckThat("An empty civilian roster does not cause a false defeat", flow.State == RescueGameFlow.GameState.Playing);
            flow.FinishMission(false);
            CheckThat("Existing player defeat retains its own cause", flow.State == RescueGameFlow.GameState.Lost && flow.DefeatReason == RescueGameFlow.LossReason.PlayerDefeated);
        }
        catch (Exception ex) { report.errors.Add(ex.ToString()); }
        finally
        {
            report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
            File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= Log;
            if (navigation.valid) navigation.Remove();
            Application.runInBackground = background; Time.timeScale = oldScale;
            EditorApplication.ExitPlaymode();
        }
    }

    static void CreateFixture(bool director)
    {
        if (fixture) Object.DestroyImmediate(fixture);
        Time.timeScale = 1;
        fixture = new GameObject("Village defeat fixture"); fixture.SetActive(false);
        var civilianRoot = new GameObject("Civilian roster"); civilianRoot.transform.SetParent(fixture.transform);
        evacuation = civilianRoot.AddComponent<EvacuationMission>(); evacuation.enabled = false;
        evacuation.showHud = false; evacuation.startAutomatically = false;
        evacuation.residents = new EvacuationResident[7];
        for (int i = 0; i < 7; i++)
        {
            var resident = new GameObject("Villager " + (i + 1)); resident.transform.SetParent(civilianRoot.transform);
            resident.transform.localPosition = new Vector3(i * 2 - 6, 0, 0);
            var capsule = resident.AddComponent<CapsuleCollider>(); capsule.height = 2; capsule.center = Vector3.up;
            evacuation.residents[i] = resident.AddComponent<EvacuationResident>(); evacuation.residents[i].enabled = false;
        }
        var playerRoot = new GameObject("Review player"); playerRoot.transform.SetParent(fixture.transform);
        var player = playerRoot.AddComponent<FireRescuePlayer>(); player.enabled = false; player.showHud = false;
        var tank = playerRoot.AddComponent<FireHoseSystem>(); tank.enabled = false; player.hose = tank;
        evacuation.player = player;
        var houses = new BurnableBuilding[5];
        for (int i = 0; i < houses.Length; i++) houses[i] = House("House " + (i + 1), new Vector3(i * 8 - 16, 0, 20), fixture.transform);
        survival = fixture.AddComponent<VolcanoSurvivalObjective>(); survival.showHud = false; survival.player = player;
        var canvasRoot = Object.Instantiate(interfaceTemplate, fixture.transform);
        hud = canvasRoot.GetComponent<RescueHUD>(); flow = canvasRoot.GetComponent<RescueGameFlow>();
        flow.targetBuildings = houses; flow.player = player;
        hud.flow = flow; hud.player = player; hud.hose = tank; hud.evacuation = evacuation; hud.survivalObjective = survival;
        hud.missionDirector = null; hud.drive = null; hud.cannon = null; hud.vitals = null; hud.eruption = null; hud.questTitleText = null;
        if (hud.contextualInteractions) hud.contextualInteractions.enabled = false;
        hud.contextualInteractions = null;
        if (hud.villageIntegrity) hud.villageIntegrity.flow = flow;
        hud.canvas.renderMode = RenderMode.ScreenSpaceCamera; hud.canvas.worldCamera = camera; hud.canvas.planeDistance = 1;
        if (director)
        {
            var mission = fixture.AddComponent<VolcanoMissionDirector>(); mission.enabled = false;
            mission.player = player; mission.evacuation = evacuation; mission.gameFlow = flow; mission.tank = tank;
            hud.missionDirector = mission;
        }
        canvasRoot.SetActive(true); fixture.SetActive(true);
    }
    static BurnableBuilding House(string name, Vector3 position, Transform parent)
    {
        var root = new GameObject(name); root.SetActive(false); root.transform.SetParent(parent); root.transform.position = position;
        var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.up * 2; collider.size = new Vector3(4, 4, 4); collider.isTrigger = true;
        var house = root.AddComponent<BurnableBuilding>(); house.fitFireToBuilding = false; house.secondsToCollapse = 5; house.ignitionBounds = collider;
        root.SetActive(true); return house;
    }
    static void BurnToDeath(EvacuationResident resident)
    {
        // Tick the real thermal damage path; enabled only for this explicit step.
        resident.Thermal.enabled = true; resident.Thermal.fireDamagePerSecond = 400;
        resident.Thermal.Ignite(); resident.Thermal.TickHeat(.25f); resident.Thermal.enabled = false;
    }
    static void Collapse(BurnableBuilding house) { house.Ignite(); house.TickFire(5.1f); }
    static void TickFlow() => flow.SendMessage("Update");
    static void CheckOutcome(string title, string detail)
    {
        hud.SendMessage("LateUpdate");
        CheckThat(title + " appears on the actual result Canvas", hud.VictoryVisible && hud.victoryTitleText.text == title && hud.victoryDetailText.text == detail,
            hud.victoryTitleText.text + " | " + hud.victoryDetailText.text);
        CheckThat(title + " keeps restart and main menu available", hud.outcomeRestartButton.gameObject.activeInHierarchy && hud.victoryMenuButton.gameObject.activeInHierarchy && hud.victoryPanel.interactable && hud.victoryPanel.blocksRaycasts);
    }
    static void Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(1920, 1080, 24); var previous = RenderTexture.active;
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
        File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
    }
    static void CheckThat(string name, bool passed, string evidence = "") => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack);
    }
}
