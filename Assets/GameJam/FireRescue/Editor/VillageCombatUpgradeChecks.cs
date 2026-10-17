using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VillageCombatUpgradeChecks
{
    const string Folder = VillageCombatUpgradeSetup.Folder;
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Report { public string status = "running", error; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
    static Report report;
    static int phase = -1, frame;
    static float started, phaseAt, oldCapture;
    static double wallStarted;
    static bool background;
    static readonly List<GameObject> hidden = new List<GameObject>();
    static Scene previous, review;
    static BurnableBuilding fire, wallHouse, leftHouse, rightHouse;
    static LavaCreatureActor hunter, raiderA, raiderB;
    static CreatureCombatPlayer target;
    static Camera camera;
    static WaterSourceVolume water;
    static Itfaiye.ItfaiyePistol pistol;
    static LavaCreatureActor bulletTarget;
    static float minDistance, maxSide;
    static int overlaps;
    static Material floorMaterial;

    static VillageCombatUpgradeChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("VillageCombat.Pending", false))
            { SessionState.SetBool("VillageCombat.Pending", false); EditorApplication.delayCall += Start; }
        };
    }
    public static void Begin()
    {
        if (phase >= 0 || EditorApplication.isPlaying) return;
        SessionState.SetBool("VillageCombat.Pending", true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        Directory.CreateDirectory(Folder); report = new Report(); Application.logMessageReceived += Log;
        oldCapture = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 60;
        background = Application.runInBackground; Application.runInBackground = true; Time.timeScale = 1;
        wallStarted = EditorApplication.timeSinceStartup;
        try
        {
            var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
            Add("Six unique sources use several lava branches", encounter && encounter.spawns.Length == 6 &&
                encounter.spawns.Select(s => s.creature).Distinct().Count() == 6 && encounter.spawns.Select(s=>s.feed).Distinct().Count() >= 3);
            Add("Mixed roles and independent movement saved", encounter && encounter.disperseIntoVillage && encounter.buildingAttackShare > .3f && encounter.buildingAttackShare < .8f);
            var authoredFire=Object.FindFirstObjectByType<BurnableBuilding>();
            var streams=new List<ParticleSystemVertexStream>(); authoredFire.flames.GetComponent<ParticleSystemRenderer>().GetActiveVertexStreams(streams);
            Add("Authored house flames have full particle UVs",!authoredFire.flames.textureSheetAnimation.enabled&&
                streams.SequenceEqual(new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV}));
            foreach (var mesh in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (mesh.name.StartsWith("Table02") || mesh.name.StartsWith("Cabinet04"))
                {
                    var renderer = mesh.GetComponent<Renderer>();
                    if (!renderer) continue;
                    var ray = new Ray(renderer.bounds.center - Vector3.right * 6, Vector3.right);
                    Add(mesh.name + " has a solid fitted collision volume", mesh.GetComponents<Collider>().Any(c => c.enabled && !c.isTrigger && c.Raycast(ray, out _, 12)));
                }
            // Exercise the real wave release code before isolating movement fixtures.
            var sequence = encounter.sequence; sequence.enabled = false;
            sequence.missionDirector = null; sequence.evacuation = null; sequence.cinematic = null;
            sequence.InitializeSequence(); sequence.BeginEruption(true);
            for (int i = 0; i < 500 && sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing; i++) sequence.TickSequence(.05f);
            foreach (var spawn in encounter.spawns) { spawn.feed.Initialize(); spawn.feed.SetDistance(spawn.feed.Length); }
            encounter.enabled = false;
            for (int i = 0; i < 100 && encounter.SpawnedCount < 3; i++) encounter.TickEncounter(.1f);
            var living = encounter.LivingCreatures.ToArray();
            Add("Released wave has both house raiders and player hunters", living.Any(a=>a.GetComponent<LavaCreatureCombat>().attackBuildings) && living.Any(a=>!a.GetComponent<LavaCreatureCombat>().attackBuildings));
            Add("Emergence points are outside houses", living.All(a=>!SpiritOutdoorNavigation.Contains(a.transform.position, .8f)));
            Add("Emergence positions are separated", living.Length >= 3 && living.Select(a=>a.transform.position).Distinct().Count() == living.Length &&
                Vector3.Distance(living[0].transform.position, living[1].transform.position) > 5);
            foreach (var a in living)
            {
                var combat = a.GetComponent<LavaCreatureCombat>(); combat.enabled = true;
                for (int i = 0; i < 100; i++) combat.TickCombat(.05f);
            }
            Add("Spirits leave their lava route within four seconds", living.All(a=>!a.GetComponent<LavaCreatureCombat>().IsDescending));
            Add("Real village spirits start independent hops", living.All(a=>a.ReachedBuilding||a.GetComponent<LavaCreatureCombat>().Leaps>0),
                string.Join("; ",living.Select(a=>$"{a.name}: {a.GetComponent<LavaCreatureCombat>().State}, hops={a.GetComponent<LavaCreatureCombat>().Leaps}, pos={a.transform.position}")));
            foreach(var a in living.Where(a=>!a.IsDefeated&&a.GetComponent<LavaCreatureCombat>().Leaps==0))
            {
                var ai=a.GetComponent<LavaCreatureCombat>();
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                float offset=(float)typeof(LavaCreatureCombat).GetField("bodyOffset",flags).GetValue(ai);
                float radius=(float)typeof(LavaCreatureCombat).GetField("radius",flags).GetValue(ai);
                var lines=new List<string>{$"actor={a.transform.position} offset={offset} radius={radius} target={ai.BuildingTarget}",
                    $"inside={SpiritOutdoorNavigation.Contains(a.transform.position,radius+.2f)}"};
                foreach(var c in Physics.OverlapSphere(a.transform.position+Vector3.up*offset,radius+.1f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    lines.Add("overlap="+c.name+" bounds="+c.bounds);
                for(int i=0;i<12;i++)
                {
                    var dir=Quaternion.Euler(0,i*30,0)*Vector3.forward;
                    var clear=typeof(LavaCreatureCombat).GetMethod("ClearDistance",flags).Invoke(ai,new object[]{a.transform.position,dir,4.6f});
                    lines.Add("direction="+i*30+" clear="+clear);
                    foreach(var hit in Physics.SphereCastAll(a.transform.position+Vector3.up*offset,radius,dir,4.6f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                        lines.Add("hit="+hit.collider.name+" distance="+hit.distance+" normal="+hit.normal);
                }
                File.WriteAllLines(Folder+"/stuck-diagnostics.txt",lines);
            }

            previous = SceneManager.GetActiveScene(); hidden.Clear();
            foreach (var root in previous.GetRootGameObjects()) if (root.activeSelf && !root.GetComponent<LavaCombatEffects>()) { hidden.Add(root); root.SetActive(false); }
            review = SceneManager.CreateScene("Village combat verification"); SceneManager.SetActiveScene(review);
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); floorMaterial.color = new Color(.22f,.24f,.22f);
            Cube("Ground", new Vector3(25,-.25f,0), new Vector3(180,.5f,100));
            var light = new GameObject("Review sunlight").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f; light.transform.rotation=Quaternion.Euler(50,-35,0);
            camera = new GameObject("Review camera").AddComponent<Camera>(); camera.fieldOfView=55; camera.farClipPlane=250;
            camera.backgroundColor=new Color(.12f,.17f,.23f); camera.clearFlags=CameraClearFlags.SolidColor;
            wallHouse = House("Rotated closed house", Vector3.zero, new Vector3(10,6,8), 18);
            wallHouse.Ignite(); wallHouse.ApplyWater(100);
            leftHouse=House("Left siege target",new Vector3(-24,0,5),new Vector3(8,6,9),-12);
            rightHouse=House("Right siege target",new Vector3(24,0,5),new Vector3(8,6,9),10);
            fire=House("Layered building fire",new Vector3(65,0,0),new Vector3(13,9,11),-15);
            fire.secondsToCollapse=400; fire.Ignite();
            target=Target(new Vector3(0,0,18));
            hunter=Creature(new Vector3(0,0,-17),target,false);
            raiderA=Creature(new Vector3(-17,0,-14),target,true);
            raiderB=Creature(new Vector3(17,0,-14),target,true);
            Physics.SyncTransforms();
            minDistance=100; maxSide=0; overlaps=0;
            var route=new SpiritOutdoorNavigation(); var first=route.Next(new Vector3(0,0,-17),new Vector3(0,0,18),.8f);
            Add("Route plans around a rotated building", Mathf.Abs(first.x)>3 && !SpiritOutdoorNavigation.Blocked(new Vector3(0,0,-17),first,.8f),first.ToString());
            var pool=new GameObject("Splash pool"); pool.transform.position=new Vector3(65,-2,20);
            var volume=pool.AddComponent<BoxCollider>(); volume.isTrigger=true; volume.size=new Vector3(20,4,14);
            water=pool.AddComponent<WaterSourceVolume>(); water.volume=volume; water.surfaceY=0; water.rejectDryTerrain=false;
            water.Effects.HandStroke(new Vector3(65,0,20),Vector3.back,1);
            Add("Hand strokes use splash droplets and crowns",water.Effects.HandSplashes==1&&water.Effects.Droplets.particleCount>=18);
            int count=water.Effects.HandSplashes; water.Effects.HandStroke(new Vector3(1000,0,1000),Vector3.back,1);
            Add("Dry land does not create swimming splashes",water.Effects.HandSplashes==count);
            started=Time.time; Next(0);
        }
        catch(Exception ex) { Finish(ex.ToString()); }
    }
    static void Update()
    {
        if(phase<0||!EditorApplication.isPlaying||EditorApplication.isPaused||frame==Time.frameCount) return;
        frame=Time.frameCount;
        try
        {
            if(EditorApplication.timeSinceStartup-wallStarted>180) throw new TimeoutException("Village review timed out in phase "+phase);
            float age=Time.time-phaseAt;
            if(phase==0)
            {
                if(hunter&&!hunter.IsDefeated)
                {
                    minDistance=Mathf.Min(minDistance,Vector3.Distance(hunter.transform.position,target.transform.position));
                    maxSide=Mathf.Max(maxSide,Mathf.Abs(hunter.transform.position.x));
                    if(SpiritOutdoorNavigation.Contains(hunter.transform.position,0)) overlaps++;
                }
                if(age>1 && age<2)
                {
                    var a=raiderA.GetComponent<LavaCreatureCombat>(); var b=raiderB.GetComponent<LavaCreatureCombat>();
                    if(a.BuildingTarget&&b.BuildingTarget)
                    { Add("House raiders distribute their targets",a.BuildingTarget!=b.BuildingTarget,a.BuildingTarget.name+" / "+b.BuildingTarget.name); Next(1); }
                }
                if(age>3) { Add("House raiders acquire targets",false); Next(1); }
            }
            else if(phase==1)
            {
                minDistance=Mathf.Min(minDistance,Vector3.Distance(hunter.transform.position,target.transform.position));
                maxSide=Mathf.Max(maxSide,Mathf.Abs(hunter.transform.position.x));
                if(SpiritOutdoorNavigation.Contains(hunter.transform.position,0)) overlaps++;
                if(age<12) return;
                Add("Hunter navigates around the house without entering it",overlaps==0&&maxSide>5&&minDistance<6,$"insideFrames={overlaps}, side={maxSide:F2}, closest={minDistance:F2}");
                Add("Both raiders ignite houses from outside",raiderA.ReachedBuilding&&raiderB.ReachedBuilding&&leftHouse.IsBurning&&rightHouse.IsBurning,
                    $"A={raiderA.transform.position} {raiderA.GetComponent<LavaCreatureCombat>().State}; B={raiderB.transform.position} {raiderB.GetComponent<LavaCreatureCombat>().State}");
                var visual=fire.GetComponent<BuildingFireVisual>();
                Add("Fire has facade emitters, smoke, embers and light",visual&&visual.FlameSeatCount>=16&&visual.ActiveParticles>150&&fire.fireLight.enabled,
                    visual?$"seats={visual.FlameSeatCount}, particles={visual.ActiveParticles}":"missing visual");
                camera.transform.position=new Vector3(84,14,-23); camera.transform.LookAt(new Vector3(65,6,0)); Capture("building-fire");
                float health=hunter.Health; hunter.ApplyBulletDamage(20,hunter.transform.position+Vector3.up,Vector3.forward);
                Add("Bullet causes local stagger and damage",hunter.Health==health-20&&hunter.GetComponent<LavaCreatureCombat>().State==LavaCreatureCombat.Motion.Staggered);
                Add("Impact feedback preserves world time",Mathf.Approximately(Time.timeScale,1)&&hunter.GetComponent<LavaSpiritVisual>().ImpactFlash>.9f);
                var retaliator=Creature(new Vector3(8,0,22),target,true); retaliator.ApplyBulletDamage(10,retaliator.transform.position+Vector3.up,Vector3.back);
                Add("Shot house raiders temporarily retaliate",!retaliator.GetComponent<LavaCreatureCombat>().PursuingBuilding);
                fire.ApplyWater(100); Add("Water immediately stops flames and marks the building rescued",fire.IsRescued&&!fire.flames.isEmitting&&!fire.fireLight.enabled);
                Next(2);
            }
            else if(phase==2 && age>2.5f)
            {
                Add("Extinguished flames drain out of the particle pool",fire.flames.particleCount==0);
                Add("Player hunters actually damage the player",target.Health<target.maximumHealth,$"health={target.Health}");
                var rig=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab"));
                rig.transform.position=new Vector3(-55,0,-20);
                foreach(var listener in rig.GetComponentsInChildren<AudioListener>()) listener.enabled=false;
                var player=rig.GetComponent<Itfaiye.ItfaiyeFirstPersonController>(); player.externalControl=true; player.SetInputCaptured(true);
                pistol=player.pistol; pistol.externalControl=true; pistol.showHud=false; pistol.Equip(); Next(3);
            }
            else if(phase==3 && age>1.1f)
            {
                Add("Real pistol is ready to fire",pistol.Ready);
                bulletTarget=Creature(new Vector3(-55,0,-16),target,false);
                bulletTarget.GetComponent<LavaCreatureCombat>().enabled=false; bulletTarget.jump.enabled=false;
                bulletTarget.jump.CombatWindup(-pistol.muzzle.forward,0);
                Physics.SyncTransforms();
                var capsule=bulletTarget.GetComponentInChildren<CapsuleCollider>();
                bulletTarget.transform.position+=pistol.muzzle.position+pistol.muzzle.forward*3-capsule.bounds.center;
                Physics.SyncTransforms();
                Add("Real pistol accepts the shot",pistol.Fire()); Next(4);
            }
            else if(phase==4 && age>.055f)
            {
                Add("Real muzzle ray damages the spirit",bulletTarget.Health==66&&pistol.LastShotCollider&&pistol.LastShotCollider.GetComponentInParent<LavaCreatureActor>()==bulletTarget,
                    "health="+bulletTarget.Health);
                Add("Real bullet triggers hot impact flash without a wall decal",bulletTarget.GetComponent<LavaSpiritVisual>().ImpactFlash>.3f&&pistol.Feedback.LastMark==null);
                var savedCamera=camera; camera=pistol.player.playerCamera; Capture("spirit-bullet-impact"); camera=savedCamera;
                FireCombatChecks.Run();
                Add("No runtime errors",report.errors.Count==0); Finish(null);
            }
        }
        catch(Exception ex) { Finish(ex.ToString()); }
    }
    static GameObject Cube(string name,Vector3 p,Vector3 size)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.position=p; go.transform.localScale=size;
        go.GetComponent<Renderer>().sharedMaterial=floorMaterial; return go;
    }
    static BurnableBuilding House(string name,Vector3 p,Vector3 size,float rotation)
    {
        var go=new GameObject(name); go.transform.SetPositionAndRotation(p,Quaternion.Euler(0,rotation,0));
        var body=Cube("Walls",p+Vector3.up*size.y*.5f,size); body.transform.SetParent(go.transform,true); body.transform.localRotation=Quaternion.identity;
        var bounds=go.AddComponent<BoxCollider>(); bounds.isTrigger=true; bounds.center=Vector3.up*size.y*.5f; bounds.size=size;
        var f=new GameObject("Fire"); f.transform.SetParent(go.transform,false); var flames=f.AddComponent<ParticleSystem>();
        var s=new GameObject("Smoke"); s.transform.SetParent(go.transform,false); var smoke=s.AddComponent<ParticleSystem>();
        var l=new GameObject("Fire glow"); l.transform.SetParent(go.transform,false); l.transform.localPosition=Vector3.up*size.y*.6f; var glow=l.AddComponent<Light>(); glow.type=LightType.Point;
        var building=go.AddComponent<BurnableBuilding>(); building.ignitionBounds=bounds; building.flames=flames; building.smoke=smoke; building.fireLight=glow;
        building.secondsToCollapse=400; building.TickFire(.001f); return building;
    }
    static CreatureCombatPlayer Target(Vector3 p)
    {
        var go=new GameObject("Player target"); go.transform.position=p;
        var body=go.AddComponent<CharacterController>(); body.height=1.8f; body.radius=.3f; body.center=Vector3.up*.9f;
        var victim=go.AddComponent<CreatureCombatPlayer>(); victim.allowRecovery=false; return victim;
    }
    static LavaCreatureActor Creature(Vector3 p,CreatureCombatPlayer victim,bool siege)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LavaCreature/Prefabs/LavaCreature.prefab");
        var root=Object.Instantiate(prefab); root.name=siege?"House raider":"Player hunter"; root.transform.position=p;
        var renderers=root.GetComponentsInChildren<Renderer>().Where(r=>!(r is ParticleSystemRenderer)&&!r.name.ToLowerInvariant().Contains("flame")&&!r.name.ToLowerInvariant().Contains("ember")).ToArray();
        Bounds bounds=renderers[0].bounds; foreach(var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.localScale*=1.8f/bounds.size.y;
        bounds=renderers[0].bounds; foreach(var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.position+=Vector3.up*(p.y-bounds.min.y);
        bounds=renderers[0].bounds; foreach(var r in renderers) bounds.Encapsulate(r.bounds);
        var hit=new GameObject("Hit capsule"); hit.transform.position=bounds.center; hit.transform.SetParent(root.transform.Find("SomersaultVisual"),true);
        var capsule=hit.AddComponent<CapsuleCollider>(); capsule.radius=.35f; capsule.height=1.8f;
        var actor=root.AddComponent<LavaCreatureActor>(); var combat=actor.GetComponent<LavaCreatureCombat>(); combat.target=victim; combat.attackBuildings=siege;
        Physics.SyncTransforms(); return actor;
    }
    static void Capture(string name)
    {
        var rt=new RenderTexture(1200,800,24); var tex=new Texture2D(1200,800,TextureFormat.RGB24,false); var active=RenderTexture.active;
        try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; tex.ReadPixels(new Rect(0,0,1200,800),0,0); tex.Apply(); File.WriteAllBytes(Folder+"/"+name+".png",tex.EncodeToPNG()); }
        finally { camera.targetTexture=null; RenderTexture.active=active; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
    }
    static void Add(string name,bool passed,string evidence="") { report.checks.Add(new Check{name=name,passed=passed,evidence=evidence}); Save(); }
    static void Next(int value) { phase=value; phaseAt=Time.time; Save(); }
    static void Save() => File.WriteAllText(Folder+"/report.json",JsonUtility.ToJson(report,true));
    static void Log(string text,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) report.errors.Add(text+"\n"+stack); }
    static void Finish(string error)
    {
        phase=-1; Application.logMessageReceived-=Log;
        report.error=error; report.status=error==null&&report.errors.Count==0&&report.checks.All(c=>c.passed)?"passed":"failed"; Save();
        Time.captureDeltaTime=oldCapture; Time.timeScale=1; Application.runInBackground=background;
        if(EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
