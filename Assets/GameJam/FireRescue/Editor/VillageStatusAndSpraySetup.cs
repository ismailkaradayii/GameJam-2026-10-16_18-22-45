using System;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VillageStatusAndSpraySetup
{
    public const string Folder = "Logs/VillageStatusAndSpray";
    static double next;
    static VillageStatusAndSpraySetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<next) return;
        next=EditorApplication.timeSinceStartup+.5;
        const string request="Logs/VillageStatusAndSpray.command";
        if(!File.Exists(request)) return;
        string command=File.ReadAllText(request).Trim(); File.Delete(request); Directory.CreateDirectory(Folder);
        try
        {
            if(command=="apply") Apply();
            else if(command=="checks") VillageStatusAndSprayChecks.Begin();
            else if(command=="water") WaterRealismChecks.Begin();
            else if(command=="audit") Audit();
            else if(command=="stop") EditorApplication.ExitPlaymode();
            else throw new InvalidOperationException(command);
            File.WriteAllText(Folder+"/command-result.txt","OK "+command);
        }
        catch(Exception ex) { File.WriteAllText(Folder+"/command-result.txt",ex.ToString()); Debug.LogException(ex); }
    }
    [MenuItem("GameJam/Final Loop/Apply village integrity display")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring.");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=FinalLoopSetup.ScenePath) throw new InvalidOperationException("Open the authored Volcano scene first.");
        var hud=Object.FindFirstObjectByType<RescueHUD>();
        if(!hud||!hud.eruptionText||!hud.flow||hud.flow.targetBuildings.Length==0) throw new InvalidOperationException("Existing timer and village bindings are required.");
        Undo.RecordObject(hud.flow,"Match health bars to the authored house numbers");
        hud.flow.targetBuildings=hud.flow.targetBuildings.OrderBy(h=>h.name,StringComparer.Ordinal).ToArray();
        EditorUtility.SetDirty(hud.flow);
        var overlays=Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Where(m=>m.name=="Warm occupied windows"&&m.sharedMesh).ToArray();
        foreach(var house in hud.flow.targetBuildings)
        {
            var source=house.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m=>m.name=="Windows01Glass"&&m.sharedMesh);
            var overlay=source?overlays.Where(m=>m.sharedMesh==source.sharedMesh)
                .OrderBy(m=>(m.transform.position-source.transform.position).sqrMagnitude).FirstOrDefault():null;
            Undo.RecordObject(house,"Retire detached warm windows with their house");
            house.windowGlowOverlays=overlay&&Vector3.Distance(overlay.transform.position,source.transform.position)<.2f
                ?new[]{overlay.GetComponent<Renderer>()}:Array.Empty<Renderer>();
            EditorUtility.SetDirty(house);
        }
        Undo.RecordObject(hud,"Show village integrity after eruption");
        var panel=hud.eruptionText.transform.parent.GetComponent<RectTransform>();
        var sizer=panel.GetComponent<VolcanicRockFrameSizer>();
        if(sizer) { Undo.RecordObject(sizer,"Use the fixed timer and health layouts"); sizer.enabled=false; EditorUtility.SetDirty(sizer); }
        Undo.RecordObject(panel,"Keep the village display width"); panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,660);
        var status=panel.GetComponent<VillageIntegrityHUD>();
        if(!status) status=Undo.AddComponent<VillageIntegrityHUD>(panel.gameObject);
        Undo.RecordObject(status,"Bind village health bars");
        var old=panel.Find("Village defense health");
        if(old) Undo.DestroyObjectImmediate(old.gameObject);
        var root=Rect(panel,"Village defense health",0,0,660,142);
        Undo.RegisterCreatedObjectUndo(root.gameObject,"Village defense health bars");
        Font font=hud.eruptionText.font;
        Label(root,"Title","KÖYÜN DAYANIKLILIĞI",font,21,18,6,470,30);
        status.percentageText=Label(root,"Total percentage","%100",font,23,520,6,122,30,TextAnchor.MiddleRight);
        status.villageFill=Fill(root,"Village total",18,42,624,15);
        int count=hud.flow.targetBuildings.Length;
        status.houseFills=new Image[count]; status.houseLabels=new Text[count];
        float width=(624-(count-1)*10f)/count;
        for(int i=0;i<count;i++)
        {
            float x=18+i*(width+10);
            status.houseLabels[i]=Label(root,"House "+(i+1)+" status","EV "+(i+1)+"  %100",font,13,x,65,width,23);
            status.houseFills[i]=Fill(root,"House "+(i+1)+" integrity",x,92,width,6);
        }
        status.statusText=Label(root,"Fire danger","",font,13,18,108,624,26);
        status.timerPanel=panel; status.timerText=hud.eruptionText;
        status.defenseGroup=root.gameObject; status.flow=hud.flow;
        status.preparationHeight=68; status.defenseHeight=142;
        hud.villageIntegrity=status; status.Refresh(false);
        if(hud.toastText)
        {
            var toast=hud.toastText.transform.parent.GetComponent<RectTransform>();
            Undo.RecordObject(toast,"Keep rescue messages below village status");
            toast.anchoredPosition=new Vector2(toast.anchoredPosition.x,-188); EditorUtility.SetDirty(toast);
        }
        EditorUtility.SetDirty(status); EditorUtility.SetDirty(hud); EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets(); Audit();
    }
    static RectTransform Rect(Transform parent,string name,float x,float y,float width,float height)
    {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent,false);
        r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(width,height); return r;
    }
    static Text Label(Transform parent,string name,string value,Font font,int size,float x,float y,float width,float height,TextAnchor alignment=TextAnchor.MiddleLeft)
    {
        var t=Rect(parent,name,x,y,width,height).gameObject.AddComponent<Text>();
        t.font=font; t.fontSize=size; t.text=value; t.color=new Color(.95f,.93f,.87f); t.alignment=alignment; t.raycastTarget=false;
        t.verticalOverflow=VerticalWrapMode.Overflow; t.horizontalOverflow=HorizontalWrapMode.Overflow;
        return t;
    }
    static Image Fill(Transform parent,string name,float x,float y,float width,float height)
    {
        var track=Rect(parent,name+" track",x,y,width,height); var bg=track.gameObject.AddComponent<Image>();
        bg.color=new Color(.13f,.15f,.16f,.97f); bg.raycastTarget=false;
        var rect=Rect(track,name,0,0,width,height); var fill=rect.gameObject.AddComponent<Image>();
        fill.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        fill.type=Image.Type.Filled; fill.fillMethod=Image.FillMethod.Horizontal; fill.fillOrigin=0;
        fill.color=new Color(.31f,.86f,.5f); fill.raycastTarget=false; fill.fillAmount=1;
        return fill;
    }
    static void Audit()
    {
        var scene=SceneManager.GetActiveScene(); var hud=Object.FindFirstObjectByType<RescueHUD>();
        string result=$"scene={scene.path} play={EditorApplication.isPlaying} dirty={scene.isDirty}\n";
        if(hud&&hud.villageIntegrity) result+=$"houseBars={hud.villageIntegrity.houseFills.Length} total={hud.villageIntegrity.Integrity01} visible={hud.villageIntegrity.IsVisible}\n";
        if(hud&&hud.flow) foreach(var house in hud.flow.targetBuildings)
            result+=$"house={house.name} attachedWindowGlows={house.windowGlowOverlays.Length}\n";
        File.WriteAllText(Folder+"/audit.txt",result);
    }
}
