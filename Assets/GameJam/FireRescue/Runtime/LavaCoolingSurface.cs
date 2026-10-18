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
        public bool HasDirectCooling
        {
            get
            {
                foreach (var patch in patches)
                    if (patch.kind != RegionKind.Downstream && Progress(patch) > 0f) return true;
                return false;
            }
        }
        const int Sides = 16;
        static readonly Vector2[] Edges = MakeEdges();
        enum RegionKind { Water, Foundation, Downstream }
        class Patch
        {
            public RegionKind kind;
            public Vector3 point;
            public float water;
            public BurnableBuilding building;
            public Matrix4x4 worldToFootprint;
            public Vector3 footprintHalfSize;
            public float lowY, highY;
            public float fromDistance, toDistance, progress;
            public MeshRenderer renderer;
            public Mesh mesh;
            public Material material;
            public MeshCollider solid;
        }
        readonly List<Patch> patches = new List<Patch>();
        static readonly List<LavaCoolingSurface> surfaces = new List<LavaCoolingSurface>();
        readonly List<Vector3> polygon = new List<Vector3>(24), clipped = new List<Vector3>(24);
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> indices = new List<int>();
        readonly Dictionary<Vector3, float> arcDistances = new Dictionary<Vector3, float>();
        LavaIgnitionSource source;
        ProgressiveLavaFlow flow;
        ParticleSystem steam;
        float nextSteam, nextMesh;
        bool dirty;
        Matrix4x4 surfaceMatrix;
        float RequiredWater => Mathf.Max(1f, litresPerPatch);

        void Awake() { source = GetComponent<LavaIgnitionSource>(); flow = GetComponent<ProgressiveLavaFlow>(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSurfaces() => surfaces.Clear();
        void OnEnable()
        {
            if (!surfaces.Contains(this)) surfaces.Add(this);
            // A delayed branch still receives the protection of houses watered before it appeared.
            foreach (var building in BurnableBuilding.ActiveBuildings)
                if (building && building.gameObject.scene == gameObject.scene && building.FoundationCooling01 > 0f &&
                    CanOverlapFoundation(building.ignitionBounds))
                {
                    if (building.FoundationSurfaces != null && System.Array.IndexOf(building.FoundationSurfaces, this) < 0)
                    {
                        var registered = new List<LavaCoolingSurface>(building.FoundationSurfaces) { this };
                        building.FoundationSurfaces = registered.ToArray();
                    }
                    SetBuildingFoundation(building, building.FoundationCooling01);
                }
        }
        void OnDisable() => surfaces.Remove(this);

        // Overlapping lava branches share the same solid cover: the glowing mesh below
        // a finished obsidian patch cannot damage someone standing on the visible rock.
        public static bool IsSolidCrustAt(Vector3 point)
        {
            foreach (var surface in surfaces)
                if (surface && surface.isActiveAndEnabled)
                    foreach (var patch in surface.patches)
                    {
                        if (surface.Progress(patch) < 1f || !patch.solid || !patch.solid.enabled) continue;
                        if (patch.solid.Raycast(new Ray(point + Vector3.up * 1.1f, Vector3.down), out var hit, 1.2f) &&
                            hit.point.y >= point.y - .05f) return true;
                    }
            return false;
        }
        void LateUpdate()
        {
            if (patches.Count == 0 || !source || !source.heatCollider) return;
            if (source.heatCollider.transform.localToWorldMatrix != surfaceMatrix) dirty = true;
            if (dirty && Time.time >= nextMesh) RefreshCrust();
        }
        public void NotifySurfaceChanged() => dirty = true;
        public bool IsHotAt(Vector3 point) => CoolingAt(point) < 1f;
        public float DirectCoolingAt(Vector3 point)
        {
            float cooled = 0;
            foreach (var p in patches)
                if (p.kind != RegionKind.Downstream && ContainsRegion(p, point))
                    cooled = Mathf.Max(cooled, Progress(p));
            return cooled;
        }
        public float CoolingAt(Vector3 point)
        {
            float cooled = DirectCoolingAt(point);
            foreach (var p in patches)
                if (p.kind == RegionKind.Downstream && ContainsRegion(p, point))
                    cooled = Mathf.Max(cooled, p.progress);
            return cooled;
        }
        /// <summary>Retains every cold reach when another dam cuts the supply farther upstream.</summary>
        public void SetDownstreamCooling(float fromDistance, float toDistance, float progress)
        {
            if (!float.IsFinite(fromDistance) || !float.IsFinite(toDistance) || !float.IsFinite(progress) ||
                toDistance <= fromDistance || progress <= 0f) return;
            if (!flow) flow = GetComponent<ProgressiveLavaFlow>();
            if (!flow) return;
            fromDistance = Mathf.Max(0, fromDistance); progress = Mathf.Clamp01(progress);
            Patch region = null;
            foreach (var p in patches)
            {
                if (p.kind != RegionKind.Downstream) continue;
                if (p.fromDistance <= fromDistance + .001f && p.toDistance >= toDistance - .001f && p.progress >= progress)
                    return;
                if (Mathf.Abs(p.fromDistance - fromDistance) < .001f && Mathf.Abs(p.toDistance - toDistance) < .001f)
                    region = p;
            }
            if (region == null)
            {
                region = new Patch { kind = RegionKind.Downstream, fromDistance = fromDistance, toDistance = toDistance };
                patches.Add(region); CreateCrust(region);
            }
            if (progress <= region.progress) return;
            region.progress = progress;
            // Equal-temperature touching reaches can share one continuous mesh. Different
            // temperatures stay separate so an old cold reach is never reheated by a new dam.
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                Patch other = patches[i];
                if (other == region || other.kind != RegionKind.Downstream ||
                    Mathf.Abs(other.progress - region.progress) > .00001f ||
                    other.toDistance < region.fromDistance || other.fromDistance > region.toDistance) continue;
                region.fromDistance = Mathf.Min(region.fromDistance, other.fromDistance);
                region.toDistance = Mathf.Max(region.toDistance, other.toDistance);
                DisposePatch(other); patches.RemoveAt(i);
            }
            UpdateHeat(region); dirty = true;
            if (progress >= 1f) RefreshCrust();
        }
        /// <summary>Uses the water already delivered to a house; never consumes another tank litre.</summary>
        public static void CoolBuildingFoundation(BurnableBuilding building, float progress)
        {
            if (!building || !building.ignitionBounds || progress <= 0f) return;
            // Include unrevealed and inactive branches so their future mesh inherits this footprint.
            if (building.FoundationSurfaces == null)
            {
                var relevant = new List<LavaCoolingSurface>();
                foreach (var surface in FindObjectsByType<LavaCoolingSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (surface.gameObject.scene == building.gameObject.scene && surface.CanOverlapFoundation(building.ignitionBounds))
                        relevant.Add(surface);
                building.FoundationSurfaces = relevant.ToArray();
            }
            foreach (var surface in building.FoundationSurfaces)
                if (surface)
                    surface.SetBuildingFoundation(building, Mathf.Clamp01(progress));
        }
        bool CanOverlapFoundation(BoxCollider box)
        {
            if (!box) return false;
            // BK retains the complete authored route while the visible MeshFilter is progressively
            // shortened. Never reject a future reach using the current front's smaller bounds.
            var lava = GetComponent<BKPureNature.BK_Lava>();
            Mesh fullMesh = lava ? lava.m_Mesh : null;
            Transform meshTransform = lava ? lava.transform : transform;
            if (!lava)
            {
                var ignition = GetComponent<LavaIgnitionSource>();
                if (ignition && ignition.heatCollider is MeshCollider collider)
                { fullMesh = collider.sharedMesh; meshTransform = collider.transform; }
            }
            if (!flow) flow = GetComponent<ProgressiveLavaFlow>();
            Bounds world;
            if (flow && flow.IsInitialized) world = flow.FullWorldBounds;
            else
            {
                if (!fullMesh) return true;
                Bounds local = fullMesh.bounds;
                world = new Bounds(meshTransform.TransformPoint(local.center), Vector3.zero);
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    world.Encapsulate(meshTransform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))));
            }
            Bounds house = box.bounds;
            Vector3 scale = box.transform.lossyScale;
            Vector3 marginX = box.transform.TransformVector(new Vector3(1.5f / Mathf.Max(.001f, Mathf.Abs(scale.x)), 0, 0));
            Vector3 marginZ = box.transform.TransformVector(new Vector3(0, 0, 1.5f / Mathf.Max(.001f, Mathf.Abs(scale.z))));
            house.Expand(new Vector3((Mathf.Abs(marginX.x) + Mathf.Abs(marginZ.x)) * 2f, 0,
                (Mathf.Abs(marginX.z) + Mathf.Abs(marginZ.z)) * 2f));
            return world.min.x <= house.max.x && world.max.x >= house.min.x &&
                world.min.z <= house.max.z && world.max.z >= house.min.z;
        }
        void SetBuildingFoundation(BurnableBuilding building, float progress)
        {
            if (!building.ignitionBounds) return;
            Patch region = null;
            foreach (var p in patches)
                if (p.kind == RegionKind.Foundation && p.building == building) { region = p; break; }
            if (region != null && region.progress >= progress) return;
            if (region == null)
            {
                region = new Patch { kind = RegionKind.Foundation, building = building };
                patches.Add(region); CreateCrust(region);
            }
            var box = building.ignitionBounds;
            Vector3 scale = box.transform.lossyScale;
            region.worldToFootprint = Matrix4x4.Translate(-box.center) * box.transform.worldToLocalMatrix;
            region.footprintHalfSize = box.size * .5f + new Vector3(1.5f / Mathf.Max(.001f, Mathf.Abs(scale.x)), 0,
                1.5f / Mathf.Max(.001f, Mathf.Abs(scale.z)));
            // Only the contact-height slab is protected, never another river overhead.
            region.lowY = box.bounds.min.y - .25f;
            region.highY = box.bounds.min.y + 1.5f;
            region.progress = Mathf.Max(region.progress, progress);
            UpdateHeat(region); dirty = true;
            if (!source) source = GetComponent<LavaIgnitionSource>();
            if (!flow) flow = GetComponent<ProgressiveLavaFlow>();
            if (flow)
            {
                flow.NotifyCoolingChanged();
                if (region.progress >= 1f) flow.RefreshCoolingBarrier();
            }
            if (region.progress >= 1f) RefreshCrust();
        }
        float Progress(Patch p) => p.kind == RegionKind.Water ? Mathf.Clamp01(p.water / RequiredWater) : p.progress;
        bool ContainsRegion(Patch p, Vector3 point)
        {
            if (p.kind == RegionKind.Water)
                return Contains(p.point, point, Mathf.Max(1f, patchRadius) * Mathf.Sqrt(Progress(p)));
            if (p.kind == RegionKind.Foundation)
            {
                Vector3 local = p.worldToFootprint.MultiplyPoint3x4(point);
                return Mathf.Abs(local.x) <= p.footprintHalfSize.x && Mathf.Abs(local.z) <= p.footprintHalfSize.z &&
                    point.y >= p.lowY && point.y <= p.highY;
            }
            if (!flow || !flow.IsInitialized) return false;
            float distance = flow.DistanceAtPoint(point);
            return distance >= p.fromDistance - .001f && distance <= p.toDistance + .001f;
        }
        public void ApplyWater(Vector3 point, float litres)
        {
            if (!source) source = GetComponent<LavaIgnitionSource>();
            if (!source || !source.IsHotSurfaceActive || !float.IsFinite(litres) || litres <= 0 ||
                !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return;
            // Continuing to spray cold rock cannot create a new hot patch over it.
            if (!IsHotAt(point)) return;
            Patch patch = null;
            int waterPatchCount = 0;
            float nearest = patchRadius * patchRadius * .42f;
            foreach (var p in patches)
            {
                if (p.kind != RegionKind.Water) continue;
                waterPatchCount++;
                float d = FlatSqr(p.point, point);
                if (p.water < RequiredWater && d < nearest) { patch = p; nearest = d; }
            }
            if (patch == null)
            {
                if (waterPatchCount >= 128) return;
                patch = new Patch { point = point };
                patches.Add(patch);
                CreateCrust(patch);
            }
            patch.water = Mathf.Min(RequiredWater, patch.water + litres);
            float t = Mathf.Clamp01(patch.water / RequiredWater);
            UpdateHeat(patch);
            dirty = true;
            if (!flow) flow = GetComponent<ProgressiveLavaFlow>();
            if (flow) flow.NotifyCoolingChanged();
            if (Time.time >= nextSteam) { nextSteam = Time.time + .08f; EmitSteam(point); }
            if (t >= 1f)
            {
                CooledPatchCount++;
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
            if (lowX > highX || lowZ > highZ) return false;
            for (int x = 0; x <= 8; x++) for (int z = 0; z <= 8; z++)
            {
                Vector3 point = new Vector3(Mathf.Lerp(lowX, highX, x / 8f), lava.max.y + 2, Mathf.Lerp(lowZ, highZ, z / 8f));
                if (source.heatCollider.Raycast(new Ray(point, Vector3.down), out var hit, lava.size.y + 4) &&
                    hit.point.y >= building.min.y - .2f && hit.point.y <= building.max.y && source.IsHotAt(hit.point)) return true;
            }
            return false;
        }
        void CreateCrust(Patch patch)
        {
            string label = patch.kind == RegionKind.Foundation ? "Water-cooled foundation obsidian" :
                patch.kind == RegionKind.Downstream ? "Unfed lava solidifying into obsidian" : "Water-cooled obsidian";
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            patch.mesh = new Mesh { name = "Obsidian clipped to lava surface", indexFormat = IndexFormat.UInt32 };
            patch.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = patch.mesh;
            patch.renderer = go.AddComponent<MeshRenderer>();
            patch.solid = go.AddComponent<MeshCollider>();
            patch.solid.enabled = false;
            patch.renderer.shadowCastingMode = ShadowCastingMode.Off;
            patch.material = crustMaterial ? new Material(crustMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            // This cover replaces the visible hot triangles, including their edges.
            if (patch.material.HasProperty("_Surface")) patch.material.SetFloat("_Surface", 0f);
            patch.material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            patch.material.EnableKeyword("_EMISSION");
            patch.renderer.sharedMaterial = patch.material;
            UpdateHeat(patch);
        }
        void UpdateHeat(Patch patch)
        {
            if (!patch.material) return;
            float progress = Progress(patch);
            // Retain the authored volcanic albedo and normals; the final glass emits no light.
            patch.material.SetColor("_BaseColor", Color.Lerp(new Color(.55f, .32f, .22f), coldTint, progress));
            patch.material.SetColor("_EmissionColor", new Color(3f, .35f, .018f) * (1 - progress) * (1 - progress));
        }
        float ClipSide(Patch patch, Vector3 point, int plane)
        {
            if (patch.kind == RegionKind.Water)
                return Side(point, patch.point, Edges[plane]) - Mathf.Max(1f, patchRadius) * Mathf.Sqrt(Progress(patch));
            if (patch.kind == RegionKind.Downstream)
            {
                if (!arcDistances.TryGetValue(point, out float distance))
                {
                    distance = flow.DistanceAtPoint(point);
                    arcDistances.Add(point, distance);
                }
                return plane == 0 ? patch.fromDistance - distance : distance - patch.toDistance;
            }
            Vector3 local = patch.worldToFootprint.MultiplyPoint3x4(point);
            switch (plane)
            {
                case 0: return local.x - patch.footprintHalfSize.x;
                case 1: return -local.x - patch.footprintHalfSize.x;
                case 2: return local.z - patch.footprintHalfSize.z;
                case 3: return -local.z - patch.footprintHalfSize.z;
                case 4: return patch.lowY - point.y;
                default: return point.y - patch.highY;
            }
        }
        /// <summary>Clip the actual lava triangles so neither hot seams nor overhanging disks remain.</summary>
        public void RefreshCrust()
        {
            dirty = false; nextMesh = Time.time + Mathf.Max(.05f, meshRefreshInterval);
            if (!source) source = GetComponent<LavaIgnitionSource>();
            if (!source || !(source.heatCollider is MeshCollider collider) || !collider.sharedMesh)
            {
                foreach (var patch in patches)
                {
                    if (patch.renderer) patch.renderer.enabled = false;
                    if (patch.solid) patch.solid.enabled = false;
                }
                return;
            }
            Mesh mesh = collider.sharedMesh;
            if (!mesh.isReadable) return;
            surfaceMatrix = collider.transform.localToWorldMatrix;
            var points = mesh.vertices;
            for (int i = 0; i < points.Length; i++) points[i] = surfaceMatrix.MultiplyPoint3x4(points[i]);
            var triangles = mesh.triangles;
            arcDistances.Clear();
            foreach (var patch in patches)
            {
                vertices.Clear(); uv.Clear(); indices.Clear();
                float progress = Progress(patch);
                float radius = Mathf.Max(1f, patchRadius) * Mathf.Sqrt(progress);
                float boundsRadius = radius * 1.03f;
                var toLocal = patch.renderer.transform.worldToLocalMatrix;
                int planeCount = patch.kind == RegionKind.Water ? Sides : patch.kind == RegionKind.Foundation ? 6 : 2;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];
                    if (patch.kind == RegionKind.Water &&
                        (Mathf.Min(a.x, b.x, c.x) > patch.point.x + boundsRadius || Mathf.Max(a.x, b.x, c.x) < patch.point.x - boundsRadius ||
                        Mathf.Min(a.z, b.z, c.z) > patch.point.z + boundsRadius || Mathf.Max(a.z, b.z, c.z) < patch.point.z - boundsRadius)) continue;
                    polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c);
                    for (int plane = 0; plane < planeCount; plane++)
                    {
                        if (polygon.Count < 3) break;
                        clipped.Clear();
                        Vector3 previous = polygon[polygon.Count - 1];
                        float previousSide = ClipSide(patch, previous, plane);
                        foreach (Vector3 current in polygon)
                        {
                            float currentSide = ClipSide(patch, current, plane);
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
                patch.renderer.enabled = indices.Count > 0 && collider.enabled &&
                    (!source.surfaceRenderer || source.surfaceRenderer.enabled);
                // Keep the visible cooled surface and the walkable/wheel surface identical.
                if (patch.solid)
                {
                    patch.solid.sharedMesh = null;
                    patch.solid.enabled = progress >= 1f && patch.renderer.enabled;
                    if (patch.solid.enabled) patch.solid.sharedMesh = patch.mesh;
                }
            }
        }
        public void ResetCooling()
        {
            foreach (var p in patches) DisposePatch(p);
            patches.Clear(); CooledPatchCount = 0; dirty = false; nextMesh = nextSteam = 0;
            if (steam) steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        static void DisposePatch(Patch p)
        {
            if (p.solid) p.solid.enabled = false;
            if (p.renderer) { p.renderer.enabled = false; Release(p.renderer.gameObject); }
            Release(p.mesh); Release(p.material);
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
