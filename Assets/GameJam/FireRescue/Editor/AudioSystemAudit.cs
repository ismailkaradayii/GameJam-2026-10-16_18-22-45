using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using Object = UnityEngine.Object;

/// <summary>Small, repeatable audit of actual assets and the explosion controller. Never saves test objects.</summary>
public static class AudioSystemAudit
{
    public static bool LastPassed { get; private set; }

    [MenuItem("ASHFALL/Audio/Run mission countdown check (Play Mode)")]
    public static void CheckMissionCountdown()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Volcano_Demo Play Mode first.");
        var lines = new StringBuilder("Actual mission director and eruption sequence in Play Mode; counter advanced through its API for a focused boundary check.\n");
        try
        {
            var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
            var timeline = Object.FindFirstObjectByType<WindAudioPulse>();
            Check("mission and timeline exist", mission && timeline && !mission.EruptionTriggered, lines);
            var voice = new SerializedObject(timeline).FindProperty("source").objectReferenceValue as AudioSource;
            var explosion = new SerializedObject(timeline).FindProperty("explosionSound").objectReferenceValue;
            var wind = new SerializedObject(timeline).FindProperty("windLoop").objectReferenceValue;
            timeline.ResetSequence();
            var counter = typeof(VolcanoMissionDirector).GetProperty("PreparationRemaining");
            counter.SetValue(mission, 3f); mission.TickMission();
            Check("before final two seconds explosion is silent", !voice.isPlaying, lines);
            counter.SetValue(mission, 2f); mission.TickMission();
            Check("visible mission countdown starts explosion at two seconds", voice.isPlaying && voice.clip == explosion, lines);
            counter.SetValue(mission, 0f); mission.TickMission();
            Check("zero countdown owns authoritative impact", mission.EruptionTriggered && voice.clip == explosion, lines);
            mission.sequence.TickSequence(3.9f);
            Check("visual warning does not reset four-second impact window", voice.clip == explosion && voice.volume > 0, lines);
            mission.sequence.TickSequence(.95f);
            Check("wind follows mission impact plus four seconds and fade", voice.clip == wind && voice.loop, lines);
            lines.AppendLine("COMPLETED");
        }
        catch (Exception ex) { lines.AppendLine("FAILED: " + ex); Debug.LogException(ex); }
        finally { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/AudioMissionCountdownChecks.txt", lines.ToString()); Debug.Log(lines.ToString()); }
    }

    [MenuItem("ASHFALL/Audio/Run lava contact checks (Play Mode)")]
    public static void CheckLavaContact()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first; fixtures are never saved.");
        var lines = new StringBuilder("Actual lava mesh, damage and cooling fixture in Play Mode; no listening test.\n");
        try
        {
            VolcanoHazardChecks.Run((name, pass, detail) =>
            {
                Check(name, pass, lines);
                var contact = Object.FindFirstObjectByType<FireRescuePlayer>().GetComponent<LavaPlayerContact>();
                var voice = new SerializedObject(contact).FindProperty("painSource").objectReferenceValue as AudioSource;
                if (name == "molten_lava_damages_player") Check("real heat damage plays non-muted pain voice", voice && voice.isPlaying && voice.volume > 0, lines);
                if (name == "cooled_lava_stops_contact_damage") Check("non-damaging contact stops pain immediately", voice && !voice.isPlaying, lines);
            });
            lines.AppendLine("COMPLETED");
        }
        catch (Exception ex) { lines.AppendLine("FAILED: " + ex); Debug.LogException(ex); }
        finally { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/AudioLavaContactChecks.txt", lines.ToString()); Debug.Log(lines.ToString()); }
    }
    [MenuItem("ASHFALL/Audio/Repair wiring and write inventory")]
    public static void Repair()
    {
        GameJam.FireRescue.Editor.AudioIntegrationSetup.IntegrateAudioSystems();
        WriteInventory();
    }

    [MenuItem("ASHFALL/Audio/Write inventory")]
    public static void WriteInventory()
    {
        Directory.CreateDirectory("Logs");
        var report = new StringBuilder("# Audio asset and serialized reference inventory\n\nGenerated " + DateTime.Now.ToString("s") +
            "\nMetadata inspection; no claim of listening or seamless loop quality. References below are direct serialized references; runtime Resources loads are noted separately.\n\n");
        var serialized = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") &&
            (p.EndsWith(".unity") || p.EndsWith(".prefab")))
            .Where(File.Exists).Select(p => new { path = p, text = File.ReadAllText(p) }).ToArray();
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            report.AppendLine("## " + path);
            report.AppendLine(clip ? $"Duration {clip.length:F3}s; {clip.channels} channel(s); {clip.frequency}Hz; load {clip.loadType}." : "ERROR: asset failed to load.");
            var refs = serialized.Where(s => s.text.Contains("guid: " + guid)).Select(s => s.path).ToArray();
            report.AppendLine(refs.Length == 0 ? "No direct serialized references. May be loaded by Resources." : string.Join("\n", refs.Select(p => "- " + p)));
            report.AppendLine();
        }
        report.AppendLine("Runtime Resources: LavaCombatEffects loads SpiritHit1/2/3, SpiritBreak, SpiritPoff, Land, PlayerHit; pistol feedback loads SpiritConfirm and SpiritKillConfirm. Other files without matching events are deliberately unassigned; no gunshot.mp3 asset exists, actual shots use pistol-shot-01/02/03.wav.");
        File.WriteAllText("Logs/AudioAssetInventory.md", report.ToString());
        Debug.Log("AUDIO_AUDIT_INVENTORY_READY: Logs/AudioAssetInventory.md");
    }

    [MenuItem("ASHFALL/Audio/Run timeline checks (Play Mode)")]
    public static void CheckTimeline()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        LastPassed = false;
        var lines = new StringBuilder("Actual Unity Play Mode component checks; no listening test.\n");
        bool had = PlayerPrefs.HasKey("Ashfall.SfxVolume");
        float previous = PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1);
        GameObject fixture = null;
        try
        {
            PlayerPrefs.SetFloat("Ashfall.SfxVolume", 1);
            fixture = new GameObject("Temporary audio timeline check"); fixture.SetActive(false);
            var source = fixture.AddComponent<AudioSource>();
            var timeline = fixture.AddComponent<WindAudioPulse>();
            var data = new SerializedObject(timeline);
            var explosion = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/explode.mp3");
            var wind = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/wind.wav");
            Check("actual explosion and wind assets imported", explosion && wind, lines);
            data.FindProperty("source").objectReferenceValue = source;
            data.FindProperty("explosionSound").objectReferenceValue = explosion;
            data.FindProperty("windLoop").objectReferenceValue = wind;
            data.ApplyModifiedPropertiesWithoutUndo(); fixture.SetActive(true);
            timeline.ResetSequence();
            Check("wind silent before impact", !source.isPlaying && source.volume == 0, lines);
            timeline.BeginExplosionSound();
            Check("explosion starts once", source.clip == explosion && source.isPlaying, lines);
            timeline.TickSequence(2f);
            Check("wind cannot start without authoritative impact", source.clip == explosion, lines);
            timeline.NotifyExplosionEvent(); timeline.TickSequence(3.9f);
            Check("explosion retained until impact plus four seconds", source.clip == explosion && source.volume > 0, lines);
            timeline.TickSequence(.2f); timeline.TickSequence(.8f);
            Check("wind replaces explosion after fade", source.clip == wind && source.loop, lines);
            timeline.TickSequence(2.1f);
            Check("wind fades to audible level", source.volume > 0, lines);
            timeline.ResetSequence();
            Check("restart clears the old timeline", !source.isPlaying && source.volume == 0, lines);
            fixture.SetActive(false);
            Check("disable stops source", !source.isPlaying, lines);
            fixture.SetActive(true);
            var spiritRoot = new GameObject("Temporary spirit audio check"); spiritRoot.SetActive(false);
            spiritRoot.transform.SetParent(fixture.transform);
            spiritRoot.AddComponent<LavaCreatureActor>();
            var spirit = spiritRoot.AddComponent<LavaSpiritAudio>();
            spirit.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/alevruhu.wav");
            spiritRoot.SetActive(true); spirit.SendMessage("Update");
            var voice = spiritRoot.GetComponentInChildren<AudioSource>();
            Check("spirit has one spatial moving voice", voice && voice.isPlaying && voice.spatialBlend == 1f && voice.transform.IsChildOf(spiritRoot.transform), lines);
            spiritRoot.SetActive(false);
            Check("pooled spirit disable stops voice", !voice.isPlaying, lines);
            spiritRoot.SetActive(true); spirit.SendMessage("Update");
            Check("reactivation reuses one voice", voice.isPlaying && spiritRoot.GetComponentsInChildren<AudioSource>().Length == 1, lines);
            CheckGameplaySources(lines);
            var video = Object.FindFirstObjectByType<VideoPlayer>();
            if (video)
            {
                Check("menu video has decoded frames", video.isPrepared && video.frame >= 0 && video.targetTexture, lines);
                Check("menu video audio disabled", video.audioOutputMode == VideoAudioOutputMode.None, lines);
            }
            lines.AppendLine("COMPLETED");
            LastPassed = true;
        }
        catch (Exception ex) { lines.AppendLine("FAILED: " + ex); Debug.LogException(ex); }
        finally
        {
            if (fixture) Object.DestroyImmediate(fixture);
            if (had) PlayerPrefs.SetFloat("Ashfall.SfxVolume", previous); else PlayerPrefs.DeleteKey("Ashfall.SfxVolume");
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/AudioTimelineChecks_" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ".txt", lines.ToString());
            Debug.Log(lines.ToString());
        }
    }

    static void CheckGameplaySources(StringBuilder lines)
    {
        var pistol = Object.FindFirstObjectByType<Itfaiye.ItfaiyePistolFeedback>();
        if (pistol)
        {
            int before = pistol.TotalImpacts;
            var ray = new Ray(new Vector3(6000, 1010, 6100), Vector3.forward);
            pistol.PlayShot(ray, ray.GetPoint(10), new RaycastHit(), 0, false);
            Check("miss does not create impact", pistol.TotalImpacts == before, lines);
            Check("shot voice plays on firing", pistol.GetComponents<AudioSource>().Any(a => a.isPlaying), lines);
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "Temporary metal impact audio check";
            target.transform.position = ray.GetPoint(5);
            target.AddComponent<Itfaiye.ItfaiyeImpactSurface>().surface = Itfaiye.ItfaiyeSurfaceKind.Metal;
            try
            {
                Physics.SyncTransforms();
                Check("real raycast hits test metal surface", Physics.Raycast(ray, out var hit, 10), lines);
                pistol.PlayShot(ray, hit.point, hit, 0, false);
                Check("metal hit has one custom spatial impact", pistol.TotalImpacts == before + 1 &&
                    Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(a => a.isPlaying && a.clip == pistol.targetImpactClip) == 1, lines);
            }
            finally { Object.DestroyImmediate(target); }
        }
        var fire = Object.FindFirstObjectByType<FireAudioAmbience>();
        var houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).Where(b => !b.IgnitionBlocked && !b.IsBurnedOut).Take(2).ToArray();
        if (fire && houses.Length == 2)
        {
            var voice = new SerializedObject(fire).FindProperty("ambienceSource").objectReferenceValue as AudioSource;
            houses[0].Ignite(); houses[1].Ignite(); fire.SendMessage("Update");
            Check("burning houses start shared fire voice", voice && voice.isPlaying, lines);
            houses[0].ApplyWater(100000); fire.SendMessage("Update");
            Check("one extinguished house does not silence other fire", voice.isPlaying && houses[1].IsBurning, lines);
            houses[1].ApplyWater(100000);
            for (int i = 0; i < 1000; i++) fire.SendMessage("Update");
            Check("all extinguished houses stop fire voice", !voice.isPlaying, lines);
        }
        var spray = Object.FindFirstObjectByType<FireTruckSprayAudio>();
        var cannon = spray ? spray.GetComponent<FireTruckWaterCannon>() : null;
        if (cannon && cannon.tank)
        {
            var voice = new SerializedObject(spray).FindProperty("spraySource").objectReferenceValue as AudioSource;
            float litres = cannon.tank.TankLitres;
            cannon.tank.SetTankLitres(250); cannon.SetSpraying(true); cannon.TickSpray(.05f); spray.SendMessage("Update");
            Check("actual water emission starts spray voice", cannon.IsSpraying && voice && voice.isPlaying, lines);
            cannon.SetSpraying(false);
            for (int i = 0; i < 1000; i++) spray.SendMessage("Update");
            Check("spray release stops loop", !voice.isPlaying, lines);
            cannon.SetSpraying(true); cannon.TickSpray(.05f); spray.SendMessage("Update");
            cannon.tank.SetTankLitres(0); cannon.TickSpray(.05f);
            for (int i = 0; i < 1000; i++) spray.SendMessage("Update");
            Check("empty tank stops spray loop", !cannon.IsSpraying && !voice.isPlaying, lines);
            cannon.SetSpraying(false); cannon.tank.SetTankLitres(litres);
        }
    }

    static void Check(string name, bool pass, StringBuilder lines)
    {
        lines.AppendLine((pass ? "PASS " : "FAIL ") + name);
        if (!pass) throw new InvalidOperationException(name);
    }
}
