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
            [NonSerialized] internal float cooling;
            [NonSerialized] internal MaterialPropertyBlock properties, originalProperties;
            [NonSerialized] internal Color emissiveColor, emissionColor, mainColor;
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
            [NonSerialized] internal float originalIntensity;
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
                if (pool.feed && pool.feed.canBeStoppedByWater && pool.source.surfaceRenderer)
                {
                    pool.properties = new MaterialPropertyBlock();
                    pool.originalProperties = new MaterialPropertyBlock();
                    pool.source.surfaceRenderer.GetPropertyBlock(pool.originalProperties);
                    Material material = pool.source.surfaceRenderer.sharedMaterial;
                    pool.emissiveColor = material && material.HasProperty("_EmissiveColor") ? material.GetColor("_EmissiveColor") : Color.black;
                    pool.emissionColor = material && material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
                    pool.mainColor = material && material.HasProperty("_MainColor") ? material.GetColor("_MainColor") : Color.white;
                }
                SetPoolActive(pool, false);
            }
            foreach (AmbientEffect fx in ambientEffects) if (fx?.particles) Stop(fx.particles);
            foreach (LavaLight glow in lavaLights) if (glow?.light)
            {
                glow.wasEnabled = glow.light.enabled;
                glow.originalIntensity = glow.light.intensity;
                glow.light.enabled = false;
                glow.light.intensity = 0f;
            }
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
            if (Time.timeScale <= 0f || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
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
                    {
                        glow.light.enabled = glow.wasEnabled;
                        glow.light.intensity = glow.originalIntensity;
                    }
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
                if (route?.flow == null) continue;
                float supply = FlowElapsed >= route.flow.startDelay ? Supply(route.feed, route.feedProgress) : 0f;
                // Disconnected branches retain their geometry and continue cooling.
                route.flow.Advance(step, supply);
            }
            foreach (Pool pool in pools)
                TickPool(pool, step, pool != null && pool.beginsWithEruption ? FlowElapsed + lavaReleaseDelay : FlowElapsed);
            foreach (AmbientEffect fx in ambientEffects)
            {
                if (fx == null || !fx.particles) continue;
                bool fed = FlowElapsed >= fx.startDelay && Supply(fx.feed, fx.feedProgress) > .001f;
                if (fed == fx.started) continue;
                fx.started = fed;
                if (fed) fx.particles.Play(true);
                else fx.particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            foreach (LavaLight glow in lavaLights)
            {
                if (!glow?.light) continue;
                float supply = Supply(glow.feed, glow.feedProgress);
                // Existing hot residue dims over the same cooling time after a cut.
                float target = glow.wasEnabled ? glow.originalIntensity * supply : 0f;
                float seconds = target < glow.light.intensity ? 6f : .6f;
                glow.light.intensity = Mathf.MoveTowards(glow.light.intensity, target, glow.originalIntensity * step / seconds);
                glow.light.enabled = glow.wasEnabled && glow.light.intensity > .001f &&
                    (supply > .001f || glow.feed && glow.feed.VisibleDistance > 0f &&
                    glow.feed.VisibleDistance + .005f >= glow.feed.Length * Mathf.Clamp01(glow.feedProgress));
            }
        }

        static float Supply(ProgressiveLavaFlow feed, float progress) => !feed ? 1f : feed.GetSupply01(progress);

        static void TickPool(Pool pool, float step, float elapsed)
        {
            if (pool?.source == null || elapsed < pool.startDelay) return;
            float supply = Supply(pool.feed, pool.feedProgress);
            pool.filling += step * supply;
            if (pool.filling <= 0f) return;
            if (supply <= .001f) pool.cooling = Mathf.Min(1f, pool.cooling + step / 6f);
            float scale = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(pool.filling / Mathf.Max(.1f, pool.fillSeconds)));
            pool.source.transform.localScale = Vector3.Scale(pool.originalScale, new Vector3(Mathf.Max(.002f, scale), 1f, Mathf.Max(.002f, scale)));
            SetPoolActive(pool, scale > .001f);
            pool.source.heatEnabled = scale > .001f && pool.cooling < 1f;
            if (pool.properties != null && pool.source.surfaceRenderer)
            {
                float heat = 1f - pool.cooling;
                pool.source.surfaceRenderer.GetPropertyBlock(pool.properties);
                pool.properties.SetColor("_EmissiveColor", pool.emissiveColor * heat * heat);
                pool.properties.SetColor("_EmissionColor", pool.emissionColor * heat * heat);
                pool.properties.SetColor("_MainColor", Color.Lerp(new Color(.09f, .1f, .11f), pool.mainColor, heat));
                pool.source.surfaceRenderer.SetPropertyBlock(pool.properties);
            }
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
                if (pool.originalProperties != null && pool.source.surfaceRenderer)
                    pool.source.surfaceRenderer.SetPropertyBlock(pool.originalProperties);
            }
            foreach (LavaLight glow in lavaLights) if (glow?.light)
            {
                glow.light.enabled = glow.wasEnabled;
                glow.light.intensity = glow.originalIntensity;
            }
        }

        void OnGUI()
        {
            if (!showHud || !Initialized || VolcanoCinematic.IsPlayingAny) return;
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) {fontSize=16,fontStyle=FontStyle.Bold};
                body = new GUIStyle(GUI.skin.label) {fontSize=13,wordWrap=true};
                heading.font = Ashfall.UI.AshfallTypography.Semibold;
                body.font = Ashfall.UI.AshfallTypography.Regular;
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
