using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CleanHudChecks
{
    const string Pending = "CleanHUD.Check";
    static readonly List<string> lines = new List<string>();
    static readonly List<int> sizes = new List<int>();
    static readonly Vector2Int[] resolutions = { new Vector2Int(1920,1080), new Vector2Int(1280,720), new Vector2Int(1024,768), new Vector2Int(2560,1080) };
    static RescueHUD hud; static VolcanoMissionDirector mission; static FireRescuePlayer player;
    static EditorWindow game; static bool maximized, background; static int originalSize, phase=-1, frame, resolution;
    static object sizeGroup; static float began; static double deadline; static string error;
    static string capture; static int captureAt;
    static CleanHudChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending,false)) { SessionState.SetBool(Pending,false); EditorApplication.delayCall += Start; }
            if (s == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Check clean HUD")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        CleanHudSetup.Validate(); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        lines.Clear(); sizes.Clear(); error=null; resolution=0; capture=null;
        lines.Add("Real-frame UI review of saved Volcano_Demo. Mission phase, timer and viewpoint are visual fixtures; this is not a manual playthrough. Existing gameplay APIs drive pause and results.");
        hud=Object.FindFirstObjectByType<RescueHUD>(); mission=hud.mission; player=hud.player;
        background=Application.runInBackground; Application.runInBackground=true;
        game=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); maximized=game.maximized; game.Focus(); game.maximized=true;
        Application.logMessageReceived+=Log; Next(0);
    }
    static void Update()
    {
        if(phase<0 || !EditorApplication.isPlaying || frame==Time.frameCount) return;
        frame=Time.frameCount;
        try { if(error!=null) throw new InvalidOperationException(error); if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("Clean HUD phase "+phase); Tick(); }
        catch(Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age=Time.unscaledTime-began;
        switch(phase)
        {
            case 0:
                if(mission.cinematic.IsPlaying || mission.State!=VolcanoMissionDirector.FlowState.Preparation) return;
                mission.enabled=false; mission.encounter.enabled=false; player.AcquirePistol();
                typeof(VolcanoMissionDirector).GetProperty("Phase").SetValue(mission,VolcanoMissionDirector.MissionPhase.Truck);
                typeof(VolcanoMissionDirector).GetProperty("PreparationRemaining").SetValue(mission,35f);
                var evac=mission.evacuation; var forward=Vector3.ProjectOnPlane(evac.shelterEntrance.position-evac.shelterApproach.position,Vector3.up).normalized;
                player.PlaceOnFoot(evac.shelterApproach.position-forward*9,Quaternion.LookRotation(forward).eulerAngles.y);
                player.enabled=false;
                player.firstPerson.SetInputCaptured(true); player.pistol.SetExternalInput(true,false,false,false,false,false); player.pistol.Equip();
                var camera=player.gameObject.AddComponent<ContextualInteractionReviewCamera>(); camera.viewCamera=player.viewCamera;
                camera.Look(player.viewCamera.transform.position,evac.shelterEntrance.position+Vector3.up*4);
                SetResolution(resolutions[0]); Next(1); return;
            case 1:
                if(age<.8f) return;
                if(!Photo("01-clean-truck-"+resolutions[resolution].x+".png")) return;
                var quest=hud.questTitleText.transform.parent as RectTransform; var timer=hud.eruptionText.transform.parent as RectTransform;
                var status=hud.objectiveText.transform.parent as RectTransform;
                Check("readable objective and timer at "+Screen.width+"x"+Screen.height,Inside(quest)&&Inside(timer)&&Inside(status)&&!Bounds(quest).Overlaps(Bounds(timer))&&!Bounds(timer).Overlaps(Bounds(status)));
                Check("objective text fits its content at "+Screen.width+"x"+Screen.height,hud.questBodyText.rectTransform.rect.height+1>=hud.questBodyText.preferredHeight&&hud.questTitleText.rectTransform.rect.height+1>=hud.questTitleText.preferredHeight);
                if(++resolution<resolutions.Length) { SetResolution(resolutions[resolution]); Next(1); return; }
                Check("walking hides the speed and water cards",!hud.speedText.transform.parent.gameObject.activeSelf&&!hud.waterGroup.activeSelf&&hud.equipmentGroup.activeSelf);
                SetResolution(resolutions[0]); mission.evacuation.BeginMission(); Next(2); return;
            case 2:
                if(age<.65f) return;
                if(!Photo("02-clean-notification.png")) return;
                Check("dispatch appears below the play area without a second toast",hud.subtitleText.transform.parent.gameObject.activeSelf&&!hud.toastText.transform.parent.gameObject.activeSelf&&Bounds(hud.subtitleText.rectTransform).yMax<Screen.height*.35f);
                typeof(VolcanoMissionDirector).GetProperty("EruptionTriggered").SetValue(mission,true);
                Next(3); return;
            case 3:
                if(age<.6f) return;
                if(!Photo("03-clean-defense.png")) return;
                Check("defense health replaces the timer compactly",hud.villageIntegrity.IsVisible&&!hud.eruptionText.gameObject.activeSelf&&hud.villageIntegrity.houseFills.Length==5&&hud.villageIntegrity.timerPanel.rect.height<=120);
                hud.flow.Pause(); Next(4); return;
            case 4:
                if(age<.6f) return;
                if(!Photo("04-clean-pause.png")) return;
                Check("pause hides gameplay HUD and retains its controls",hud.PauseVisible&&hud.gameplayGroup.alpha==0&&hud.resumeButton.isActiveAndEnabled&&Time.timeScale==0);
                hud.flow.Resume(); Next(5); return;
            case 5:
                if(age<.3f) return;
                Check("resume restores HUD and time",hud.gameplayGroup.alpha==1&&Time.timeScale==1);
                hud.flow.FinishMission(true); Next(6); return;
            case 6:
                if(age<hud.flow.resultDelaySeconds+hud.outcomeFadeInSeconds+.2f) return;
                if(!Photo("05-clean-success.png")) return;
                Check("result keeps real statistics and restart",hud.VictoryVisible&&hud.victoryStatsText.text.Contains("Saved Villagers")&&hud.victoryStatsText.text.Contains("Houses Saved")&&hud.outcomeRestartButton.isActiveAndEnabled&&hud.gameplayGroup.alpha==0);
                Finish(null); return;
        }
    }
    static Rect Bounds(RectTransform rect)
    { var c=new Vector3[4]; rect.GetWorldCorners(c); return Rect.MinMaxRect(c[0].x,c[0].y,c[2].x,c[2].y); }
    static bool Inside(RectTransform rect) { var b=Bounds(rect); return b.xMin>=0&&b.yMin>=0&&b.xMax<=Screen.width+1&&b.yMax<=Screen.height+1; }
    static bool Photo(string file)
    {
        if(capture!=file) { ScreenCapture.CaptureScreenshot(CleanHudSetup.Reports+"/"+file); capture=file; captureAt=Time.frameCount; return false; }
        return Time.frameCount>captureAt+2;
    }
    static void Check(string label,bool passed) { lines.Add((passed?"PASS ":"FAIL ")+label); File.WriteAllLines(CleanHudSetup.Reports+"/checks.txt",lines); if(!passed) throw new InvalidOperationException(label); }
    static void Next(int value) { phase=value; began=Time.unscaledTime; deadline=EditorApplication.timeSinceStartup+45; }
    static void Log(string condition,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) error=condition+"\n"+stack; }
    static void SetResolution(Vector2Int size)
    {
        var a=typeof(Editor).Assembly; var sizesType=a.GetType("UnityEditor.GameViewSizes"); var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var instance=singleton.GetProperty("instance").GetValue(null); var groupType=sizesType.GetProperty("currentGroupType").GetValue(instance);
        sizeGroup=sizesType.GetMethod("GetGroup").Invoke(instance,new[]{groupType}); var group=sizeGroup.GetType();
        int count=(int)group.GetMethod("GetTotalCount").Invoke(sizeGroup,null); if(sizes.Count==0) originalSize=(int)game.GetType().GetProperty("selectedSizeIndex").GetValue(game);
        var item=Activator.CreateInstance(a.GetType("UnityEditor.GameViewSize"),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
            new[]{Enum.Parse(a.GetType("UnityEditor.GameViewSizeType"),"FixedResolution"),(object)size.x,size.y,"Clean HUD review"},null);
        group.GetMethod("AddCustomSize").Invoke(sizeGroup,new[]{item}); sizes.Add(count); game.GetType().GetProperty("selectedSizeIndex").SetValue(game,count); game.Repaint();
    }
    static void Finish(string failure)
    {
        if(phase<0) return; phase=-1; Application.logMessageReceived-=Log;
        if(sizeGroup!=null&&sizes.Count>0) { game.GetType().GetProperty("selectedSizeIndex").SetValue(game,originalSize); foreach(int i in sizes.AsEnumerable().Reverse()) sizeGroup.GetType().GetMethod("RemoveCustomSize").Invoke(sizeGroup,new object[]{i}); }
        if(game) game.maximized=maximized; Application.runInBackground=background;
        lines.Add(failure==null?"COMPLETED":"FAILED "+failure); File.WriteAllLines(CleanHudSetup.Reports+"/checks.txt",lines);
        Time.timeScale=1; EditorApplication.ExitPlaymode();
    }
}
