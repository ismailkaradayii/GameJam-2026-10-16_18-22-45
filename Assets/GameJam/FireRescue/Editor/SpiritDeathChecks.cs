using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit review with real scene actors in a temporary Play Mode scene; never saves the level.
[InitializeOnLoad]
public static class SpiritDeathChecks
{
    const string Pending = "SpiritDeathChecks.Pending", Folder = "Logs/SpiritDeath";
    static readonly List<string> results = new List<string>(), errors = new List<string>();
    static LavaCreatureActor template, actor;
    static Camera camera;
    static AudioSource poff;
    static int phase = -1, frame, shatters, puffs, poolCount;
    static float phaseAt, soundAt, oldCapture, oldTimeScale;
    static bool oldBackground, oldAudioPause;
    static double deadline;

    static SpiritDeathChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                EditorApplication.delayCall += Start;
            }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Review interrupted");
        };
    }

    [MenuItem("GameJam/Final Loop/Check spirit death feedback")]
    public static void Begin()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/checks.txt", "STARTING\n");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void Start()
    {
        results.Clear(); errors.Clear();
        oldBackground = Application.runInBackground; oldAudioPause = AudioListener.pause;
        oldCapture = Time.captureDeltaTime; oldTimeScale = Time.timeScale;
        Application.runInBackground = true; AudioListener.pause = false;
        // Audio uses DSP time, so this review must run at natural frame time too.
        Time.timeScale = 1f; Time.captureDeltaTime = 0f;
        Application.logMessageReceived += Log;
        deadline = EditorApplication.timeSinceStartup + 100;
        try
        {
            template = Object.FindObjectsByType<LavaCreatureActor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(a => a.jump || a.GetComponent<LavaCreatureAutoJump>());
            results.Add("Scene template: " + template.name + " in " + template.gameObject.scene.path);
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            SceneManager.SetActiveScene(SceneManager.CreateScene("Temporary spirit death review"));
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0, -.35f, 0); ground.transform.localScale = new Vector3(30, .5f, 30);
            var light = new GameObject("Review light").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(50, -35, 0);
            camera = new GameObject("Review camera").AddComponent<Camera>();
            camera.gameObject.AddComponent<AudioListener>(); camera.fieldOfView = 48;
            camera.transform.position = new Vector3(4, 2.7f, 5.8f); camera.transform.LookAt(Vector3.up * .9f);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .08f, .10f);
            var clip = Resources.Load<AudioClip>("LavaCombat/SpiritPoff");
            Check("Poff is a short mono clip included in builds", clip && clip.channels == 1 && clip.length > .2f && clip.length < .6f);
            actor = Creature(Vector3.zero);
            Check("Authored spirit and animated visual are used", actor.GetComponent<LavaSpiritVisual>().IsBuilt && actor.GetComponentInChildren<Animator>());
            shatters = LavaCombatEffects.ShattersPlayed; puffs = LavaCombatEffects.DeathPuffsPlayed;
            actor.ApplyBulletDamage(34, actor.transform.position + Vector3.up, Vector3.forward);
            Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static LavaCreatureActor Creature(Vector3 position)
    {
        var staging = new GameObject("Inactive staging"); staging.SetActive(false);
        var root = Object.Instantiate(template.gameObject, staging.transform);
        root.SetActive(false); root.transform.SetParent(null, true); Object.Destroy(staging);
        SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());
        root.name = "Review actual scene spirit"; root.transform.position = position;
        root.GetComponent<LavaCreatureAutoJump>().groundTerrain = null;
        root.SetActive(true);
        var result = root.GetComponent<LavaCreatureActor>();
        result.GetComponent<LavaCreatureCombat>().enabled = false;
        if (result.jump) result.jump.enabled = false;
        foreach (var animator in root.GetComponentsInChildren<Animator>()) animator.enabled = false;
        // Authored mountain spawns begin below lava. Lift only this review clone onto the test floor.
        var body = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        Bounds bounds = body[0].bounds; foreach (var renderer in body) bounds.Encapsulate(renderer.bounds);
        root.transform.position += Vector3.up * (position.y + .05f - bounds.min.y);
        return result;
    }

    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup > deadline) { Finish("Review timed out at phase " + phase); return; }
        if (EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { Tick(); } catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void Tick()
    {
        float age = Time.time - phaseAt;
        if (phase == 0 && age >= .2f)
        {
            Check("Nonlethal bullet does not play death feedback", actor.Health == 66 && LavaCombatEffects.ShattersPlayed == shatters && LavaCombatEffects.DeathPuffsPlayed == puffs);
            var body = actor.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            Bounds bounds = body[0].bounds; foreach (var renderer in body) bounds.Encapsulate(renderer.bounds);
            results.Add("Review visual center=" + bounds.center + "; size=" + bounds.size);
            camera.transform.position = bounds.center + new Vector3(3.5f, 1.8f, 5.5f);
            camera.transform.LookAt(bounds.center);
            Capture("01-alive");
            actor.ApplyBulletDamage(100, actor.transform.position + Vector3.up, Vector3.forward);
            actor.ApplyBulletDamage(100, Vector3.up, Vector3.forward); actor.ApplyWater(100);
            Check("Lethal bullet immediately disables damage hitboxes", actor.IsDefeated && actor.GetComponentsInChildren<Collider>().All(c => !c.enabled));
            Next(1);
        }
        else if (phase == 1 && LavaCombatEffects.ShattersPlayed > shatters)
        {
            poff = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Single(a => a.clip && a.clip.name == "SpiritPoff");
            Check("One spatial poff starts at the defeated spirit", poff.isPlaying && poff.spatialBlend == 1 && poff.dopplerLevel == 0 && poff.volume > 0);
            Check("Sound precedes the fire cloud", LavaCombatEffects.DeathPuffsPlayed == puffs && poff.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount) == 0);
            Check("Repeated lethal damage produces one shatter", LavaCombatEffects.ShattersPlayed == shatters + 1);
            Check("Source renderers disappear after shattering", actor.GetComponentsInChildren<Renderer>().All(r => !r.enabled));
            soundAt = Time.time;
            Object.Destroy(actor.gameObject);
            Next(2);
        }
        else if (phase == 2 && LavaCombatEffects.DeathPuffsPlayed > puffs)
        {
            Check("Delayed puff survives removal of its source actor", !actor && LavaCombatEffects.DeathPuffsPlayed == puffs + 1);
            Check("Fire follows sound by the configured short delay", Time.time - soundAt >= .08f && Time.time - soundAt <= .18f);
            Check("Fire, sparks and smoke are emitted", poff.GetComponentsInChildren<ParticleSystem>().Count(p => p.particleCount > 0) >= 3);
            Check("Effect has no damage or blocking colliders", poff.GetComponentsInChildren<Collider>().Length == 0);
            Next(3);
        }
        else if (phase == 3 && age >= .08f)
        {
            var flames = poff.transform.Find("Flame tongues").GetComponent<ParticleSystem>();
            var particles = new ParticleSystem.Particle[flames.main.maxParticles];
            int count = flames.GetParticles(particles);
            Check("Flame cloud stays small", count > 0 && particles.Take(count).All(p => Vector3.Distance(p.position, poff.transform.position) < 2f && p.startSize < .65f));
            Capture("02-small-fire-puff");
            Next(4);
        }
        else if (phase == 4 && age >= 1.1f)
        {
            Check("Poff finishes within a second", !poff.isPlaying);
            Check("Fire cloud finishes within a second", poff.GetComponentsInChildren<ParticleSystem>().All(p => p.particleCount == 0));
            actor = Creature(Vector3.zero);
            actor.ApplyWater(10);
            Check("Nonlethal water retains normal health behavior", actor.Health == 80 && !actor.IsDefeated);
            actor.ApplyWater(100); actor.ApplyWater(100);
            Next(5);
        }
        else if (phase == 5 && age >= .35f)
        {
            Check("Water kill also plays exactly one death puff", actor.IsDefeated && LavaCombatEffects.DeathPuffsPlayed == puffs + 2 && LavaCombatEffects.ShattersPlayed == shatters + 2);
            Capture("03-water-kill");
            Creature(Vector3.left * 4).ConsumeAtBuilding();
            Creature(Vector3.right * 4).Dissipate();
            Next(6);
        }
        else if (phase == 6 && age >= .5f)
        {
            Check("House arrival and timeout keep their existing effects", LavaCombatEffects.DeathPuffsPlayed == puffs + 2 && LavaCombatEffects.ShattersPlayed == shatters + 4);
            poolCount = Object.FindFirstObjectByType<LavaCombatEffects>().GetComponentsInChildren<AudioSource>(true).Length;
            Next(7);
        }
        else if (phase == 7 && age >= 5f)
        {
            var pool = Object.FindFirstObjectByType<LavaCombatEffects>();
            Check("All pooled effects and fragment meshes clean up", LavaCombatEffects.ActiveFragments == 0 && pool.GetComponentsInChildren<AudioSource>().Length == 0);
            Creature(Vector3.zero).ApplyDamage(1000);
            Next(8);
        }
        else if (phase == 8 && age >= .35f)
        {
            var pool = Object.FindFirstObjectByType<LavaCombatEffects>();
            Check("Reused effect slot plays one new delayed puff", LavaCombatEffects.DeathPuffsPlayed == puffs + 3 && pool.GetComponentsInChildren<AudioSource>(true).Length == poolCount);
            Check("No runtime errors", errors.Count == 0);
            Finish(null);
        }
    }

    static void Capture(string name)
    {
        var target = new RenderTexture(1100, 740, 24); var image = new Texture2D(1100, 740, TextureFormat.RGB24, false);
        var old = RenderTexture.active; var previous = camera.targetTexture;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1100, 740), 0, 0); image.Apply();
            File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG());
        }
        finally { camera.targetTexture = previous; RenderTexture.active = old; Object.DestroyImmediate(target); Object.DestroyImmediate(image); }
    }
    static void Next(int value) { phase = value; phaseAt = Time.time; }
    static void Check(string label, bool passed) => results.Add((passed ? "PASS " : "FAIL ") + label);
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
    }
    static void Finish(string error)
    {
        phase = -1; Application.logMessageReceived -= Log;
        Time.timeScale = oldTimeScale; Time.captureDeltaTime = oldCapture;
        Application.runInBackground = oldBackground; AudioListener.pause = oldAudioPause;
        if (error != null) errors.Add(error);
        results.AddRange(errors);
        results.Add(error == null && errors.Count == 0 && results.All(r => !r.StartsWith("FAIL ")) ? "COMPLETED" : "FAILED");
        File.WriteAllLines(Folder + "/checks.txt", results);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
