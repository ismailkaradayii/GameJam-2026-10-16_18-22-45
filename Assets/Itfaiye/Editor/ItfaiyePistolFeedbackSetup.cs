using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Itfaiye.Editor
{
    public static class ItfaiyePistolFeedbackSetup
    {
        const string Root="Assets/Itfaiye/";
        [MenuItem("Tools/Itfaiye/Install Shot Feedback")]
        public static void Install()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before installing feedback.");
            var prefab=PrefabUtility.LoadPrefabContents(Root+"Prefabs/ItfaiyeFirstPersonPlayer.prefab");
            try
            {
                var hands=prefab.GetComponentInChildren<ItfaiyeFirstPersonHands>(true);
                string gripBefore=EditorJsonUtility.ToJson(hands);
                var pistol=prefab.GetComponentInChildren<ItfaiyePistol>(true);
                var feedback=pistol.GetComponent<ItfaiyePistolFeedback>() ?? pistol.gameObject.AddComponent<ItfaiyePistolFeedback>();
                feedback.impactMaterial=Material("PistolImpact", "Itfaiye/Pistol Impact");
                feedback.tracerMaterial=Material("PistolTracer", "Itfaiye/Pistol Tracer");
                feedback.dustMaterial=Material("ImpactDust", "Itfaiye/Pistol Particle");
                feedback.dustMaterial.SetFloat("_Glow",0); feedback.dustMaterial.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                feedback.sparkMaterial=Material("ImpactSpark", "Itfaiye/Pistol Particle");
                feedback.sparkMaterial.SetFloat("_Glow",.25f); feedback.sparkMaterial.SetFloat("_DstBlend",(float)BlendMode.One);
                feedback.shotClips=new[]{Clip("pistol-shot-01"),Clip("pistol-shot-02"),Clip("pistol-shot-03")};
                feedback.concreteImpact=Clip("impact-concrete"); feedback.metalImpact=Clip("impact-metal");
                feedback.woodImpact=Clip("impact-wood"); feedback.dirtImpact=Clip("impact-dirt");
                EditorUtility.SetDirty(feedback.dustMaterial); EditorUtility.SetDirty(feedback.sparkMaterial);
                if(gripBefore!=EditorJsonUtility.ToJson(hands)) throw new InvalidOperationException("Grip was changed during setup.");
                PrefabUtility.SaveAsPrefabAsset(prefab,Root+"Prefabs/ItfaiyeFirstPersonPlayer.prefab");
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Library/ItfaiyePistol/Feedback");
                File.WriteAllText("Library/ItfaiyePistol/Feedback/install.txt","SUCCESS: Feedback installed; hand component unchanged.\n"+gripBefore);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            SelectSettings();
        }
        static AudioClip Clip(string name)
        {
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"Audio/"+name+".wav");
            if(clip==null) throw new InvalidOperationException("Missing feedback audio: "+name);
            return clip;
        }
        static Material Material(string name,string shaderName)
        {
            string path=Root+"Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material!=null) return material;
            var shader=Shader.Find(shaderName);
            if(shader==null || !shader.isSupported) throw new InvalidOperationException("Missing or unsupported shader: "+shaderName);
            material=new Material(shader); AssetDatabase.CreateAsset(material,path); return material;
        }
        [MenuItem("Tools/Itfaiye/Mermi Izi ve Vurus Ayarlari")]
        public static void SelectSettings()
        {
            var feedback=UnityEngine.Object.FindFirstObjectByType<ItfaiyePistolFeedback>();
            if(feedback!=null) { Selection.activeObject=feedback; EditorGUIUtility.PingObject(feedback); }
        }
    }
}
