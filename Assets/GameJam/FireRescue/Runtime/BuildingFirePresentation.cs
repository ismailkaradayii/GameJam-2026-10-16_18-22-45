using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>Fits bounded flame/smoke buffers to actual house surfaces without changing fire or water rules.</summary>
    public sealed class BuildingFirePresentation
    {
        struct Source { public Vector3 point, normal; public Vector3[] patchPoints; }
        readonly BurnableBuilding building;
        readonly Collider[] solids;
        readonly List<Source> flames = new List<Source>(16), roof = new List<Source>(8);
        readonly ParticleSystem embers;
        readonly float buildingScale;
        float heat, burnAge, clock, flameBudget, smokeBudget, emberBudget, steam, smolder;
        bool available;
        int nextFlame, nextSmoke;
        public int FlameSourceCount => flames.Count;
        public int RoofSourceCount => roof.Count;

        public static BuildingFirePresentation Create(BurnableBuilding building)
        {
            if (!building || !building.flames || !building.smoke || !building.ignitionBounds) return null;
            var material = Resources.Load<Material>("BuildingFire/Flame");
            var smoke = Resources.Load<Material>("BuildingFire/Smoke");
            if (!material || !smoke) return null;
            return new BuildingFirePresentation(building, material, smoke);
        }
        BuildingFirePresentation(BurnableBuilding owner, Material flameMaterial, Material smokeMaterial)
        {
            building = owner;
            buildingScale = Mathf.Clamp(owner.ignitionBounds.bounds.size.y / 8f,.7f,3f);
            solids = owner.GetComponentsInChildren<Collider>();
            FitSources();
            FitPatches();
            Configure(owner.flames, flameMaterial, 192, true);
            Configure(owner.smoke, smokeMaterial, 96, false);
            embers = Object.Instantiate(owner.flames,owner.transform,false);
            embers.name = "House fire embers (runtime)";
            Configure(embers, Resources.Load<Material>("LavaCombat/Flame"), 64, false);
            var uv = embers.textureSheetAnimation; uv.enabled = false;
            var size = embers.sizeOverLifetime; size.enabled = false;
            if (owner.fireLight) { owner.fireLight.color = new Color(1, .38f, .08f); owner.fireLight.range = Mathf.Clamp(owner.ignitionBounds.bounds.size.magnitude*.85f,18,75); }
        }
        void FitSources()
        {
            var box = building.ignitionBounds; Vector3 c = box.center, s = box.size;
            // Find facade surfaces, then roof surfaces. Flames never spawn in a floating box volume.
            for (int side = -1; side <= 1; side += 2)
            for (int slot = -1; slot <= 1; slot += 2)
            {
                Facade(c + new Vector3(s.x * slot * .24f, -s.y * .15f, s.z * side * .5f), box.transform.forward * side);
                Facade(c + new Vector3(s.x * side * .5f, -s.y * .15f, s.z * slot * .21f), box.transform.right * side);
            }
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 origin = box.transform.TransformPoint(c + new Vector3(s.x * x * .27f, s.y * .5f, s.z * z * .24f)) + Vector3.up * 5;
                if (Surface(new Ray(origin, Vector3.down), box.bounds.size.y + 10, out var hit) && hit.normal.y > .3f)
                {
                    var source = new Source { point = hit.point + Vector3.up * .07f, normal = hit.normal };
                    roof.Add(source); flames.Add(source);
                }
            }
            if (roof.Count == 0)
            {
                // Fallback remains attached to the roof height of the fitted ignition volume.
                var source = new Source { point = new Vector3(box.bounds.center.x, box.bounds.max.y, box.bounds.center.z), normal = Vector3.up };
                roof.Add(source); flames.Add(source);
            }
        }
        void Facade(Vector3 local, Vector3 outward)
        {
            var box = building.ignitionBounds;
            Vector3 position = box.transform.TransformPoint(local);
            if (Surface(new Ray(position + outward * (4*buildingScale), -outward), 8*buildingScale, out var hit))
                flames.Add(new Source { point = hit.point + hit.normal * .09f, normal = hit.normal });
        }
        void FitPatches()
        {
            // Cache surface samples once. The runtime does not raycast for each flame particle.
            for (int index = 0; index < flames.Count; index++)
            {
                Source source = flames[index]; var points = new List<Vector3>(7) {source.point};
                Vector3 tangent = Vector3.Cross(source.normal,Vector3.up);
                if (tangent.sqrMagnitude < .01f) tangent = building.transform.right;
                tangent.Normalize(); Vector3 bitangent = Vector3.Cross(source.normal,tangent).normalized;
                for (int sample = 1; sample <= 6; sample++)
                {
                    float angle = sample * 2.399963f, radius = Mathf.Sqrt(sample/6f)*buildingScale*1.25f;
                    Vector3 offset = tangent*Mathf.Cos(angle)*radius + bitangent*Mathf.Sin(angle)*radius*(source.normal.y < .3f ? .25f : 1);
                    Vector3 candidate = source.point + offset;
                    if (Surface(new Ray(candidate+source.normal*(2*buildingScale),-source.normal),4*buildingScale,out var hit) && Vector3.Dot(hit.normal,source.normal) > .2f)
                        points.Add(hit.point+hit.normal*.1f);
                }
                source.patchPoints = points.ToArray(); flames[index] = source;
            }
        }
        bool Surface(Ray ray, float length, out RaycastHit closest)
        {
            closest = default; float distance = length; bool found = false;
            foreach (var collider in solids)
            {
                if (!collider || !collider.enabled || collider.isTrigger || collider is CharacterController) continue;
                if (!collider.Raycast(ray, out var hit, length) || hit.distance >= distance) continue;
                closest = hit; distance = hit.distance; found = true;
            }
            return found;
        }
        static void Configure(ParticleSystem ps, Material material, int maximum, bool flame)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = false; main.playOnAwake = false; main.duration = 8;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = maximum; main.startSpeed = 0; main.gravityModifier = 0; main.startSize3D = flame; main.startRotation3D = false;
            var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 0; emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
            var shape = ps.shape; shape.enabled = false;
            var velocity = ps.velocityOverLifetime; velocity.enabled = false;
            var force = ps.forceOverLifetime; force.enabled = false;
            var trails = ps.trails; trails.enabled = false;
            var size = ps.sizeOverLifetime; size.enabled = true; size.separateAxes = false;
            size.size = new ParticleSystem.MinMaxCurve(1, flame ? new AnimationCurve(new Keyframe(0,.4f),new Keyframe(.15f,1),new Keyframe(.7f,.85f),new Keyframe(1,.15f)) : AnimationCurve.Linear(0,.7f,1,2.7f));
            var gradient = new Gradient();
            gradient.SetKeys(new[] {new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[] {new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.7f,.65f),new GradientAlphaKey(0,1)});
            var color = ps.colorOverLifetime; color.enabled = true; color.color = gradient;
            var noise = ps.noise; noise.enabled = !flame; noise.strength = .35f; noise.frequency = .35f; noise.scrollSpeed = .25f; noise.quality = ParticleSystemNoiseQuality.Low;
            if (flame) { var uv = ps.textureSheetAnimation; uv.enabled = false; }
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.renderMode = flame ? ParticleSystemRenderMode.VerticalBillboard : ParticleSystemRenderMode.Billboard;
            renderer.pivot = flame ? new Vector3(0,.5f,0) : Vector3.zero;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        public void BeginAt(Vector3 point)
        {
            // Roof impacts kindle the nearest roof patches before spreading down the facade.
            flames.Sort((a, b) => (a.point - point).sqrMagnitude.CompareTo((b.point - point).sqrMagnitude));
            nextFlame = 0;
        }

        public void SetState(float value, bool enabled)
        {
            if (!enabled) { available = false; heat = 0; Stop(); return; }
            bool newlyActive = !available;
            available = true;
            if ((heat <= 0 || newlyActive) && value > 0)
            {
                burnAge = 0; nextFlame = nextSmoke = 0;
                Play(building.flames); Play(building.smoke); Play(embers);
                if (building.fireLight) building.fireLight.enabled = true;
            }
            if (value < heat - .002f) steam = 1.4f;
            if (heat > 0 && value <= 0)
            {
                smolder = 4;
                building.flames.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                embers.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                if (building.fireLight) building.fireLight.enabled = false;
            }
            heat = value;
        }
        public void Tick(float deltaTime)
        {
            if (!available || deltaTime <= 0 || !float.IsFinite(deltaTime)) return;
            float dt = Mathf.Min(deltaTime, .1f); clock += dt; burnAge += dt;
            steam = Mathf.Max(0, steam - dt); smolder = Mathf.Max(0, smolder - dt);
            if (heat > 0)
            {
                Play(building.flames); Play(embers);
                int active = Mathf.Clamp(Mathf.CeilToInt(flames.Count * Mathf.Lerp(.35f,1,Mathf.Clamp01(burnAge / Mathf.Max(.2f,building.visualFireSpreadSeconds)))),1,flames.Count);
                flameBudget += dt * (65 + flames.Count * 7) * Mathf.Sqrt(heat);
                while (flameBudget >= 1)
                {
                    flameBudget--;
                    Source source = flames[nextFlame++ % active]; float scale = buildingScale * building.flameVisualScale * Mathf.Lerp(.55f,1,heat);
                    building.flames.Emit(new ParticleSystem.EmitParams
                    {
                        position = source.patchPoints[Random.Range(0,source.patchPoints.Length)],
                        velocity = (Vector3.up * Random.Range(.08f,.25f) + Vector3.ProjectOnPlane(source.normal,Vector3.up) * .08f) * Mathf.Sqrt(buildingScale),
                        startSize3D = new Vector3(Random.Range(.95f,1.8f),Random.Range(1.6f,3.8f),1) * scale,
                        startLifetime = Random.Range(.42f,.85f), rotation = Random.Range(-7f,7f),
                        startColor = new Color(1,Random.Range(.8f,1f),Random.Range(.04f,.25f),Random.Range(.7f,1f))
                    },1);
                }
                emberBudget += dt * heat * 13;
                while (emberBudget >= 1)
                {
                    emberBudget--; Source source = roof[Random.Range(0,roof.Count)];
                    embers.Emit(new ParticleSystem.EmitParams {position=source.point+Vector3.up*.2f,velocity=new Vector3(Random.Range(-.4f,.8f),Random.Range(2,4),Random.Range(-.5f,.5f)),startSize=Random.Range(.025f,.065f),startLifetime=Random.Range(1,2.2f),startColor=new Color(1,.48f,.05f,.9f)},1);
                }
            }
            else { flameBudget = emberBudget = 0; }
            if (heat > 0 || smolder > 0)
            {
                Play(building.smoke);
                float amount = heat > 0 ? heat : .22f * smolder / 4;
                smokeBudget += dt * (roof.Count + 1) * amount;
                while (smokeBudget >= 1)
                {
                    smokeBudget--; Source source = roof[nextSmoke++ % roof.Count]; bool cooling = steam > 0;
                    building.smoke.Emit(new ParticleSystem.EmitParams
                    {
                        position=source.point+Vector3.up*.3f+Random.insideUnitSphere*.3f,
                        velocity=new Vector3(.3f,Random.Range(2.2f,3.7f),.15f)*Mathf.Sqrt(buildingScale),startSize=Random.Range(1.7f,2.6f)*buildingScale,
                        startLifetime=Random.Range(4,6.5f),rotation=Random.Range(-180f,180f),
                        startColor=cooling ? new Color(.8f,.8f,.78f,.45f) : new Color(.34f,.31f,.28f,.65f)
                    },1);
                }
            }
            else
            {
                smokeBudget = 0;
                if (building.smoke.isEmitting) building.smoke.Stop(false,ParticleSystemStopBehavior.StopEmitting);
            }
            if (building.fireLight)
            {
                building.fireLight.enabled = heat > 0;
                float seed = Mathf.Abs(building.transform.position.x) * .13f;
                building.fireLight.intensity = 48 * buildingScale * buildingScale * heat * (.65f + .35f * Mathf.PerlinNoise(clock * 8,seed));
            }
        }
        static void Play(ParticleSystem ps) { if (ps && !ps.isPlaying) ps.Play(false); }
        public void Stop()
        {
            available = false;
            flameBudget = smokeBudget = emberBudget = steam = smolder = 0;
            if (building.flames) building.flames.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            if (building.smoke) building.smoke.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            if (embers) embers.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            if (building.fireLight) building.fireLight.enabled = false;
        }
        public void Dispose()
        {
            if (!embers) return;
            if (Application.isPlaying) Object.Destroy(embers.gameObject);
            else Object.DestroyImmediate(embers.gameObject);
        }
    }
}
