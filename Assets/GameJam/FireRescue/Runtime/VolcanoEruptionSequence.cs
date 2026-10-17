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
            public bool beginsWithEruption;
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
        [SerializeField] private WindAudioPulse audioTimeline;
        public bool autoErupt = true;
        [Min(0f)] public float autoEruptAfter = 15f;
        [Min(0f)] public float warningSeconds = 3f;
        [Min(0f)] public float lavaReleaseDelay = 4f;
        public bool enableCameraShake = true;
        public bool showHud = true;
        [Header("Pre-eruption objective")]
        public EvacuationMission evacuation;
        public VolcanoMissionDirector missionDirector;
        public VolcanoCinematic cinematic;
        public bool WaitingForEvacuation => evacuation && !evacuation.IsComplete;
        public bool WaitingForPreparation => WaitingForEvacuation || (missionDirector && !missionDirector.PreparationComplete);

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
            if (!missionDirector && Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
                Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                BeginEruption();
            TickSequence(Time.deltaTime);
        }

        public void InitializeSequence()
        {
            if (Initialized) return;
            if (!audioTimeline) audioTimeline = FindFirstObjectByType<WindAudioPulse>();
            if (audioTimeline) audioTimeline.ResetSequence();
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

        // The mission countdown can own the audible pre-impact window independently of visual warning duration.
        public void BeginExplosionAudio()
        {
            InitializeSequence();
            if (audioTimeline) audioTimeline.BeginExplosionSound();
        }

        public void NotifyExplosionAudioEvent(float minimumPostSeconds = 0f)
        {
            InitializeSequence();
            if (audioTimeline) audioTimeline.NotifyExplosionEvent(minimumPostSeconds);
        }

        public bool BeginEruption(bool ignorePreparation = false)
        {
            InitializeSequence();
            if (Stage != EruptionStage.Waiting || (!ignorePreparation && WaitingForPreparation)) return false;
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
            if (Stage == EruptionStage.Waiting && WaitingForPreparation) return;
            if (audioTimeline) audioTimeline.TickSequence(step);

            // Warning is the final authored countdown before the actual eruption event.
            // Start on the simulation step that crosses the one-second lead boundary.
            if (Stage == EruptionStage.Warning && audioTimeline &&
                StageElapsed <= Mathf.Max(0f, warningSeconds - WindAudioPulse.ExplosionLeadSeconds) &&
                StageElapsed + step >= Mathf.Max(0f, warningSeconds - WindAudioPulse.ExplosionLeadSeconds))
                audioTimeline.BeginExplosionSound();

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
                    if (audioTimeline) audioTimeline.NotifyExplosionEvent(cinematic ? cinematic.eruptionSeconds : 0f);
                    if (effects) effects.BeginEruption();
                    if (cinematic) cinematic.BeginShot();
                    if (enableCameraShake && cameraShake) cameraShake.Shake(8f, .35f);
                }
                return;
            }
            if (effects) effects.TickEffect(step);
            if (Stage == EruptionStage.Erupting)
            {
                foreach (Pool pool in pools)
                    if (pool != null && pool.beginsWithEruption) TickPool(pool, step, StageElapsed);
                // The crater's authored glow lights the eruption while downhill lava stays gated.
                foreach (LavaLight glow in lavaLights)
                    if (glow?.light && effects && (glow.light.transform.position - effects.transform.position).sqrMagnitude < 40000f)
                        glow.light.enabled = glow.wasEnabled;
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
                TickPool(pool, step, pool != null && pool.beginsWithEruption ? FlowElapsed + lavaReleaseDelay : FlowElapsed);
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

        static void TickPool(Pool pool, float step, float elapsed)
        {
            if (pool?.source == null || elapsed < pool.startDelay || !Fed(pool.feed, pool.feedProgress)) return;
            pool.filling += step;
            float scale = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(pool.filling / Mathf.Max(.1f, pool.fillSeconds)));
            pool.source.transform.localScale = Vector3.Scale(pool.originalScale, new Vector3(Mathf.Max(.002f, scale), 1f, Mathf.Max(.002f, scale)));
            SetPoolActive(pool, scale > .001f);
        }

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
            if (!showHud || !Initialized || VolcanoCinematic.IsPlayingAny) return;
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
            if(Stage==EruptionStage.Waiting){title="VOLCANO · PREPARATION";detail=WaitingForEvacuation?"First lead the villagers to shelter.\nEvacuated: "+evacuation.RescuedCount+" / "+evacuation.TotalCount:autoErupt?"Eruption in "+Mathf.CeilToInt(Countdown)+" sec\nV: Start eruption now":"V: Start eruption";}
            else if(Stage==EruptionStage.Warning){title="THE GROUND IS SHAKING";detail="The volcano is about to erupt!\nTake the fire truck to town.";}
            else if(Stage==EruptionStage.Erupting){title="VOLCANO ERUPTED";detail="Lava is beginning to flow.\nProtect the houses.";}
            else {int burning=0;foreach(var house in BurnableBuilding.ActiveBuildings)if(house&&house.IsBurning)burning++;title="LAVA ADVANCING";detail="Burning houses: "+burning+"\nRight click to spray water.";}
            GUI.Label(new Rect(x+12,y+9,292,24),title,heading);
            GUI.Label(new Rect(x+12,y+35,292,50),detail,body);
            GUI.matrix=oldMatrix;GUI.color=oldColor;
        }
    }
}
