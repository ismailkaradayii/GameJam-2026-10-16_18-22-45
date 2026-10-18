using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>Bounded world-space tire impressions and soil lifted by real wheel contacts.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(FireTruckDrive)), DefaultExecutionOrder(350)]
    public sealed class FireTruckGroundEffects : MonoBehaviour
    {
        public FireTruckDrive drive;
        public Material tireTrackMaterial, dustMaterial;
        public Shader soilDustShader;
        public Texture2D dustTexture;
        [Tooltip("Optional loose terrain palette. Empty accepts terrain except lava, rock and paved layers.")]
        public TerrainLayer[] looseTerrainLayers;
        [Tooltip("Non-terrain surfaces require explicit opt-in; building and fence colliders are excluded by default.")]
        public LayerMask additionalLooseSurfaceLayers;
        [Header("Lasting tire impressions")]
        [Range(32, 12000)] public int maximumSegments = 4096;
        [Min(.2f)] public float trackLifetime = 80f;
        [Min(.1f)] public float sampleSpacing = .35f;
        [Min(1f)] public float teleportBreakDistance = 8f;
        [Min(0f)] public float minimumSpeedKph = 1.8f;
        [Range(.2f, 1.5f)] public float tireWidthMultiplier = 1f;
        [Range(.1f, .9f)] public float fallbackWidthToRadius = .42f;
        [Range(0f, 1f)] public float trackOpacity = .62f;
        public Color trackTint = new Color(.055f, .035f, .018f, 1f);
        [Min(.005f)] public float surfaceOffset = .025f;
        [Range(.3f, .95f)] public float minimumGroundNormalY = .62f;
        [Header("Soil and dust")]
        [Range(32, 2048)] public int maximumDustParticles = 480;
        [Min(0f)] public float dustRatePerWheel = 21f;
        [Min(.2f)] public float dustLifetime = 1.65f;
        public Color dustTint = new Color(.46f, .355f, .25f, .55f);

        public int ActiveSegmentCount { get; private set; }
        public long TotalSegmentsCreated { get; private set; }
        public int DustParticleCount => dust ? dust.particleCount : 0;
        public int MaximumDustParticles => dust ? dust.main.maxParticles : Mathf.Max(32, maximumDustParticles);
        public int GroundedWheelCount { get; private set; }
        public int EmittingWheelCount { get; private set; }
        public Mesh TrackMesh => mesh;
        public int WheelCount => wheelStates != null ? wheelStates.Length : 0;
        public long GetWheelSegmentTotal(int index) => ValidWheel(index) ? wheelStates[index].total : 0;
        public Vector3 GetWheelContact(int index) => ValidWheel(index) ? wheelStates[index].contact : Vector3.zero;
        public float GetWheelWidth(int index) => ValidWheel(index) ? wheelStates[index].width : 0f;
        public bool GetWheelSurfaceAllowed(int index) => ValidWheel(index) && wheelStates[index].surfaceAllowed;

        sealed class WheelState
        {
            public Vector3 contact, anchor, left, right, normal, side, surfacePoint;
            public Collider collider, surfaceCollider;
            public Terrain terrain;
            public TerrainData terrainData;
            public TerrainLayer[] layers;
            public bool[] looseLayers;
            public float radius, width, distance, dustRemainder, nextSurfaceAt, looseness;
            public bool anchored, surfaceAllowed;
            public long total;
        }
        WheelState[] wheelStates;
        Transform root;
        Mesh mesh;
        MeshRenderer trackRenderer;
        ParticleSystem dust;
        Material ownedTrackMaterial, ownedDustMaterial;
        Vector3[] vertices, normals;
        Vector2[] uv;
        Color32[] colors;
        Color[] originalColors;
        float[] created;
        bool[] occupied;
        int cursor, capacity;
        bool geometryDirty, colorsDirty, paused;
        float lastFade;
        Vector3 previousRoot;
        uint randomState = 2938727;

        bool ValidWheel(int index) => wheelStates != null && index >= 0 && index < wheelStates.Length;
        void Awake()
        {
            if (!drive) drive = GetComponent<FireTruckDrive>();
            capacity = Mathf.Clamp(maximumSegments, 32, 12000);
            int count = drive && drive.wheels != null ? drive.wheels.Length : 0;
            wheelStates = new WheelState[count];
            for (int i = 0; i < count; i++)
            {
                var wheel = drive.wheels[i];
                var state = new WheelState(); wheelStates[i] = state;
                if (!wheel) continue;
                state.radius = wheel.radius * Mathf.Max(Mathf.Abs(wheel.transform.lossyScale.y), Mathf.Abs(wheel.transform.lossyScale.z));
                state.width = MeasureWidth(i, state.radius) * tireWidthMultiplier;
            }
            randomState ^= (uint)GetInstanceID();
            root = new GameObject("Fire truck ground effects (runtime)").transform;
            root.gameObject.hideFlags = HideFlags.DontSave;
            CreateTracks(); CreateDust(); previousRoot = transform.position;
        }
        void OnEnable() { BreakAll(); previousRoot = transform.position; if (root) root.gameObject.SetActive(true); if (dust) dust.Play(false); }
        void OnDisable() { BreakAll(); if (dust) dust.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); if (root) root.gameObject.SetActive(false); }
        void OnDestroy()
        {
            if (root) Destroy(root.gameObject);
            if (mesh) Destroy(mesh);
            if (ownedTrackMaterial) Destroy(ownedTrackMaterial);
            if (ownedDustMaterial) Destroy(ownedDustMaterial);
        }
        void Update()
        {
            bool nowPaused = Time.timeScale <= 0f;
            if (nowPaused != paused)
            {
                paused = nowPaused; BreakAll(); previousRoot = transform.position;
                if (dust)
                {
                    if (paused) dust.Pause(false); else dust.Play(false);
                }
            }
        }
        void FixedUpdate()
        {
            GroundedWheelCount = EmittingWheelCount = 0;
            if (!drive || !drive.body || wheelStates == null || paused || Time.timeScale <= 0f) return;
            Vector3 movement = transform.position - previousRoot; previousRoot = transform.position;
            if (movement.sqrMagnitude > teleportBreakDistance * teleportBreakDistance || drive.body.isKinematic)
            { BreakAll(); return; }
            for (int i = 0; i < wheelStates.Length; i++)
            {
                var wheel = drive.wheels[i]; var state = wheelStates[i];
                state.surfaceAllowed = false;
                if (!wheel || !wheel.enabled || !wheel.GetGroundHit(out var hit)) { Break(state); continue; }
                GroundedWheelCount++; state.contact = hit.point;
                if (!AllowedSurface(state, hit)) { Break(state); continue; }
                state.surfaceAllowed = true;
                Vector3 velocity = Vector3.ProjectOnPlane(drive.body.GetPointVelocity(hit.point), hit.normal);
                float speed = velocity.magnitude, kph = speed * 3.6f;
                if (kph < minimumSpeedKph || speed < .05f) { Break(state); continue; }
                float slip = Mathf.Clamp01(Mathf.Abs(hit.forwardSlip) * .85f + Mathf.Abs(hit.sidewaysSlip) * 1.4f);
                float braking = Mathf.Clamp01(wheel.brakeTorque / Mathf.Max(1, drive.brakeTorque));
                float strength = Mathf.Clamp01(.4f + Mathf.InverseLerp(2, 55, kph) * .4f + slip * .45f + braking * .15f);
                Vector3 side = Vector3.Cross(hit.normal, hit.forwardDir).normalized;
                if (side.sqrMagnitude < .1f) side = Vector3.ProjectOnPlane(wheel.transform.right, hit.normal).normalized;
                if (state.anchored && (state.collider != hit.collider || Vector3.Distance(state.anchor, hit.point) > teleportBreakDistance || Vector3.Dot(state.side, side) < .4f || Vector3.Dot(state.normal, hit.normal) < .65f)) Break(state);
                if (!state.anchored)
                {
                    if (!Edges(state, hit.point, side, hit.normal, out var left, out var right)) continue;
                    Anchor(state, hit.point, left, right, hit.normal, side, hit.collider);
                }
                else
                {
                    Vector3 start = state.anchor;
                    float distance = Vector3.Distance(start, hit.point), spacing = Mathf.Max(.1f, sampleSpacing);
                    int steps = Mathf.Min(8, Mathf.FloorToInt(distance / spacing));
                    for (int step = 1; step <= steps; step++)
                    {
                        Vector3 point = Vector3.Lerp(start, hit.point, spacing * step / Mathf.Max(.001f, distance));
                        if (!Edges(state, point, side, hit.normal, out var left, out var right)) { Break(state); break; }
                        AddSegment(state, point, left, right, hit.normal, strength);
                        Anchor(state, point, left, right, hit.normal, side, hit.collider);
                    }
                }
                float dustSpeed = Mathf.InverseLerp(2.5f, 48f, kph);
                if (dustSpeed <= 0 || state.looseness <= .05f || !dust) continue;
                EmittingWheelCount++;
                state.dustRemainder += dustRatePerWheel * (.15f + dustSpeed * .85f) * (1f + slip * 1.3f + braking * .3f) * state.looseness * Time.fixedDeltaTime;
                int emit = Mathf.Min(6, Mathf.FloorToInt(state.dustRemainder)); state.dustRemainder -= emit;
                for (int n = 0; n < emit; n++) EmitDust(state, hit, velocity, side, dustSpeed);
            }
        }
        void LateUpdate()
        {
            if (!mesh || occupied == null) return;
            if (Time.time - lastFade >= .1f)
            {
                lastFade = Time.time;
                float lifetime = Mathf.Max(.2f, trackLifetime);
                for (int slot = 0; slot < capacity; slot++)
                {
                    if (!occupied[slot]) continue;
                    float age = Time.time - created[slot];
                    if (age >= lifetime)
                    {
                        occupied[slot] = false; ActiveSegmentCount--;
                        for (int k = 0; k < 4; k++) { vertices[slot * 4 + k] = Vector3.zero; colors[slot * 4 + k] = new Color32(); }
                        geometryDirty = colorsDirty = true; continue;
                    }
                    float fade = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(lifetime * .6f, lifetime, age));
                    Color c = originalColors[slot]; c.a *= fade;
                    for (int k = 0; k < 4; k++) colors[slot * 4 + k] = c;
                    colorsDirty = true;
                }
            }
            if (geometryDirty)
            {
                mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv;
                Bounds bounds = default; bool found = false;
                for (int slot = 0; slot < capacity; slot++) if (occupied[slot])
                    for (int k = 0; k < 4; k++) { if (!found) { bounds = new Bounds(vertices[slot * 4 + k], Vector3.one * .1f); found = true; } else bounds.Encapsulate(vertices[slot * 4 + k]); }
                mesh.bounds = bounds; geometryDirty = false;
            }
            if (colorsDirty) { mesh.colors32 = colors; colorsDirty = false; }
            trackRenderer.enabled = ActiveSegmentCount > 0;
        }
        float MeasureWidth(int index, float radius)
        {
            var visual = drive.wheelVisuals != null && index < drive.wheelVisuals.Length ? drive.wheelVisuals[index] : null;
            if (!visual) return radius * fallbackWidthToRadius;
            Vector3 axis = drive.wheels[index].transform.right;
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    float value = Vector3.Dot(filter.transform.TransformPoint(corner), axis);
                    low = Mathf.Min(low, value); high = Mathf.Max(high, value);
                }
            }
            float measured = high - low;
            return float.IsInfinity(measured) || measured <= 0 ? radius * fallbackWidthToRadius : Mathf.Clamp(measured, radius * .18f, radius * .9f);
        }
        bool AllowedSurface(WheelState state, WheelHit hit)
        {
            if (!hit.collider || hit.normal.y < minimumGroundNormalY || WaterSourceVolume.IsSurfaceCollider(hit.collider) || Wet(hit.point)) return false;
            if (!(hit.collider is TerrainCollider))
            {
                state.terrain = null; state.looseness = 1;
                return (additionalLooseSurfaceLayers.value & (1 << hit.collider.gameObject.layer)) != 0;
            }
            var terrain = hit.collider.GetComponent<Terrain>();
            if (!terrain || !terrain.terrainData) return false;
            if (state.terrain != terrain || state.terrainData != terrain.terrainData)
            {
                state.terrain = terrain; state.terrainData = terrain.terrainData; state.layers = state.terrainData.terrainLayers; state.nextSurfaceAt = -1;
                state.looseLayers = new bool[state.layers.Length];
                for (int i = 0; i < state.layers.Length; i++) state.looseLayers[i] = LooseLayer(state.layers[i]);
            }
            if (state.surfaceCollider != hit.collider || Time.time >= state.nextSurfaceAt || (state.surfacePoint - hit.point).sqrMagnitude > 6.25f)
            {
                state.surfaceCollider = hit.collider; state.surfacePoint = hit.point; state.nextSurfaceAt = Time.time + .3f;
                var data = state.terrainData; Vector3 relative = hit.point - terrain.transform.position, size = data.size;
                if (state.layers == null || state.layers.Length == 0) state.looseness = 1;
                else
                {
                    int x = Mathf.Clamp((int)(relative.x / size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
                    int z = Mathf.Clamp((int)(relative.z / size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
                    var weights = data.GetAlphamaps(x, z, 1, 1); float loose = 0;
                    for (int layer = 0; layer < state.layers.Length && layer < weights.GetLength(2); layer++)
                        if (state.looseLayers[layer]) loose += weights[0, 0, layer];
                    state.looseness = loose;
                }
            }
            return state.looseness >= .3f;
        }
        bool LooseLayer(TerrainLayer layer)
        {
            if (!layer) return true;
            if (looseTerrainLayers != null && looseTerrainLayers.Length > 0)
            {
                for (int i = 0; i < looseTerrainLayers.Length; i++) if (looseTerrainLayers[i] == layer) return true;
                return false;
            }
            string name = layer.name.ToLowerInvariant();
            return !name.Contains("lava") && !name.Contains("rock") && !name.Contains("stone") && !name.Contains("asphalt") && !name.Contains("concrete");
        }
        bool Wet(Vector3 point)
        {
            var sources = WaterSourceVolume.ActiveSources;
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                if (source && source.isActiveAndEnabled && point.y <= source.SurfaceY + .08f && source.ContainsSurface(point)) return true;
            }
            return false;
        }
        bool Edges(WheelState state, Vector3 point, Vector3 side, Vector3 normal, out Vector3 left, out Vector3 right)
        {
            left = point - side * (state.width * .5f); right = point + side * (state.width * .5f);
            if (Wet(point) || Wet(left) || Wet(right)) return false;
            if (state.terrain)
            {
                if (!TerrainPoint(state.terrain, ref left) || !TerrainPoint(state.terrain, ref right)) return false;
            }
            left += normal * surfaceOffset; right += normal * surfaceOffset;
            return true;
        }
        bool TerrainPoint(Terrain terrain, ref Vector3 point)
        {
            var data = terrain.terrainData; Vector3 relative = point - terrain.transform.position, size = data.size;
            if (relative.x < 0 || relative.z < 0 || relative.x > size.x || relative.z > size.z) return false;
            float x = relative.x / size.x, z = relative.z / size.z;
            if (data.GetInterpolatedNormal(x, z).y < minimumGroundNormalY) return false;
            if (data.holesResolution > 0 && data.IsHole(Mathf.Clamp((int)(x * data.holesResolution), 0, data.holesResolution - 1), Mathf.Clamp((int)(z * data.holesResolution), 0, data.holesResolution - 1))) return false;
            point.y = terrain.SampleHeight(point) + terrain.transform.position.y; return true;
        }
        void Anchor(WheelState state, Vector3 point, Vector3 left, Vector3 right, Vector3 normal, Vector3 side, Collider collider)
        { state.anchored = true; state.anchor = point; state.left = left; state.right = right; state.normal = normal; state.side = side; state.collider = collider; }
        void AddSegment(WheelState state, Vector3 point, Vector3 left, Vector3 right, Vector3 normal, float strength)
        {
            int slot = cursor; cursor = (cursor + 1) % capacity;
            if (!occupied[slot]) ActiveSegmentCount++;
            occupied[slot] = true; created[slot] = Time.time;
            Color color = trackTint; color.a *= trackOpacity * strength * Mathf.Lerp(.65f, 1f, state.looseness); originalColors[slot] = color;
            int v = slot * 4; vertices[v] = state.left; vertices[v + 1] = state.right; vertices[v + 2] = left; vertices[v + 3] = right;
            normals[v] = normals[v + 1] = state.normal; normals[v + 2] = normals[v + 3] = normal;
            float before = state.distance; state.distance += Vector3.Distance(state.anchor, point) / Mathf.Max(.18f, state.radius * .22f);
            uv[v] = new Vector2(0, before); uv[v + 1] = new Vector2(1, before); uv[v + 2] = new Vector2(0, state.distance); uv[v + 3] = new Vector2(1, state.distance);
            for (int k = 0; k < 4; k++) colors[v + k] = color;
            TotalSegmentsCreated++; state.total++; geometryDirty = colorsDirty = true;
        }
        void Break(WheelState state) { state.anchored = false; state.distance = state.dustRemainder = 0; }
        void BreakAll() { if (wheelStates != null) foreach (var state in wheelStates) { Break(state); state.surfaceAllowed = false; } GroundedWheelCount = EmittingWheelCount = 0; }
        float Random01() { randomState ^= randomState << 13; randomState ^= randomState >> 17; randomState ^= randomState << 5; return (randomState & 0xffffff) / 16777216f; }
        void EmitDust(WheelState state, WheelHit hit, Vector3 velocity, Vector3 side, float speed)
        {
            dust.Emit(new ParticleSystem.EmitParams
            {
                position = hit.point + hit.normal * .12f + side * ((Random01() - .5f) * state.width * .8f),
                velocity = -velocity * .055f + hit.normal * Mathf.Lerp(.45f, 1.5f, Random01()) + side * ((Random01() - .5f) * .65f),
                startSize = state.radius * Mathf.Lerp(.38f, .7f, Random01()),
                startLifetime = dustLifetime * Mathf.Lerp(.7f, 1.15f, Random01()),
                startColor = new Color(dustTint.r, dustTint.g, dustTint.b, dustTint.a * Mathf.Lerp(.6f, 1f, speed)),
                rotation = Random01() * 360
            }, 1);
        }
        void CreateTracks()
        {
            mesh = new Mesh { name = "Bounded fire truck tire impressions" }; mesh.MarkDynamic();
            vertices = new Vector3[capacity * 4]; normals = new Vector3[capacity * 4]; uv = new Vector2[capacity * 4]; colors = new Color32[capacity * 4];
            created = new float[capacity]; occupied = new bool[capacity]; originalColors = new Color[capacity];
            var indices = new int[capacity * 6];
            for (int i = 0; i < capacity; i++) { int v = i * 4, t = i * 6; indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1; indices[t + 3] = v + 2; indices[t + 4] = v + 3; indices[t + 5] = v + 1; }
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.colors32 = colors; mesh.triangles = indices;
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; trackRenderer = root.gameObject.AddComponent<MeshRenderer>();
            if (!tireTrackMaterial)
            {
                var shader = Shader.Find("GameJam/Fire Truck Tire Tracks");
                if (shader) tireTrackMaterial = ownedTrackMaterial = new Material(shader) { name = "Runtime tire tread material" };
            }
            trackRenderer.sharedMaterial = tireTrackMaterial; trackRenderer.shadowCastingMode = ShadowCastingMode.Off; trackRenderer.receiveShadows = true; trackRenderer.enabled = false;
        }
        void CreateDust()
        {
            var shader = soilDustShader ? soilDustShader : Shader.Find("GameJam/Fire Truck Soil Dust");
            if (dustMaterial) ownedDustMaterial = new Material(dustMaterial) { name = "Runtime fire truck soil dust" };
            else if (shader) ownedDustMaterial = new Material(shader) { name = "Runtime fire truck soil dust" };
            if (!ownedDustMaterial) return;
            if (shader) ownedDustMaterial.shader = shader;
            if (dustTexture && ownedDustMaterial.HasProperty("_BaseMap")) ownedDustMaterial.SetTexture("_BaseMap", dustTexture);
            var go = new GameObject("Contact-driven soil dust"); go.transform.SetParent(root, false); dust = go.AddComponent<ParticleSystem>(); dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = Mathf.Clamp(maximumDustParticles, 32, 2048); main.startSpeed = 0; main.startLifetime = dustLifetime; main.gravityModifier = .035f;
            var emission = dust.emission; emission.enabled = false; var shape = dust.shape; shape.enabled = false;
            var color = dust.colorOverLifetime; color.enabled = true; var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.8f, .12f), new GradientAlphaKey(.6f, .5f), new GradientAlphaKey(0, 1) }); color.color = gradient;
            var size = dust.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .45f), new Keyframe(.25f, 1), new Keyframe(1, 1.8f)));
            var noise = dust.noise; noise.enabled = true; noise.strength = .18f; noise.frequency = .35f; noise.scrollSpeed = .4f;
            var renderer = dust.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = ownedDustMaterial; renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true; renderer.sortMode = ParticleSystemSortMode.Distance; dust.Play(false);
        }
    }
}
