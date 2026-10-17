using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit authoring and fixed-camera review of the rescue village.</summary>
public static partial class VillageRescuePolishSetup
{
    public const string Output = "C:/Users/omanc/Documents/ChatGPT/game jam/Artifacts/TruckAndVillage";
    const string ScenePath = TownDressingSetup.ScenePath;

    [MenuItem("GameJam/Final Loop/Village rescue before")]
    public static void Before()
    {
        RequireScene();
        Directory.CreateDirectory(Output + "/Before");
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Output + "/Before/Scene-in-editor.unity", true);
        Audit("Before");
        Capture("Before");
    }

    [MenuItem("GameJam/Final Loop/Village rescue after")]
    public static void After() { RequireScene(); Audit("After"); Capture("After"); }

    public static void CaptureGameplay()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Gameplay capture requires Play Mode.");
        Capture("After/Gameplay");
    }

    static void RequireScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before village authoring.");
        if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open Volcano_Demo first.");
    }

    public static float Ground(Vector3 p)
    {
        foreach (var terrain in Terrain.activeTerrains)
        {
            Vector3 q = p - terrain.transform.position, size = terrain.terrainData.size;
            if (q.x >= 0 && q.z >= 0 && q.x <= size.x && q.z <= size.z)
                return terrain.SampleHeight(p) + terrain.transform.position.y;
        }
        return p.y;
    }

    public static Bounds BoundsOf(Transform t)
    {
        bool first = true; Bounds bounds = new Bounds(t.position, Vector3.zero);
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    static void Audit(string phase)
    {
        Directory.CreateDirectory(Output + "/" + phase);
        var sb = new StringBuilder();
        var scene = SceneManager.GetActiveScene();
        sb.AppendLine($"scene={scene.path} dirty={scene.isDirty} play={EditorApplication.isPlaying}");
        sb.AppendLine($"sky={RenderSettings.skybox} ambient={RenderSettings.ambientMode} skyColor={RenderSettings.ambientSkyColor} equator={RenderSettings.ambientEquatorColor}");
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional))
            sb.AppendLine($"SUN {light.name} enabled={light.enabled} intensity={light.intensity} color={light.color} rotation={light.transform.eulerAngles}");
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).OrderBy(h => h.name))
        {
            sb.AppendLine($"HOUSE {house.name} position={house.transform.position:F3} yaw={house.transform.eulerAngles.y:F2} bounds={BoundsOf(house.transform)} ignition={house.ignitionBounds.bounds}");
            foreach (var door in house.GetComponentsInChildren<Transform>().Where(t => t.name == "opendoor" || t.GetComponent<EvacuationDoor>()))
                sb.AppendLine($" DOOR {door.name} pos={door.position:F2}");
        }
        var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        if (nav)
        {
            foreach (var t in nav.lakeRoad) if (t) sb.AppendLine($"ROAD {t.name} {t.position:F2}");
            foreach (var t in nav.lakeTurnaround) if (t) sb.AppendLine($"TURN {t.name} {t.position:F2}");
        }
        foreach (var r in Object.FindObjectsByType<EvacuationResident>(FindObjectsSortMode.None))
            sb.AppendLine($"RESIDENT {r.name} pos={r.transform.position:F2} exit={r.houseExit.position:F2} door={r.door.transform.position:F2}");
        foreach (var root in scene.GetRootGameObjects())
            sb.AppendLine($"ROOT {root.name} pos={root.transform.position:F2} bounds={BoundsOf(root.transform)}");
        File.WriteAllText(Output + "/" + phase + "/layout.txt", sb.ToString());
    }

    static void Capture(string phase)
    {
        Directory.CreateDirectory(Output + "/" + phase);
        var positions = new[] { new Vector3(-30, 165, -748), new Vector3(-15, 11, -574), new Vector3(44, 13, -593), new Vector3(105, 16, -532) };
        var targets = new[] { new Vector3(42, 0, -577), new Vector3(63, 5, -565), new Vector3(69, 5, -550), new Vector3(65, 5, -574) };
        var names = new[] { "01-village-overview", "02-village-entry", "03-village-square", "04-upper-lane" };
        var go = new GameObject("Temporary village review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        camera.nearClipPlane = .3f; camera.farClipPlane = 2200; camera.fieldOfView = 62;
        camera.allowHDR = true; camera.clearFlags = CameraClearFlags.Skybox;
        var extra = camera.GetUniversalAdditionalCameraData();
        extra.renderPostProcessing = true; extra.renderShadows = true; extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        extra.volumeLayerMask = ~0;
        var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        var hiddenLava = new System.Collections.Generic.List<Renderer>();
        try
        {
            // Review the same calm starting state that the sequencer establishes on entering the game.
            foreach (var flow in Object.FindObjectsByType<ProgressiveLavaFlow>(FindObjectsSortMode.None))
            {
                var renderer = flow.GetComponent<Renderer>();
                if (flow.name.StartsWith("Town") && renderer && renderer.enabled) { renderer.enabled = false; hiddenLava.Add(renderer); }
            }
            for (int i = 0; i < positions.Length; i++)
            {
                camera.transform.position = positions[i]; camera.transform.LookAt(targets[i]); camera.targetTexture = rt;
                camera.Render(); RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); texture.Apply();
                File.WriteAllBytes(Output + "/" + phase + "/" + names[i] + ".png", texture.EncodeToPNG());
            }
        }
        finally
        {
            foreach (var renderer in hiddenLava) if (renderer) renderer.enabled = true;
            RenderTexture.active = previous; camera.targetTexture = null; rt.Release();
            Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        }
        Debug.Log("Village review captured: " + phase);
    }
}
