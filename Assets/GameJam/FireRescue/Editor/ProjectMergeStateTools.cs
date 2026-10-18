using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>Explicit save/reload boundaries for a project merge; never rebuilds gameplay.</summary>
public static class ProjectMergeStateTools
{
    const string Folder = "Library/TeamMerge1810Current";

    [MenuItem("GameJam/Final Loop/Save all local project changes")]
    public static void Save()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Save project changes in idle Edit Mode.");
        Directory.CreateDirectory(Folder);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            var scene = SceneManager.GetSceneAt(index);
            if (!scene.isLoaded) continue;
            if (string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Name the untitled scene before merging.");
            if (!EditorSceneManager.SaveScene(scene, Folder + "/before-merge-" + stamp + "-" + index + ".unity", true))
                throw new IOException("Could not back up the open scene: " + scene.path);
        }
        if (!EditorSceneManager.SaveOpenScenes()) throw new IOException("Could not save all open scenes.");
        AssetDatabase.SaveAssets();
        File.WriteAllText(Folder + "/saved.txt", "COMPLETED " + DateTime.UtcNow.ToString("O") + "\nSaved all open scenes and pending assets without rebuilding gameplay.\n");
    }

    [MenuItem("GameJam/Final Loop/Reload merged project scene")]
    public static void Reload()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Reload the merged scene in idle Edit Mode.");
        for (int index = 0; index < SceneManager.sceneCount; index++)
            if (SceneManager.GetSceneAt(index).isDirty)
                throw new InvalidOperationException("Preserve new unsaved scene edits before reloading the merge.");
        EditorSceneManager.OpenScene(FinalLoopSetup.ScenePath);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/reloaded.txt", "COMPLETED " + DateTime.UtcNow.ToString("O") + "\n" + SceneManager.GetActiveScene().path);
    }
}
