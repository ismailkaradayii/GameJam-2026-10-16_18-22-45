using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VillageStatusAndSprayChecks
{
    const string Folder=VillageStatusAndSpraySetup.Folder;
    [Serializable] public class Check { public string name,evidence; public bool passed; }
    [Serializable] public class Report { public string status="running",error; public List<Check> checks=new List<Check>(); public List<string> errors=new List<string>(); }
    static Report report;
    static RescueHUD hud;
    static VillageIntegrityHUD status;
    static VolcanoMissionDirector mission;
    static VolcanoSurvivalObjective survival;
    static BurnableBuilding[] houses;
    static FireTruckWaterCannon cannon;
    static Camera camera;
    static Vector3 villageView,villageLook;
    static int phase=-1,frame;
    static float phaseAt,oldCapture;
    static bool background;
    static double deadline;
    static readonly ParticleSystem.Particle[] drops=new ParticleSystem.Particle[4096];

    static VillageStatusAndSprayChecks()
    {
        EditorApplication.update+=Update;
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("VillageStatusAndSpray.Pending",false))
            { SessionState.SetBool("VillageStatusAndSpray.Pending",false); EditorApplication.delayCall+=Start; }
        };
    }
    public static void Begin()
    {
        if(EditorApplication.isPlaying||phase>=0) return;
        SessionState.SetBool("VillageStatusAndSpray.Pending",true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        report=new Report(); Directory.CreateDirectory(Folder); Application.logMessageReceived+=Log;
        oldCapture=Time.captureDeltaTime; Time.captureDeltaTime=1f/60; Time.timeScale=1;
        background=Application.runInBackground; Application.runInBackground=true;
        deadline=EditorApplication.timeSinceStartup+180;
        try
        {
            hud=Object.FindFirstObjectByType<RescueHUD>(); status=hud.villageIntegrity;
            mission=hud.missionDirector; survival=hud.survivalObjective;
            houses=hud.flow.targetBuildings;
            Add("Saved timer has one health bar per authored house",status&&status.houseFills.Length==houses.Length&&houses.Length==5);
            Add("House bars match the village house number order",houses.Select(h=>h.name).SequenceEqual(houses.Select(h=>h.name).OrderBy(n=>n,StringComparer.Ordinal)));
            Add("Each house owns its detached warm window overlay",houses.All(h=>h.windowGlowOverlays.Length==1&&h.windowGlowOverlays[0])&&
                houses.Select(h=>h.windowGlowOverlays[0]).Distinct().Count()==houses.Length);
            mission.enabled=false; mission.sequence.enabled=false;
            if(mission.cinematic) { mission.cinematic.EndShot(); mission.cinematic.enabled=false; }
            mission.cinematic=null; mission.sequence.cinematic=null; mission.sequence.evacuation=null; mission.sequence.missionDirector=null;
            if(mission.evacuation) mission.evacuation.enabled=false;
            mission.encounter.enabled=false; mission.encounter.StopEncounter();
            if(survival) survival.enabled=false;
            foreach(var flow in Object.FindObjectsByType<ProgressiveLavaFlow>(FindObjectsSortMode.None)) flow.enabled=false;
            foreach(var source in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsSortMode.None)) source.enabled=false;
            hud.player.enabled=false; hud.player.firstPerson.enabled=false;
            foreach(var view in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) view.enabled=false;
            camera=new GameObject("Village status review camera").AddComponent<Camera>(); camera.fieldOfView=58;
            camera.farClipPlane=2000; camera.nearClipPlane=.1f; camera.allowHDR=true;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var b=houses[1].ignitionBounds.bounds;
            villageView=b.center+new Vector3(-b.extents.x*1.6f,b.size.y*.5f,b.extents.z*2.1f);
            villageLook=b.center+Vector3.up*2; camera.transform.position=villageView; camera.transform.LookAt(villageLook);
            hud.canvas.renderMode=RenderMode.ScreenSpaceCamera; hud.canvas.worldCamera=camera; hud.canvas.planeDistance=1;
            status.Refresh(false); Next(0);
        }
        catch(Exception ex) { Finish(ex.ToString()); }
    }
    static void Update()
    {
        if(phase<0||!EditorApplication.isPlaying||EditorApplication.isPaused||frame==Time.frameCount) return;
        frame=Time.frameCount;
        try
        {
            if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("Status and spray phase "+phase);
            float age=Time.time-phaseAt;
            if(phase==0&&age>.35f)
            {
                Add("Preparation still shows the countdown",!status.IsVisible&&status.timerText.gameObject.activeSelf&&Mathf.Abs(status.timerPanel.rect.height-68)<1);
                mission.TriggerDeadline(); mission.sequence.InitializeSequence();
                for(int i=0;i<500&&mission.sequence.Stage!=VolcanoEruptionSequence.EruptionStage.Flowing;i++) mission.sequence.TickSequence(.05f);
                Time.timeScale=1; Next(1);
            }
            else if(phase==1&&age>.4f)
            {
                Add("Eruption replaces timer with village integrity",status.IsVisible&&!status.timerText.gameObject.activeSelf&&status.timerPanel.rect.height>130);
                Add("All houses start with full health",Mathf.Approximately(status.Integrity01,1)&&status.houseFills.All(fill=>Mathf.Approximately(fill.fillAmount,1)));
                Canvas.ForceUpdateCanvases();
                Add("Village percentage is visible inside the timer panel",status.percentageText.text=="%100"&&status.percentageText.cachedTextGenerator.vertexCount>0&&!status.percentageText.canvasRenderer.cull);
                var original=houses[0]; var fixture=new GameObject("Extinguishing health fixture").AddComponent<BurnableBuilding>();
                fixture.secondsToCollapse=20; houses[0]=fixture;
                fixture.Ignite(); fixture.TickFire(5); status.Refresh(true);
                Add("A burning house loses real health continuously",Mathf.Abs(status.houseFills[0].fillAmount-.75f)<.001f&&Mathf.Abs(status.Integrity01-.95f)<.001f);
                fixture.ApplyWater(100); float health=fixture.Integrity01; fixture.TickFire(12); status.Refresh(true);
                Add("Extinguishing stops health loss without healing damage",fixture.IsRescued&&Mathf.Abs(fixture.Integrity01-health)<.001f&&Mathf.Abs(status.houseFills[0].fillAmount-.75f)<.001f);
                houses[0]=original; Object.DestroyImmediate(fixture.gameObject);
                houses[1].secondsToCollapse=5; houses[1].Ignite(); Next(2);
            }
            else if(phase==2&&age>.9f)
            {
                var fire=houses[1]; fire.TickFire(2); status.Refresh(true);
                Add("Each house has a separate decreasing bar",status.houseFills[1].fillAmount<.6f&&status.houseFills[0].fillAmount==1&&status.BurningCount==1);
                Capture("01-village-health-burning");
                Add("Burning house emits hot particles before collapse",fire.GetComponent<BuildingFireVisual>().ActiveParticles>0);
                fire.TickFire(8); status.Refresh(true);
                Add("Collapse empties that house and reduces total health",fire.IsBurnedOut&&status.houseFills[1].fillAmount==0&&Mathf.Abs(status.Integrity01-.8f)<.001f&&status.LostCount==1);
                Add("Collapse immediately clears flames and yellow embers",fire.GetComponent<BuildingFireVisual>().ActiveParticles==0&&
                    fire.GetComponentsInChildren<ParticleSystem>().Where(p=>p.name!="Collapse ash cloud").All(p=>p.particleCount==0&&!p.isEmitting));
                Add("Ruins have no fire lights",fire.GetComponentsInChildren<Light>().All(l=>!l.enabled));
                var dust=fire.transform.Find("Burned house · ash and charred beams/Collapse ash cloud").GetComponent<ParticleSystem>();
                Color tint=dust.main.startColor.color;
                Add("Collapse starts a textured staged ruin with neutral dust",fire.GetComponent<BuildingRuinVisual>().FragmentCount>3&&
                    fire.GetComponent<BuildingRuinVisual>().IsCollapsing&&tint.r<.3f&&Mathf.Abs(tint.r-tint.g)<.04f&&
                    Mathf.Abs(tint.g-tint.b)<.04f&&!dust.main.loop&&dust.main.startLifetime.constantMax<=3f);
                Next(3);
            }
            else if(phase==3&&age>3.4f)
            {
                var ruin=houses[1].GetComponent<BuildingRuinVisual>();
                Add("Roof and walls fall over time instead of becoming instant ash",ruin.IsCollapsing&&ruin.CollapseProgress>.1f&&ruin.CollapseProgress<1);
                Capture("02a-house-collapse-in-progress"); Next(30);
            }
            else if(phase==30&&age>.1f)
            {
                houses[1].GetComponent<BuildingRuinVisual>().TickCollapse(houses[1].visualCollapseSeconds+16);
                foreach(var particles in houses[1].GetComponentsInChildren<ParticleSystem>()) particles.Simulate(4,false,false);
                Add("Textured house sections finish settling",!houses[1].GetComponent<BuildingRuinVisual>().IsCollapsing);
                Add("Ash settles without restarting yellow effects",houses[1].GetComponentsInChildren<ParticleSystem>().All(p=>p.particleCount==0&&!p.isEmitting));
                var originalRenderers=houses[1].GetComponentsInChildren<Renderer>(true).Where(r=>
                    !r.transform.IsChildOf(houses[1].transform.Find("Burned house · ash and charred beams"))).ToArray();
                Add("Window LODs cannot restore yellow panes above the ruins",houses[1].GetComponentsInChildren<LODGroup>(true).All(l=>!l.enabled)&&originalRenderers.All(r=>!r.enabled),
                    string.Join("; ",originalRenderers.Where(r=>r.enabled).Select(r=>r.GetType().Name+" "+r.name)));
                Add("Detached yellow window overlays disappear with their house",houses[1].windowGlowOverlays.All(r=>!r.enabled&&!r.gameObject.activeInHierarchy));
                Capture("02-ruined-house-no-yellow"); BuildCannon(); Next(4);
            }
            else if(phase==4)
            {
                cannon.TickSpray(1f/60);
                if(age<1.1f) return;
                var visual=cannon.Visuals;
                Add("Spray starts narrow at the nozzle",Mathf.Abs(visual.SprayRadiusAtAge(0)-visual.nozzleRadius)<.001f);
                Add("Spray opens into a cone along its travel",visual.SprayRadiusAtAge(.3f)>1.5f&&visual.SprayRadiusAtAge(.6f)>visual.SprayRadiusAtAge(.3f)*1.8f);
                Add("Cone retains a liquid core, droplets and mist",visual.CoreSegments>8&&visual.VisibleDroplets>100&&visual.Mist.particleCount>0);
                int count=visual.Droplets.GetParticles(drops); float near=0,far=0;
                for(int i=0;i<count;i++)
                {
                    Vector3 p=drops[i].position-cannon.muzzle.position;
                    if(p.z<4) near=Mathf.Max(near,Mathf.Abs(p.x));
                    if(p.z>10) far=Mathf.Max(far,Mathf.Abs(p.x));
                }
                Add("Rendered droplets widen with distance",far>near*2&&far>1,$"near radius={near:F2}, far radius={far:F2}");
                bool canvas=hud.canvas.enabled; hud.canvas.enabled=false; Capture("03-opening-water-cone"); hud.canvas.enabled=canvas;
                float tank=cannon.tank.TankLitres; cannon.SetSpraying(false); cannon.TickSpray(.1f);
                Add("Releasing the cone stops consumption and keeps water in flight",Mathf.Abs(cannon.tank.TankLitres-tank)<.001f&&cannon.InFlightPacketCount>0&&!cannon.IsSpraying);
                cannon.enabled=false; camera.transform.position=villageView; camera.transform.LookAt(villageLook);
                FireCombatChecks.Run();
                var regression=File.ReadAllText("Logs/FireCombatChecks.json"); Add("Existing water combat regression passes",regression.Contains("\"allPassed\": true"));
                foreach(var house in houses) if(house&&!house.IsBurnedOut) { house.secondsToCollapse=1; house.Ignite(); house.TickFire(2); }
                status.Refresh(true);
                Add("Every destroyed house reaches zero on the village bar",status.Integrity01==0&&status.LostCount==houses.Length&&status.houseFills.All(f=>f.fillAmount==0));
                Add("Empty health clearly explains village loss",status.statusText.text.Contains("KÖY KAYBEDİLDİ"));
                survival.enabled=true; survival.TickObjective();
                Add("Zero village health triggers the real defeat state",hud.flow.State==RescueGameFlow.GameState.Lost&&survival.State==VolcanoSurvivalObjective.MissionState.Lost&&Time.timeScale==0);
                Next(5);
            }
            else if(phase==5)
            {
                Add("Defeat screen explains that all houses burned down",hud.victoryDetailText.text.Contains("Tüm evler kül oldu"));
                Capture("04-village-lost"); Add("No runtime errors",report.errors.Count==0); Finish(null);
            }
        }
        catch(Exception ex) { Finish(ex.ToString()); }
    }
    static void BuildCannon()
    {
        Vector3 origin=new Vector3(12000,100,12000);
        var truck=new GameObject("Cone water verification monitor"); truck.transform.position=origin+Vector3.up*3;
        var tank=truck.AddComponent<FireHoseSystem>(); tank.SetTankLitres(1000);
        cannon=truck.AddComponent<FireTruckWaterCannon>(); cannon.enabled=false; cannon.tank=tank; cannon.muzzle=truck.transform;
        cannon.muzzleSpeed=35; cannon.SetSpraying(true);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name="Water cone impact surface";
        wall.transform.position=origin+new Vector3(0,4,23); wall.transform.localScale=new Vector3(28,14,.5f);
        var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.color=new Color(.12f,.18f,.2f); wall.GetComponent<Renderer>().sharedMaterial=mat;
        camera.transform.position=origin+new Vector3(10,7,-3); camera.transform.LookAt(origin+new Vector3(-1,2,13)); camera.farClipPlane=200;
        Physics.SyncTransforms();
    }
    static void Capture(string name)
    {
        var rt=new RenderTexture(1440,900,24); var texture=new Texture2D(1440,900,TextureFormat.RGB24,false); var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=rt; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,1440,900),0,0); texture.Apply(); File.WriteAllBytes(Folder+"/"+name+".png",texture.EncodeToPNG());
        }
        finally { camera.targetTexture=null; RenderTexture.active=previous; Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); }
    }
    static void Add(string name,bool passed,string evidence="") { report.checks.Add(new Check{name=name,passed=passed,evidence=evidence}); Save(); }
    static void Next(int value) { phase=value; phaseAt=Time.time; Save(); }
    static void Save()=>File.WriteAllText(Folder+"/report.json",JsonUtility.ToJson(report,true));
    static void Log(string text,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) report.errors.Add(text+"\n"+stack); }
    static void Finish(string error)
    {
        phase=-1; Application.logMessageReceived-=Log;
        report.error=error; report.status=error==null&&report.errors.Count==0&&report.checks.All(c=>c.passed)?"passed":"failed"; Save();
        Time.timeScale=1; Time.captureDeltaTime=oldCapture; Application.runInBackground=background;
        if(EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
