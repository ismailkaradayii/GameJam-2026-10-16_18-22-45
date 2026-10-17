using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using Itfaiye;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit Play Mode review in an isolated temporary scene; never saves gameplay scenes.</summary>
[InitializeOnLoad]
public static class LavaCombatChecks
{
    const string Folder = "Library/LavaCombatReview";
    const string ActiveKey = "LavaCombatChecks.Pending";
    [Serializable] public class CheckResult { public string name, evidence; public bool passed; }
    [Serializable] public class Report
    {
        public string status, error;
        public List<CheckResult> checks = new List<CheckResult>();
        public List<string> errors = new List<string>();
    }
    static Report report;
    static Scene reviewScene, previousScene;
    static readonly List<GameObject> temporarilyHidden = new List<GameObject>();
    static LavaCreatureActor actor, wallActor, pistolActor, distantActor, descentActor;
    static CreatureCombatPlayer target, wallTarget;
    static LavaCreatureCombat combat;
    static ItfaiyePistol pistol;
    static Camera camera;
    static Material floorMaterial;
    static int phase = -1, lastFrame = -1, shotCount;
    static float phaseAt, began, initialDistance;
    static bool wasPlaying, wasBackground, capturedLeap, capturedWindup;
    static bool capturedPistolImpact;
    static int deathCountBefore, hitCountBefore, peakAttackers;
    static CreatureCombatPlayer crowdTarget;
    static LavaCreatureActor[] crowdActors;
    static float crowdSpacing;
    static CursorLockMode oldLock;
    static bool oldCursor;
    static double nextPoll;

    static LavaCombatChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ActiveKey, false))
            {
                SessionState.SetBool(ActiveKey, false);
                EditorApplication.delayCall += StartRun;
            }
        };
    }

    static void Update()
    {
        if (EditorApplication.timeSinceStartup >= nextPoll && !EditorApplication.isCompiling)
        {
            nextPoll = EditorApplication.timeSinceStartup + .5;
            string request = Folder + "/request.txt";
            if (File.Exists(request))
            {
                string command = File.ReadAllText(request).Trim(); File.Delete(request);
                if (command == "inspect") Inspect();
                else if (command == "run") Begin();
            }
        }
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { Tick(); } catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void Inspect()
    {
        Directory.CreateDirectory(Folder);
        var lines = new List<string> { "playing=" + EditorApplication.isPlaying, "compiling=" + EditorApplication.isCompiling };
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            lines.Add("scene=" + scene.path + "; dirty=" + scene.isDirty);
        }
        lines.Add("actors=" + Resources.FindObjectsOfTypeAll<LavaCreatureActor>().Count(a => a.gameObject.scene.IsValid()));
        foreach (var creature in Resources.FindObjectsOfTypeAll<LavaCreatureActor>())
        {
            if (!creature.gameObject.scene.IsValid()) continue;
            var jump = creature.GetComponent<LavaCreatureAutoJump>();
            lines.Add(creature.name + ": position=" + creature.transform.position + "; terrain=" + (jump && jump.groundTerrain) +
                "; target=" + (jump && jump.Target ? jump.Target.name : "none") +
                "; distance=" + (jump && jump.Target ? Vector3.Distance(creature.transform.position, jump.Target.position).ToString("F2") : "unknown"));
        }
        foreach (var sequence in Object.FindObjectsByType<VolcanoEruptionSequence>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (var route in sequence.routes)
            {
                var flow = route?.flow;
                if (!flow || !flow.source || !flow.source.m_Container) continue;
                lines.Add("route=" + flow.name + "; id=" + GlobalObjectId.GetGlobalObjectIdSlow(flow) + "; feed=" + (route.feed ? route.feed.name : "none") + "; reverse=" + flow.reverse + "; speed=" + flow.speed + "; delay=" + flow.startDelay);
                for (int n = 0; n <= 10; n++)
                {
                    float t = n / 10f;
                    Vector3 p = (Vector3)flow.source.m_Container.EvaluatePosition(flow.reverse ? 1f - t : t);
                    float terrainY = flow.conformToTerrain ? flow.conformToTerrain.SampleHeight(p) + flow.conformToTerrain.transform.position.y : p.y;
                    lines.Add("  t=" + t + "; point=" + p.ToString("F2") + "; ground=" + terrainY.ToString("F2"));
                }
            }
        }
        File.WriteAllLines(Folder + "/inspection.txt", lines);
    }

    [MenuItem("GameJam/Combat/Review aggressive lava spirits")]
    public static void Begin()
    {
        if (phase >= 0 || EditorApplication.isCompiling) return;
        Directory.CreateDirectory(Folder);
        wasPlaying = EditorApplication.isPlaying;
        SessionState.SetBool("LavaCombatChecks.WasPlaying", wasPlaying);
        if (wasPlaying) StartRun();
        else { SessionState.SetBool(ActiveKey, true); EditorApplication.EnterPlaymode(); }
    }

    static void StartRun()
    {
        try
        {
            wasPlaying = SessionState.GetBool("LavaCombatChecks.WasPlaying", false);
            report = new Report { status = "running" };
            Application.logMessageReceived += Log;
            wasBackground = Application.runInBackground; Application.runInBackground = true;
            oldLock = Cursor.lockState; oldCursor = Cursor.visible;
            previousScene = SceneManager.GetActiveScene();
            temporarilyHidden.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if (root.activeSelf) { temporarilyHidden.Add(root); root.SetActive(false); }
            reviewScene = SceneManager.CreateScene("Temporary lava combat review");
            SceneManager.SetActiveScene(reviewScene);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Review ground"; ground.transform.position = new Vector3(20, -.3f, 4);
            ground.transform.localScale = new Vector3(100, .5f, 100);
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(.09f, .11f, .13f); ground.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var light = new GameObject("Review key light").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 2.2f; light.transform.rotation = Quaternion.Euler(45, -30, 0);
            camera = new GameObject("Review camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .045f, .06f); camera.fieldOfView = 50; camera.farClipPlane = 100;
            camera.transform.position = new Vector3(4.2f, 2.9f, 6.7f); camera.transform.LookAt(new Vector3(0, .9f, 0));
            camera.gameObject.AddComponent<AudioListener>();
            target = Target(new Vector3(0, 0, 26));
            actor = Creature(Vector3.zero, target); combat = actor.GetComponent<LavaCreatureCombat>();
            initialDistance = Vector3.Distance(actor.transform.position, target.transform.position);
            Check("Resources are included for builds", Resources.Load<Material>("LavaCombat/Flame") && Resources.Load<Material>("LavaCombat/Smoke") && Resources.Load<Material>("LavaCombat/Shard") && Resources.Load<AudioClip>("LavaCombat/Shatter"));
            Check("Original animated creature is used", actor.GetComponentInChildren<Animator>() != null);
            var spirit = actor.GetComponent<LavaSpiritVisual>();
            Check("Living flame visual is attached to the animated pivot", spirit && spirit.IsBuilt && spirit.VisualRoot.parent.name == "SomersaultVisual");
            Check("Spirit shaders compile and are supported", new[] { "SpiritBody", "SpiritObsidian", "SpiritEye", "SpiritFire", "ImpactStar", "ImpactRing" }
                .All(n => { var m = Resources.Load<Material>("LavaCombat/" + n); return m && m.shader.isSupported && !ShaderUtil.ShaderHasError(m.shader); }));
            Check("Impact and confirmation sounds are available in builds", new[] { "SpiritHit1", "SpiritHit2", "SpiritHit3", "SpiritBreak", "SpiritConfirm", "SpiritKillConfirm" }
                .All(n => Resources.Load<AudioClip>("LavaCombat/" + n)));
            Check("Flame effects do not add bullet-blocking colliders", spirit && spirit.VisualRoot.GetComponentsInChildren<Collider>().Length == 0);
            capturedLeap = capturedWindup = false; began = Time.time;
            Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static CreatureCombatPlayer Target(Vector3 position)
    {
        var go = new GameObject("Review target"); go.transform.position = position;
        var body = go.AddComponent<CharacterController>(); body.height = 1.8f; body.radius = .3f; body.center = Vector3.up * .9f;
        return go.AddComponent<CreatureCombatPlayer>();
    }

    static LavaCreatureActor Creature(Vector3 position, CreatureCombatPlayer victim)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LavaCreature/Prefabs/LavaCreature.prefab");
        var root = Object.Instantiate(prefab); root.name = "Review animated lava spirit";
        root.transform.position = position;
        var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer) && !r.name.ToLowerInvariant().Contains("flame") && !r.name.ToLowerInvariant().Contains("ember")).ToArray();
        Bounds bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.localScale *= 1.8f / bounds.size.y;
        bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.position += Vector3.up * (position.y - bounds.min.y);
        bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var hitObject = new GameObject("Review hit capsule"); hitObject.transform.position = bounds.center;
        hitObject.transform.SetParent(root.transform.Find("SomersaultVisual"), true);
        var capsule = hitObject.AddComponent<CapsuleCollider>(); capsule.radius = .35f; capsule.height = 1.8f;
        var result = root.AddComponent<LavaCreatureActor>();
        result.GetComponent<LavaCreatureCombat>().target = victim;
        Physics.SyncTransforms();
        return result;
    }

    static void Tick()
    {
        if (Time.time - began > 65f) throw new TimeoutException("Review phase " + phase);
        float age = Time.time - phaseAt;
        if (phase == 0)
        {
            if (!capturedWindup && combat.State == LavaCreatureCombat.Motion.Windup)
            {
                Capture("01-windup"); capturedWindup = true;
                Vector3 oldPosition = camera.transform.position; Quaternion oldRotation = camera.transform.rotation;
                float oldFov = camera.fieldOfView;
                var visual = actor.GetComponent<LavaSpiritVisual>().VisualRoot;
                Vector3 center = visual.TransformPoint(new Vector3(0, .92f, 0));
                camera.transform.position = center + visual.forward * 3.1f + visual.right * .9f + visual.up * .35f;
                camera.transform.LookAt(center, visual.up); camera.fieldOfView = 42f;
                Capture("00-living-flame-closeup");
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation); camera.fieldOfView = oldFov;
            }
            if (!capturedLeap && combat.State == LavaCreatureCombat.Motion.Leaping)
            { Capture("02-pursuit-leap"); capturedLeap = true; }
            if (combat.SuccessfulAttacks == 0 && age < 10f) return;
            Check("Chases by repeated leaps", combat.Leaps >= 2 && Vector3.Distance(actor.transform.position, target.transform.position) < initialDistance - 6f, "leaps=" + combat.Leaps);
            Check("Pounce damages player", combat.SuccessfulAttacks > 0 && target.Health < 100f, "health=" + target.Health);
            float before = target.Health;
            bool second = target.TryTakeDamage(14f, actor.transform.position);
            Check("Group damage has an invulnerability gate", !second && target.Health == before);
            Check("Successful pounce consumes its attacking spirit", actor.SelfDetonated && actor.IsDefeated);
            actor = Creature(Vector3.zero, target); combat = actor.GetComponent<LavaCreatureCombat>();
            actor.ApplyDamage(float.NaN); actor.ApplyDamage(float.PositiveInfinity); actor.ApplyDamage(-1);
            Check("Invalid damage cannot change health", actor.Health == 100f);
            var heart = actor.GetComponent<LavaSpiritVisual>().VisualRoot.Find("Molten heart").GetComponent<Renderer>();
            Quaternion struckPose = actor.GetComponent<LavaSpiritVisual>().VisualRoot.parent.localRotation;
            hitCountBefore = LavaCombatEffects.HitsPlayed;
            actor.ApplyBulletDamage(34f, heart.bounds.center + Vector3.forward * .3f, Vector3.back);
            Check("Hit interrupts an attack", actor.Health == 66f && combat.State == LavaCreatureCombat.Motion.Staggered);
            Check("Impact immediately lights the spirit", actor.GetComponent<LavaSpiritVisual>().ImpactFlash > .99f);
            Check("Impact pause is local to the enemy", Time.timeScale == 1f && actor.GetComponentInChildren<Animator>().speed == 0f);
            Check("Impact holds the actual struck pose without snapping upright", Quaternion.Angle(struckPose,
                actor.GetComponent<LavaSpiritVisual>().VisualRoot.parent.localRotation) < .01f);
            camera.transform.position = actor.transform.position + new Vector3(3.6f, 2.4f, 5.5f);
            camera.transform.LookAt(actor.transform.position + Vector3.up * .8f);
            Next(1); return;
        }
        if (phase == 1)
        {
            if (age < .04f) return;
            Capture("03-hit-flash");
            actor.ApplyBulletDamage(34, actor.transform.position + Vector3.up, Vector3.back);
            Check("Second pistol hit leaves 32 health", actor.Health == 32f);
            deathCountBefore = LavaCombatEffects.ShattersPlayed;
            actor.ApplyBulletDamage(34, actor.transform.position + Vector3.up, Vector3.back);
            Check("Every bullet including the killing shot has impact feedback", LavaCombatEffects.HitsPlayed == hitCountBefore + 3);
            Check("Lethal hit holds the hot death pose before shattering", actor.IsDefeated && LavaCombatEffects.ShattersPlayed == deathCountBefore &&
                actor.GetComponent<LavaSpiritVisual>().VisualRoot.Find("Molten heart").GetComponent<Renderer>().enabled);
            Check("Death immediately disables all hitboxes", actor.GetComponentsInChildren<Collider>().All(c => !c.enabled));
            actor.ApplyDamage(100); actor.ApplyWater(100);
            Next(2); return;
        }
        if (phase == 2)
        {
            if (age < .15f) return;
            Check("Death shatters once after its short anticipation", LavaCombatEffects.ShattersPlayed == deathCountBefore + 1 && actor.Health == 0);
            Check("Death hides all source rendering after shattering", actor.GetComponentsInChildren<Renderer>().All(r => !r.enabled));
            Check("Pounce, death and three hit bursts create bounded fragments", LavaCombatEffects.ActiveFragments > 7 && LavaCombatEffects.ActiveFragments <= 64, "fragments=" + LavaCombatEffects.ActiveFragments);
            Check("Main body is fractured into smaller meshes", Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Count(m => m.sharedMesh && m.sharedMesh.name == "Fractured lava region") >= 4);
            Capture("04-shatter-burst");
            wallTarget = Target(new Vector3(20, 0, 2.1f));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Combat obstruction";
            wall.transform.position = new Vector3(20, 1.7f, 1f); wall.transform.localScale = new Vector3(14, 5, .4f);
            wallActor = Creature(new Vector3(20, 0, -1f), wallTarget);
            var distantTarget = Target(new Vector3(100, 0, 100));
            distantActor = Creature(new Vector3(100, 0, 0), distantTarget);
            var jumpSettings = new SerializedObject(distantActor.GetComponent<LavaCreatureAutoJump>());
            jumpSettings.FindProperty("target").objectReferenceValue = distantTarget.transform;
            jumpSettings.ApplyModifiedPropertiesWithoutUndo();
            var playerRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab"));
            foreach (var listener in playerRoot.GetComponentsInChildren<AudioListener>()) listener.enabled = false;
            playerRoot.transform.position = new Vector3(50, 0, 0);
            var player = playerRoot.GetComponent<ItfaiyeFirstPersonController>(); player.externalControl = true; player.SetInputCaptured(true);
            pistol = player.pistol; pistol.externalControl = true; pistol.showHud = false; pistol.Equip();
            shotCount = 0;
            Next(3); return;
        }
        if (phase == 3)
        {
            if (age > .38f && age < .6f) Capture("05-burning-fragments");
            if (age < 7f) return;
            Check("Fragments clean up after their lifetime", LavaCombatEffects.ActiveFragments == 0, "fragments=" + LavaCombatEffects.ActiveFragments);
            Check("Walls block pounce damage", wallTarget.Health == 100f, "health=" + wallTarget.Health);
            Check("Authored target is pursued beyond notice range", distantActor.GetComponent<LavaCreatureCombat>().Leaps > 0,
                "distance=100; noticeRange=70; leaps=" + distantActor.GetComponent<LavaCreatureCombat>().Leaps);
            distantActor.gameObject.SetActive(false);
            wallActor.enabled = false; wallActor.gameObject.SetActive(false);
            // Retire the wall fixture before the later, unobstructed descent interception case.
            var obstruction = GameObject.Find("Combat obstruction"); if (obstruction) obstruction.SetActive(false);
            Check("Real pistol rig draws correctly", pistol.Ready);
            pistolActor = Creature(new Vector3(50, 0, 4), target);
            pistolActor.name = "Actual flame spirit pistol target";
            pistolActor.GetComponent<LavaCreatureCombat>().enabled = false;
            pistolActor.jump.enabled = false;
            pistolActor.jump.CombatWindup(-pistol.muzzle.forward, 0f);
            Physics.SyncTransforms();
            var capsule = pistolActor.GetComponentInChildren<CapsuleCollider>();
            pistolActor.transform.position += pistol.muzzle.position + pistol.muzzle.forward * 3f - capsule.bounds.center;
            Physics.SyncTransforms(); capturedPistolImpact = false;
            Capture("06-pistol-before-hit", pistol.player.playerCamera);
            Check("Pistol target lies on the real muzzle ray", capsule.Raycast(new Ray(pistol.muzzle.position, pistol.muzzle.forward), out var targetHit, 10f),
                "target=" + capsule.bounds.center + "; muzzle=" + pistol.muzzle.position + "; direction=" + pistol.muzzle.forward);
            Check("Real pistol accepts first shot", pistol.Fire()); shotCount++;
            Next(4); return;
        }
        if (phase == 4)
        {
            if (!capturedPistolImpact && age > .025f && pistolActor.Health < 100f)
            { Capture("07-pistol-impact", pistol.player.playerCamera); capturedPistolImpact = true; }
            if (age < .3f) return;
            // Ballistic impact can arrive after Fire(). Allow its feedback to finish before
            // checking the resting visual or sending the next round.
            if (shotCount == 1 && age < 1f && pistolActor.GetComponent<LavaSpiritVisual>().ImpactFlash > 0f) return;
            if (shotCount == 1)
            {
                Check("Real muzzle ray damages creature", pistolActor.Health == 66f && pistol.LastShotCollider && pistol.LastShotCollider.GetComponentInParent<LavaCreatureActor>() == pistolActor, "health=" + pistolActor.Health);
                Check("Creature hit does not leave concrete decal", pistol.Feedback.LastMark == null);
                Check("Spirit flash returns to normal after impact", pistolActor.GetComponent<LavaSpiritVisual>().ImpactFlash == 0f);
            }
            if (shotCount < 3)
            { Check("Follow-up shot accepted " + shotCount, pistol.Fire()); shotCount++; Next(4); return; }
            Check("Three real shots defeat enemy and spend three rounds", pistolActor.IsDefeated && pistol.Ammo == pistol.magazineSize - 3);
            Capture("08-pistol-kill", pistol.player.playerCamera);
            var waterObject = new GameObject("Water compatibility victim"); waterObject.transform.position = new Vector3(70, 0, 0);
            var waterActor = waterObject.AddComponent<LavaCreatureActor>(); waterActor.ApplyWater(20);
            Check("Water cannon damage still works", waterActor.Health == 60f);
            waterActor.ApplyWater(30); Check("Water can still defeat creature", waterActor.IsDefeated);
            var driverObject = new GameObject("Driving pursuit target");
            driverObject.transform.position = new Vector3(-30, 0, 0);
            driverObject.AddComponent<CharacterController>();
            var coordinator = driverObject.AddComponent<FireRescuePlayer>(); coordinator.enabled = false;
            var truck = new GameObject("Review truck target").AddComponent<FireTruckDrive>();
            truck.enabled = false; truck.body.isKinematic = true; truck.transform.position = new Vector3(-10, 0, 15);
            coordinator.drive = truck; coordinator.SetDriving(true);
            var driver = driverObject.AddComponent<CreatureCombatPlayer>();
            Check("Driving pursuit follows truck instead of parked player root", driver.PursuitPosition == truck.transform.position && !driver.CanBeAttacked);
            coordinator.SetDriving(false);
            Check("Leaving vehicle restores player as attack target", driver.PursuitPosition == driver.transform.position && driver.CanBeAttacked);
            var interceptor = Target(new Vector3(20, 0, 12));
            descentActor = Creature(new Vector3(20, 0, 0), interceptor);
            Vector3 origin = descentActor.transform.position;
            descentActor.GetComponent<LavaCreatureCombat>().ConfigureLavaDescent(
                new[] { origin, origin + Vector3.forward * 30f, origin + Vector3.forward * 60f }, 3.1f);
            Next(5); return;
        }
        if (phase == 5 && age >= 5.3f &&
            (descentActor.GetComponent<LavaCreatureCombat>().SuccessfulAttacks > 0 || age >= 12f))
        {
            var descent = descentActor.GetComponent<LavaCreatureCombat>();
            Check("Nearby player interrupts the downhill route", !descent.IsDescending);
            Check("Descending spirit switches to a damaging pounce", descent.SuccessfulAttacks > 0,
                "attacks=" + descent.SuccessfulAttacks + "; state=" + descent.State + "; leaps=" + descent.Leaps +
                "; position=" + descent.transform.position + "; target=" + descent.target.transform.position + "; age=" + age);
            descentActor.gameObject.SetActive(false);
            crowdTarget = Target(new Vector3(0, 0, 70));
            crowdTarget.transform.localScale = Vector3.one * 2f;
            crowdTarget.maximumHealth = 1000f;
            crowdActors = new LavaCreatureActor[8];
            for (int i = 0; i < crowdActors.Length; i++)
                crowdActors[i] = Creature(new Vector3((i % 4) * .4f - .6f, 0, 46 - i / 4 * .4f), crowdTarget);
            peakAttackers = 0; crowdSpacing = 0f;
            Next(6); return;
        }
        if (phase == 6)
        {
            peakAttackers = Mathf.Max(peakAttackers, crowdActors.Count(a => a.GetComponent<LavaCreatureCombat>().IsAttacking));
            if (age < 10f) return;
            crowdSpacing = crowdActors.Average(a => crowdActors.Where(b => b != a).Min(b =>
                Vector2.Distance(new Vector2(a.transform.position.x, a.transform.position.z), new Vector2(b.transform.position.x, b.transform.position.z))));
            Check("Crowd starts overlapping and separates into distinct positions", crowdSpacing > 1.25f, "mean nearest spacing=" + crowdSpacing);
            Check("At most two spirits commit attacks together", peakAttackers > 0 && peakAttackers <= 2, "peak=" + peakAttackers);
            Check("Coordinated attacks reach the double size player", crowdActors.Sum(a => a.GetComponent<LavaCreatureCombat>().SuccessfulAttacks) >= 2);
            camera.transform.position = new Vector3(12, 10, 80); camera.transform.LookAt(new Vector3(0, 1, 68));
            Capture("09-spaced-crowd");
            foreach (var a in crowdActors) a.gameObject.SetActive(false);
            crowdTarget.transform.position = new Vector3(0, 0, 100);
            wallActor = Creature(new Vector3(0, 0, 95.5f), crowdTarget);
            Next(7); return;
        }
        if (phase == 7)
        {
            var attack = wallActor.GetComponent<LavaCreatureCombat>();
            if (!attack.IsAttacking || attack.State != LavaCreatureCombat.Motion.Windup)
            { if (age > 8f) throw new TimeoutException("No telegraphed attack"); return; }
            Check("Attack provides a visible committed direction", wallActor.GetComponentsInChildren<LineRenderer>().Any(r => r.enabled));
            wallActor.ApplyBulletDamage(1f, wallActor.transform.position + Vector3.up, Vector3.back);
            Check("A bullet cancels the tell and its attack reservation", !attack.IsAttacking &&
                wallActor.GetComponentsInChildren<LineRenderer>().All(r => !r.enabled));
            Next(8); return;
        }
        if (phase == 8)
        {
            var attack = wallActor.GetComponent<LavaCreatureCombat>();
            if (!attack.IsAttacking || attack.State != LavaCreatureCombat.Motion.Windup)
            { if (age > 8f) throw new TimeoutException("No attack after stagger"); return; }
            crowdTarget.transform.position += Vector3.right * 5f;
            Physics.SyncTransforms();
            Next(9); return;
        }
        if (phase == 9 && age > 1.3f)
        {
            Check("Sidestepping a committed lunge avoids damage", wallActor.GetComponent<LavaCreatureCombat>().SuccessfulAttacks == 0);
            Check("No runtime errors", report.errors.Count == 0, string.Join("; ", report.errors));
            Finish(null);
        }
    }

    static void Capture(string name, Camera captureCamera = null)
    {
        if (!captureCamera) captureCamera = camera;
        var rt = new RenderTexture(1100, 740, 24);
        var texture = new Texture2D(1100, 740, TextureFormat.RGB24, false);
        var old = RenderTexture.active; var oldTarget = captureCamera.targetTexture;
        try
        {
            captureCamera.targetTexture = rt; captureCamera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1100, 740), 0, 0); texture.Apply();
            File.WriteAllBytes(Folder + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally { captureCamera.targetTexture = oldTarget; RenderTexture.active = old; Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); }
    }
    static void Next(int value) { phase = value; phaseAt = Time.time; Save(); }
    static void Check(string name, bool pass, string evidence = "") { report.checks.Add(new CheckResult { name = name, passed = pass, evidence = evidence }); }
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack);
    }
    static void Save() { if (report != null) File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true)); }
    static void Finish(string error)
    {
        phase = -1;
        if (report == null) report = new Report();
        report.error = error;
        report.status = error == null && report.checks.All(c => c.passed) && report.errors.Count == 0 ? "SUCCESS" : "FAILED";
        Save(); Application.logMessageReceived -= Log;
        Application.runInBackground = wasBackground;
        if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        foreach (var root in temporarilyHidden) if (root) root.SetActive(true);
        temporarilyHidden.Clear();
        if (reviewScene.IsValid() && reviewScene.isLoaded) SceneManager.UnloadSceneAsync(reviewScene);
        if (floorMaterial) Object.Destroy(floorMaterial);
        Cursor.lockState = oldLock; Cursor.visible = oldCursor;
        if (!wasPlaying && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
