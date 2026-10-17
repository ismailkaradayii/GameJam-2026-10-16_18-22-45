using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Water cools a local section permanently. Uncooled lava remains hazardous.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(LavaIgnitionSource))]
    public sealed class LavaCoolingSurface : MonoBehaviour
    {
        public float patchRadius = 13f;
        public float litresPerPatch = 120f;
        public Material crustMaterial;
        public Material steamMaterial;
        public int CooledPatchCount { get; private set; }
        class Patch { public Vector3 point; public float water; public MeshRenderer renderer; public Mesh mesh; public Material material; }
        readonly List<Patch> patches = new List<Patch>();
        LavaIgnitionSource source;
        ParticleSystem steam;
        float nextSteam;
        void Awake() => source = GetComponent<LavaIgnitionSource>();
        public bool IsHotAt(Vector3 point)
        {
            foreach (var p in patches)
                if (p.water >= litresPerPatch && FlatSqr(p.point, point) < patchRadius * patchRadius * .88f) return false;
            return true;
        }
        public float CoolingAt(Vector3 point)
        {
            float cooled = 0;
            foreach (var p in patches) if (FlatSqr(p.point, point) < patchRadius * patchRadius * .88f)
                cooled = Mathf.Max(cooled, Mathf.Clamp01(p.water / litresPerPatch));
            return cooled;
        }
        public void ApplyWater(Vector3 point, float litres)
        {
            if (!source) source = GetComponent<LavaIgnitionSource>();
            if (!source || !source.IsHotSurfaceActive || !float.IsFinite(litres) || litres <= 0) return;
            Patch patch = null;
            foreach (var p in patches) if (FlatSqr(p.point, point) < patchRadius * patchRadius * .4f) { patch = p; break; }
            if (patch == null)
            {
                // The finite map cannot accumulate unbounded mesh objects from spraying.
                if (patches.Count >= 128) return;
                patch = new Patch { point = point };
                patches.Add(patch);
                BuildCrust(patch);
            }
            bool cold = patch.water >= litresPerPatch;
            patch.water = Mathf.Min(litresPerPatch, patch.water + litres);
            if (!cold && patch.water >= litresPerPatch) CooledPatchCount++;
            float t = Mathf.Clamp01(patch.water / litresPerPatch);
            if (patch.material)
            {
                patch.material.color = Color.Lerp(new Color(.3f, .075f, .025f), new Color(.045f, .047f, .05f), t);
                patch.material.SetColor("_EmissionColor", new Color(1.5f, .2f, .01f) * (1 - t) * (1 - t));
            }
            if (!cold && Time.time >= nextSteam)
            { nextSteam = Time.time + .06f; EmitSteam(point); }
        }
        public bool HasHotContact(Bounds building)
        {
            if (!source || !source.heatCollider) return false;
            Bounds lava = source.heatCollider.bounds;
            float lowX = Mathf.Max(building.min.x, lava.min.x), highX = Mathf.Min(building.max.x, lava.max.x);
            float lowZ = Mathf.Max(building.min.z, lava.min.z), highZ = Mathf.Min(building.max.z, lava.max.z);
            for (int x = 0; x <= 8; x++) for (int z = 0; z <= 8; z++)
            {
                Vector3 point = new Vector3(Mathf.Lerp(lowX, highX, x / 8f), lava.max.y + 2, Mathf.Lerp(lowZ, highZ, z / 8f));
                if (source.heatCollider.Raycast(new Ray(point, Vector3.down), out var hit, lava.size.y + 4) &&
                    hit.point.y >= building.min.y - .2f && hit.point.y <= building.max.y && IsHotAt(hit.point)) return true;
            }
            return false;
        }
        void BuildCrust(Patch patch)
        {
            var go = new GameObject("Water-cooled lava crust"); go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;
            int n = 15; var vertices = new Vector3[n * n]; var valid = new bool[n * n]; var triangles = new List<int>();
            Bounds bounds = source.heatCollider.bounds;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                Vector3 p = patch.point + new Vector3((x / (n - 1f) * 2 - 1) * patchRadius, 0, (z / (n - 1f) * 2 - 1) * patchRadius);
                p.y = bounds.max.y + 2;
                if (FlatSqr(p, patch.point) <= patchRadius * patchRadius && source.heatCollider.Raycast(new Ray(p, Vector3.down), out var hit, bounds.size.y + 4))
                { vertices[i] = go.transform.InverseTransformPoint(hit.point + Vector3.up * .09f); valid[i] = true; }
            }
            for (int z = 0; z < n - 1; z++) for (int x = 0; x < n - 1; x++)
            {
                int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                if (valid[a] && valid[c] && valid[b]) { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
                if (valid[b] && valid[c] && valid[d]) { triangles.Add(b); triangles.Add(c); triangles.Add(d); }
            }
            patch.mesh = new Mesh { name = "Cooled lava patch" }; patch.mesh.vertices = vertices; patch.mesh.SetTriangles(triangles, 0); patch.mesh.RecalculateNormals(); patch.mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = patch.mesh;
            patch.renderer = go.AddComponent<MeshRenderer>();
            patch.material = crustMaterial ? new Material(crustMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            patch.material.EnableKeyword("_EMISSION"); patch.material.SetFloat("_Smoothness", .14f);
            patch.renderer.sharedMaterial = patch.material;
        }
        void EmitSteam(Vector3 point)
        {
            if (!steam)
            {
                var go = new GameObject("Water on lava · steam"); go.transform.SetParent(transform, false);
                steam = go.AddComponent<ParticleSystem>(); steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = steam.main; main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f); main.startSize = new ParticleSystem.MinMaxCurve(2, 4);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3, 7); main.startColor = new Color(.7f, .78f, .8f, .65f); main.maxParticles = 240;
                var emission = steam.emission; emission.enabled = false;
                var shape = steam.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 22; shape.radius = 2;
                var size = steam.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .35f, 1, 2));
                var colors = steam.colorOverLifetime; colors.enabled = true; var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.7f, .15f), new GradientAlphaKey(0, 1) }); colors.color = gradient;
                var renderer = go.GetComponent<ParticleSystemRenderer>(); if (steamMaterial) renderer.sharedMaterial = steamMaterial;
            }
            steam.transform.SetPositionAndRotation(point + Vector3.up * .2f, Quaternion.Euler(-90, 0, 0)); steam.Emit(5);
        }
        static float FlatSqr(Vector3 a, Vector3 b) { a.y = b.y = 0; return (a - b).sqrMagnitude; }
        void OnDestroy() { foreach (var p in patches) { if (p.mesh) Destroy(p.mesh); if (p.material) Destroy(p.material); } }
    }
}
