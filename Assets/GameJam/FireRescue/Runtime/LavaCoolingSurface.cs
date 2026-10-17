using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>Physical water turns local lava into permanent, textured volcanic glass.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(LavaIgnitionSource))]
    public sealed class LavaCoolingSurface : MonoBehaviour
    {
        [Min(1f)] public float patchRadius = 13f;
        [Min(1f)] public float litresPerPatch = 90f;
        public Material crustMaterial;
        public Material steamMaterial;
        [Min(.1f)] public float textureMetres = 8f;
        [Min(.02f)] public float crustThickness = .16f;
        [Min(.05f)] public float meshRefreshInterval = .12f;
        public Color coldTint = new Color(.38f, .43f, .48f, 1f);
        public int CooledPatchCount { get; private set; }
        public bool BlocksFlow => flow && flow.StoppedByCooling;
        const int Sides = 16;
        static readonly Vector2[] Edges = MakeEdges();
        class Patch
        {
            public Vector3 point;
            public float water;
            public MeshRenderer renderer;
            public Mesh mesh;
            public Material material;
        }
        readonly List<Patch> patches = new List<Patch>();
        readonly List<Vector3> polygon = new List<Vector3>(24), clipped = new List<Vector3>(24);
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> indices = new List<int>();
        LavaIgnitionSource source;
        ProgressiveLavaFlow flow;
        ParticleSystem steam;
        float nextSteam, nextMesh;
        bool dirty;
        Matrix4x4 surfaceMatrix;
        float RequiredWater => Mathf.Max(1f, litresPerPatch);

        void Awake() { source = GetComponent<LavaIgnitionSource>(); flow = GetComponent<ProgressiveLavaFlow>(); }
        void LateUpdate()
        {
            if (patches.Count == 0 || !source || !source.heatCollider) return;
            if (source.heatCollider.transform.localToWorldMatrix != surfaceMatrix) dirty = true;
            if (dirty && Time.time >= nextMesh) RefreshCrust();
        }
        public void NotifySurfaceChanged() => dirty = true;
        public bool IsHotAt(Vector3 point) => CoolingAt(point) < 1f;
        public float CoolingAt(Vector3 point)
        {
            float cooled = 0;
            foreach (var p in patches)
                if (Contains(p.point, point, Mathf.Max(1f, patchRadius) * .98f))
                    cooled = Mathf.Max(cooled, Mathf.Clamp01(p.water / RequiredWater));
            return cooled;
        }
        public void ApplyWater(Vector3 point, float litres)
        {
            if (!source) source = GetComponent<LavaIgnitionSource>();
            if (!source || !source.IsHotSurfaceActive || !float.IsFinite(litres) || litres <= 0 ||
                !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return;
            // Continuing to spray cold rock cannot create a new hot patch over it.
            if (!IsHotAt(point)) return;
            Patch patch = null;
            float nearest = patchRadius * patchRadius * .42f;
            foreach (var p in patches)
            {
                float d = FlatSqr(p.point, point);
                if (p.water < RequiredWater && d < nearest) { patch = p; nearest = d; }
            }
            if (patch == null)
            {
                if (patches.Count >= 128) return;
                patch = new Patch { point = point };
                patches.Add(patch);
                CreateCrust(patch);
            }
            patch.water = Mathf.Min(RequiredWater, patch.water + litres);
            float t = Mathf.Clamp01(patch.water / RequiredWater);
            if (patch.material)
            {
                // Preserve the pack's albedo and normal maps. Only the remaining heat fades.
                patch.material.SetColor("_BaseColor", Color.Lerp(new Color(.55f, .32f, .22f), coldTint, t));
                patch.material.SetColor("_EmissionColor", new Color(3f, .35f, .018f) * (1 - t) * (1 - t));
            }
            dirty = true;
            if (Time.time >= nextSteam) { nextSteam = Time.time + .08f; EmitSteam(point); }
            if (t >= 1f)
            {
                CooledPatchCount++;
                if (!flow) flow = GetComponent<ProgressiveLavaFlow>();
                if (flow) flow.RefreshCoolingBarrier();
                RefreshCrust();
            }
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
        void CreateCrust(Patch patch)
        {
            var go = new GameObject("Water-cooled obsidian");
            go.transform.SetParent(transform, false);
            patch.mesh = new Mesh { name = "Obsidian clipped to lava surface", indexFormat = IndexFormat.UInt32 };
            patch.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = patch.mesh;
            patch.renderer = go.AddComponent<MeshRenderer>();
            patch.renderer.shadowCastingMode = ShadowCastingMode.Off;
            patch.material = crustMaterial ? new Material(crustMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            patch.material.EnableKeyword("_EMISSION");
            patch.renderer.sharedMaterial = patch.material;
        }
        /// <summary>Clip the actual lava triangles so neither hot seams nor overhanging disks remain.</summary>
        public void RefreshCrust()
        {
            dirty = false; nextMesh = Time.time + Mathf.Max(.05f, meshRefreshInterval);
            if (!source || !(source.heatCollider is MeshCollider collider) || !collider.sharedMesh) return;
            Mesh mesh = collider.sharedMesh;
            if (!mesh.isReadable) return;
            surfaceMatrix = collider.transform.localToWorldMatrix;
            var points = mesh.vertices;
            for (int i = 0; i < points.Length; i++) points[i] = surfaceMatrix.MultiplyPoint3x4(points[i]);
            var triangles = mesh.triangles;
            foreach (var patch in patches)
            {
                vertices.Clear(); uv.Clear(); indices.Clear();
                float progress = Mathf.Clamp01(patch.water / RequiredWater);
                float radius = Mathf.Max(1f, patchRadius) * Mathf.Sqrt(progress);
                float boundsRadius = radius * 1.03f;
                var toLocal = patch.renderer.transform.worldToLocalMatrix;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];
                    if (Mathf.Min(a.x, b.x, c.x) > patch.point.x + boundsRadius || Mathf.Max(a.x, b.x, c.x) < patch.point.x - boundsRadius ||
                        Mathf.Min(a.z, b.z, c.z) > patch.point.z + boundsRadius || Mathf.Max(a.z, b.z, c.z) < patch.point.z - boundsRadius) continue;
                    polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c);
                    foreach (Vector2 edge in Edges)
                    {
                        if (polygon.Count < 3) break;
                        clipped.Clear();
                        Vector3 previous = polygon[polygon.Count - 1];
                        float previousSide = Side(previous, patch.point, edge) - radius;
                        foreach (Vector3 current in polygon)
                        {
                            float currentSide = Side(current, patch.point, edge) - radius;
                            if ((previousSide <= 0) != (currentSide <= 0))
                                clipped.Add(Vector3.LerpUnclamped(previous, current, previousSide / (previousSide - currentSide)));
                            if (currentSide <= 0) clipped.Add(current);
                            previous = current; previousSide = currentSide;
                        }
                        polygon.Clear(); polygon.AddRange(clipped);
                    }
                    if (polygon.Count < 3) continue;
                    int start = vertices.Count;
                    foreach (Vector3 point in polygon)
                    {
                        // World UVs keep neighbouring patches aligned; normals show real rock relief.
                        uv.Add(new Vector2(point.x, point.z) / Mathf.Max(.1f, textureMetres));
                        float relief = Mathf.PerlinNoise(point.x * .6f, point.z * .6f) * .035f;
                        // Finished rock sits above any overlapping patch still shedding heat.
                        vertices.Add(toLocal.MultiplyPoint3x4(point + Vector3.up * (crustThickness + relief + (progress >= 1f ? .045f : 0f))));
                    }
                    for (int j = 1; j < polygon.Count - 1; j++) { indices.Add(start); indices.Add(start + j); indices.Add(start + j + 1); }
                    // Seal the thin crust's sides: raised rock must not reveal a glowing strip underneath.
                    for (int j = 0; j < polygon.Count; j++)
                    {
                        int k = (j + 1) % polygon.Count, edgeStart = vertices.Count;
                        float width = Vector3.Distance(polygon[j], polygon[k]) / Mathf.Max(.1f, textureMetres);
                        vertices.Add(vertices[start + j]); vertices.Add(toLocal.MultiplyPoint3x4(polygon[j] - Vector3.up * .02f));
                        vertices.Add(vertices[start + k]); vertices.Add(toLocal.MultiplyPoint3x4(polygon[k] - Vector3.up * .02f));
                        uv.Add(new Vector2(0, .05f)); uv.Add(Vector2.zero); uv.Add(new Vector2(width, .05f)); uv.Add(new Vector2(width, 0));
                        indices.Add(edgeStart); indices.Add(edgeStart + 1); indices.Add(edgeStart + 2);
                        indices.Add(edgeStart + 2); indices.Add(edgeStart + 1); indices.Add(edgeStart + 3);
                    }
                }
                patch.mesh.Clear(); patch.mesh.SetVertices(vertices); patch.mesh.SetUVs(0, uv); patch.mesh.SetTriangles(indices, 0);
                patch.mesh.RecalculateNormals(); patch.mesh.RecalculateTangents(); patch.mesh.RecalculateBounds();
            }
        }
        public void ResetCooling()
        {
            foreach (var p in patches)
            {
                if (p.renderer) { p.renderer.enabled = false; Release(p.renderer.gameObject); }
                Release(p.mesh); Release(p.material);
            }
            patches.Clear(); CooledPatchCount = 0; dirty = false; nextMesh = nextSteam = 0;
            if (steam) steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        void EmitSteam(Vector3 point)
        {
            if (!steam)
            {
                var go = new GameObject("Water on lava · steam"); go.transform.SetParent(transform, false);
                steam = go.AddComponent<ParticleSystem>(); steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = steam.main; main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.7f, 1.5f); main.startSize = new ParticleSystem.MinMaxCurve(1, 2.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2, 4); main.startColor = new Color(.8f, .85f, .88f, .45f); main.maxParticles = 180;
                var emission = steam.emission; emission.enabled = false;
                var shape = steam.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 22; shape.radius = 1;
                var size = steam.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .35f, 1, 2));
                var colors = steam.colorOverLifetime; colors.enabled = true; var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.55f, .15f), new GradientAlphaKey(0, 1) }); colors.color = gradient;
                var renderer = go.GetComponent<ParticleSystemRenderer>(); if (steamMaterial) renderer.sharedMaterial = steamMaterial;
            }
            steam.transform.SetPositionAndRotation(point + Vector3.up * .2f, Quaternion.Euler(-90, 0, 0));
            if (!steam.isPlaying) steam.Play();
            steam.Emit(4);
        }
        static Vector2[] MakeEdges()
        {
            var edges = new Vector2[Sides];
            for (int i = 0; i < Sides; i++) { float a = i * Mathf.PI * 2 / Sides; edges[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
            return edges;
        }
        static bool Contains(Vector3 centre, Vector3 point, float radius)
        {
            if (FlatSqr(centre, point) > radius * radius * 1.05f) return false;
            foreach (Vector2 edge in Edges) if (Side(point, centre, edge) > radius) return false;
            return true;
        }
        static float Side(Vector3 point, Vector3 centre, Vector2 edge) => (point.x - centre.x) * edge.x + (point.z - centre.z) * edge.y;
        static float FlatSqr(Vector3 a, Vector3 b) { a.y = b.y = 0; return (a - b).sqrMagnitude; }
        static void Release(Object value) { if (!value) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        void OnDestroy() => ResetCooling();
    }
}
