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
        public int CrustMeshRebuildCount { get; private set; }
        public int CrustColliderCookCount { get; private set; }
        public int SourceMeshReadCount { get; private set; }
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
            public Bounds regionBounds;
            public bool hasRegionBounds;
            public float fromDistance, toDistance, progress;
            public MeshRenderer renderer;
            public Mesh mesh;
            public Material material;
            public MeshCollider solid;
            public bool geometryDirty = true;
            public readonly List<int> candidateTriangles = new List<int>();
            public bool candidatesDirty = true;
            public float candidateWaterRadius;
        }
        readonly List<Patch> patches = new List<Patch>();
        static readonly List<LavaCoolingSurface> surfaces = new List<LavaCoolingSurface>();
        readonly List<Vector3> polygon = new List<Vector3>(24), clipped = new List<Vector3>(24);
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> indices = new List<int>();
        readonly Dictionary<Vector3, float> arcDistances = new Dictionary<Vector3, float>();
        readonly List<Vector3> sourcePoints = new List<Vector3>();
        readonly List<int> sourceTriangles = new List<int>();
        readonly List<int> submeshTriangles = new List<int>();
        readonly List<Bounds> sourceTriangleBounds = new List<Bounds>();
        Mesh cachedSurfaceMesh;
        LavaIgnitionSource source;
        ProgressiveLavaFlow flow;
        ParticleSystem steam;
        float nextSteam, nextMesh;
        bool dirty, sourceGeometryDirty = true, sourceWasVisible;
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
                        Bounds bounds = patch.solid.bounds;
                        if (point.x < bounds.min.x || point.x > bounds.max.x ||
                            point.z < bounds.min.z || point.z > bounds.max.z ||
                            bounds.max.y < point.y - .05f || bounds.min.y > point.y + 1.1f) continue;
                        if (patch.solid.Raycast(new Ray(point + Vector3.up * 1.1f, Vector3.down), out var hit, 1.2f) &&
                            hit.point.y >= point.y - .05f) return true;
                    }
            return false;
        }
        void LateUpdate()
        {
            if (patches.Count == 0) return;
            if (!source) source = GetComponent<LavaIgnitionSource>();
            if (!source || !source.heatCollider)
            {
                if (sourceWasVisible) { NotifySurfaceChanged(); RefreshCrust(); }
                return;
            }
            Mesh current = source.heatCollider is MeshCollider collider ? collider.sharedMesh : null;
            bool visible = source.heatCollider.enabled && (!source.surfaceRenderer || source.surfaceRenderer.enabled);
            if (current != cachedSurfaceMesh || visible != sourceWasVisible ||
                source.heatCollider.transform.localToWorldMatrix != surfaceMatrix) NotifySurfaceChanged();
            if (dirty && Time.time >= nextMesh) RefreshCrust();
        }
        public void NotifySurfaceChanged()
        {
            sourceGeometryDirty = true;
            foreach (var patch in patches) InvalidateGeometry(patch);
        }
        /// <summary>A growing front changes only its old partial cell and the newly revealed cells.</summary>
        public void NotifySurfaceChanged(Bounds changedBounds)
        {
            // Even an untouched cover must read the latest lava when a new region is added later.
            sourceGeometryDirty = true;
            foreach (var patch in patches)
            {
                if (patch.kind == RegionKind.Downstream)
                {
                    // Arc clipping uses the nearest centreline, which can differ from authored
                    // row distance on a hairpin. Keep residual reaches conservative.
                    InvalidateGeometry(patch);
                    continue;
                }
                if (patch.kind == RegionKind.Water)
                {
                    float radius = Mathf.Max(1f, patchRadius) * Mathf.Sqrt(Progress(patch)) * 1.03f;
                    if (changedBounds.min.x <= patch.point.x + radius && changedBounds.max.x >= patch.point.x - radius &&
                        changedBounds.min.z <= patch.point.z + radius && changedBounds.max.z >= patch.point.z - radius)
                        InvalidateGeometry(patch);
                    continue;
                }
                if (changedBounds.max.y < patch.lowY || changedBounds.min.y > patch.highY) continue;
                // Transform the changed cells' bounds, not the currently clipped cover: a protected
                // foundation can be empty until the front first reaches it. This is conservative
                // for rotated/tilted boxes and curved routes, so it never misses newly arriving lava.
                Bounds local = new Bounds(patch.worldToFootprint.MultiplyPoint3x4(changedBounds.center), Vector3.zero);
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    local.Encapsulate(patch.worldToFootprint.MultiplyPoint3x4(changedBounds.center +
                        Vector3.Scale(changedBounds.extents, new Vector3(x, y, z))));
                if (local.min.x <= patch.footprintHalfSize.x && local.max.x >= -patch.footprintHalfSize.x &&
                    local.min.z <= patch.footprintHalfSize.z && local.max.z >= -patch.footprintHalfSize.z)
                    InvalidateGeometry(patch);
            }
        }
        void InvalidateGeometry(Patch patch) { patch.geometryDirty = true; dirty = true; }
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
            bool becomesSolid = region.progress < 1f && progress >= 1f;
            region.progress = progress;
            // Keep warming reaches independent: the flow continues updating their original
            // ranges every frame. Merging them early would recreate those subranges next tick.
            // Finished touching reaches can share a mesh without losing their update identity.
            for (int i = patches.Count - 1; progress >= 1f && i >= 0; i--)
            {
                Patch other = patches[i];
                if (other == region || other.kind != RegionKind.Downstream ||
                    other.progress < 1f ||
                    other.toDistance < region.fromDistance || other.fromDistance > region.toDistance) continue;
                region.fromDistance = Mathf.Min(region.fromDistance, other.fromDistance);
                region.toDistance = Mathf.Max(region.toDistance, other.toDistance);
                InvalidateGeometry(region);
                DisposePatch(other); patches.RemoveAt(i);
            }
            UpdateHeat(region);
            if (becomesSolid) { InvalidateGeometry(region); RefreshCrust(); }
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
            bool existingRegion = region != null;
            if (region == null)
            {
                region = new Patch { kind = RegionKind.Foundation, building = building };
                patches.Add(region); CreateCrust(region);
            }
            var box = building.ignitionBounds;
            Vector3 scale = box.transform.lossyScale;
            Matrix4x4 footprint = Matrix4x4.Translate(-box.center) * box.transform.worldToLocalMatrix;
            Vector3 halfSize = box.size * .5f + new Vector3(1.5f / Mathf.Max(.001f, Mathf.Abs(scale.x)), 0,
                1.5f / Mathf.Max(.001f, Mathf.Abs(scale.z)));
            float lowY = box.bounds.min.y - .25f, highY = box.bounds.min.y + 1.5f;
            bool footprintChanged = region.worldToFootprint != footprint || region.footprintHalfSize != halfSize ||
                region.lowY != lowY || region.highY != highY;
            Bounds previousBounds = region.regionBounds;
            bool hadBounds = region.hasRegionBounds;
            if (footprintChanged)
            {
                InvalidateGeometry(region);
                region.candidatesDirty = true;
            }
            region.worldToFootprint = footprint; region.footprintHalfSize = halfSize;
            // Only the contact-height slab is protected, never another river overhead.
            region.lowY = lowY; region.highY = highY;
            if (footprintChanged) region.hasRegionBounds = TryGetFoundationBounds(region, out region.regionBounds);
            bool becomesSolid = region.progress < 1f && progress >= 1f;
            region.progress = Mathf.Max(region.progress, progress);
            UpdateHeat(region);
            if (becomesSolid) InvalidateGeometry(region);
            if (!source) source = GetComponent<LavaIgnitionSource>();
            if (!flow) flow = GetComponent<ProgressiveLavaFlow>();
            if (flow)
            {
                if (region.hasRegionBounds && (!footprintChanged || !existingRegion || hadBounds))
                {
                    Bounds changed = region.regionBounds;
                    if (footprintChanged && existingRegion) changed.Encapsulate(previousBounds);
                    flow.NotifyCoolingChanged(changed);
                }
                else flow.NotifyCoolingChanged();
                if (region.progress >= 1f) flow.RefreshCoolingBarrier();
            }
            if (becomesSolid) RefreshCrust();
        }
        static bool TryGetFoundationBounds(Patch patch, out Bounds bounds)
        {
            bounds = default;
            Matrix4x4 toWorld = patch.worldToFootprint.inverse;
            // The footprint is infinite along local Y and clipped by a world-height slab.
            // Solve its eight corners instead of using the finite box, which would miss
            // protected ground just outside a tilted building's bottom face.
            if (Mathf.Abs(toWorld.m11) < .00001f) return false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2) for (int y = 0; y < 2; y++)
            {
                float localX = x * patch.footprintHalfSize.x, localZ = z * patch.footprintHalfSize.z;
                float worldY = y == 0 ? patch.lowY : patch.highY;
                float localY = (worldY - toWorld.m13 - toWorld.m10 * localX - toWorld.m12 * localZ) / toWorld.m11;
                Vector3 point = toWorld.MultiplyPoint3x4(new Vector3(localX, localY, localZ));
                min = Vector3.Min(min, point); max = Vector3.Max(max, point);
            }
            bounds.SetMinMax(min, max);
            bounds.Expand(.002f); // Keep broad-phase rounding outside the exact clipping planes.
            return true;
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
            InvalidateGeometry(patch);
            if (!flow) flow = GetComponent<ProgressiveLavaFlow>();
            if (flow)
            {
                float radius = Mathf.Max(1f, patchRadius) * Mathf.Sqrt(t) * 1.03f;
                flow.NotifyCoolingChanged(new Bounds(patch.point, new Vector3(radius * 2f, 0f, radius * 2f)));
            }
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
            InvalidateGeometry(patch);
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
        List<int> CandidateTriangles(Patch patch)
        {
            if (patch.kind == RegionKind.Downstream || (patch.kind == RegionKind.Foundation && !patch.hasRegionBounds))
                return null;
            float radius = Mathf.Max(1f, patchRadius) * 1.03f;
            if (patch.kind == RegionKind.Water && patch.candidateWaterRadius != radius)
                patch.candidatesDirty = true;
            if (!patch.candidatesDirty) return patch.candidateTriangles;
            patch.candidateTriangles.Clear();
            for (int triangle = 0; triangle < sourceTriangleBounds.Count; triangle++)
            {
                Bounds bounds = sourceTriangleBounds[triangle];
                if (patch.kind == RegionKind.Water)
                {
                    // The circle grows inside this maximum extent. Each small water dose
                    // can reuse the same local triangles without rescanning the whole river.
                    if (bounds.min.x > patch.point.x + radius || bounds.max.x < patch.point.x - radius ||
                        bounds.min.z > patch.point.z + radius || bounds.max.z < patch.point.z - radius) continue;
                }
                else if (!patch.regionBounds.Intersects(bounds)) continue;
                patch.candidateTriangles.Add(triangle * 3);
            }
            patch.candidateWaterRadius = radius;
            patch.candidatesDirty = false;
            return patch.candidateTriangles;
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
                sourceWasVisible = false;
                return;
            }
            Mesh mesh = collider.sharedMesh;
            if (!mesh.isReadable) return;
            bool visible = collider.enabled && (!source.surfaceRenderer || source.surfaceRenderer.enabled);
            if (mesh != cachedSurfaceMesh || collider.transform.localToWorldMatrix != surfaceMatrix || visible != sourceWasVisible)
                NotifySurfaceChanged();
            dirty = false;
            sourceWasVisible = visible;
            if (sourceGeometryDirty)
            {
                // Keep reusable world-space buffers while only a region's temperature changes.
                // A genuinely changed/extended lava mesh invalidates every affected cover.
                surfaceMatrix = collider.transform.localToWorldMatrix;
                mesh.GetVertices(sourcePoints); mesh.GetTriangles(sourceTriangles, 0);
                for (int submesh = 1; submesh < mesh.subMeshCount; submesh++)
                {
                    mesh.GetTriangles(submeshTriangles, submesh);
                    sourceTriangles.AddRange(submeshTriangles);
                }
                for (int i = 0; i < sourcePoints.Count; i++) sourcePoints[i] = surfaceMatrix.MultiplyPoint3x4(sourcePoints[i]);
                sourceTriangleBounds.Clear();
                for (int i = 0; i < sourceTriangles.Count; i += 3)
                {
                    Bounds bounds = new Bounds(sourcePoints[sourceTriangles[i]], Vector3.zero);
                    bounds.Encapsulate(sourcePoints[sourceTriangles[i + 1]]);
                    bounds.Encapsulate(sourcePoints[sourceTriangles[i + 2]]);
                    sourceTriangleBounds.Add(bounds);
                }
                // Completed covers keep their meshes when growth is elsewhere, but any later
                // edit must select candidates from the newly revealed source geometry.
                foreach (var patch in patches) patch.candidatesDirty = true;
                cachedSurfaceMesh = mesh; sourceGeometryDirty = false; arcDistances.Clear(); SourceMeshReadCount++;
            }
            foreach (var patch in patches)
            {
                if (!patch.geometryDirty) continue;
                patch.geometryDirty = false;
                vertices.Clear(); uv.Clear(); indices.Clear();
                float progress = Progress(patch);
                float radius = Mathf.Max(1f, patchRadius) * Mathf.Sqrt(progress);
                float boundsRadius = radius * 1.03f;
                var toLocal = patch.renderer.transform.worldToLocalMatrix;
                int planeCount = patch.kind == RegionKind.Water ? Sides : patch.kind == RegionKind.Foundation ? 6 : 2;
                List<int> candidates = CandidateTriangles(patch);
                int triangleCount = candidates != null ? candidates.Count : sourceTriangles.Count / 3;
                for (int triangle = 0; triangle < triangleCount; triangle++)
                {
                    int i = candidates != null ? candidates[triangle] : triangle * 3;
                    Bounds bounds = sourceTriangleBounds[i / 3];
                    if (patch.kind == RegionKind.Water &&
                        (bounds.min.x > patch.point.x + boundsRadius || bounds.max.x < patch.point.x - boundsRadius ||
                        bounds.min.z > patch.point.z + boundsRadius || bounds.max.z < patch.point.z - boundsRadius)) continue;
                    if (patch.kind == RegionKind.Foundation && patch.hasRegionBounds && !patch.regionBounds.Intersects(bounds)) continue;
                    Vector3 a = sourcePoints[sourceTriangles[i]], b = sourcePoints[sourceTriangles[i + 1]], c = sourcePoints[sourceTriangles[i + 2]];
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
                CrustMeshRebuildCount++;
                patch.renderer.enabled = indices.Count > 0 && visible;
                // Keep the visible cooled surface and the walkable/wheel surface identical.
                if (patch.solid)
                {
                    patch.solid.sharedMesh = null;
                    patch.solid.enabled = progress >= 1f && patch.renderer.enabled;
                    if (patch.solid.enabled) { patch.solid.sharedMesh = patch.mesh; CrustColliderCookCount++; }
                }
            }
        }
        public void ResetCooling()
        {
            foreach (var p in patches) DisposePatch(p);
            patches.Clear(); CooledPatchCount = 0; dirty = false; nextMesh = nextSteam = 0;
            cachedSurfaceMesh = null; sourceGeometryDirty = true; sourceWasVisible = false;
            sourcePoints.Clear(); sourceTriangles.Clear(); submeshTriangles.Clear(); sourceTriangleBounds.Clear(); arcDistances.Clear();
            CrustMeshRebuildCount = CrustColliderCookCount = SourceMeshReadCount = 0;
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
