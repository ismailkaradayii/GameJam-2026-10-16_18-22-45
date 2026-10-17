using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit visual review and matched-view water benchmark. Never saves gameplay changes.</summary>
[InitializeOnLoad]
public static class VolcanoPolishReviewChecks
{
    const string Active = "GameJam.PolishReview.Active";
    const string Phase = "GameJam.PolishReview.Phase";
    const string Folder = "Logs/PolishReview";
    [Serializable] public sealed class Report
    {
        public string status, startedUtc, finishedUtc, error;
        public bool allPassed;
        public string measurement = "Editor Game View frame times at one stationary camera; off/on six seconds each, no eruption. Visual fixtures use a stationary truck and simulated completion, not a road playthrough.";
        public float waterOffMeanMs, waterOffMedianMs, waterOnMeanMs, waterOnMedianMs;
        public int waterOffFrames, waterOnFrames, maximumWaterPackets;
        public Vector3 waterMuzzle, waterLaunchVelocity, waterImpact;
        public float waterImpactDistance;
        public int visibleStreamPoints, visibleDroplets;
        public List<string> images = new List<string>();
        public List<string> errors = new List<string>();
    }
    static Report report;
    static RescueGameFlow flow;
    static RescueHUD hud;
    static FireRescuePlayer player;
    static FireTruckWaterCannon cannon;
    static readonly List<float> off = new List<float>(), on = new List<float>();
    static double deadline;
    static int frame = -1;
    static int sampleAfterFrame;
    static bool waterCaptured, originalBackground;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static VolcanoPolishReviewChecks()
    {
        EditorApplication.update += Update;
        Application.logMessageReceived += OnLog;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Active, false))
            {
                SessionState.SetBool(Active, false);
                Application.runInBackground = originalBackground;
                Time.timeScale = 1f;
                if (report != null && report.status == "running") Finish("Play Mode stopped during the review.");
            }
        };
    }

    [MenuItem("GameJam/Integration/Review polish HUD and water performance")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Start polish review in Edit Mode after compilation.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save open scenes before reviewing.");
        SessionState.SetBool(Active, true);
        SessionState.SetString(Phase, "Start");
        EditorSceneManager.OpenScene(VolcanoTeamIntegrationSetup.VolcanoPath);
        EditorApplication.EnterPlaymode();
    }
    static void Next(string phase, double seconds)
    {
        SessionState.SetString(Phase, phase);
        deadline = EditorApplication.timeSinceStartup + seconds;
    }
    static void Update()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isPaused) return;
        try
        {
            string phase = SessionState.GetString(Phase, "");
            if (phase == "Start")
            {
                report = new Report { status = "running", startedUtc = DateTime.UtcNow.ToString("O") };
                originalBackground = Application.runInBackground;
                Application.runInBackground = true;
                off.Clear(); on.Clear(); waterCaptured = false; frame = -1;
                Next("Settle", 3); return;
            }
            if (phase == "Settle")
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                flow = Object.FindFirstObjectByType<RescueGameFlow>();
                hud = Object.FindFirstObjectByType<RescueHUD>();
                player = Object.FindFirstObjectByType<FireRescuePlayer>();
                if (!flow || !flow.IsConfigured || !hud || !player) throw new InvalidOperationException("Polished scene references are missing.");
                cannon = player.cannon;
                hud.eruption.enabled = false; // Match both benchmark passes at exactly the same world state.
                player.enabled = false;
                player.SetDriving(true);
                player.drive.body.isKinematic = true;
                // Use the normal raised RMB camera, aimed across the lake, for both passes.
                typeof(FireRescuePlayer).GetField("orbitYaw", Private).SetValue(player, 90f);
                typeof(FireRescuePlayer).GetField("lookPitch", Private).SetValue(player, 5f);
                typeof(FireRescuePlayer).GetMethod("SetCannonActive", Private).Invoke(player, new object[] { true });
                typeof(FireRescuePlayer).GetMethod("UpdateDrivingCamera", Private).Invoke(player, null);
                cannon.SetSpraying(false);
                Next("CapturePlaying", 1); return;
            }
            if (phase == "CapturePlaying")
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                CapturePair("playing");
                player.hose.SetTankLitres(2000f);
                sampleAfterFrame = Time.frameCount + 3;
                Next("Off", 6); return;
            }
            if (phase == "Off" || phase == "On")
            {
                if (frame != Time.frameCount && Time.frameCount > sampleAfterFrame)
                {
                    frame = Time.frameCount;
                    (phase == "Off" ? off : on).Add(Time.unscaledDeltaTime * 1000f);
                    report.maximumWaterPackets = Mathf.Max(report.maximumWaterPackets, cannon.InFlightPacketCount);
                }
                if (phase == "On" && !waterCaptured && EditorApplication.timeSinceStartup > deadline - 3)
                {
                    report.waterMuzzle = cannon.muzzle.position;
                    report.waterLaunchVelocity = cannon.LaunchVelocity;
                    report.waterImpact = cannon.AimPoint;
                    report.waterImpactDistance = Vector3.Distance(cannon.muzzle.position, cannon.AimPoint);
                    report.visibleStreamPoints = cannon.waterJet ? cannon.waterJet.positionCount : 0;
                    report.visibleDroplets = cannon.sprayParticles ? cannon.sprayParticles.particleCount : 0;
                    CapturePair("water"); waterCaptured = true;
                    // Render-readback is excluded from the performance samples.
                    on.Clear(); sampleAfterFrame = Time.frameCount + 3; Next("On", 6); return;
                }
                if (EditorApplication.timeSinceStartup < deadline) return;
                if (phase == "Off") { cannon.SetSpraying(true); Next("On", 6); }
                else { cannon.SetSpraying(false); Next("Drain", 10); }
                return;
            }
            if (phase == "Drain")
            {
                if (cannon.InFlightPacketCount > 0 && EditorApplication.timeSinceStartup < deadline) return;
                flow.Pause(); Next("CapturePaused", .3); return;
            }
            if (phase == "CapturePaused")
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                if (!hud.PauseVisible || Time.timeScale != 0f) throw new InvalidOperationException("Pause panel was not visible.");
                CapturePair("paused"); flow.Resume(); player.hose.SetTankLitres(100f);
                Next("CaptureLowWater", .3); return;
            }
            if (phase == "CaptureLowWater")
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                CapturePair("low-water");
                player.hose.SetTankLitres(0f);
                CapturePair("empty-tank");
                foreach (var house in flow.targetBuildings) { house.TickFire(100f); house.Ignite(); house.ApplyWater(house.litresToExtinguish); }
                Next("CaptureWon", 2); return;
            }
            if (phase == "CaptureWon")
            {
                if (flow.State != RescueGameFlow.GameState.Won)
                {
                    if (EditorApplication.timeSinceStartup >= deadline) throw new InvalidOperationException("Victory did not appear after five visual-fixture rescues.");
                    return;
                }
                if (!hud.VictoryVisible) return;
                CapturePair("won");
                report.waterOffFrames = off.Count; report.waterOnFrames = on.Count;
                report.waterOffMeanMs = Mean(off); report.waterOffMedianMs = Median(off);
                report.waterOnMeanMs = Mean(on); report.waterOnMedianMs = Median(on);
                Finish(null);
            }
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    static float Mean(List<float> values) => values.Count > 0 ? values.Average() : 0;
    static float Median(List<float> values) { if (values.Count == 0) return 0; var sorted = values.OrderBy(x => x).ToArray(); return sorted[sorted.Length / 2]; }
    static void CapturePair(string name)
    {
        Capture(name, 1280, 720);
        Capture(name, 1920, 1080);
    }
    static void Capture(string name, int width, int height)
    {
        Directory.CreateDirectory(Folder);
        var camera = player.viewCamera;
        var canvas = hud.canvas;
        var savedTarget = camera.targetTexture;
        var savedMode = canvas.renderMode;
        var savedCamera = canvas.worldCamera;
        float savedPlane = canvas.planeDistance, savedAspect = camera.aspect;
        var active = RenderTexture.active;
        var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = texture; camera.aspect = (float)width / height;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .2f;
            typeof(RescueHUD).GetMethod("LateUpdate", Private).Invoke(hud, null);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            string path = Folder + "/" + name + "-" + height + "p.png";
            File.WriteAllBytes(path, pixels.EncodeToPNG()); report.images.Add(path);
        }
        finally
        {
            RenderTexture.active = active;
            camera.targetTexture = savedTarget; camera.aspect = savedAspect;
            canvas.renderMode = savedMode; canvas.worldCamera = savedCamera; canvas.planeDistance = savedPlane;
            Canvas.ForceUpdateCanvases();
            texture.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(pixels);
        }
    }
    static void OnLog(string message, string stack, LogType type)
    {
        if (SessionState.GetBool(Active, false) && report != null && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            report.errors.Add(message);
    }
    static void Finish(string error)
    {
        SessionState.SetBool(Active, false);
        if (report != null)
        {
            report.error = error; report.allPassed = string.IsNullOrEmpty(error) && report.errors.Count == 0 && report.images.Count == 12;
            report.status = report.allPassed ? "passed" : "failed"; report.finishedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/review.json", JsonUtility.ToJson(report, true));
            Debug.Log("POLISH_REVIEW " + report.status + ": " + Folder + "/review.json");
        }
        Application.runInBackground = originalBackground;
        Time.timeScale = 1f;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
