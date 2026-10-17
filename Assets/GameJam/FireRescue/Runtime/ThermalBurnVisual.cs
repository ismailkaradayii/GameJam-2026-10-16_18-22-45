using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent, DefaultExecutionOrder(180)]
    public sealed class ThermalBurnVisual : MonoBehaviour
    {
        ThermalDamage damage;
        ThermalAudioProfile audioProfile;
        ParticleSystem flames, smoke, sparks;
        AudioSource voice, crackle;
        Light glow;
        Renderer[] surfaces;
        Color[] baseColors;
        MaterialPropertyBlock block;
        float emitRemainder, nextCry, corpseSmoke;
        bool pausedVoice, pausedCrackle;
        Transform[] anchors;
        BoxCollider truckBody;
        public int ScreamsPlayed { get; private set; }
        public int EmittedFlames { get; private set; }
        public bool AudioPaused => pausedVoice || pausedCrackle;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            damage = GetComponent<ThermalDamage>();
            if (damage.truck) truckBody = GetComponent<BoxCollider>();
            audioProfile = Resources.Load<ThermalAudioProfile>("ThermalHazards/Audio");
            var visible = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (!(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer) && r.sharedMaterial) visible.Add(r);
            surfaces = visible.ToArray(); baseColors = new Color[surfaces.Length]; block = new MaterialPropertyBlock();
            for (int i = 0; i < surfaces.Length; i++)
            {
                var m = surfaces[i].sharedMaterial;
                baseColors[i] = m.HasProperty(BaseColor) ? m.GetColor(BaseColor) : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            }
            if (damage.resident && damage.resident.animator && damage.resident.animator.isHuman)
            {
                var a = damage.resident.animator;
                anchors = new[] { a.GetBoneTransform(HumanBodyBones.Hips), a.GetBoneTransform(HumanBodyBones.Chest),
                    a.GetBoneTransform(HumanBodyBones.LeftLowerArm), a.GetBoneTransform(HumanBodyBones.RightLowerArm),
                    a.GetBoneTransform(HumanBodyBones.LeftLowerLeg), a.GetBoneTransform(HumanBodyBones.RightLowerLeg) };
            }
            flames = Particles("Clothing and engine flames", "BuildingFire/Flame", 260, false);
            smoke = Particles("Heat smoke", "BuildingFire/Smoke", 180, false);
            sparks = Particles("Burning embers", "LavaCombat/SpiritFire", 100, true);
            glow = new GameObject("Firelight").AddComponent<Light>(); glow.transform.SetParent(transform, false);
            glow.type = LightType.Point; glow.color = new Color(1f, .25f, .035f); glow.shadows = LightShadows.None; glow.enabled = false;
            voice = Emitter("Burn pain and death", false); crackle = Emitter("Burn crackle", true);
            if (audioProfile && audioProfile.fire) crackle.clip = audioProfile.fire;
        }
        AudioSource Emitter(string label, bool loop)
        {
            var s = new GameObject(label).AddComponent<AudioSource>(); s.transform.SetParent(transform, false);
            s.playOnAwake = false; s.loop = loop; s.spatialBlend = 1f; s.dopplerLevel = 0f;
            s.minDistance = damage.truck ? 7f : 3f; s.maxDistance = damage.truck ? 60f : 48f;
            s.rolloffMode = AudioRolloffMode.Logarithmic; s.priority = loop ? 150 : 65;
            return s;
        }
        ParticleSystem Particles(string label, string material, int count, bool ember)
        {
            var p = new GameObject(label).AddComponent<ParticleSystem>(); p.transform.SetParent(transform, false);
            p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = p.main; m.loop = true; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Shape; m.maxParticles = count;
            var e = p.emission; e.enabled = false; var shape = p.shape; shape.enabled = false;
            var renderer = p.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = Resources.Load<Material>(material);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var size = p.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, material == "BuildingFire/Smoke" ? AnimationCurve.Linear(0, .4f, 1, 2f) : ember ? AnimationCurve.Linear(0, 1, 1, .05f) :
                new AnimationCurve(new Keyframe(0, .55f), new Keyframe(.25f, 1f), new Keyframe(1, .08f)));
            var colors = p.colorOverLifetime; colors.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(.65f, .5f), new GradientAlphaKey(0, 1) });
            colors.color = gradient; p.Play(); return p;
        }
        public void OnIgnited()
        {
            nextCry = Time.time + 2.5f + Random.value;
            if (damage.resident) Cry(false);
            if (crackle.clip && !crackle.isPlaying) { crackle.time = Random.Range(0f, Mathf.Min(3f, crackle.clip.length)); crackle.Play(); }
        }
        public void OnDeath() { corpseSmoke = damage.truck ? 50f : 12f; if (damage.resident) Cry(true); }
        void Cry(bool death)
        {
            if (!audioProfile || !voice) return;
            var clip = death && audioProfile.death ? audioProfile.death : audioProfile.pain;
            if (!clip) return;
            voice.Stop(); voice.clip = clip; voice.pitch = .9f + (Mathf.Abs(GetInstanceID()) % 19) * .012f;
            voice.volume = .8f * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f)); voice.Play(); ScreamsPlayed++;
        }
        void LateUpdate()
        {
            if (!damage) return;
            if (Time.timeScale <= 0f)
            {
                if (voice.isPlaying) { voice.Pause(); pausedVoice = true; }
                if (crackle.isPlaying) { crackle.Pause(); pausedCrackle = true; }
                return;
            }
            if (pausedVoice) { voice.UnPause(); pausedVoice = false; }
            if (pausedCrackle) { crackle.UnPause(); pausedCrackle = false; }
            float sfx = Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f));
            voice.volume = .8f * sfx;
            crackle.volume = (damage.truck ? .45f : .18f) * damage.Heat * sfx;
            if (!damage.IsBurning && crackle.isPlaying) crackle.Stop();
            if (damage.IsBurning && damage.resident && !damage.IsDestroyed && Time.time >= nextCry)
            { Cry(false); nextCry = Time.time + 3f + Random.value * 1.3f; }
            if (!damage.IsBurning && !damage.IsDestroyed && voice.isPlaying) voice.Stop();
            float charAmount = 1f - damage.Integrity01;
            for (int i = 0; i < surfaces.Length; i++)
            {
                var r = surfaces[i]; if (!r) continue;
                r.GetPropertyBlock(block);
                Color c = Color.Lerp(baseColors[i], new Color(.065f, .055f, .05f, baseColors[i].a), charAmount * .88f);
                block.SetColor(BaseColor, c); block.SetColor(ColorId, c); r.SetPropertyBlock(block);
            }
            Bounds bounds = new Bounds(transform.position + Vector3.up, Vector3.one);
            bool first = true;
            foreach (var r in surfaces) if (r && r.enabled) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
            glow.transform.position = bounds.center;
            voice.transform.position = bounds.center; crackle.transform.position = bounds.center;
            glow.enabled = damage.IsBurning; glow.range = Mathf.Max(4f, bounds.size.magnitude * .65f);
            glow.intensity = damage.Heat * (damage.truck ? 5f : 2.2f) * (.9f + Mathf.Sin(Time.time * 18f) * .12f);
            corpseSmoke = Mathf.Max(0, corpseSmoke - Time.deltaTime);
            if (!damage.IsBurning && corpseSmoke <= 0f) return;
            emitRemainder += Time.deltaTime * (damage.truck ? 100f : 75f);
            int count = Mathf.Min(24, Mathf.FloorToInt(emitRemainder)); emitRemainder -= count;
            float scale = damage.truck ? Mathf.Clamp(bounds.size.y * .32f, .8f, 2.2f) : Mathf.Clamp(bounds.size.y * .32f, .35f, 1.4f);
            for (int i = 0; i < count; i++)
            {
                Vector3 point;
                if (truckBody)
                {
                    Vector3 c = truckBody.center, s = truckBody.size;
                    // Place fire on the engine, cab and outer panels rather than hiding it inside the mesh.
                    float side = i % 2 == 0 ? -1f : 1f;
                    Vector3 offset = i % 4 < 2 ? new Vector3(side * s.x * .28f, -s.y * .3f, s.z * .49f) :
                        i % 4 == 2 ? new Vector3(side * s.x * .32f, s.y * .48f, s.z * .28f) :
                        new Vector3(side * s.x * .49f, s.y * .05f, -s.z * .2f);
                    point = truckBody.transform.TransformPoint(c + offset) + Random.insideUnitSphere * scale * .15f;
                }
                else if (anchors != null && anchors.Length > 0 && anchors[i % anchors.Length])
                    point = anchors[i % anchors.Length].position + Random.insideUnitSphere * scale * .25f;
                else point = bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents * .8f);
                if (damage.IsBurning)
                {
                    flames.Emit(new ParticleSystem.EmitParams { position = point, velocity = Vector3.up * Random.Range(.7f, 2f),
                        startLifetime = Random.Range(.45f, .85f), startSize = scale * Random.Range(.7f, 1.5f), startColor = Color.white, rotation = Random.Range(-15f, 15f) }, 1);
                    EmittedFlames++;
                    if (i % 3 == 0) sparks.Emit(new ParticleSystem.EmitParams { position = point, velocity = Vector3.up * 2f + Random.insideUnitSphere,
                        startLifetime = .75f, startSize = scale * .06f, startColor = new Color(1f, .45f, .05f) }, 1);
                }
                if (i % 3 == 0) smoke.Emit(new ParticleSystem.EmitParams { position = point + Vector3.up * scale,
                    velocity = Vector3.up * Random.Range(.5f, 1.5f), startLifetime = 1.8f, startSize = scale * 1.3f,
                    startColor = new Color(.17f, .15f, .14f, .45f), rotation = Random.Range(0f, 360f) }, 1);
            }
        }
        void OnDisable()
        {
            if (voice) voice.Stop(); if (crackle) crackle.Stop(); if (glow) glow.enabled = false;
            pausedVoice = pausedCrackle = false;
        }
    }
}
