using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>Persistent scorching, followed by a staggered collapse of the house's own textured geometry.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingRuinVisual : MonoBehaviour
    {
        sealed class Surface
        {
            public MeshRenderer renderer;
            public Material[] materials;
            public Color[] colors;
        }
        sealed class Fragment
        {
            public Transform transform;
            public MeshRenderer renderer;
            public Vector3 start, end;
            public Quaternion rotation, resting;
            public float delay, duration;
            public bool landed;
        }
        sealed class Chunk
        {
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Vector4> tangents = new List<Vector4>();
            public readonly Dictionary<Material, List<int>> triangles = new Dictionary<Material, List<int>>();
        }
        struct Vertex
        {
            public Vector3 position, normal;
            public Vector2 uv;
            public Vector4 tangent;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex {
                position = Vector3.LerpUnclamped(a.position, b.position, t), normal = Vector3.LerpUnclamped(a.normal, b.normal, t).normalized,
                uv = Vector2.LerpUnclamped(a.uv, b.uv, t), tangent = Vector4.LerpUnclamped(a.tangent, b.tangent, t) };
        }
        sealed class SourceMesh
        {
            public Vector3[] vertices, normals;
            public Vector2[] uv;
            public Vector4[] tangents;
        }
        readonly List<Surface> surfaces = new List<Surface>();
        readonly List<Fragment> fragments = new List<Fragment>();
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Material> scorchMaterials = new Dictionary<Material, Material>();
        MaterialPropertyBlock block;
        BurnableBuilding owner;
        RescueGameFlow flow;
        ParticleSystem dust;
        Transform rubble;
        Bounds footprint;
        Terrain[] terrains;
        float elapsed, duration, lastDamage = -1, smokeBudget;
        bool started;
        public bool IsCollapsing => started && CollapseProgress < 1;
        public float CollapseProgress => started ? Mathf.Clamp01(elapsed / duration) : 0;
        public int FragmentCount => fragments.Count;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color"), Emission = Shader.PropertyToID("_EmissionColor");

        void Initialize(BurnableBuilding building)
        {
            if (owner) return;
            owner = building; flow = FindFirstObjectByType<RescueGameFlow>(); block = new MaterialPropertyBlock(); terrains = Terrain.activeTerrains;
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.GetComponent<MeshFilter>()) continue;
                var surface = new Surface { renderer = renderer, materials = renderer.sharedMaterials };
                bool converted = false;
                for (int i = 0; i < surface.materials.Length; i++)
                {
                    Material original = surface.materials[i]; surface.materials[i] = ScorchMaterial(original);
                    converted |= original != surface.materials[i];
                }
                if (converted) renderer.sharedMaterials = surface.materials;
                surface.colors = new Color[surface.materials.Length];
                for (int i = 0; i < surface.materials.Length; i++)
                {
                    var material = surface.materials[i];
                    surface.colors[i] = material && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) :
                        material && material.HasProperty(ColorId) ? material.GetColor(ColorId) : Color.white;
                }
                surfaces.Add(surface);
            }
        }
        Material ScorchMaterial(Material source)
        {
            if (!source || !source.shader || source.shader.name.IndexOf("Autodesk", System.StringComparison.OrdinalIgnoreCase) < 0) return source;
            if (scorchMaterials.TryGetValue(source, out var material)) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit"); if (!shader) return source;
            // Autodesk's color-map branch ignores its tint color. Keep its maps on a shader that can darken them gradually.
            material = new Material(shader) { name = source.name + " · burnable surface (runtime)" };
            material.SetTexture("_BaseMap", source.mainTexture); material.SetColor(BaseColor, Color.white);
            material.SetTextureScale("_BaseMap", source.mainTextureScale); material.SetTextureOffset("_BaseMap", source.mainTextureOffset);
            if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap"))
            {
                material.SetTexture("_BumpMap", source.GetTexture("_BumpMap")); material.EnableKeyword("_NORMALMAP");
                if (source.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
            }
            if (source.HasProperty("_OcclusionMap") && source.GetTexture("_OcclusionMap"))
            { material.SetTexture("_OcclusionMap", source.GetTexture("_OcclusionMap")); material.EnableKeyword("_OCCLUSIONMAP"); }
            material.SetFloat("_Smoothness", .22f);
            if (source.shader.name.IndexOf("Masked", System.StringComparison.OrdinalIgnoreCase) >= 0)
            { material.SetFloat("_AlphaClip", 1); material.EnableKeyword("_ALPHATEST_ON"); material.renderQueue = 2450; }
            scorchMaterials[source] = material; return material;
        }
        public void SetDamage(BurnableBuilding building)
        {
            Initialize(building);
            if (started) return;
            float damage = 1 - building.Integrity01;
            if (damage < 1 && Mathf.Abs(damage - lastDamage) < .003f) return;
            lastDamage = damage;
            for (int n = 0; n < surfaces.Count; n++)
            {
                var surface = surfaces[n]; if (!surface.renderer) continue;
                float scorch = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, .98f, damage));
                scorch *= .82f + (n % 7) * .025f;
                for (int i = 0; i < surface.materials.Length; i++)
                {
                    var material = surface.materials[i]; if (!material) continue;
                    surface.renderer.GetPropertyBlock(block, i);
                    Color tint = surface.colors[i] * Color.Lerp(Color.white, new Color(.34f, .30f, .26f, 1), scorch);
                    if (material.HasProperty(BaseColor)) block.SetColor(BaseColor, tint);
                    if (material.HasProperty(ColorId)) block.SetColor(ColorId, tint);
                    if (material.HasProperty(Emission)) block.SetColor(Emission, material.GetColor(Emission) * (1 - scorch));
                    surface.renderer.SetPropertyBlock(block, i);
                }
            }
        }
        public void Collapse(BurnableBuilding building)
        {
            if (started) return;
            SetDamage(building); started = true; duration = Mathf.Max(2, building.visualCollapseSeconds);
            footprint = building.ignitionBounds ? building.ignitionBounds.bounds : new Bounds(transform.position + Vector3.up * 5, Vector3.one * 15);
            var fire = building.GetComponent<BuildingFireVisual>(); if (fire) fire.ClearForRuin();
            foreach (var particles in GetComponentsInChildren<ParticleSystem>(true)) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var light in GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var overlay in building.windowGlowOverlays)
                if (overlay) { overlay.enabled = false; overlay.gameObject.SetActive(false); }
            rubble = new GameObject("Burned house · ash and charred beams").transform; rubble.SetParent(transform, false);
            BuildFragments();
            // Retire LOD controllers before hiding their renderers, so no bright window can reappear.
            foreach (var lod in GetComponentsInChildren<LODGroup>(true)) lod.enabled = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (!renderer.transform.IsChildOf(rubble)) renderer.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            CreateDust();
        }
        void BuildFragments()
        {
            var alternateLods = new HashSet<Renderer>();
            var firstLods = new HashSet<Renderer>();
            foreach (var group in GetComponentsInChildren<LODGroup>(true))
            {
                var levels = group.GetLODs();
                if (levels.Length > 0) foreach (var r in levels[0].renderers) if (r) firstLods.Add(r);
                for (int i = 1; i < levels.Length; i++) foreach (var r in levels[i].renderers) if (r) alternateLods.Add(r);
            }
            alternateLods.ExceptWith(firstLods);
            Bounds localBounds = new Bounds(transform.InverseTransformPoint(footprint.center), Vector3.zero);
            for (int i = 0; i < 8; i++) localBounds.Encapsulate(transform.InverseTransformPoint(footprint.center +
                Vector3.Scale(footprint.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            foreach (var surface in surfaces)
            {
                if (!surface.renderer || !surface.renderer.gameObject.activeInHierarchy || alternateLods.Contains(surface.renderer)) continue;
                Bounds visible = surface.renderer.bounds;
                for (int i = 0; i < 8; i++) localBounds.Encapsulate(transform.InverseTransformPoint(visible.center +
                    Vector3.Scale(visible.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            }
            var chunks = new Chunk[27];
            var meshData = new Dictionary<Mesh, SourceMesh>();
            var polygon = new List<Vertex>(12); var clipped = new List<Vertex>(12);
            var triangle = new Vertex[3];
            for (int source = 0; source < surfaces.Count; source++)
            {
                var surface = surfaces[source]; var renderer = surface.renderer;
                if (!renderer || !renderer.gameObject.activeInHierarchy || alternateLods.Contains(renderer) ||
                    (!renderer.enabled && !firstLods.Contains(renderer))) continue;
                // Static batching replaces a render mesh; the fitted solid collider retains the original house mesh.
                var collider = renderer.GetComponent<MeshCollider>();
                bool originalGeometry = collider && collider.sharedMesh && collider.sharedMesh.isReadable;
                var mesh = originalGeometry ? collider.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh || !mesh.isReadable) { AddFallback(renderer); continue; }
                if (!meshData.TryGetValue(mesh, out var data))
                    meshData[mesh] = data = new SourceMesh { vertices = mesh.vertices, normals = mesh.normals, uv = mesh.uv, tangents = mesh.tangents };
                var vertices = data.vertices; var normals = data.normals; var uv = data.uv; var tangents = data.tangents;
                // Renderer matrices and submesh ranges also describe geometry in a static batch.
                Matrix4x4 matrix = transform.worldToLocalMatrix * (originalGeometry ? renderer.transform.localToWorldMatrix : renderer.localToWorldMatrix);
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                float handedness = matrix.determinant < 0 ? -1 : 1;
                int first = !originalGeometry && renderer.isPartOfStaticBatch ? renderer.subMeshStartIndex : 0;
                for (int slot = 0; slot < surface.materials.Length && first + slot < mesh.subMeshCount; slot++)
                {
                    var material = surface.materials[slot]; if (!material || mesh.GetTopology(first + slot) != MeshTopology.Triangles) continue;
                    var triangles = mesh.GetTriangles(first + slot);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        for (int v = 0; v < 3; v++)
                        {
                            int original = triangles[t + v];
                            Vector4 tangent = tangents.Length == vertices.Length ? tangents[original] : new Vector4(1, 0, 0, 1);
                            Vector3 direction = matrix.MultiplyVector(tangent).normalized;
                            triangle[v] = new Vertex {
                                position = matrix.MultiplyPoint3x4(vertices[original]),
                                normal = normals.Length == vertices.Length ? normalMatrix.MultiplyVector(normals[original]).normalized : Vector3.up,
                                uv = uv.Length == vertices.Length ? uv[original] : Vector2.zero,
                                tangent = new Vector4(direction.x, direction.y, direction.z, tangent.w * handedness) };
                        }
                        Bounds triangleBounds = new Bounds(triangle[0].position, Vector3.zero);
                        triangleBounds.Encapsulate(triangle[1].position); triangleBounds.Encapsulate(triangle[2].position);
                        int minX = Cell(triangleBounds.min.x, localBounds.min.x, localBounds.max.x), maxX = Cell(triangleBounds.max.x, localBounds.min.x, localBounds.max.x);
                        int minZ = Cell(triangleBounds.min.z, localBounds.min.z, localBounds.max.z), maxZ = Cell(triangleBounds.max.z, localBounds.min.z, localBounds.max.z);
                        for (int y = 0; y < 3; y++) for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
                        {
                            Vector3 lower = new Vector3(Mathf.Lerp(localBounds.min.x, localBounds.max.x, x / 3f),
                                Mathf.Lerp(localBounds.min.y, localBounds.max.y, y == 0 ? 0 : y == 1 ? .1f : .6f), Mathf.Lerp(localBounds.min.z, localBounds.max.z, z / 3f));
                            Vector3 upper = new Vector3(Mathf.Lerp(localBounds.min.x, localBounds.max.x, (x + 1) / 3f),
                                Mathf.Lerp(localBounds.min.y, localBounds.max.y, y == 0 ? .1f : y == 1 ? .6f : 1), Mathf.Lerp(localBounds.min.z, localBounds.max.z, (z + 1) / 3f));
                            if (triangleBounds.max.y < lower.y || triangleBounds.min.y > upper.y) continue;
                            polygon.Clear(); polygon.AddRange(triangle);
                            for (int axis = 0; axis < 3 && polygon.Count > 0; axis++)
                            {
                                Clip(polygon, clipped, axis, lower[axis], 1); var swap = polygon; polygon = clipped; clipped = swap;
                                Clip(polygon, clipped, axis, upper[axis], -1); swap = polygon; polygon = clipped; clipped = swap;
                            }
                            if (polygon.Count < 3) continue;
                            int cell = x + z * 3 + y * 9;
                            var chunk = chunks[cell] ?? (chunks[cell] = new Chunk());
                            if (!chunk.triangles.TryGetValue(material, out var indices)) chunk.triangles[material] = indices = new List<int>();
                            int firstVertex = chunk.vertices.Count;
                            foreach (var vertex in polygon)
                            { chunk.vertices.Add(vertex.position); chunk.normals.Add(vertex.normal); chunk.uv.Add(vertex.uv); chunk.tangents.Add(vertex.tangent); }
                            for (int v = 1; v < polygon.Count - 1; v++) { indices.Add(firstVertex); indices.Add(firstVertex + v); indices.Add(firstVertex + v + 1); }
                        }
                    }
                }
            }
            for (int i = 0; i < chunks.Length; i++)
            {
                var chunk = chunks[i]; if (chunk == null || chunk.vertices.Count == 0) continue;
                Bounds bounds = new Bounds(chunk.vertices[0], Vector3.zero); foreach (var p in chunk.vertices) bounds.Encapsulate(p);
                for (int v = 0; v < chunk.vertices.Count; v++) chunk.vertices[v] -= bounds.center;
                var mesh = new Mesh { name = "Burned house section " + i, indexFormat = chunk.vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(chunk.vertices); mesh.SetNormals(chunk.normals); mesh.SetUVs(0, chunk.uv); mesh.SetTangents(chunk.tangents);
                mesh.subMeshCount = chunk.triangles.Count;
                var palette = new Material[chunk.triangles.Count]; int slot = 0;
                foreach (var pair in chunk.triangles) { mesh.SetTriangles(pair.Value, slot); palette[slot++] = BurnedMaterial(pair.Key); }
                mesh.RecalculateBounds(); meshes.Add(mesh);
                var go = new GameObject(i >= 18 ? "Falling roof section" : i >= 9 ? "Falling wall section" : "Burned foundation section", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(rubble, false); go.transform.localPosition = bounds.center;
                go.GetComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterials = palette;
                AddFragment(renderer, i, i < 9);
            }
        }
        static int Cell(float value, float min, float max) => Mathf.Clamp((int)(Mathf.InverseLerp(min, max, value) * 3), 0, 2);
        static void Clip(List<Vertex> source, List<Vertex> destination, int axis, float plane, int sign)
        {
            destination.Clear(); if (source.Count == 0) return;
            Vertex previous = source[source.Count - 1]; float before = (previous.position[axis] - plane) * sign;
            foreach (var current in source)
            {
                float after = (current.position[axis] - plane) * sign;
                if ((before >= 0) != (after >= 0)) destination.Add(Vertex.Lerp(previous, current, before / (before - after)));
                if (after >= 0) destination.Add(current);
                previous = current; before = after;
            }
        }
        Material BurnedMaterial(Material source)
        {
            if (materials.TryGetValue(source, out var material)) return material;
            material = new Material(source) { name = source.name + " · scorched ruin (runtime)" };
            Color tint = new Color(.40f, .36f, .32f, 1);
            if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, source.GetColor(BaseColor) * tint);
            if (material.HasProperty(ColorId)) material.SetColor(ColorId, source.GetColor(ColorId) * tint);
            if (material.HasProperty(Emission)) material.SetColor(Emission, Color.black);
            material.DisableKeyword("_EMISSION");
            if (material.HasProperty("_UseEmissiveMap")) material.SetFloat("_UseEmissiveMap", 0);
            if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", Texture2D.blackTexture);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .12f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .12f);
            if (material.HasProperty("_Roughness")) material.SetFloat("_Roughness", .88f);
            materials[source] = material; return material;
        }
        void AddFallback(MeshRenderer source)
        {
            // Unreadable decorative meshes still collapse as fitted sections; never duplicate a whole static batch.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Falling structural timber";
            go.transform.SetParent(rubble, true); go.transform.position = source.bounds.center;
            Vector3 size = Vector3.Max(source.bounds.size, Vector3.one * .02f), scale = rubble.lossyScale;
            go.transform.localScale = new Vector3(size.x / Mathf.Abs(scale.x), size.y / Mathf.Abs(scale.y), size.z / Mathf.Abs(scale.z));
            var renderer = go.GetComponent<MeshRenderer>(); if (source.sharedMaterial) renderer.sharedMaterial = BurnedMaterial(source.sharedMaterial);
            Destroy(go.GetComponent<Collider>()); AddFragment(renderer, fragments.Count);
        }
        void AddFragment(MeshRenderer renderer, int seed, bool foundation = false)
        {
            var random = new System.Random(seed * 7919 + Mathf.RoundToInt(footprint.center.x * 100));
            var part = renderer.transform; Vector3 centre = renderer.bounds.center;
            float height = Mathf.InverseLerp(footprint.min.y, footprint.max.y, centre.y);
            Vector3 inward = Vector3.ProjectOnPlane(footprint.center - centre, Vector3.up).normalized;
            if (inward.sqrMagnitude < .01f) inward = transform.right;
            float tilt = foundation ? (float)random.NextDouble() * 3 : height > .6f ? 3 + (float)random.NextDouble() * 12 : 68 + (float)random.NextDouble() * 18;
            Quaternion resting = Quaternion.AngleAxis(tilt, Vector3.Cross(Vector3.up, inward)) * part.rotation;
            Vector3 extents = renderer.localBounds.extents, scale = part.lossyScale;
            extents = Vector3.Scale(extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            float halfHeight = Mathf.Abs((resting * Vector3.right).y) * extents.x + Mathf.Abs((resting * Vector3.up).y) * extents.y + Mathf.Abs((resting * Vector3.forward).y) * extents.z;
            Vector3 end = part.position + inward * Mathf.Min(footprint.size.x, footprint.size.z) * (foundation ? 0 : .08f);
            end.y = Ground(end) + halfHeight + .1f;
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            if (mesh && mesh.isReadable)
            {
                float support = float.NegativeInfinity;
                foreach (var vertex in mesh.vertices)
                {
                    Vector3 offset = resting * Vector3.Scale(vertex, scale);
                    Vector3 sample = new Vector3(end.x + offset.x, 0, end.z + offset.z);
                    support = Mathf.Max(support, Ground(sample) - offset.y);
                }
                if (float.IsFinite(support)) end.y = support + .06f;
            }
            fragments.Add(new Fragment { transform = part, renderer = renderer, start = part.position, end = end,
                rotation = part.rotation, resting = resting, delay = duration * (.04f + (1 - height) * .36f + (float)random.NextDouble() * .05f),
                duration = duration * (.32f + (float)random.NextDouble() * .1f) });
        }
        float Ground(Vector3 point)
        {
            foreach (var terrain in terrains)
            {
                Vector3 p = point - terrain.transform.position, size = terrain.terrainData.size;
                if (p.x >= 0 && p.z >= 0 && p.x <= size.x && p.z <= size.z) return terrain.SampleHeight(point) + terrain.transform.position.y;
            }
            return footprint.min.y;
        }
        void CreateDust()
        {
            var go = new GameObject("Collapse ash cloud"); go.transform.SetParent(rubble, false); dust = go.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main; main.loop = false; main.playOnAwake = false; main.duration = duration + 18; main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 3); main.startSpeed = 0;
            main.startColor = new Color(.28f, .26f, .23f, .27f);
            var emission = dust.emission; emission.rateOverTime = 0; var shape = dust.shape; shape.enabled = false;
            var colors = dust.colorOverLifetime; colors.enabled = true;
            colors.color = new Gradient { colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.65f, .15f), new GradientAlphaKey(0, 1) } };
            var growth = dust.sizeOverLifetime; growth.enabled = true; growth.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .55f, 1, 1.8f));
            var renderer = dust.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = Resources.Load<Material>("BuildingFire/Smoke");
            if (!renderer.sharedMaterial && owner.smoke) renderer.sharedMaterial = owner.smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            dust.Play(false);
        }
        void Update()
        {
            bool result = flow && flow.HasResult;
            if (dust) { var main = dust.main; main.useUnscaledTime = result; }
            TickCollapse(result ? Time.unscaledDeltaTime : Time.deltaTime);
        }
        public void TickCollapse(float deltaTime)
        {
            if (!started || deltaTime <= 0 || !float.IsFinite(deltaTime) || elapsed >= duration + 16) return;
            elapsed += deltaTime;
            foreach (var fragment in fragments)
            {
                if (fragment.landed || !fragment.transform) continue;
                float t = Mathf.Clamp01((elapsed - fragment.delay) / fragment.duration);
                if (t <= 0) continue;
                float fall = t * t * (2 - t);
                fragment.transform.position = Vector3.Lerp(fragment.start, fragment.end, fall);
                fragment.transform.rotation = Quaternion.Slerp(fragment.rotation, fragment.resting, Mathf.SmoothStep(0, 1, t));
                if (t < 1) continue;
                fragment.landed = true;
                EmitDust(new Vector3(fragment.end.x, Ground(fragment.end) + .2f, fragment.end.z), 7, false);
                // Settled debris alone receives an obstacle; moving pieces cannot fling the player or villagers.
                var collider = fragment.transform.gameObject.AddComponent<BoxCollider>();
                collider.center = fragment.renderer.localBounds.center; collider.size = Vector3.Max(fragment.renderer.localBounds.size, Vector3.one * .02f);
            }
            if (elapsed < duration + 12)
            {
                smokeBudget += deltaTime * 2.5f * Mathf.Clamp01(1 - Mathf.Max(0, elapsed - duration) / 12);
                while (smokeBudget >= 1) { smokeBudget--; EmitDust(new Vector3(footprint.center.x, Ground(footprint.center) + .5f, footprint.center.z), 1, true); }
            }
            else if (dust && dust.isEmitting) dust.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        void EmitDust(Vector3 point, int count, bool smolder)
        {
            if (!dust) return;
            if (!dust.isPlaying) dust.Play(false);
            float scale = Mathf.Clamp(footprint.size.magnitude / 22, .7f, 2.5f);
            for (int i = 0; i < count; i++)
                dust.Emit(new ParticleSystem.EmitParams { position = point + Random.insideUnitSphere * scale * .5f,
                    velocity = Vector3.up * (smolder ? .7f : .35f) + new Vector3(.15f, 0, .08f),
                    startSize = scale * (smolder ? 1.1f : 1.5f), startLifetime = smolder ? 3 : 2.1f,
                    startColor = new Color(.28f, .26f, .23f, smolder ? .18f : .3f) }, 1);
        }
        void OnDestroy()
        {
            foreach (var mesh in meshes) if (mesh) Destroy(mesh);
            foreach (var material in materials.Values) if (material) Destroy(material);
            foreach (var material in scorchMaterials.Values) if (material) Destroy(material);
        }
    }
}
