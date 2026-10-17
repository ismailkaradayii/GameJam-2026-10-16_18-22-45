using System;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VolcanoCinematicChecks
{
    const string Pending = "VolcanoCinematicChecks.Pending";
    static VolcanoEruptionSequence sequence;
    static double start;
    static int capture;
    static bool running;
    static bool previousBackground;
    static readonly System.Collections.Generic.List<string> results = new System.Collections.Generic.List<string>();
    static VolcanoCinematicChecks()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending,false)) { SessionState.SetBool(Pending,false); EditorApplication.delayCall += Start; } };
    }
    public static void Begin()
    {
        if (EditorApplication.isPlaying) return;
        results.Clear();
        var effects=Object.FindFirstObjectByType<VolcanoEruptionSequence>().effects;
        string smoke=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(effects.smokePlume);
        string burst=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(effects.eruptionBurst);
        string sparks=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(effects.lavaBombs);
        Check("Original asset smoke and eruption prefabs",smoke.EndsWith("/VolcanoSmoke.prefab") && burst.EndsWith("/VolcanoSmoke2.prefab") && sparks.EndsWith("/Lava_sparkles.prefab"),smoke+"; "+burst+"; "+sparks);
        File.WriteAllLines(VolcanoMissionSetup.Folder+"/native-fx-checks.txt",results);
        File.Delete(VolcanoMissionSetup.Folder+"/preview-error.txt");
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        results.Clear(); capture=0; start=EditorApplication.timeSinceStartup; running=true; previousBackground=Application.runInBackground;
        try
        {
            results.AddRange(File.ReadAllLines(VolcanoMissionSetup.Folder+"/native-fx-checks.txt"));
            sequence=Object.FindFirstObjectByType<VolcanoEruptionSequence>();
            sequence.missionDirector.enabled=false; sequence.missionDirector=null; sequence.evacuation=null;
            var player=sequence.cinematic.player; player.hose.fillingUnlocked=true;
            sequence.BeginEruption(); Time.timeScale=1; Application.runInBackground=true;
        }
        catch(Exception ex){Fail(ex);}
    }
    static void Tick()
    {
        if(!running || !EditorApplication.isPlaying || EditorApplication.isCompiling)return;
        try
        {
            if(EditorApplication.timeSinceStartup-start>35)throw new TimeoutException("Cinematic preview timed out.");
            var cinema=sequence.cinematic;
            if(cinema.IsPlaying && capture<3 && cinema.Elapsed>=2+capture*3)
            {
                if(capture==0)
                {
                    Check("Native eruption particles are playing",sequence.effects.smokePlume.isPlaying&&sequence.effects.eruptionBurst.isPlaying&&sequence.effects.lavaBombs.isPlaying,"Original smoke, explosion cloud and sparks active.");
                    Check("Crater glows before downhill lava is released",sequence.pools.Any(p=>p.beginsWithEruption&&p.source.IsHotSurfaceActive)&&sequence.routes.All(r=>r.flow.VisibleDistance<=0),"Original crater lava visible; downhill routes held until the shot ends.");
                }
                EvacuationMissionChecks.CaptureCinema("cinematic-"+(capture+1),cinema.ShotCamera); capture++;
            }
            if(!cinema.HasPlayed||cinema.IsPlaying)return;
            Check("Ten seconds with restored camera",Mathf.Abs(cinema.Elapsed-10)<.001f&&!cinema.player.CinematicLocked&&cinema.player.viewCamera.enabled,cinema.Elapsed.ToString());
            CaptureHazardExamples(Check);
            File.WriteAllLines(VolcanoMissionSetup.Folder+"/cinematic-checks.txt",results);
            running=false;Application.runInBackground=previousBackground;EditorApplication.ExitPlaymode();
        }
        catch(Exception ex){Fail(ex);}
    }
    public static void CaptureHazardExamples(Action<string,bool,string> check)
    {
            var branch=Object.FindObjectsByType<ProgressiveLavaFlow>(FindObjectsSortMode.None).Where(f=>f.canBeStoppedByWater).OrderByDescending(f=>f.Length).First();
            branch.SetDistance(branch.Length*.35f);
            var cooling=branch.GetComponent<LavaCoolingSurface>(); cooling.ApplyWater(branch.FrontPosition,150);
            float distance=branch.VisibleDistance; branch.Advance(5);
            check("Cooled advancing front stops house-bound lava",branch.StoppedByCooling&&branch.VisibleDistance==distance,"distance="+distance);
            EvacuationSceneAudit.Capture("cooled-lava",branch.FrontPosition+new Vector3(-8,38,3),branch.FrontPosition);
            var house=Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).Where(h=>!h.IsBurnedOut).OrderBy(h=>h.transform.position.x).First();
            Vector3 housePosition=house.transform.position;
            house.Ignite();house.TickFire(91);
            check("Actual evacuated house becomes ash",house.IsBurnedOut&&house.GetComponent<BuildingRuinVisual>(),house.name);
            EvacuationSceneAudit.Capture("burned-house",housePosition+new Vector3(32,23,32),housePosition);
    }
    static void Fail(Exception ex){running=false;Application.runInBackground=previousBackground;File.WriteAllText(VolcanoMissionSetup.Folder+"/preview-error.txt",ex.ToString());EditorApplication.ExitPlaymode();}
    static void Check(string name,bool pass,string evidence){results.Add((pass?"PASS ":"FAIL ")+name+": "+evidence);if(!pass)throw new Exception(name);}
}
