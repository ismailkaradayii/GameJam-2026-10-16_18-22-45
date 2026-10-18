using System;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class VehicleInteractionSetup
{
    [MenuItem("GameJam/Final Loop/Apply E vehicle interaction")]
    public static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Open Volcano_Demo in Edit Mode.");
        const string backup = "C:/Users/omanc/Documents/ChatGPT/game jam/Artifacts/VehicleInteraction";
        Directory.CreateDirectory(backup);
        if (!File.Exists(backup + "/Before.unity")) EditorSceneManager.SaveScene(scene, backup + "/Before.unity", true);
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        Undo.RecordObject(player, "Share E for vehicle entry and exit");
        player.interactKey = Key.E; player.pumpKey = Key.F;
        EditorUtility.SetDirty(player);
        var words = player.contextualInteractions.words;
        Undo.RecordObject(words, "Describe moving vehicle exit");
        words.stopBeforeExit = "İnmek için hızı 20 km/sa altına düşür";
        EditorUtility.SetDirty(words);
        foreach (var target in player.contextualInteractions.targets.Where(t => t && t.kind == ContextualInteractionKind.TruckExit))
        { Undo.RecordObject(target.gameObject, "Update exit binding label"); target.name = "Truck exit - E"; EditorUtility.SetDirty(target.gameObject); }
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
        var menu = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Additive);
        try
        {
            foreach (var text in menu.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Text>(true)))
            {
                string original = text.text;
                text.text = original.Replace("E / F", "E")
                    .Replace("Press E at the driver's door to enter; stop and press F to exit.", "Press E at the driver's door to enter. Press E to exit below 20 km/h.")
                    .Replace("Sürücü kapısında E ile bin; durduktan sonra F ile in.", "Sürücü kapısında E ile bin; hız 20 km/sa altındayken E ile in.")
                    .Replace("Stop in the BLUE lake ring and press E to connect.", "Stop in the BLUE lake ring and press F to connect. E exits the truck.")
                    .Replace("Araçla MAVİ göl halkasında dur. E bağlantıyı kurar;", "Araçla MAVİ göl halkasında dur. F bağlantıyı kurar; E araçtan indirir.")
                    .Replace("fill its tank with E at the BLUE lake marker.", "fill its tank with F at the BLUE lake marker. E exits below 20 km/h.")
                    .Replace("MAVİ göl işaretinde E ile depoyu doldur.", "MAVİ göl işaretinde F ile depoyu doldur. Hız 20 km/sa altındayken E ile in.");
                if (text.text != original) EditorUtility.SetDirty(text);
            }
            EditorSceneManager.MarkSceneDirty(menu); EditorSceneManager.SaveScene(menu);
        }
        finally { EditorSceneManager.CloseScene(menu, true); }
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/VehicleInteractionSetup.txt", "Saved: E enter/exit, exit only below 20 km/h, F lake filling, updated prompts and menu help.");
    }
}
