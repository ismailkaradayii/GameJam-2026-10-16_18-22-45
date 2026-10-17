using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

namespace Itfaiye
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Itfaiye/Pistol Feedback")]
    public sealed class ItfaiyePistolFeedback : MonoBehaviour
    {
        [Header("Bullet marks")]
        public Material impactMaterial;
        [Range(.02f, .16f)] public float markDiameter = .075f;
        [Range(8, 160)] public int maximumMarks = 72;
        [Min(1f)] public float markLifetime = 45f;
        [Min(.1f)] public float markFadeTime = 5f;
        [Header("Impact particles and bullet streak")]
        public Material dustMaterial, sparkMaterial, tracerMaterial;
        public bool showTracer = true;
        public bool showHitMarker = true;
        [Header("Camera response (does not change grip)")]
        [Range(0f, 3f)] public float cameraKick = .9f;
        [Range(0f, 1f)] public float aimedKickMultiplier = .55f;
        [Range(.1f, .5f)] public float kickRecovery = .23f;
        [Range(0f, .04f)] public float cameraTravel = .012f;
        [Range(0f, .8f)] public float shotShake = .24f;
        [Range(0f, 2f)] public float shotFovKick = .65f;
        [Header("Brief shot grading and optional controller rumble")]
        public VolumeProfile shotPostProfile;
        [Range(0f, 1f)] public float shotPostIntensity = .65f;
        public bool gamepadRumble = true;
        [Range(0f, 1f)] public float rumbleStrength = .3f;
        [Range(.02f, .2f)] public float rumbleSeconds = .065f;
        [Header("Audio")]
        public AudioClip[] shotClips;
        public AudioClip concreteImpact, metalImpact, woodImpact, dirtImpact;
        [Tooltip("Dedicated contact sound for the fire spirit and metallic vehicles. Weapon shots remain separate.")]
        public AudioClip targetImpactClip;
        [Range(0f, 1f)] public float shotVolume = .65f;
        [Range(0f, 1f)] public float impactVolume = .65f;
        [Header("Creature hit confirmation")]
        [Range(0f, 1f)] public float creatureConfirmVolume = .34f;
        [Range(0f, 1f)] public float creatureImpactKick = .46f;

        public int TotalImpacts { get; private set; }
        public int ActiveMarkCount { get { int n=0; if (marks != null) foreach (var m in marks) if (m != null && m.root != null && m.root.activeSelf) n++; return n; } }
        public Transform LastMark { get; private set; }
        public ItfaiyeSurfaceKind LastSurface { get; private set; }
        public int LastImpactParticleCount => lastEffect == null ? 0 : lastEffect.dust.particleCount + lastEffect.chips.particleCount;
        public Quaternion CameraRotation
        {
            get
            {
                if (!CanPresent) return Quaternion.identity;
                float age = Time.time - lastShotTime;
                // ADS attenuation is applied once here; the weapon's own recoil has
                // a separate aimed multiplier and must not attenuate the camera twice.
                float envelope = ItfaiyePistol.ShotImpulse(age / Mathf.Max(.1f, kickRecovery / .23f));
                float impactAge = Mathf.Max(0f, Time.time - confirmTime);
                float impact = Mathf.Sin(impactAge * 62f) * Mathf.Exp(-impactAge * 24f) * creatureImpactKick * (killingHit ? 1.35f : 1f);
                float shudder = Mathf.Sin(age * 155f) * Mathf.Exp(-Mathf.Max(0, age) * 32f) * shotShake * shotWeight;
                return Quaternion.Euler(-kickStrength * envelope - impact + shudder * .3f, kickSide * (kickStrength * .12f * envelope + shudder),
                    kickSide * kickStrength * .14f * envelope + impact * .32f + shudder * .7f);
            }
        }
        bool CanPresent => isActiveAndEnabled && pistol && pistol.Available && pistol.Equipped &&
            pistol.player && pistol.player.InputCaptured && Time.timeScale > 0f;
        float ViewImpulse => CanPresent && lastShotTime > -99f ? ItfaiyePistol.ShotImpulse(Time.time - lastShotTime) * shotWeight : 0f;
        public Vector3 CameraOffset => new Vector3(kickSide * cameraTravel * .12f, cameraTravel * .18f, -cameraTravel) * ViewImpulse;
        public float FieldOfViewKick => shotFovKick * ViewImpulse;
        public float ShotPostWeight => shotPost ? shotPost.weight : 0f;
        public bool RumbleActive => rumblePad != null;

        sealed class Mark
        {
            public GameObject root;
            public Mesh mesh;
            public MeshRenderer renderer;
            public MaterialPropertyBlock properties = new MaterialPropertyBlock();
            public float expires;
        }
        sealed class ImpactEffect
        {
            public Transform root;
            public ParticleSystem dust, chips;
            public AudioSource audio;
        }
        sealed class Tracer
        {
            public LineRenderer line;
            public Vector3 start, end;
            public float started;
        }
        const int EffectPoolSize = 12, TracerPoolSize = 6, Grid = 5;
        static readonly int OpacityId=Shader.PropertyToID("_Opacity"), TintId=Shader.PropertyToID("_Tint"), SeedId=Shader.PropertyToID("_Seed"), SurfaceId=Shader.PropertyToID("_Surface");
        readonly Vector3[] vertices = new Vector3[Grid*Grid];
        readonly Vector2[] uvs = new Vector2[Grid*Grid];
        readonly bool[] valid = new bool[Grid*Grid];
        readonly List<int> triangles = new List<int>((Grid-1)*(Grid-1)*6);
        Mark[] marks;
        ImpactEffect[] effects;
        Tracer[] tracers;
        ImpactEffect lastEffect;
        Transform effectRoot;
        AudioSource shotAudio, confirmAudio;
        AudioClip hitConfirm, killConfirm;
        ItfaiyePistol pistol;
        int markIndex, effectIndex, tracerIndex, shotIndex;
        float lastShotTime=-100f, hitTime=-100f, kickStrength, kickSide;
        float confirmTime = -100f;
        Vector3 hitPoint;
        Transform hitTransform;
        Vector3 localHitPoint;
        bool creatureHit, killingHit;
        float shotWeight = 1f, rumbleUntil;
        Volume shotPost;
        Gamepad rumblePad;

        void Awake()
        {
            pistol = GetComponent<ItfaiyePistol>();
            marks = new Mark[Mathf.Clamp(maximumMarks,8,160)];
            effects = new ImpactEffect[EffectPoolSize];
            tracers = new Tracer[TracerPoolSize];
            effectRoot = new GameObject("Pistol World Effects").transform;
            shotAudio = gameObject.AddComponent<AudioSource>();
            shotAudio.playOnAwake=false; shotAudio.spatialBlend=0f; shotAudio.volume=1f;
            confirmAudio = gameObject.AddComponent<AudioSource>();
            confirmAudio.playOnAwake = false; confirmAudio.spatialBlend = 0f;
            hitConfirm = Resources.Load<AudioClip>("LavaCombat/SpiritConfirm");
            killConfirm = Resources.Load<AudioClip>("LavaCombat/SpiritKillConfirm");
            if (shotPostProfile)
            {
                var volumeObject = new GameObject("Pistol Shot Grading");
                volumeObject.transform.SetParent(transform, false);
                shotPost = volumeObject.AddComponent<Volume>();
                shotPost.isGlobal = true; shotPost.priority = 100f; shotPost.weight = 0f;
                shotPost.sharedProfile = shotPostProfile;
            }
        }

        public void PlayShot(Ray ray, Vector3 end, RaycastHit hit, float aimAmount, bool obstructed)
        {
            if (!isActiveAndEnabled) return;
            lastShotTime=Time.time;
            shotWeight=Mathf.Lerp(1f,aimedKickMultiplier,aimAmount);
            kickStrength=cameraKick*Mathf.Lerp(1f,aimedKickMultiplier,aimAmount);
            kickSide=(shotIndex%2==0?1f:-.6f);
            if (shotVolume>0f && shotClips != null && shotClips.Length>0)
            {
                var clip=shotClips[shotIndex%shotClips.Length];
                if (clip != null) { shotAudio.pitch=Random.Range(.97f,1.03f); shotAudio.PlayOneShot(clip,shotVolume); }
            }
            shotIndex++;
            if (gamepadRumble && Gamepad.current != null && Application.isFocused)
            {
                StopRumble(); rumblePad = Gamepad.current;
                rumblePad.SetMotorSpeeds(rumbleStrength * .5f, rumbleStrength);
                rumbleUntil = Time.unscaledTime + rumbleSeconds;
            }
            if (showTracer && !obstructed && tracerMaterial != null && Vector3.Distance(ray.origin,end)>.8f) SpawnTracer(ray.origin,end);
            if (hit.collider == null) return;
            TotalImpacts++;
            LastSurface=ResolveSurface(hit.collider);
            bool metalVehicle = hit.collider.GetComponentInParent<GameJam.FireRescue.FireTruckDrive>() != null;
            if (metalVehicle) LastSurface = ItfaiyeSurfaceKind.Metal;
            hitTime=Time.time; hitPoint=hit.point; hitTransform=hit.collider.transform; localHitPoint=hitTransform.InverseTransformPoint(hit.point);
            var creature = hit.collider.GetComponentInParent<GameJam.FireRescue.LavaCreatureActor>();
            creatureHit = creature != null;
            killingHit = creatureHit && creature.IsDefeated;
            if (creatureHit)
            {
                // Animated bodies get molten impacts from the actor, not concrete decals/dust.
                LastMark = null;
                if (killingHit) hitTransform = null;
                confirmTime = Time.time;
                var confirmation = killingHit ? killConfirm : hitConfirm;
                if (confirmation && creatureConfirmVolume > 0f)
                {
                    confirmAudio.pitch = killingHit ? 1f : Random.Range(.96f, 1.04f);
                    confirmAudio.PlayOneShot(confirmation, creatureConfirmVolume * (killingHit ? 1.15f : 1f));
                }
                PlayTargetImpact(hit.point);
                return;
            }
            SpawnMark(hit, LastSurface);
            SpawnImpact(hit, LastSurface);
        }

        public static ItfaiyeSurfaceKind ResolveSurface(Collider collider)
        {
            var explicitSurface=collider.GetComponentInParent<ItfaiyeImpactSurface>();
            if (explicitSurface != null) return explicitSurface.surface;
            if (collider is TerrainCollider) return ItfaiyeSurfaceKind.Dirt;
            var renderer=collider.GetComponent<Renderer>() ?? collider.GetComponentInParent<Renderer>();
            string hint=collider.name;
            if (renderer != null && renderer.sharedMaterial != null) hint+=" "+renderer.sharedMaterial.name;
            hint=hint.ToLowerInvariant();
            if (hint.Contains("metal") || hint.Contains("steel") || hint.Contains("iron") || hint.Contains("pipe")) return ItfaiyeSurfaceKind.Metal;
            if (hint.Contains("wood") || hint.Contains("bark") || hint.Contains("trunk") || hint.Contains("timber")) return ItfaiyeSurfaceKind.Wood;
            if (hint.Contains("dirt") || hint.Contains("soil") || hint.Contains("sand") || hint.Contains("grass")) return ItfaiyeSurfaceKind.Dirt;
            return ItfaiyeSurfaceKind.Concrete;
        }

        void SpawnMark(RaycastHit hit, ItfaiyeSurfaceKind surface)
        {
            LastMark=null;
            if (impactMaterial == null) return;
            int index=markIndex++%marks.Length;
            var mark=marks[index];
            if (mark == null || mark.root == null)
            {
                if (mark != null && mark.mesh != null) Destroy(mark.mesh);
                mark=new Mark { root=new GameObject("Bullet Impact Mark"), mesh=new Mesh { name="Conforming Bullet Mark" } };
                mark.root.AddComponent<MeshFilter>().sharedMesh=mark.mesh;
                mark.renderer=mark.root.AddComponent<MeshRenderer>();
                mark.renderer.sharedMaterial=impactMaterial;
                mark.renderer.shadowCastingMode=ShadowCastingMode.Off; mark.renderer.receiveShadows=false;
                marks[index]=mark;
            }
            var t=mark.root.transform;
            t.SetParent(null); t.localScale=Vector3.one;
            t.SetPositionAndRotation(hit.point,Quaternion.LookRotation(hit.normal)*Quaternion.Euler(0,0,Random.Range(0,360)));
            float size=markDiameter*Random.Range(.85f,1.15f);
            triangles.Clear();
            // Project a small grid back onto the actual collider. Missing edge samples
            // are clipped instead of letting a flat quad hang over a ledge.
            for (int y=0;y<Grid;y++) for(int x=0;x<Grid;x++)
            {
                int v=y*Grid+x;
                uvs[v]=new Vector2(x/(float)(Grid-1),y/(float)(Grid-1));
                var p=t.TransformPoint(new Vector3((uvs[v].x-.5f)*size,(uvs[v].y-.5f)*size,0));
                valid[v]=hit.collider.Raycast(new Ray(p+hit.normal*.06f,-hit.normal),out var sample,.12f) && Vector3.Dot(sample.normal,hit.normal)>.65f;
                vertices[v]=valid[v]?t.InverseTransformPoint(sample.point+sample.normal*.0012f):t.InverseTransformPoint(p);
            }
            for(int y=0;y<Grid-1;y++) for(int x=0;x<Grid-1;x++)
            {
                int a=y*Grid+x,b=a+1,c=a+Grid,d=c+1;
                AddTriangle(a,c,b); AddTriangle(b,c,d);
            }
            mark.mesh.Clear(); mark.mesh.vertices=vertices; mark.mesh.uv=uvs; mark.mesh.SetTriangles(triangles,0); mark.mesh.RecalculateBounds();
            t.SetParent(hit.collider.transform,true);
            Color tint=surface==ItfaiyeSurfaceKind.Metal?new Color(.27f,.29f,.3f):surface==ItfaiyeSurfaceKind.Wood?new Color(.25f,.15f,.075f):surface==ItfaiyeSurfaceKind.Dirt?new Color(.19f,.135f,.075f):new Color(.32f,.30f,.27f);
            mark.properties.SetColor(TintId,tint); mark.properties.SetFloat(SeedId,Random.Range(0f,30f)); mark.properties.SetFloat(OpacityId,1);
            mark.properties.SetFloat(SurfaceId,(float)surface);
            mark.renderer.SetPropertyBlock(mark.properties);
            mark.expires=Time.time+markLifetime;
            mark.root.SetActive(triangles.Count>0);
            if (mark.root.activeSelf) LastMark=t;
        }
        void AddTriangle(int a,int b,int c) { if(valid[a]&&valid[b]&&valid[c]) { triangles.Add(a); triangles.Add(b); triangles.Add(c); } }

        void SpawnImpact(RaycastHit hit,ItfaiyeSurfaceKind surface)
        {
            if (dustMaterial == null || sparkMaterial == null) return;
            int index=effectIndex++%effects.Length;
            var effect=effects[index];
            if(effect==null)
            {
                effect=new ImpactEffect { root=new GameObject("Pooled Bullet Impact").transform };
                effect.root.SetParent(effectRoot);
                effect.dust=CreateParticles("Dust",effect.root,dustMaterial);
                effect.chips=CreateParticles("Chips and sparks",effect.root,dustMaterial);
                effect.audio=effect.root.gameObject.AddComponent<AudioSource>();
                effect.audio.playOnAwake=false; effect.audio.spatialBlend=1; effect.audio.minDistance=2; effect.audio.maxDistance=45; effect.audio.rolloffMode=AudioRolloffMode.Logarithmic;
                effects[index]=effect;
            }
            lastEffect=effect;
            effect.root.SetPositionAndRotation(hit.point+hit.normal*.005f,Quaternion.LookRotation(hit.normal));
            effect.dust.Clear(); effect.chips.Clear(); effect.audio.Stop();
            var chipRenderer=effect.chips.GetComponent<ParticleSystemRenderer>();
            chipRenderer.sharedMaterial=surface==ItfaiyeSurfaceKind.Metal?sparkMaterial:dustMaterial;
            chipRenderer.renderMode=surface==ItfaiyeSurfaceKind.Metal?ParticleSystemRenderMode.Stretch:ParticleSystemRenderMode.Billboard;
            chipRenderer.lengthScale=1.2f; chipRenderer.velocityScale=.035f;
            var dustColor=surface==ItfaiyeSurfaceKind.Wood?new Color(.52f,.36f,.19f,.8f):surface==ItfaiyeSurfaceKind.Dirt?new Color(.45f,.33f,.2f,.75f):new Color(.61f,.58f,.53f,.65f);
            effect.dust.Play(); effect.chips.Play();
            for(int i=0;i<(surface==ItfaiyeSurfaceKind.Metal?4:10);i++)
                Emit(effect.dust,hit,Random.Range(.2f,.8f),Random.Range(.06f,.13f),Random.Range(.25f,.55f),dustColor);
            for(int i=0;i<(surface==ItfaiyeSurfaceKind.Metal?15:8);i++)
                Emit(effect.chips,hit,Random.Range(1.1f,3.7f),Random.Range(.012f,.028f),Random.Range(.13f,.32f),surface==ItfaiyeSurfaceKind.Metal?new Color(2.5f,1.45f,.35f,1):dustColor);
            var clip=surface==ItfaiyeSurfaceKind.Metal?(targetImpactClip?targetImpactClip:metalImpact):surface==ItfaiyeSurfaceKind.Wood?woodImpact:surface==ItfaiyeSurfaceKind.Dirt?dirtImpact:concreteImpact;
            effect.audio.clip=clip; effect.audio.volume=impactVolume; effect.audio.pitch=Random.Range(.9f,1.1f);
            if(clip!=null && impactVolume>0f) effect.audio.Play();
        }
        void PlayTargetImpact(Vector3 point)
        {
            if (!targetImpactClip || impactVolume <= 0f) return;
            float sfx = Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f));
            AudioSource.PlayClipAtPoint(targetImpactClip, point, impactVolume * sfx);
        }
        static ParticleSystem CreateParticles(string name,Transform parent,Material material)
        {
            var go=new GameObject(name); go.transform.SetParent(parent,false);
            var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main; main.loop=false; main.playOnAwake=false; main.duration=.6f; main.maxParticles=40; main.simulationSpace=ParticleSystemSimulationSpace.World; main.gravityModifier=name=="Dust"?.12f:.8f;
            var emission=ps.emission; emission.enabled=false;
            var shape=ps.shape; shape.enabled=false;
            var color=ps.colorOverLifetime; color.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.7f,.3f),new GradientAlphaKey(0,1)}); color.color=gradient;
            var size=ps.sizeOverLifetime; size.enabled=true; size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,name=="Dust"?.4f:1,1,name=="Dust"?2.4f:.2f));
            var renderer=go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=material; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            return ps;
        }
        static void Emit(ParticleSystem ps,RaycastHit hit,float speed,float size,float life,Color color)
        {
            var direction=(hit.normal+Random.insideUnitSphere*.75f).normalized;
            var emit=new ParticleSystem.EmitParams { position=hit.point+hit.normal*.009f,velocity=direction*speed,startSize=size,startLifetime=life,startColor=color,rotation=Random.Range(0,360) };
            ps.Emit(emit,1);
        }

        void SpawnTracer(Vector3 start,Vector3 end)
        {
            int index=tracerIndex++%tracers.Length;
            var tracer=tracers[index];
            if(tracer==null)
            {
                var go=new GameObject("Pooled Bullet Streak"); go.transform.SetParent(effectRoot);
                var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=tracerMaterial; line.useWorldSpace=true; line.positionCount=2; line.widthMultiplier=.006f;
                line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
                tracer=new Tracer { line=line }; tracers[index]=tracer;
            }
            tracer.start=start; tracer.end=start+Vector3.ClampMagnitude(end-start,60f); tracer.started=Time.time;
            UpdateTracer(tracer);
        }
        static void UpdateTracer(Tracer tracer)
        {
            float t=(Time.time-tracer.started)/.055f;
            tracer.line.enabled=t<1;
            if(t>=1) return;
            var delta=tracer.end-tracer.start; float length=delta.magnitude;
            float front=Mathf.Lerp(Mathf.Min(2f,length),length,Mathf.Clamp01(t*1.8f));
            float tail=Mathf.Max(.15f,front-Mathf.Min(5f,length*.65f));
            tracer.line.SetPosition(0,tracer.start+delta.normalized*tail); tracer.line.SetPosition(1,tracer.start+delta.normalized*front);
            var color=new Color(1.15f,.94f,.62f,.42f*(1-t)); tracer.line.startColor=color; tracer.line.endColor=color;
        }
        void Update()
        {
            if (shotPost) shotPost.weight = ViewImpulse * shotPostIntensity;
            if (rumblePad != null && (!CanPresent || Time.unscaledTime >= rumbleUntil)) StopRumble();
            foreach(var mark in marks)
            {
                if(mark==null||mark.root==null||!mark.root.activeSelf) continue;
                float remaining=mark.expires-Time.time;
                if(remaining<=0) { mark.root.SetActive(false); continue; }
                mark.properties.SetFloat(OpacityId,Mathf.Clamp01(remaining/Mathf.Min(markFadeTime,markLifetime))); mark.renderer.SetPropertyBlock(mark.properties);
            }
            foreach(var tracer in tracers) if(tracer!=null && tracer.line!=null && tracer.line.enabled) UpdateTracer(tracer);
        }
        void OnGUI()
        {
            float age=Time.time-hitTime;
            float lifetime = killingHit ? .46f : creatureHit ? .27f : .14f;
            if(!showHitMarker||age>lifetime||pistol==null||!pistol.showHud||!pistol.Equipped||!pistol.player.InputCaptured) return;
            Vector3 screen=pistol.player.playerCamera.WorldToScreenPoint(hitTransform!=null?hitTransform.TransformPoint(localHitPoint):hitPoint);
            if(screen.z<=0) return;
            var center=new Vector2(screen.x,Screen.height-screen.y); var matrix=GUI.matrix; var color=GUI.color;
            float fade = creatureHit ? 1f - Mathf.SmoothStep(0f, 1f, (age - .075f) / (lifetime - .075f)) : 1f - age / lifetime;
            Color ink = killingHit ? new Color(1f, .38f, .085f, fade) : new Color(1f, .97f, .87f, (creatureHit ? 1f : .7f) * fade);
            float pop = Mathf.Exp(-age * 32f);
            float spread = killingHit ? 8f + pop * 5f + age * 7f : creatureHit ? 6f + pop * 4f : 7f;
            float length = killingHit ? 10f : creatureHit ? 7f : 4f;
            float thickness = creatureHit ? 2.2f : 1.3f;
            GUIUtility.RotateAroundPivot(45,center);
            for(int sign=-1;sign<=1;sign+=2)
            {
                if (creatureHit)
                {
                    GUI.color = new Color(.08f, .025f, .012f, fade * .9f);
                    GUI.DrawTexture(new Rect(center.x+sign*spread-length*.5f-1,center.y-thickness*.5f-1,length+2,thickness+2),Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(center.x-thickness*.5f-1,center.y+sign*spread-length*.5f-1,thickness+2,length+2),Texture2D.whiteTexture);
                }
                GUI.color = ink;
                GUI.DrawTexture(new Rect(center.x+sign*spread-length*.5f,center.y-thickness*.5f,length,thickness),Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(center.x-thickness*.5f,center.y+sign*spread-length*.5f,thickness,length),Texture2D.whiteTexture);
            }
            GUI.matrix=matrix; GUI.color=color;
        }
        void OnDisable()
        {
            ClearViewEffects();
            if(shotAudio!=null) shotAudio.Stop();
            if(confirmAudio!=null) confirmAudio.Stop();
            if(effects!=null) foreach(var e in effects) if(e!=null)
            {
                if(e.dust!=null) e.dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                if(e.chips!=null) e.chips.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                if(e.audio!=null) e.audio.Stop();
            }
            if(tracers!=null) foreach(var t in tracers) if(t!=null && t.line!=null) t.line.enabled=false;
        }
        void OnDestroy()
        {
            StopRumble();
            if (shotPost) Destroy(shotPost.gameObject);
            if(marks!=null) foreach(var m in marks) if(m!=null) { if(m.root!=null) Destroy(m.root); if(m.mesh!=null) Destroy(m.mesh); }
            if(effectRoot!=null) Destroy(effectRoot.gameObject);
            if(shotAudio!=null) Destroy(shotAudio);
            if(confirmAudio!=null) Destroy(confirmAudio);
        }
        public void ClearViewEffects()
        {
            lastShotTime=hitTime=confirmTime=-100f; kickStrength=0f;
            if (shotPost) shotPost.weight=0f;
            StopRumble();
        }
        void StopRumble()
        {
            if (rumblePad != null && rumblePad.added) rumblePad.SetMotorSpeeds(0f, 0f);
            rumblePad = null;
        }
        void OnApplicationFocus(bool focused) { if (!focused) ClearViewEffects(); }
        void OnApplicationPause(bool paused) { if (paused) ClearViewEffects(); }
    }
}
