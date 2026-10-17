using UnityEngine;

namespace Itfaiye
{
    [DisallowMultipleComponent]
    public sealed class ItfaiyePistolCasing : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        public Collider Collider { get; private set; }
        public float ExpiresAt { get; private set; }
        public int ContactsPlayed { get; private set; }
        public bool HasLanded { get; private set; }
        ItfaiyePistolCasings owner;
        AudioSource contactAudio;
        float spawnedAt, nextContact;

        public void Initialize(ItfaiyePistolCasings emitter,Rigidbody body,Collider collider)
        {
            owner=emitter; Body=body; Collider=collider;
            contactAudio=gameObject.AddComponent<AudioSource>();
            contactAudio.playOnAwake=false; contactAudio.spatialBlend=1; contactAudio.dopplerLevel=0;
            contactAudio.minDistance=1.5f; contactAudio.maxDistance=18; contactAudio.rolloffMode=AudioRolloffMode.Logarithmic;
        }
        public void Launch(Vector3 position,Quaternion rotation,Vector3 velocity,Vector3 spin,float lifetime)
        {
            transform.SetPositionAndRotation(position,rotation); transform.localScale=Vector3.one;
            spawnedAt=Time.time; nextContact=spawnedAt+.06f; ExpiresAt=Time.time+lifetime;
            ContactsPlayed=0; HasLanded=false;
            gameObject.SetActive(true);
            Body.position=position; Body.rotation=rotation; Body.linearVelocity=velocity; Body.angularVelocity=spin; Body.WakeUp();
            contactAudio.Stop();
        }
        void OnCollisionEnter(Collision collision)
        {
            if(Time.time-spawnedAt<.05f || collision.contactCount==0) return;
            if(Vector3.Dot(collision.GetContact(0).normal,Vector3.up)>.35f) HasLanded=true;
            float speed=collision.relativeVelocity.magnitude;
            if(ContactsPlayed>=3 || Time.time<nextContact || speed<.35f) return;
            owner.PlayLanding(this,collision,speed,ContactsPlayed);
            ContactsPlayed++; nextContact=Time.time+.085f;
        }
        internal void PlayContact(AudioClip clip,float volume,float pitch)
        {
            contactAudio.pitch=pitch; contactAudio.PlayOneShot(clip,volume);
        }
        void OnDisable() { if(contactAudio!=null) contactAudio.Stop(); }
    }
}
