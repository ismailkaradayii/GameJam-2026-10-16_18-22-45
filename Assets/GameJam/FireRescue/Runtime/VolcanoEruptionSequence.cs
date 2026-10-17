using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameJam.FireRescue
{
    /// <summary>Warning, eruption and a connected lava network. Heat follows the visible mesh.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class VolcanoEruptionSequence : MonoBehaviour
    {
        public enum EruptionStage { Waiting, Warning, Erupting, Flowing }
        [Serializable] public sealed class Route
        {
            public ProgressiveLavaFlow flow;
            public ProgressiveLavaFlow feed;
            [Range(0f, 1f)] public float feedProgress = 1f;
        }
        [Serializable] public sealed class Pool
        {
            public LavaIgnitionSource source;
            public ProgressiveLavaFlow feed;
            [Range(0f, 1f)] public float feedProgress = 1f;
            [Min(0f)] public float startDelay;
            [Min(.1f)] public float fillSeconds = 10f;
            [NonSerialized] internal Vector3 originalScale;
            [NonSerialized] internal bool visible, colliderEnabled, heatEnabled;
            [NonSerialized] internal float filling;
        }
        [Serializable] public sealed class AmbientEffect
        {
            public ParticleSystem particles;
            public ProgressiveLavaFlow feed;
            [Range(0f, 1f)] public float feedProgress = 1f;
            [Min(0f)] public float startDelay;
            [NonSerialized] internal bool started;
        }
        [Serializable] public sealed class LavaLight
        {
            public Light light;
            public ProgressiveLavaFlow feed;
            [Range(0f,1f)] public float feedProgress;
            [NonSerialized] internal bool wasEnabled;
        }

        public Route[] routes = Array.Empty<Route>();
        public Pool[] pools = Array.Empty<Pool>();
        public AmbientEffect[] ambientEffects = Array.Empty<AmbientEffect>();
        public LavaLight[] lavaLights = Array.Empty<LavaLight>();
        public ParticleSystem[] suppressedLegacyEffects = Array.Empty<ParticleSystem>();
        public VolcanoEruptionEffects effects;
        public VolcanoCameraShake cameraShake;
        public bool autoErupt = true;
        [Min(0f)] public float autoEruptAfter = 15f;
        [Min(0f)] public float warningSeconds = 3f;
        [Min(0f)] public float lavaReleaseDelay = 4f;
        public bool enableCameraShake = true;
        public bool showHud = true;

        public EruptionStage Stage { get; private set; }
        public float FlowElapsed { get; private set; }
        public float StageElapsed { get; private set; }
        public float Countdown => Mathf.Max(0f, autoEruptAfter - StageElapsed);
        public bool Initialized { get; private set; }
        GUIStyle heading, body;

        void Start() => InitializeSequence();
        void Update()
        {
            if (Time.timeScale <= 0f) return;
            if (Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
                Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                BeginEruption();
            TickSequence(Time.deltaTime);
        }

        public void InitializeSequence()
        {
            if (Initialized) return;
            Stage = EruptionStage.Waiting;
            StageElapsed = FlowElapsed = 0f;
            foreach (Route route in routes) if (route?.flow) route.flow.Initialize();
            foreach (Pool pool in pools)
            {
                if (pool?.source == null) continue;
                pool.originalScale = pool.source.transform.localScale;
                pool.visible = pool.source.surfaceRenderer && pool.source.surfaceRenderer.enabled;
                pool.colliderEnabled = pool.source.heatCollider && pool.source.heatCollider.enabled;
                pool.heatEnabled = pool.source.heatEnabled;
                SetPoolActive(pool, false);
            }
            foreach (AmbientEffect fx in ambientEffects) if (fx?.particles) Stop(fx.particles);
            foreach (LavaLight glow in lavaLights) if (glow?.light) {glow.wasEnabled=glow.light.enabled;glow.light.enabled=false;}
            foreach (ParticleSystem fx in suppressedLegacyEffects) Stop(fx);
            if (effects) effects.ResetEffects();
            Initialized = true;
        }

        public bool BeginEruption()
        {
            InitializeSequence();
            if (Stage != EruptionStage.Waiting) return false;
            Stage = EruptionStage.Warning;
            StageElapsed = 0f;
            if (enableCameraShake && cameraShake) cameraShake.Shake(warningSeconds, .07f);
            return true;
        }

        public void TickSequence(float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            InitializeSequence();
            // Bounded substeps preserve branch timing across a long frame and make checks deterministic.
            float remaining = deltaTime;
            while (remaining > .00001f)
            {
                float step = Mathf.Min(.1f, remaining);
                TickStep(step);
                remaining -= step;
            }
        }

        void TickStep(float step)
        {
            StageElapsed += step;
            if (Stage == EruptionStage.Waiting)
            {
                if (autoErupt && StageElapsed >= Mathf.Max(0f, autoEruptAfter)) BeginEruption();
                return;
            }
            if (Stage == EruptionStage.Warning)
            {
                if (StageElapsed >= Mathf.Max(0f, warningSeconds))
                {
                    Stage = EruptionStage.Erupting;
                    StageElapsed = 0f;
                    if (effects) effects.BeginEruption();
                    if (enableCameraShake && cameraShake) cameraShake.Shake(8f, .35f);
                }
                return;
            }
            if (effects) effects.TickEffect(step);
            if (Stage == EruptionStage.Erupting)
            {
                if (StageElapsed >= Mathf.Max(0f, lavaReleaseDelay))
                {
                    Stage = EruptionStage.Flowing;
                    StageElapsed = FlowElapsed = 0f;
                }
                return;
            }

            FlowElapsed += step;
            foreach (Route route in routes)
            {
                if (route?.flow == null || FlowElapsed < route.flow.startDelay || !Fed(route.feed, route.feedProgress)) continue;
                route.flow.Advance(step);
            }
            foreach (Pool pool in pools)
            {
                if (pool?.source == null || FlowElapsed < pool.startDelay || !Fed(pool.feed, pool.feedProgress)) continue;
                pool.filling += step;
                float scale = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(pool.filling / Mathf.Max(.1f, pool.fillSeconds)));
                pool.source.transform.localScale = Vector3.Scale(pool.originalScale, new Vector3(Mathf.Max(.002f, scale), 1f, Mathf.Max(.002f, scale)));
                SetPoolActive(pool, scale > .001f);
            }
            foreach (AmbientEffect fx in ambientEffects)
            {
                if (fx == null || fx.started || !fx.particles || FlowElapsed < fx.startDelay || !Fed(fx.feed, fx.feedProgress)) continue;
                fx.started = true;
                fx.particles.Play(true);
            }
            foreach (LavaLight glow in lavaLights) if (glow?.light) glow.light.enabled=glow.wasEnabled&&Fed(glow.feed,glow.feedProgress);
        }

        static bool Fed(ProgressiveLavaFlow feed, float progress) =>
            !feed || (feed.VisibleDistance > 0f && feed.VisibleDistance + .005f >= feed.Length * Mathf.Clamp01(progress));

        static void SetPoolActive(Pool pool, bool active)
        {
            if (pool.source.surfaceRenderer) pool.source.surfaceRenderer.enabled = active;
            if (pool.source.heatCollider) pool.source.heatCollider.enabled = active;
            pool.source.heatEnabled = active;
        }

        static void Stop(ParticleSystem fx)
        {
            if (!fx) return;
            var main = fx.main; main.playOnAwake = false;
            fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDestroy()
        {
            if (!Initialized) return;
            foreach (Pool pool in pools)
            {
                if (pool?.source == null) continue;
                pool.source.transform.localScale = pool.originalScale;
                if (pool.source.surfaceRenderer) pool.source.surfaceRenderer.enabled = pool.visible;
                if (pool.source.heatCollider) pool.source.heatCollider.enabled = pool.colliderEnabled;
                pool.source.heatEnabled = pool.heatEnabled;
            }
            foreach(LavaLight glow in lavaLights)if(glow?.light)glow.light.enabled=glow.wasEnabled;
        }

        void OnGUI()
        {
            if (!showHud || !Initialized) return;
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) {fontSize=16,fontStyle=FontStyle.Bold};
                body = new GUIStyle(GUI.skin.label) {fontSize=13,wordWrap=true};
                heading.normal.textColor=body.normal.textColor=Color.white;
            }
            float scale = Mathf.Clamp(Screen.height / 900f, .75f, 1.5f);
            var oldMatrix=GUI.matrix;var oldColor=GUI.color;
            GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float width=Screen.width/scale;
            // Stack under the water HUD when the Game panel is narrow.
            float x=width>=720f?width-334f:18f;
            float y=width>=720f?18f:210f;
            GUI.color=new Color(.1f,.055f,.035f,.88f);
            GUI.DrawTexture(new Rect(x,y,316,92),Texture2D.whiteTexture);
            GUI.color=Color.white;
            string title, detail;
            if(Stage==EruptionStage.Waiting){title="VOLKAN · HAZIRLIK";detail=autoErupt?"Patlamaya "+Mathf.CeilToInt(Countdown)+" sn\nV: Patlamayı şimdi başlat":"V: Volkan patlamasını başlat";}
            else if(Stage==EruptionStage.Warning){title="YER SARSILIYOR";detail="Volkan patlamak üzere!\nİtfaiyeyi kasabaya götür.";}
            else if(Stage==EruptionStage.Erupting){title="VOLKAN PATLADI";detail="Lav akışı başlıyor.\nKasabadaki evleri koru.";}
            else {int burning=0;foreach(var house in BurnableBuilding.ActiveBuildings)if(house&&house.IsBurning)burning++;title="LAV KASABAYA İLERLİYOR";detail="Yanan ev: "+burning+"\nSağ tıkla su püskürterek söndür.";}
            GUI.Label(new Rect(x+12,y+9,292,24),title,heading);
            GUI.Label(new Rect(x+12,y+35,292,50),detail,body);
            GUI.matrix=oldMatrix;GUI.color=oldColor;
        }
    }
}
