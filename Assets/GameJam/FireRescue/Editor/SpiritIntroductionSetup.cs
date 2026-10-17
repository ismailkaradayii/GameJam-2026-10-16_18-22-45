using System;
using System.IO;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Small, repeatable scene binding update; shares the existing local editor bridge.</summary>
[InitializeOnLoad]
public static class SpiritIntroductionSetup
{
    public const string Folder = "Logs/SpiritIntroduction";
    static double nextPoll;
    static SpiritIntroductionSetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string request = "Logs/SpiritIntroduction.command";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim();
        if (command != "inspect" && EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        Directory.CreateDirectory(Folder);
        try
        {
            if (command == "apply") Apply();
            else if (command == "checks") SpiritIntroductionChecks.Run();
            else if (command == "opening-audit") EditorApplication.ExecuteMenuItem("GameJam/Story/Audit opening route");
            else if (command == "opening-apply") EditorApplication.ExecuteMenuItem("GameJam/Story/Apply clear opening route");
            else if (command == "opening-previews") EditorApplication.ExecuteMenuItem("GameJam/Story/Preview closer window framing");
            else if (command == "inspect") File.WriteAllText(Folder + "/state.txt", "playing=" + EditorApplication.isPlaying + "; paused=" + EditorApplication.isPaused + "; dirty=" + SceneManager.GetActiveScene().isDirty + "; scene=" + SceneManager.GetActiveScene().path);
            else throw new InvalidOperationException(command);
            File.WriteAllText(Folder + "/command-result.txt", "OK " + command);
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/command-result.txt", ex.ToString()); Debug.LogException(ex); }
    }
    [MenuItem("GameJam/Final Loop/Apply spirit introduction")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode for authoring.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != FinalLoopSetup.ScenePath) throw new InvalidOperationException("Open the existing Volcano_Demo scene.");
        Directory.CreateDirectory(Folder);
        if (!File.Exists(Folder + "/Volcano_Demo.before.unity")) EditorSceneManager.SaveScene(scene, Folder + "/Volcano_Demo.before.unity", true);
        var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var hud = Object.FindFirstObjectByType<RescueHUD>();
        if (!nav || !mission || !mission.cinematic || !hud) throw new InvalidOperationException("Existing mission navigation bindings are missing.");
        Undo.RecordObjects(new Object[] { nav, mission.cinematic, nav.ring, nav.arrow, nav.markerGlow }, "Arrow guidance and first spirit introduction");
        bool hadArrow = nav.directionArrow;
        nav.EnsureDirectionArrow();
        if (!hadArrow && nav.directionArrow) Undo.RegisterCreatedObjectUndo(nav.directionArrow.gameObject, "Objective direction arrow");
        nav.ring.positionCount = 0;
        nav.ring.loop = false;
        nav.ring.numCornerVertices = 2;
        nav.ringBrightness = 2.2f;
        mission.cinematic.spiritTimeScale = .5f;
        mission.cinematic.spiritViewingDistance = 9f;
        mission.cinematic.spiritSeconds = 2.6f;
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/ASHFALL/UI/Fonts/SourceSans3-Regular.ttf");
        if (!font) throw new InvalidOperationException("The project's Turkish-capable subtitle font is missing.");
        foreach (var label in new[] { nav.label, hud.questTitleText, hud.questBodyText, hud.questHintText })
        {
            if (!label) continue;
            Undo.RecordObject(label, "Readable mission guidance");
            label.font = font;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.resizeTextForBestFit = false;
            EditorUtility.SetDirty(label);
        }
        if (hud.subtitleText && hud.subtitleText.transform.parent.parent != hud.canvas.transform)
        {
            // Narrative captions remain readable while the task HUD is hidden for the window shot.
            Undo.SetTransformParent(hud.subtitleText.transform.parent, hud.canvas.transform, "Independent story captions");
            EditorUtility.SetDirty(hud.subtitleText.transform.parent);
        }
        foreach (var value in new Object[] { nav, nav.ring, nav.arrow, nav.markerGlow, nav.directionArrow, mission.cinematic })
            if (value) EditorUtility.SetDirty(value);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Folder + "/setup.txt", "Applied: glowing destination circles, truck road arrows, readable mission copy, real jump freeze-frame and player-paced first-contact briefing.\n" + DateTime.UtcNow.ToString("O"));
    }
}
