using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Itfaiye.Editor
{
    public static class ItfaiyePistolCasingsSetup
    {
        const string Root="Assets/Itfaiye/";
        [MenuItem("Tools/Itfaiye/Install Casings and Slide Polish %#F11")]
        public static void Install()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before installing casing assets.");
            if(!AssetDatabase.IsValidFolder(Root+"Models")) AssetDatabase.CreateFolder("Assets/Itfaiye","Models");
            var prefab=PrefabUtility.LoadPrefabContents(Root+"Prefabs/ItfaiyeFirstPersonPlayer.prefab");
            try
            {
                var pistol=prefab.GetComponent<ItfaiyePistol>();
                var hands=prefab.GetComponentInChildren<ItfaiyeFirstPersonHands>(true);
                Vector3 hipPosition=hands.pistolHipPosition, hipEuler=hands.pistolHipEuler;
                float aimDistance=hands.pistolAimDistance;
                var casings=pistol.GetComponent<ItfaiyePistolCasings>() ?? pistol.gameObject.AddComponent<ItfaiyePistolCasings>();
                casings.casingMesh=CreateMesh();
                casings.brassMaterial=Material("CasingBrass",new Color(.66f,.41f,.12f),.78f,.58f);
                casings.insideMaterial=Material("CasingInside",new Color(.16f,.095f,.025f),.55f,.25f);
                string physicsPath=Root+"Materials/CasingPhysics.physicMaterial";
                casings.casingPhysics=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsPath);
                if(casings.casingPhysics==null)
                {
                    casings.casingPhysics=new PhysicsMaterial("CasingPhysics") { dynamicFriction=.48f,staticFriction=.65f,bounciness=.28f,frictionCombine=PhysicsMaterialCombine.Average,bounceCombine=PhysicsMaterialCombine.Maximum };
                    AssetDatabase.CreateAsset(casings.casingPhysics,physicsPath);
                }
                casings.hardSurfaceClinks=new[]{Clip("casing-clink-01"),Clip("casing-clink-02"),Clip("casing-clink-03")};
                casings.softSurfaceClink=Clip("casing-soft");
                casings.slidePullSound=Clip("slide-pull"); casings.slideReleaseSound=Clip("slide-release");
                // Only the reload-specific left-wrist contact changes. Hip, ADS,
                // support fingers and weapon rotation settings remain untouched.
                hands.slideGripPosition=new Vector3(-.088f,.106f,-.077f);
                if(hands.pistolHipPosition!=hipPosition || hands.pistolHipEuler!=hipEuler || hands.pistolAimDistance!=aimDistance)
                    throw new InvalidOperationException("Normal grip changed during casing setup.");
                PrefabUtility.SaveAsPrefabAsset(prefab,Root+"Prefabs/ItfaiyeFirstPersonPlayer.prefab");
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Library/ItfaiyePistol/Casings");
                File.WriteAllText("Library/ItfaiyePistol/Casings/install.txt","SUCCESS: casing assets installed; normal grip unchanged; reload wrist adjusted by 4/-2/-12 mm.");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        static AudioClip Clip(string name)
        {
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"Audio/"+name+".wav");
            if(clip==null) throw new InvalidOperationException("Missing audio: "+name);
            return clip;
        }
        static Material Material(string name,Color color,float metallic,float smoothness)
        {
            string path=Root+"Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material!=null) return material;
            material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor",color); material.SetFloat("_Metallic",metallic); material.SetFloat("_Smoothness",smoothness);
            AssetDatabase.CreateAsset(material,path); return material;
        }
        static Mesh CreateMesh()
        {
            string path=Root+"Models/EmptyCasing.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path); if(mesh!=null) return mesh;
            // Turned brass with a rim, thin open mouth and a recessed hollow inside.
            var profile=new[]{new Vector2(0,-.012f),new Vector2(.0054f,-.012f),new Vector2(.0054f,-.0108f),new Vector2(.00465f,-.0103f),new Vector2(.00465f,.0105f),new Vector2(.0048f,.011f),new Vector2(.00385f,.011f),new Vector2(.00385f,-.007f),new Vector2(0,-.007f)};
            const int sides=20;
            var vertices=new Vector3[profile.Length*(sides+1)]; var uv=new Vector2[vertices.Length];
            var outer=new List<int>(); var inner=new List<int>();
            for(int row=0;row<profile.Length;row++) for(int s=0;s<=sides;s++)
            {
                int v=row*(sides+1)+s; float angle=s/(float)sides*Mathf.PI*2;
                vertices[v]=new Vector3(Mathf.Cos(angle)*profile[row].x,profile[row].y,Mathf.Sin(angle)*profile[row].x);
                uv[v]=new Vector2(s/(float)sides,row/(float)(profile.Length-1));
            }
            for(int row=0;row<profile.Length-1;row++) for(int s=0;s<sides;s++)
            {
                int a=row*(sides+1)+s,b=a+sides+1,c=a+1,d=b+1;
                var triangles=row>=6?inner:outer;
                triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(c); triangles.Add(b); triangles.Add(d);
            }
            mesh=new Mesh { name="Hollow Empty Brass Casing",vertices=vertices,uv=uv,subMeshCount=2 };
            mesh.SetTriangles(outer,0); mesh.SetTriangles(inner,1); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path); return mesh;
        }
    }
}
