using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent]
    public sealed class BuildingFireVisual : MonoBehaviour
    {
        BurnableBuilding building;
        ParticleSystem embers, steam;
        Material fireMaterial, smokeMaterial;
        readonly List<Vector3> seats = new List<Vector3>();
        float heat, age, flamesDue, smokeDue, embersDue, steamLeft;
        float size, seed, lastHeat;
        int seat;
        bool built;
        bool collapsed;
        public int FlameSeatCount => seats.Count;
        public int ActiveParticles => (building && building.flames ? building.flames.particleCount : 0) +
            (building && building.smoke ? building.smoke.particleCount : 0) + (embers ? embers.particleCount : 0);

        public static BuildingFireVisual Ensure(BurnableBuilding owner)
        {
            if (!owner.ignitionBounds || !owner.flames || !owner.smoke) return null;
            var visual = owner.GetComponent<BuildingFireVisual>() ?? owner.gameObject.AddComponent<BuildingFireVisual>();
            if (!visual.built) visual.Build(owner);
            return visual.built ? visual : null;
        }

        void Build(BurnableBuilding owner)
        {
            var shader = Resources.Load<Shader>("BuildingFire/BuildingFire");
            if (!shader) return;
            building = owner;
            fireMaterial = new Material(shader) { name = "Layered building flames (runtime)" };
            smokeMaterial = new Material(shader) { name = "Turbulent building smoke (runtime)" }; smokeMaterial.SetFloat("_Smoke", 1);
            var box = owner.ignitionBounds; var half = box.size * .5f;
            var surfaces = owner.GetComponentsInChildren<Collider>();
            size = Mathf.Clamp(box.bounds.size.y * .22f, .65f, 3.2f);
            seed = Mathf.Repeat(GetInstanceID() * .137f, 100);
            for (int side = 0; side < 4; side++) for (int n = 0; n < 4; n++)
            {
                float fraction = Mathf.Lerp(-.8f, .8f, n / 3f);
                Vector3 local = side < 2 ? new Vector3((side == 0 ? -1 : 1) * (half.x + .04f), 0, half.z * fraction) :
                    new Vector3(half.x * fraction, 0, (side == 2 ? -1 : 1) * (half.z + .04f));
                // Alternate lower wall fires with eaves; expansion gradually lights the roof line.
                local.y = Mathf.Lerp(-half.y, half.y, n % 2 == 0 ? .28f : .69f);
                Vector3 point = box.transform.TransformPoint(box.center + local);
                Vector3 inward = box.bounds.center - point; inward.y = 0; inward.Normalize();
                float reach = box.bounds.size.magnitude;
                var ray = new Ray(point - inward * reach, inward);
                float nearest = float.PositiveInfinity;
                // Fit each source to the actual wall/eave. A rectangular ignition volume also
                // encloses empty space around porches and L-shaped houses.
                foreach (var surface in surfaces)
                {
                    if (!surface || !surface.enabled || surface.isTrigger) continue;
                    if (surface.Raycast(ray, out var hit, reach * 2) && hit.distance < nearest)
                    { nearest = hit.distance; point = hit.point - inward * .09f; }
                }
                seats.Add(point);
            }
            Configure(owner.flames, fireMaterial, 420, false);
            Configure(owner.smoke, smokeMaterial, 180, true);
            embers = NewParticles("Updraft embers", fireMaterial, 130, false);
            steam = NewParticles("Extinguishing steam", smokeMaterial, 90, true);
            var main = embers.main; main.gravityModifier = -.035f;
            if (owner.fireLight) { owner.fireLight.shadows = LightShadows.None; owner.fireLight.range = Mathf.Max(12, box.bounds.size.magnitude * .65f); }
            built = true;
        }

        ParticleSystem NewParticles(string label, Material material, int capacity, bool smoke)
        {
            var go = new GameObject(label); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); Configure(ps, material, capacity, smoke); return ps;
        }

        static void Configure(ParticleSystem ps, Material material, int capacity, bool smoke)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.maxParticles = capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startSpeed = 0; main.startLifetime = 1; main.gravityModifier = 0; main.startRotation = 0;
            main.startRotation3D = false;
            main.startSize3D = true; main.startSizeX = main.startSizeY = main.startSizeZ = 1;
            var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new ParticleSystem.Burst[0]);
            var shape = ps.shape; shape.enabled = false;
            var sheet = ps.textureSheetAnimation; sheet.enabled = false;
            var trails = ps.trails; trails.enabled = false;
            var sizeBySpeed = ps.sizeBySpeed; sizeBySpeed.enabled = false;
            var colorBySpeed = ps.colorBySpeed; colorBySpeed.enabled = false;
            var velocity = ps.velocityOverLifetime; velocity.enabled = false;
            var force = ps.forceOverLifetime; force.enabled = false;
            var noise = ps.noise; noise.enabled = true; noise.strength = smoke ? .55f : .17f;
            noise.frequency = smoke ? .35f : .9f; noise.scrollSpeed = .45f; noise.quality = ParticleSystemNoiseQuality.Low;
            var color = ps.colorOverLifetime; color.enabled = true;
            color.color = new Gradient { colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(.65f, .55f), new GradientAlphaKey(0, 1) } };
            var growth = ps.sizeOverLifetime; growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, smoke ? .5f : .65f), new Keyframe(.35f, 1), new Keyframe(1, smoke ? 2.3f : .25f)));
            var rotation = ps.rotationOverLifetime; rotation.enabled = smoke; rotation.z = new ParticleSystem.MinMaxCurve(-.18f, .18f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            // Authored URP particles used a different packed vertex layout. Explicit UVs are
            // required here, otherwise the procedural mask becomes a solid rectangular card.
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            renderer.SetPropertyBlock(null);
            renderer.renderMode = smoke ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.VerticalBillboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.maxParticleSize = .8f; renderer.sortMode = ParticleSystemSortMode.Distance;
        }

        public void SetHeat(float value)
        {
            if (!built) return;
            if (building.IsBurnedOut) { ClearForRuin(); return; }
            heat = isActiveAndEnabled ? value : 0;
            if (lastHeat > heat + .001f) steamLeft = Mathf.Max(steamLeft, 1.8f);
            if (heat > 0 && lastHeat <= 0) age = 0;
            Toggle(building.flames, heat > 0); Toggle(building.smoke, heat > 0); Toggle(embers, heat > 0);
            lastHeat = heat;
        }

        void LateUpdate() => TickVisuals(Time.deltaTime);
        public void TickVisuals(float dt)
        {
            if (!built || dt <= 0 || !isActiveAndEnabled) return;
            if (building.IsBurnedOut) { ClearForRuin(); return; }
            dt = Mathf.Min(dt, .1f);
            var box = building.ignitionBounds;
            if (heat > 0)
            {
                age += dt;
                float spread = Mathf.Lerp(.3f, 1, Mathf.Clamp01(age / 7));
                flamesDue += dt * 176 * heat * spread;
                while (flamesDue >= 1)
                {
                    flamesDue--;
                    int index = (seat++ * 5) % Mathf.Max(1, Mathf.CeilToInt(seats.Count * spread));
                    Vector3 p = seats[index] + new Vector3(Random.Range(-.22f, .22f), 0, Random.Range(-.22f, .22f)) * size;
                    Vector3 flameSize = new Vector3(Random.Range(.85f, 1.45f), Random.Range(1.6f, 2.8f), 1) * size;
                    Emit(building.flames, p + Vector3.up * flameSize.y * .26f, new Vector3(.18f, Random.Range(.55f, 1.4f), .12f) * size,
                        flameSize,
                        Random.Range(.65f, 1.25f), new Color(1, .9f, Random.Range(.7f, 1), .82f * Mathf.Sqrt(heat)), false);
                }
                smokeDue += dt * 16 * heat;
                while (smokeDue >= 1)
                {
                    smokeDue--; Vector3 p = seats[(seat * 5 + (int)smokeDue) % seats.Count];
                    p.y = Mathf.Lerp(p.y, box.bounds.max.y, .7f);
                    float soot = Random.Range(.095f, .21f);
                    Emit(building.smoke, p, new Vector3(.55f, 1.65f, .24f) * size, Vector3.one * Random.Range(1.6f, 2.6f) * size,
                        Random.Range(4.2f, 6.6f), new Color(soot, soot * .96f, soot * .92f, .72f), true);
                }
                embersDue += dt * 28 * heat;
                while (embersDue >= 1)
                {
                    embersDue--; var p = seats[Random.Range(0, seats.Count)];
                    Emit(embers, p, new Vector3(Random.Range(-.5f, 1), Random.Range(2, 4), Random.Range(-.5f, .6f)) * size,
                        Vector3.one * Random.Range(.025f, .07f) * size, Random.Range(1.2f, 3), new Color(2, 1, .5f, 1), false);
                }
            }
            if (building.fireLight)
            {
                building.fireLight.enabled = heat > 0;
                building.fireLight.color = new Color(1, .33f, .07f);
                building.fireLight.intensity = heat * (2.8f + Mathf.PerlinNoise(seed, Time.time * 8) * 3.5f);
            }
            if (steamLeft > 0 && !building.IsBurnedOut)
            {
                Toggle(steam, true); steamLeft -= dt;
                if (Random.value < dt * 24)
                    Emit(steam, seats[Random.Range(0, seats.Count)], Vector3.up * size * 1.6f,
                        Vector3.one * size * 1.4f, 1.7f, new Color(.72f, .76f, .77f, .34f), true);
            }
            else Toggle(steam, false);
        }

        static void Emit(ParticleSystem ps, Vector3 p, Vector3 v, Vector3 size, float life, Color tint, bool rotate)
        {
            ps.Emit(new ParticleSystem.EmitParams { position = p, velocity = v, startSize3D = size, startLifetime = life,
                startColor = tint, rotation = rotate ? Random.Range(0, 360) : 0 }, 1);
        }
        static void Toggle(ParticleSystem ps, bool on)
        {
            if (!ps) return;
            if (on && !ps.isPlaying) ps.Play(false);
            else if (!on && ps.isPlaying) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        public void ClearForRuin()
        {
            if (collapsed) return;
            collapsed = true; heat = lastHeat = steamLeft = flamesDue = smokeDue = embersDue = 0;
            if (building)
            {
                Clear(building.flames); Clear(building.smoke);
                if (building.fireLight) building.fireLight.enabled = false;
            }
            Clear(embers); Clear(steam);
        }
        static void Clear(ParticleSystem ps)
        {
            if (ps) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        void OnDisable()
        {
            if (building) { Toggle(building.flames, false); Toggle(building.smoke, false); }
            Toggle(embers, false); Toggle(steam, false);
        }
        void OnDestroy() { if (fireMaterial) Destroy(fireMaterial); if (smokeMaterial) Destroy(smokeMaterial); }
    }
}
