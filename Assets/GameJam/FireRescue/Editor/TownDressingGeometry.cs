using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Small authored prop kit. Meshes are shared assets; scene instances contain no scripts.</summary>
public static class TownDressingGeometry
{
    public sealed class Palette
    {
        public Material wood, darkWood, metal, accent, fabric, foliage, stone, glow;
        public Material[] Materials => new[] { wood, darkWood, metal, accent, fabric, foliage, stone, glow };
    }
    const string Folder = "Assets/GameJam/TownDressing/Meshes";
    public static GameObject Create(string kind, Transform parent, Palette p)
    {
        string path = Folder + "/" + kind + "-v1.asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh)
        {
            EnsureFolder(Folder);
            var b = new Builder(); Build(kind, b);
            mesh = b.Finish(kind); AssetDatabase.CreateAsset(mesh, path);
        }
        var root = new GameObject(kind);
        root.transform.SetParent(parent, false);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = p.Materials;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.allowOcclusionWhenDynamic = false;
        GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic);
        return root;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    static void Build(string kind, Builder b)
    {
        switch (kind)
        {
            case "bench":
                for (int i = 0; i < 4; i++) b.Box(V(0,.49f,-.27f+i*.18f), V(1.9f,.08f,.15f), 0);
                foreach (float x in new[] {-.72f,.72f})
                {
                    b.Box(V(x,.24f,0),V(.12f,.48f,.6f),1);
                    b.Box(V(x,.74f,.3f),V(.11f,.9f,.1f),1,Quaternion.Euler(-8,0,0));
                    b.Box(V(x,.77f,0),V(.09f,.075f,.74f),0);
                    foreach(float z in new[]{-.26f,.26f}) b.Tube(V(x,.53f,z),V(x,.76f,z),.025f,2);
                }
                for(int i=0;i<3;i++) b.Box(V(0,.75f+i*.17f,.33f),V(1.9f,.13f,.065f),0,Quaternion.Euler(-8,0,0));
                b.Box(V(0,.17f,0),V(1.55f,.09f,.08f),1); break;
            case "fence":
                foreach(float x in new[]{-1.2f,1.2f})
                { b.Box(V(x,.58f,0),V(.14f,1.16f,.14f),1); b.Box(V(x,1.2f,0),V(.19f,.1f,.19f),0); }
                foreach(float y in new[]{.35f,.8f}) b.Box(V(0,y,0),V(2.45f,.105f,.09f),1);
                for(int i=0;i<11;i++)
                {
                    float x=-1.08f+i*.216f; b.Box(V(x,.61f,-.085f),V(.13f,.88f,.06f),0);
                    b.Box(V(x,1.065f,-.085f),V(.11f,.11f,.055f),0,Quaternion.Euler(0,0,45));
                } break;
            case "crate": Crate(b,Vector3.zero,1); break;
            case "supplies":
                Crate(b,V(-.48f,0,0),1); Crate(b,V(.3f,0,.2f),.85f); Crate(b,V(-.4f,.68f,0),.72f);
                b.Box(V(.5f,.52f,.2f),V(.5f,.08f,.6f),4,Quaternion.Euler(0,0,4)); break;
            case "logs":
                for(int row=0;row<3;row++) for(int col=0;col<4-row;col++)
                {
                    float x=(col-(3-row)*.5f)*.34f,y=.19f+row*.29f;
                    Vector3 a=V(x,y,-.56f),c=V(x,y,.56f+(col%2)*.09f);
                    b.Tube(a,c,.175f,1,12); b.Tube(a-V(0,0,.009f),a,.147f,0,12);
                    b.Tube(c,c+V(0,0,.009f),.147f,0,12);
                    b.Tube(a-V(0,0,.011f),a-V(0,0,.009f),.065f,1,12);
                }
                foreach(float x in new[]{-.78f,.78f}) b.Box(V(x,.38f,0),V(.08f,.76f,1.32f),2); break;
            case "bucket": Bucket(b,Vector3.zero,1); break;
            case "planter":
                b.Taper(V(0,.04f,0),V(0,.42f,0),.22f,.32f,3,16,false);
                b.Ring(V(0,.43f,0),.32f,.045f,3); b.Disc(V(0,.395f,0),.28f,1);
                for(int i=0;i<11;i++)
                {
                    float a=i*2.399963f; Vector3 end=V(Mathf.Cos(a)*.4f,.65f+(i%3)*.09f,Mathf.Sin(a)*.4f);
                    Vector3 start=V(0,.4f,0), mid=Vector3.Lerp(start,end,.6f)+Vector3.up*.15f;
                    Vector3 side=Vector3.Cross(end-start,Vector3.up).normalized*.055f;
                    b.Quad(start,mid-side,end,mid+side,5,Vector3.up,true);
                } break;
            case "workbench":
                foreach(float x in new[]{-.86f,.86f}) foreach(float z in new[]{-.32f,.32f}) b.Box(V(x,.43f,z),V(.11f,.86f,.11f),1);
                for(int i=0;i<4;i++) b.Box(V(0,.9f,-.33f+i*.22f),V(2.1f,.1f,.2f),0);
                b.Box(V(0,.22f,0),V(1.84f,.075f,.68f),1); Crate(b,V(-.25f,.27f,0),.48f);
                b.Box(V(.6f,1.015f,0),V(.24f,.14f,.2f),2); b.Tube(V(.6f,1.09f,-.21f),V(.6f,1.09f,.2f),.022f,2);
                b.Box(V(-.55f,.98f,-.13f),V(.48f,.05f,.07f),0,Quaternion.Euler(0,25,0));
                b.Box(V(-.73f,1.015f,-.05f),V(.11f,.11f,.26f),2,Quaternion.Euler(0,25,0)); break;
            case "laundry":
                foreach(float x in new[]{-1.65f,1.65f}) {b.Box(V(x,1.1f,0),V(.11f,2.2f,.11f),1); b.Box(V(x,2.16f,0),V(.16f,.09f,.16f),0);}
                for(int i=0;i<12;i++)
                {
                    float x0=-1.65f+i*.275f,x1=x0+.275f;
                    b.Tube(V(x0,2.12f-.18f*(1-x0*x0/2.7225f),0),V(x1,2.12f-.18f*(1-x1*x1/2.7225f),0),.012f,2,6);
                }
                for(int j=0;j<3;j++)
                {
                    float centre=-1.02f+j*1.02f;
                    for(int i=0;i<6;i++)
                    {
                        float x0=centre-.38f+i*.1267f,x1=x0+.1267f;
                        float z0=Mathf.Sin(i*1.5f)*.035f,z1=Mathf.Sin((i+1)*1.5f)*.035f;
                        b.Quad(V(x0,1.98f,z0),V(x1,1.98f,z1),V(x1,1.05f+j*.08f,z1+.05f),V(x0,1.05f+j*.08f,z0+.05f),j==1?3:4,Vector3.back,true);
                    }
                    foreach(float x in new[]{centre-.3f,centre+.3f}) b.Box(V(x,1.99f,-.025f),V(.025f,.1f,.055f),0);
                } break;
            case "noticeboard":
                foreach(float x in new[]{-.76f,.76f}) b.Box(V(x,1.15f,0),V(.13f,2.3f,.13f),1);
                b.Box(V(0,1.6f,0),V(1.68f,1.1f,.1f),1);
                for(int i=0;i<7;i++) b.Box(V(-.7f+i*.233f,1.6f,-.07f),V(.22f,1,.045f),0);
                foreach(float x in new[]{-.88f,.88f}) b.Box(V(x,1.6f,-.08f),V(.09f,1.18f,.12f),1);
                foreach(float y in new[]{1.02f,2.18f}) b.Box(V(0,y,-.08f),V(1.85f,.09f,.12f),1);
                b.Box(V(-.36f,1.68f,-.109f),V(.48f,.6f,.012f),4,Quaternion.Euler(0,0,-6));
                b.Box(V(.28f,1.65f,-.114f),V(.5f,.45f,.012f),3,Quaternion.Euler(0,0,5));
                for(int i=0;i<6;i++) b.Box(V(.28f,1.78f-i*.05f,-.126f),V(.34f,.012f,.004f),1);
                b.Box(V(0,2.29f,0),V(2.05f,.08f,.65f),1,Quaternion.Euler(-7,0,0)); break;
            case "lantern":
                b.Box(V(0,.06f,0),V(.36f,.12f,.36f),6);
                b.Box(V(0,.96f,0),V(.09f,1.85f,.09f),2);
                b.Box(V(0,1.79f,0),V(.17f,.08f,.17f),2);
                b.offset=V(0,1.85f,0);
                b.Box(V(0,.025f,0),V(.27f,.05f,.27f),2);
                b.Box(V(0,.19f,0),V(.16f,.28f,.16f),7);
                foreach(float x in new[]{-.11f,.11f}) foreach(float z in new[]{-.11f,.11f}) b.Box(V(x,.2f,z),V(.025f,.35f,.025f),2);
                b.Box(V(0,.38f,0),V(.29f,.06f,.29f),2);
                b.Taper(V(0,.41f,0),V(0,.5f,0),.19f,.04f,2,4,true);
                b.Tube(V(0,.51f,0),V(0,.58f,0),.025f,2,8); b.offset=Vector3.zero; break;
            case "shelf":
                foreach(float x in new[]{-.56f,.56f}) b.Box(V(x,.6f,.12f),V(.07f,1.2f,.07f),1);
                foreach(float y in new[]{.1f,.57f,1.05f}) b.Box(V(0,y,0),V(1.26f,.065f,.35f),0);
                for(int i=0;i<5;i++) b.Box(V(-.4f+i*.09f,.75f,0),V(.055f,.3f+(i%2)*.05f,.2f),i%2==0?3:4,Quaternion.Euler(0,0,i==4?-8:0)); break;
            case "teacup":
                b.Taper(V(0,.015f,0),V(0,.13f,0),.05f,.065f,4,16,false); b.Ring(V(0,.13f,0),.063f,.008f,4);
                b.Disc(V(0,.118f,0),.054f,1);
                for(int i=0;i<10;i++)
                {float a=i*Mathf.PI/10,beta=(i+1)*Mathf.PI/10; b.Tube(V(.06f+Mathf.Sin(a)*.04f,.075f+Mathf.Cos(a)*.045f,0),V(.06f+Mathf.Sin(beta)*.04f,.075f+Mathf.Cos(beta)*.045f,0),.009f,4,6);}
                b.Taper(V(0,0,0),V(0,.012f,0),.105f,.105f,4,20,true); break;
            case "book":
                b.Box(V(0,.012f,0),V(.23f,.024f,.32f),3);b.Box(V(0,.045f,0),V(.214f,.04f,.3f),4);
                b.Box(V(0,.076f,0),V(.23f,.02f,.32f),3); b.Box(V(-.105f,.045f,0),V(.022f,.07f,.32f),3); break;
            case "rug":
                b.Box(V(0,.012f,0),V(1.7f,.022f,1.1f),4);
                foreach(float x in new[]{-.73f,.73f}) b.Box(V(x,.025f,0),V(.045f,.002f,.95f),3);
                foreach(float z in new[]{-.43f,.43f}) b.Box(V(0,.025f,z),V(1.48f,.002f,.04f),3);
                for(int i=0;i<14;i++) foreach(float z in new[]{-.58f,.58f}) b.Box(V(-.78f+i*.12f,.01f,z),V(.025f,.012f,.09f),4); break;
            case "platform":
                for(int i=0;i<16;i++) b.Box(V(-1.875f+i*.25f,.23f,0),V(.235f,.09f,2.8f),0);
                foreach(float z in new[]{-1.12f,1.12f}) b.Box(V(0,.1f,z),V(4,.15f,.16f),1);
                foreach(float x in new[]{-1.85f,1.85f}) foreach(float z in new[]{-1.15f,1.15f}) b.Box(V(x,.12f,z),V(.14f,.24f,.14f),1); break;
            case "barrel":
                b.Taper(V(0,0,0),V(0,.46f,0),.32f,.39f,0,16,true);
                b.Taper(V(0,.46f,0),V(0,.92f,0),.39f,.32f,0,16,true);
                foreach(float y in new[]{.1f,.32f,.6f,.82f}) b.Ring(V(0,y,0),.32f+.07f*(1-Mathf.Abs(y-.46f)/.46f),.024f,2);
                b.Disc(V(0,.925f,0),.3f,1); b.Tube(V(.09f,.926f,0),V(.09f,.95f,0),.045f,0,10); break;
            default: throw new ArgumentException("Unknown town prop kind: " + kind);
        }
    }
    static void Crate(Builder b, Vector3 p, float s)
    {
        for(int i=0;i<4;i++)
        {
            float y=.09f+i*.16f;
            foreach(float z in new[]{-.37f,.37f}) b.Box(p+V(0,y,z)*s,V(.8f,.145f,.055f)*s,0);
            foreach(float x in new[]{-.38f,.38f}) b.Box(p+V(x,y,0)*s,V(.055f,.145f,.72f)*s,0);
        }
        for(int i=0;i<5;i++) b.Box(p+V(-.32f+i*.16f,.66f,0)*s,V(.145f,.05f,.78f)*s,0);
        foreach(float x in new[]{-.31f,.31f}) foreach(float z in new[]{-.41f,.41f}) b.Box(p+V(x,.33f,z)*s,V(.07f,.69f,.035f)*s,1);
        foreach(float z in new[]{-.415f,.415f}) b.Box(p+V(0,.33f,z)*s,V(.07f,.87f,.035f)*s,1,Quaternion.Euler(0,0,-43));
    }
    static void Bucket(Builder b, Vector3 p, float s)
    {
        b.Taper(p+V(0,.015f,0)*s,p+V(0,.37f,0)*s,.16f*s,.22f*s,2,16,false);
        b.Taper(p+V(0,.035f,0)*s,p+V(0,.37f,0)*s,.145f*s,.205f*s,1,16,false,true);
        b.Disc(p+V(0,.04f,0)*s,.145f*s,1); b.Ring(p+V(0,.375f,0)*s,.215f*s,.015f*s,2);
        for(int i=0;i<16;i++)
        {
            float a=i*Mathf.PI/16,c=(i+1)*Mathf.PI/16;
            b.Tube(p+V(Mathf.Cos(a)*.225f,.35f+Mathf.Sin(a)*.26f,0)*s,p+V(Mathf.Cos(c)*.225f,.35f+Mathf.Sin(c)*.26f,0)*s,.012f*s,2,6);
        }
    }
    sealed class Builder
    {
        readonly List<Vector3> vertices=new List<Vector3>();
        readonly List<Vector2> uv=new List<Vector2>();
        readonly List<int>[] tris=new List<int>[8];
        public Vector3 offset;
        public Builder(){for(int i=0;i<8;i++)tris[i]=new List<int>();}
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int m,Vector3 normal,bool both=false)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)<0){var t=b;b=d;d=t;}
            int v=vertices.Count;vertices.AddRange(new[]{a+offset,b+offset,c+offset,d+offset});
            float w=Vector3.Distance(a,b),h=Vector3.Distance(b,c);uv.AddRange(new[]{Vector2.zero,new Vector2(w,0),new Vector2(w,h),new Vector2(0,h)});
            tris[m].AddRange(new[]{v,v+1,v+2,v,v+2,v+3});
            if(both)Quad(d,c,b,a,m,-normal);
        }
        void Tri(Vector3 a,Vector3 b,Vector3 c,int m,Vector3 normal)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)<0){var t=b;b=c;c=t;}
            int v=vertices.Count;vertices.AddRange(new[]{a+offset,b+offset,c+offset});uv.AddRange(new[]{Vector2.zero,Vector2.right*.1f,Vector2.up*.1f});tris[m].AddRange(new[]{v,v+1,v+2});
        }
        public void Box(Vector3 centre,Vector3 size,int material,Quaternion rotation=default)
        {
            if(rotation==default)rotation=Quaternion.identity;
            Vector3 h=size*.5f;float bevel=Mathf.Min(.015f,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*.22f);
            Vector3 Local(Vector3 p)=>centre+rotation*p;
            Vector3 P(int axis,float value){Vector3 v=Vector3.zero;v[axis]=value;return v;}
            for(int axis=0;axis<3;axis++)foreach(int sign in new[]{-1,1})
            {
                int u=(axis+1)%3,v=(axis+2)%3;Vector3 n=P(axis,sign),baseP=P(axis,h[axis]*sign);
                Vector3 a=baseP-P(u,h[u]-bevel)-P(v,h[v]-bevel),b=baseP+P(u,h[u]-bevel)-P(v,h[v]-bevel);
                Vector3 c=baseP+P(u,h[u]-bevel)+P(v,h[v]-bevel),d=baseP-P(u,h[u]-bevel)+P(v,h[v]-bevel);
                Quad(Local(a),Local(b),Local(c),Local(d),material,rotation*n);
            }
            for(int edge=0;edge<3;edge++)foreach(int aSign in new[]{-1,1})foreach(int bSign in new[]{-1,1})
            {
                int a=(edge+1)%3,b=(edge+2)%3;Vector3 p=P(a,h[a]*aSign)+P(b,(h[b]-bevel)*bSign),q=P(a,(h[a]-bevel)*aSign)+P(b,h[b]*bSign),e=P(edge,h[edge]-bevel);
                Quad(Local(p-e),Local(p+e),Local(q+e),Local(q-e),material,rotation*(P(a,aSign)+P(b,bSign)));
            }
            foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
            {
                Vector3 c=Vector3.Scale(h,V(x,y,z)),p=c-V(0,y*bevel,z*bevel),q=c-V(x*bevel,0,z*bevel),r=c-V(x*bevel,y*bevel,0);
                Tri(Local(p),Local(q),Local(r),material,rotation*V(x,y,z));
            }
        }
        public void Tube(Vector3 from,Vector3 to,float radius,int material,int segments=10)=>Taper(from,to,radius,radius,material,segments,true);
        public void Taper(Vector3 from,Vector3 to,float r0,float r1,int material,int segments,bool caps,bool inward=false)
        {
            Vector3 axis=(to-from).normalized,right=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.forward).normalized,forward=Vector3.Cross(axis,right);
            for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2/segments,c=(i+1)*Mathf.PI*2/segments;
                Vector3 x=right*Mathf.Cos(a)+forward*Mathf.Sin(a),y=right*Mathf.Cos(c)+forward*Mathf.Sin(c);
                Quad(from+x*r0,from+y*r0,to+y*r1,to+x*r1,material,(x+y)*(inward?-1:1));
                if(caps){Tri(from,from+y*r0,from+x*r0,material,-axis);Tri(to,to+x*r1,to+y*r1,material,axis);}
            }
        }
        public void Disc(Vector3 centre,float radius,int material)
        {for(int i=0;i<20;i++){float a=i*Mathf.PI/10,c=(i+1)*Mathf.PI/10;Tri(centre,centre+V(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius),centre+V(Mathf.Cos(c)*radius,0,Mathf.Sin(c)*radius),material,Vector3.up);}}
        public void Ring(Vector3 centre,float radius,float tubeRadius,int material)
        {for(int i=0;i<24;i++){float a=i*Mathf.PI/12,c=(i+1)*Mathf.PI/12;Tube(centre+V(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius),centre+V(Mathf.Cos(c)*radius,0,Mathf.Sin(c)*radius),tubeRadius,material,6);}}
        public Mesh Finish(string name)
        {
            var m=new Mesh{name="Town "+name,indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            m.SetVertices(vertices);m.SetUVs(0,uv);m.subMeshCount=8;
            for(int i=0;i<8;i++)m.SetTriangles(tris[i],i);
            m.RecalculateNormals();m.RecalculateTangents();m.RecalculateBounds();return m;
        }
    }
}
