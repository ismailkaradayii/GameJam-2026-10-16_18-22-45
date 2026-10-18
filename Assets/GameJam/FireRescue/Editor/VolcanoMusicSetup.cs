using System;
using System.IO;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Installs only the two soundtrack bindings in the already-authored volcano scene.</summary>
public static class VolcanoMusicSetup
{
    public const string PassagePath = "Assets/Art/sesdosya/Music/Volcanic_Passage_2026-10-18T035040.mp3";
    public const string HorizonPath = "Assets/Art/sesdosya/Music/Volcanic_Horizon_2026-10-18T035544.mp3";
    public const string Folder = "Library/VolcanoMusicReview";

    [MenuItem("GameJam/Final Loop/Install adaptive volcano music")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Install the score in idle Edit Mode.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != FinalLoopSetup.ScenePath || scene.isDirty)
            throw new InvalidOperationException("Use the saved authored volcano scene; preserve unsaved edits first.");
        Directory.CreateDirectory(Folder);
        if (!EditorSceneManager.SaveScene(scene, Folder + "/before-music.unity", true))
            throw new IOException("Could not preserve the original scene.");
        ConfigureImporter(PassagePath); ConfigureImporter(HorizonPath);
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        if (!mission) throw new InvalidOperationException("The authored mission is missing.");
        var passage = AssetDatabase.LoadAssetAtPath<AudioClip>(PassagePath);
        var horizon = AssetDatabase.LoadAssetAtPath<AudioClip>(HorizonPath);
        if (!passage || !horizon) throw new InvalidOperationException("Both soundtrack recordings must import successfully.");
        Undo.RecordObject(mission.gameObject, "Install adaptive volcano music");
        var music = mission.GetComponent<VolcanoMusicDirector>();
        if (!music) music = Undo.AddComponent<VolcanoMusicDirector>(mission.gameObject);
        Undo.RecordObject(music, "Bind Passage and Horizon");
        music.mission = mission; music.passage = passage; music.horizon = horizon;
        EditorUtility.SetDirty(music);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the soundtrack bindings.");
        File.WriteAllText(Folder + "/installed.txt", "COMPLETED " + DateTime.UtcNow.ToString("O") +
            "\nPassage: " + passage.length + " seconds\nHorizon: " + horizon.length + " seconds\n" +
            "Stereo streaming; music setting; visible eruption crossfade; foreground priority.\n");
    }

    static void ConfigureImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (!importer) throw new InvalidOperationException("Audio importer missing: " + path);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.Streaming;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = .85f;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = false;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = false;
        importer.loadInBackground = true;
        importer.SaveAndReimport();
    }
}
