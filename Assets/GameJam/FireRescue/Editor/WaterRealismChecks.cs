using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using GameJam.FireRescue;
using Itfaiye;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class WaterRealismChecks
{
    const string Folder = "Logs/WaterRealismReview";
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Report { public string status = "running", error; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
    static Report report;
    static Scene previous, review;
    static readonly List<GameObject> hidden = new List<GameObject>();
    static readonly List<Material> materials = new List<Material>();
    static ItfaiyeFirstPersonController player;
    static WaterReviewDriver driver;
    static WaterSourceVolume water;
    static FireTruckWaterCannon cannon;
    static Camera reviewCamera;
    static GameObject wall;
    static int phase = -1, lastFrame;
    static float began, phaseAt, originalCapture, maxHeight, baseHeight, initialY, initialZ, stoppedTank;
    static bool originalBackground, originallyPlaying, sawUnarmedJump;
    static double wallStarted;

    static WaterRealismChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("WaterRealism.Checks", false))
            { SessionState.SetBool("WaterRealism.Checks", false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Verification interrupted");
        };
    }
    public static void Begin()
    {
        if (phase >= 0) return;
        SessionState.SetBool("WaterRealism.WasPlaying", EditorApplication.isPlaying);
        if (EditorApplication.isPlaying) Start();
        else { SessionState.SetBool("WaterRealism.Checks", true); EditorApplication.EnterPlaymode(); }
    }
    static void Start()
    {
        report = new Report(); Directory.CreateDirectory(Folder);
        try
        {
            originallyPlaying = SessionState.GetBool("WaterRealism.WasPlaying", false);
            originalCapture = Time.captureDeltaTime; originalBackground = Application.runInBackground;
            Time.captureDeltaTime = 1f / 60; Application.runInBackground = true;
            Application.logMessageReceived += Log;
            previous = SceneManager.GetActiveScene(); hidden.Clear(); materials.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++) foreach (var root in SceneManager.GetSceneAt(s).GetRootGameObjects())
                if (root.activeSelf) { hidden.Add(root); root.SetActive(false); }
            review = SceneManager.CreateScene("Temporary water realism verification"); SceneManager.SetActiveScene(review);
            var mat = MakeMaterial(new Color(.22f, .26f, .28f));
            Cube("Dry platform", new Vector3(0, -.2f, -6), new Vector3(50, .4f, 16), mat);
            Cube("Raised pool coping", new Vector3(0, .22f, 3), new Vector3(24, .45f, .3f), mat);
            Cube("Pool floor", new Vector3(0, -4.1f, 15), new Vector3(24, .3f, 24), MakeMaterial(new Color(.12f, .22f, .24f)));
            Cube("Pool far wall", new Vector3(0, -1.8f, 27), new Vector3(24, 4, .5f), mat);
            Cube("Pool left wall", new Vector3(-12, -1.8f, 15), new Vector3(.4f, 4, 24), mat);
            Cube("Pool right wall", new Vector3(12, -1.8f, 15), new Vector3(.4f, 4, 24), mat);
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2f;
            light.transform.rotation = Quaternion.Euler(45, -25, 0); light.shadows = LightShadows.Soft;
            var pool = new GameObject("Review pool"); pool.transform.position = new Vector3(0, -2, 15);
            var box = pool.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(24, 4.64f, 24);
            water = pool.AddComponent<WaterSourceVolume>(); water.volume = box; water.surfaceY = .32f; water.rejectDryTerrain = false;
            var surface = GameObject.CreatePrimitive(PrimitiveType.Plane); surface.name = "Visible pool surface";
            surface.transform.position = new Vector3(0, .32f, 15); surface.transform.localScale = new Vector3(2.4f, 1, 2.4f);
            Object.Destroy(surface.GetComponent<Collider>()); water.waterSurface = surface.transform;
            surface.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("WaterRealism/PoolSurface");
            var container = new GameObject("Player container"); container.SetActive(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab");
            var character = Object.Instantiate(prefab, container.transform); character.transform.position = new Vector3(0, .05f, -3);
            character.transform.localScale = Vector3.one * 2f;
            player = character.GetComponent<ItfaiyeFirstPersonController>(); player.externalControl = true; player.recoverFromFall = false;
            player.walkSpeed = 4; player.runSpeed = 6; player.pistol.externalControl = true; player.pistol.showHud = false;
            container.SetActive(true); driver = container.AddComponent<WaterReviewDriver>(); driver.player = player;
            // Match the 960x720 review captures instead of inheriting a narrow docked Game View.
            player.playerCamera.aspect = 4f / 3f;
            if (player.handsCamera) player.handsCamera.aspect = 4f / 3f;
            var data = player.playerCamera.GetUniversalAdditionalCameraData(); data.requiresDepthTexture = true; data.requiresColorTexture = true; data.renderPostProcessing = true;
            player.playerCamera.clearFlags = CameraClearFlags.SolidColor; player.playerCamera.backgroundColor = new Color(.26f, .35f, .4f);
            reviewCamera = new GameObject("Water review camera").AddComponent<Camera>(); reviewCamera.enabled = false;
            reviewCamera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            reviewCamera.GetUniversalAdditionalCameraData().requiresColorTexture = true;
            reviewCamera.clearFlags = CameraClearFlags.SolidColor; reviewCamera.backgroundColor = new Color(.15f, .2f, .24f);
            reviewCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("FirstPersonHands")); reviewCamera.fieldOfView = 52;
            var truck = new GameObject("Review monitor"); truck.transform.position = new Vector3(-7, 2, 4);
            var tank = truck.AddComponent<FireHoseSystem>(); tank.SetTankLitres(1000);
            cannon = truck.AddComponent<FireTruckWaterCannon>(); cannon.tank = tank; cannon.muzzle = truck.transform;
            cannon.muzzle.rotation = Quaternion.Euler(-4, 8, 0); cannon.muzzleSpeed = 35;
            wall = Cube("Jet impact wall", new Vector3(-4, 4, 24), new Vector3(8, 12, .5f), mat);
            Physics.SyncTransforms(); began = Time.time; wallStarted = EditorApplication.timeSinceStartup; lastFrame = -1; Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try { Tick(); } catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        if (Time.time - began > 75 || EditorApplication.timeSinceStartup - wallStarted > 240) throw new TimeoutException("Phase " + phase);
        float age = Time.time - phaseAt;
        if (phase == 0)
        {
            if (age < .6f) return;
            sawUnarmedJump = false;
            player.pistol.SetAvailable(true); player.pistol.Holster();
            Add("URP water materials compile", !ShaderUtil.ShaderHasError(Resources.Load<Material>("WaterRealism/Jet").shader));
            baseHeight = player.transform.position.y; maxHeight = baseHeight; driver.jump = true; Next(1);
        }
        else if (phase == 1)
        {
            maxHeight = Mathf.Max(maxHeight, player.transform.position.y);
            sawUnarmedJump |= player.bodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Standing Jump") || player.bodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Moving Jump");
            if (age < 1.1f) return;
            Add("Unarmed Space jumps on dry ground", maxHeight - baseHeight > .65f, (maxHeight - baseHeight).ToString("F3"));
            Add("Unarmed jump plays an actual jump animation", sawUnarmedJump);
            player.pistol.Equip(); Next(2);
        }
        else if (phase == 2)
        {
            if (age < .65f) return;
            baseHeight = player.transform.position.y; maxHeight = baseHeight; driver.jump = true; Next(3);
        }
        else if (phase == 3)
        {
            maxHeight = Mathf.Max(maxHeight, player.transform.position.y);
            if (age < 1.1f) return;
            Add("Armed jump preserved", maxHeight - baseHeight > .65f && player.pistol.Equipped);
            // The real posed pistol aims at a physical pool, so this also tests ordering before solid hits.
            Teleport(new Vector3(0, .05f, 1)); driver.pitch = 28; Next(4);
        }
        else if (phase == 4)
        {
            if (age < .6f) return;
            Add("Pistol accepts pool shot", player.pistol.Fire()); Next(5);
        }
        else if (phase == 5)
        {
            if (age < .12f) return;
            Add("Bullet resolves nearest water surface", player.pistol.LastShotWater == water, player.pistol.LastShotPoint.ToString());
            Add("Bullet produces splash and ripples", water.Effects.BulletImpacts == 1 && water.Effects.ActiveRipples > 0 && water.Effects.Droplets.particleCount > 0);
            reviewCamera.transform.position = new Vector3(5, 3, 7); reviewCamera.transform.LookAt(player.pistol.LastShotPoint);
            Capture(reviewCamera, "01-bullet-splash");
            var blocker = Cube("Bullet cover", new Vector3(0, 1.5f, 4), new Vector3(5, 6, .4f), materials[0]);
            Physics.SyncTransforms(); Next(6);
        }
        else if (phase == 6)
        {
            if (age < .6f) return;
            player.pistol.Fire(); Next(7);
        }
        else if (phase == 7)
        {
            if (age < .12f) return;
            Add("Cover prevents splash behind wall", !player.pistol.LastShotWater && water.Effects.BulletImpacts == 1);
            var blocker = GameObject.Find("Bullet cover"); Object.Destroy(blocker);
            Teleport(new Vector3(0, 1, 12)); driver.pitch = 0; driver.move = Vector2.zero; Next(8);
        }
        else if (phase == 8)
        {
            if (age < 2.5f) return;
            var swim = player.Swimming;
            Add("Falling into pool starts swimming", swim.IsSwimming && swim.EntryCount == 1);
            Add("Buoyancy holds eyes above water", player.playerCamera.transform.position.y - water.SurfaceY > .1f && player.playerCamera.transform.position.y - water.SurfaceY < .8f,
                player.playerCamera.transform.position.y.ToString("F3"));
            Add("Gun is holstered and hands camera stays visible", !player.pistol.Available && !player.pistol.Equipped && player.handsCamera.enabled);
            Add("Swimmer does not use grounded walking", !player.IsGrounded && !player.IsSprinting);
            initialZ = player.transform.position.z; driver.move = Vector2.up; Next(9);
        }
        else if (phase == 9)
        {
            if (age < 1.1f) return;
            Add("Swimming moves through water", player.transform.position.z > initialZ + 1);
            Add("Swimming emits wakes and stroke splashes", water.Effects.SwimImpulses >= 3);
            var left = player.handsAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            var right = player.handsAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            Vector3 lv = player.playerCamera.WorldToViewportPoint(left.position), rv = player.playerCamera.WorldToViewportPoint(right.position);
            Add("Both swimming hands are inside the view", Inside(lv) && Inside(rv), lv + " / " + rv);
            Capture(player.playerCamera, "02-swimming-hands");
            initialY = player.transform.position.y; driver.move = Vector2.zero; driver.vertical = -1; Next(10);
        }
        else if (phase == 10)
        {
            if (age < .8f) return;
            Add("Ctrl dives below surface", player.transform.position.y < initialY - .45f && player.Swimming.IsSubmerged);
            Capture(player.playerCamera, "03-underwater-hands"); driver.vertical = 1; Next(11);
        }
        else if (phase == 11)
        {
            if (age < 2.1f) return;
            Add("Space resurfaces without land jumping", player.Swimming.IsSwimming && !player.Swimming.IsSubmerged && player.playerCamera.transform.position.y - water.SurfaceY < .85f);
            Teleport(new Vector3(0, player.transform.position.y, 4f));
            player.transform.rotation = Quaternion.Euler(0, 180, 0); driver.move = Vector2.up; driver.vertical = 1; Next(12);
        }
        else if (phase == 12)
        {
            if (age < .9f) return;
            Add("Space and forward climb out at a clear pool edge", !player.IsSwimming && player.transform.position.z < 2.7f,
                player.transform.position.ToString());
            Add("Leaving water restores capsule and weapon", !player.IsSwimming && player.GetComponent<CharacterController>().height > 1.6f && player.pistol.Equipped);
            driver.move = Vector2.zero; driver.vertical = 0; player.transform.rotation = Quaternion.identity; Teleport(new Vector3(0, .05f, -3));
            cannon.SetSpraying(true); Next(13);
        }
        else if (phase == 13)
        {
            if (age < 1.2f) return;
            Add("Jet has dense core and separate droplets", cannon.Visuals.CoreSegments > 8 && cannon.Visuals.VisibleDroplets > 100);
            Add("Jet produces fine mist", cannon.Visuals.Mist.particleCount > 0);
            Add("Solid impact sprays droplets", cannon.Visuals.ImpactCount > 0 && cannon.Visuals.Splashes.particleCount > 0);
            bool physicalParticles = false;
            foreach (var ps in cannon.Visuals.GetComponentsInChildren<ParticleSystem>()) physicalParticles |= ps.collision.enabled || ps.GetComponent<Rigidbody>() != null;
            Add("Visual particles have no physics collisions", !physicalParticles);
            reviewCamera.transform.position = new Vector3(3, 6, -1); reviewCamera.transform.LookAt(new Vector3(-5, 2.8f, 14)); Capture(reviewCamera, "04-pressure-jet");
            stoppedTank = cannon.tank.TankLitres; cannon.SetSpraying(false); Next(14);
        }
        else if (phase == 14)
        {
            if (age < .1f) return;
            Add("Released trigger keeps in-flight water", !cannon.IsSpraying && cannon.InFlightPacketCount > 0 && Mathf.Abs(cannon.tank.TankLitres - stoppedTank) < .001f);
            Next(15);
        }
        else if (phase == 15)
        {
            if (age < 1.2f) return;
            Add("Impact retires all ballistic packets", cannon.InFlightPacketCount == 0);
            wall.SetActive(false); cannon.muzzle.rotation = Quaternion.Euler(18, 15, 0); cannon.SetSpraying(true); Next(16);
        }
        else if (phase == 16)
        {
            if (age < .8f) return;
            Add("Cannon hits water and disturbs surface", water.Effects.CannonImpacts > 0);
            Add("All effect pools stay bounded", water.Effects.ActiveRipples <= WaterSurfaceInteraction.RippleCapacity && cannon.Visuals.VisibleDroplets <= 4096 && cannon.Visuals.Mist.particleCount <= 240);
            reviewCamera.transform.position = new Vector3(4, 5, 1); reviewCamera.transform.LookAt(cannon.AimPoint); Capture(reviewCamera, "05-jet-water-contact");
            cannon.SetSpraying(false);
            FireCombatChecks.Run();
            string regression = File.ReadAllText("Logs/FireCombatChecks.json");
            Add("Existing ballistic fire combat regression passes", regression.Contains("\"allPassed\": true"));
            Finish(null);
        }
    }
    static bool Inside(Vector3 p) => p.z > .05f && p.x > .01f && p.x < .99f && p.y > .01f && p.y < .99f;
    static void Teleport(Vector3 position)
    {
        var motor = player.GetComponent<CharacterController>(); motor.enabled = false; player.transform.position = position; motor.enabled = true; Physics.SyncTransforms();
    }
    static Material MakeMaterial(Color color)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = color; materials.Add(m); return m;
    }
    static GameObject Cube(string name, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; return go;
    }
    static void Capture(Camera camera, string name) => Itfaiye.Editor.ItfaiyeWeaponFeelReview.Capture(camera, Folder + "/" + name + ".png");
    static void Next(int value) { phase = value; phaseAt = Time.time; Save(); }
    static void Add(string name, bool passed, string evidence = "") { report.checks.Add(new Check { name = name, passed = passed, evidence = evidence }); Save(); }
    static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
    static void Save() { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true)); }
    static void Finish(string error)
    {
        phase = -1;
        if (report == null) return;
        report.error = error; report.status = error == null && report.errors.Count == 0 && report.checks.TrueForAll(c => c.passed) ? "passed" : "failed";
        Save(); Application.logMessageReceived -= Log;
        if (driver) driver.enabled = false;
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        if (review.IsValid() && review.isLoaded) SceneManager.UnloadSceneAsync(review);
        foreach (var root in hidden) if (root) root.SetActive(true); hidden.Clear();
        foreach (var material in materials) if (material) Object.Destroy(material); materials.Clear();
        Time.captureDeltaTime = originalCapture; Application.runInBackground = originalBackground;
        if (!originallyPlaying && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(-95)]
public sealed class WaterReviewDriver : MonoBehaviour
{
    public ItfaiyeFirstPersonController player;
    public Vector2 move;
    public bool jump, sprint;
    public float vertical, pitch;
    void Update()
    {
        player.SimulateExternalMovement(move, sprint, jump, false, true, pitch, Vector2.zero, null, vertical);
        jump = false; player.pistol.SetExternalInput(true, false, false, false, false, false);
    }
    void LateUpdate() { player.ApplyExternalView(); }
}
