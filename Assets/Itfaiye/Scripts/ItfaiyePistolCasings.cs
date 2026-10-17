using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Itfaiye
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    [AddComponentMenu("Itfaiye/Pistol Casings")]
    public sealed class ItfaiyePistolCasings : MonoBehaviour
    {
        [Header("Empty brass casing")]
        public Mesh casingMesh;
        public Material brassMaterial;
        public Material insideMaterial;
        public PhysicsMaterial casingPhysics;
        [Tooltip("Metres relative to the unscaled weapon frame. X is the pistol's right side.")]
        public Vector3 ejectionPosition = new Vector3(.018f,.071f,-.006f);
        public Vector3 ejectionVelocity = new Vector3(2.15f,1.1f,.3f);
        [Range(8,96)] public int maximumCasings = 36;
        [Min(2f)] public float casingLifetime = 12f;
        [Header("Ground contact sound")]
        public AudioClip[] hardSurfaceClinks;
        public AudioClip softSurfaceClink;
        [Range(0,1)] public float landingVolume = .5f;
        [Header("Slide movement sounds")]
        public AudioClip slidePullSound;
        public AudioClip slideReleaseSound;
        [Range(0,1)] public float slideVolume = .3f;

        public int EjectedCount { get; private set; }
        public int ContactSoundCount { get; private set; }
        public ItfaiyePistolCasing LastCasing { get; private set; }
        public Vector3 LastEjectionPosition { get; private set; }
        public Vector3 LastEjectionVelocity { get; private set; }
        public int ActiveCasingCount { get { int count=0; if(pool!=null) foreach(var casing in pool) if(casing!=null && casing.gameObject.activeSelf) count++; return count; } }
        public int SlidePullSoundCount { get; private set; }
        public int SlideReleaseSoundCount { get; private set; }

        readonly Queue<float> pendingEjections = new Queue<float>();
        ItfaiyePistolCasing[] pool;
        Transform poolRoot;
        ItfaiyePistol pistol;
        Collider[] playerColliders;
        AudioSource slideAudio;
        int nextCasing;
        bool pullPlayed, releasePlayed;

        void Awake()
        {
            pistol=GetComponent<ItfaiyePistol>();
            playerColliders=GetComponentsInChildren<Collider>(true);
            pool=new ItfaiyePistolCasing[Mathf.Clamp(maximumCasings,8,96)];
            poolRoot=new GameObject("Pooled Empty Casings").transform;
            slideAudio=gameObject.AddComponent<AudioSource>();
            slideAudio.playOnAwake=false; slideAudio.spatialBlend=0;
        }

        public void QueueEjection()
        {
            if(isActiveAndEnabled) pendingEjections.Enqueue(Time.time+.02f);
        }

        void LateUpdate()
        {
            // Wait until the cycling slide has opened. Only accepted shots enqueue
            // a casing; empty clicks and manual reloads never generate spent brass.
            while(pendingEjections.Count>0 && pendingEjections.Peek()<=Time.time)
            {
                pendingEjections.Dequeue(); Eject();
            }
            foreach(var casing in pool) if(casing!=null && casing.gameObject.activeSelf && Time.time>=casing.ExpiresAt) casing.gameObject.SetActive(false);
            if(!pistol.IsReloading) { pullPlayed=releasePlayed=false; return; }
            float progress=pistol.ReloadProgress;
            if(!pullPlayed && progress>=ItfaiyePistolReloadPose.SlidePullStarts)
            {
                pullPlayed=true; SlidePullSoundCount++;
                if(slidePullSound!=null && slideVolume>0) slideAudio.PlayOneShot(slidePullSound,slideVolume);
            }
            if(!releasePlayed && progress>=ItfaiyePistolReloadPose.SlideReleasedAt)
            {
                releasePlayed=true; SlideReleaseSoundCount++;
                if(slideReleaseSound!=null && slideVolume>0) slideAudio.PlayOneShot(slideReleaseSound,slideVolume);
            }
        }

        void Eject()
        {
            if(pistol.weapon==null || casingMesh==null || brassMaterial==null) return;
            int index=nextCasing++%pool.Length;
            var casing=pool[index];
            if(casing==null)
            {
                var go=new GameObject("Empty Brass Casing"); go.SetActive(false);
                go.layer=2; // Built-in Ignore Raycast: brass must not steal the crosshair.
                go.transform.SetParent(poolRoot,false);
                go.AddComponent<MeshFilter>().sharedMesh=casingMesh;
                var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterials=new[]{brassMaterial,insideMaterial!=null?insideMaterial:brassMaterial};
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                var collider=go.AddComponent<CapsuleCollider>(); collider.direction=1; collider.radius=.0052f; collider.height=.024f;
                collider.contactOffset=.0006f; collider.sharedMaterial=casingPhysics;
                var body=go.AddComponent<Rigidbody>(); body.mass=.0075f; body.linearDamping=.12f; body.angularDamping=.25f;
                body.maxAngularVelocity=75f; body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic; body.interpolation=RigidbodyInterpolation.Interpolate;
                body.solverIterations=8; body.solverVelocityIterations=4;
                casing=go.AddComponent<ItfaiyePistolCasing>();
                casing.Initialize(this,body,collider);
                pool[index]=casing;
            }
            casing.gameObject.SetActive(false);
            var weapon=pistol.weapon.transform;
            var position=weapon.position+weapon.rotation*ejectionPosition;
            var velocity=weapon.rotation*(ejectionVelocity+new Vector3(Random.Range(-.25f,.25f),Random.Range(-.18f,.22f),Random.Range(-.2f,.2f)));
            velocity+=pistol.player.transform.TransformDirection(new Vector3(pistol.player.LocalVelocity.x,0,pistol.player.LocalVelocity.y));
            casing.Launch(position,weapon.rotation*Quaternion.Euler(0,0,-72f),velocity,weapon.rotation*new Vector3(Random.Range(16,28),Random.Range(-30,30),Random.Range(15,35)),casingLifetime);
            // Ignore pairs after activation; Unity can discard ignored pairs when
            // pooled colliders are disabled. No global layer settings are changed.
            foreach(var own in playerColliders) if(own!=null) Physics.IgnoreCollision(casing.Collider,own,true);
            foreach(var other in pool) if(other!=null && other!=casing && other.gameObject.activeSelf) Physics.IgnoreCollision(casing.Collider,other.Collider,true);
            EjectedCount++; LastCasing=casing; LastEjectionPosition=position; LastEjectionVelocity=velocity;
        }

        internal void PlayLanding(ItfaiyePistolCasing casing,Collision collision,float strength,int bounce)
        {
            var surface=ItfaiyePistolFeedback.ResolveSurface(collision.collider);
            bool soft=surface==ItfaiyeSurfaceKind.Dirt || surface==ItfaiyeSurfaceKind.Wood;
            var clips=hardSurfaceClinks;
            var clip=soft?softSurfaceClink:clips!=null && clips.Length>0?clips[(EjectedCount+bounce)%clips.Length]:null;
            if(clip==null || landingVolume<=0) return;
            float volume=landingVolume*Mathf.Lerp(.18f,1f,Mathf.InverseLerp(.35f,4f,strength))*Mathf.Pow(.52f,bounce)*(surface==ItfaiyeSurfaceKind.Dirt?.25f:soft?.65f:1f);
            casing.PlayContact(clip,volume,Random.Range(.91f,1.12f));
            ContactSoundCount++;
        }

        void OnDisable()
        {
            pendingEjections.Clear(); pullPlayed=releasePlayed=false;
            if(slideAudio!=null) slideAudio.Stop();
            if(pool!=null) foreach(var casing in pool) if(casing!=null) casing.gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            if(poolRoot!=null) Destroy(poolRoot.gameObject);
            if(slideAudio!=null) Destroy(slideAudio);
        }
    }
}
