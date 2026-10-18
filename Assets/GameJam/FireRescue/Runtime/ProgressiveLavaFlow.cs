using System;
using System.Collections.Generic;
using BKPureNature;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Reveals a BK lava grid in world metres without changing its authored mesh.</summary>
    [DisallowMultipleComponent]
    public sealed class ProgressiveLavaFlow : MonoBehaviour
    {
        public BK_Lava source;
        public LavaIgnitionSource ignitionSource;
        [Tooltip("Scheduling metadata consumed by the eruption sequencer; Advance does not apply this delay.")]
        [Min(0f)] public float startDelay;
        [Min(0f)] public float speed = 5f;
        public bool reverse;
        [Tooltip("Optional: conform the private reference mesh once during Initialize. Intended for new routes over static terrain.")]
        public Terrain conformToTerrain;
        [Min(0f)] public float groundClearance = 0.2f;
        [Tooltip("The renderer and collider advance together at this interval. Explicit SetDistance is immediate.")]
        [Min(0.02f)] public float meshUpdateInterval = 0.1f;
        [Tooltip("A fully cooled cross-section cuts off this village branch's supply, even after the front has passed it.")]
        public bool canBeStoppedByWater;
        public bool StoppedByCooling { get; private set; }
        public float FlowRate01 { get; private set; } = 1f;
        public float CoolingBarrierDistance { get; private set; } = float.PositiveInfinity;
        public float DownstreamCooling01 { get; private set; }
        public float FlowPhase { get; private set; }
        [Header("Flow ambience")]
        [SerializeField] private AudioSource flowAudioSource;
        [SerializeField] private AudioClip bubblingClip;
        [SerializeField, Range(0f, 1f)] private float bubblingVolume = .5f;
        [SerializeField, Min(.01f)] private float bubblingFadeSpeed = 1.5f;

        public bool IsInitialized { get; private set; }
        public float DistanceTravelled { get; private set; }
        public float Length { get; private set; }
        public float Progress => !IsInitialized ? 0f : Length > Epsilon ? DistanceTravelled / Length : 1f;
        public bool HasArrived => IsInitialized && (Length <= Epsilon || DistanceTravelled >= Length);
        public float VisibleDistance { get; private set; }
        public Bounds FullWorldBounds { get; private set; }
        public Vector3 FrontPosition => PositionAtDistance(VisibleDistance);

        private const float Epsilon = 0.00001f;
        private MeshFilter surface;
        private MeshRenderer surfaceRenderer;
        private MeshCollider surfaceCollider;
        private Mesh referenceMesh;
        private Mesh revealedMesh;
        private Mesh originalSurfaceMesh;
        private Mesh originalColliderMesh;
        private bool originalSourceEnabled;
        private bool originalRendererEnabled;
        private bool originalColliderEnabled;
        private bool originalColliderConvex;
        private bool originalColliderTrigger;
        private bool addedCollider;
        private bool originalHeatEnabled;
        private Collider originalHeatCollider;
        private bool originalHeatColliderEnabled;
        private MeshFilter originalHeatSurface;
        private Renderer originalHeatRenderer;
        private bool capturedReverse;
        private int columnCount;
        private int rowCount;
        private float untilMeshUpdate;
        private LavaCoolingSurface cooling;
        private float incomingSupply = 1f, untilCoolingScan, residualFrom = float.PositiveInfinity;
        private float rateStart = 1f, rateTarget = 1f, rateElapsed = FlowResponseSeconds;
        private sealed class CoolingReach { public float from, to, progress; }
        private readonly List<CoolingReach> coolingReaches = new List<CoolingReach>();
        private bool coolingDirty = true;
        private float[] sectionCapacity, prefixCapacity;
        private const float CoolingScanSpacing = .5f, CoolingWidthSpacing = .25f;
        private const float FlowResponseSeconds = .6f, ResidualCoolingSeconds = 6f;
        private Material[] originalMaterials, controlledMaterials;
        private MaterialPropertyBlock flowProperties, originalProperties;
        private static readonly int FlowPhaseId = Shader.PropertyToID("_FlowPhase");
        private bool audioFlowActive;
        private bool audioPaused;
        private Vector3[] fullVertices;
        private Vector3[] fullNormals;
        private Vector4[] fullTangents;
        private Vector2[] fullUV;
        private Vector2[] fullUV2;
        private Color32[] fullColors;
        private Vector3[] rowCentres;
        private float[] rowDistances;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector4> tangents = new List<Vector4>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<Vector2> uv2 = new List<Vector2>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();

        /// <summary>Call after BK_Lava has generated its mesh (normally from the sequencer's Start).</summary>
        public void Initialize()
        {
            if (IsInitialized)
                return;
            if (!flowAudioSource)
            {
                var emitter = transform.Find("Lava Flow Audio");
                if (!emitter) { var child = new GameObject("Lava Flow Audio"); emitter = child.transform; emitter.SetParent(transform, false); }
                flowAudioSource = emitter.GetComponent<AudioSource>();
                if (!flowAudioSource) flowAudioSource = emitter.gameObject.AddComponent<AudioSource>();
            }
            flowAudioSource.playOnAwake = false;
            flowAudioSource.loop = true;
            flowAudioSource.spatialBlend = 1f;
            flowAudioSource.dopplerLevel = 0f;
            flowAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            flowAudioSource.minDistance = 8f;
            flowAudioSource.maxDistance = 70f;
            if (bubblingClip) { flowAudioSource.clip = bubblingClip; bubblingClip.LoadAudioData(); }
            flowAudioSource.volume = 0f;
            if (source == null)
                source = GetComponent<BK_Lava>();
            if (source == null)
                throw new InvalidOperationException(name + ": ProgressiveLavaFlow requires a BK_Lava source.");
            surface = source.GetComponent<MeshFilter>();
            surfaceRenderer = source.GetComponent<MeshRenderer>();
            if (surface == null || surfaceRenderer == null)
                throw new InvalidOperationException(name + ": lava source has no mesh filter or renderer.");

            originalSurfaceMesh = surface.sharedMesh;
            columnCount = source.m_WidthSegments + 1;
            referenceMesh = CreateReferenceMesh();
            if (referenceMesh != null)
            {
                referenceMesh.name = source.name + " (flow reference)";
                referenceMesh.hideFlags = HideFlags.DontSave;
                fullVertices = referenceMesh.vertices;
                if (conformToTerrain != null && fullVertices.Length > 0)
                {
                    float clearance = IsFinite(groundClearance) ? Mathf.Max(0f, groundClearance) : 0.2f;
                    float terrainY = conformToTerrain.transform.position.y;
                    Vector3[] authored = canBeStoppedByWater ? (Vector3[])fullVertices.Clone() : null;
                    for (int i = 0; i < fullVertices.Length; i++)
                    {
                        Vector3 world = source.transform.TransformPoint(fullVertices[i]);
                        float height = clearance;
                        if (canBeStoppedByWater)
                        {
                            // Keep the authored route; vary only its private banks so village lava
                            // settles into the terrain instead of floating as a rectangular sheet.
                            int column = i % columnCount, row = i / columnCount;
                            Vector3 left = source.transform.TransformPoint(authored[row * columnCount]);
                            Vector3 right = source.transform.TransformPoint(authored[row * columnCount + columnCount - 1]);
                            Vector3 centre = (left + right) * .5f;
                            float bank = .82f + .18f * Mathf.PerlinNoise(row * .63f, 1.7f);
                            world.x = Mathf.Lerp(centre.x, world.x, bank); world.z = Mathf.Lerp(centre.z, world.z, bank);
                            height = Mathf.Lerp(.025f, clearance, Mathf.Sin(column / (columnCount - 1f) * Mathf.PI));
                        }
                        world.y = conformToTerrain.SampleHeight(world) + terrainY + height;
                        fullVertices[i] = source.transform.InverseTransformPoint(world);
                    }
                    referenceMesh.vertices = fullVertices;
                    referenceMesh.RecalculateNormals();
                    if (referenceMesh.uv.Length == fullVertices.Length)
                        referenceMesh.RecalculateTangents();
                    referenceMesh.RecalculateBounds();
                }
                fullNormals = referenceMesh.normals;
                fullTangents = referenceMesh.tangents;
                fullUV = referenceMesh.uv;
                fullUV2 = referenceMesh.uv2;
                fullColors = referenceMesh.colors32;
            }
            else
            {
                fullVertices = Array.Empty<Vector3>();
                fullNormals = Array.Empty<Vector3>();
                fullTangents = Array.Empty<Vector4>();
                fullUV = Array.Empty<Vector2>();
                fullUV2 = Array.Empty<Vector2>();
                fullColors = Array.Empty<Color32>();
            }
            rowCount = columnCount > 0 ? fullVertices.Length / columnCount : 0;
            CacheDistances();
            capturedReverse = reverse;
            originalSourceEnabled = source.enabled;
            originalRendererEnabled = surfaceRenderer.enabled;
            surfaceCollider = source.GetComponent<MeshCollider>();
            if (surfaceCollider == null)
            {
                surfaceCollider = source.gameObject.AddComponent<MeshCollider>();
                addedCollider = true;
            }
            originalColliderMesh = surfaceCollider.sharedMesh;
            originalColliderEnabled = surfaceCollider.enabled;
            originalColliderConvex = surfaceCollider.convex;
            originalColliderTrigger = surfaceCollider.isTrigger;

            if (ignitionSource == null)
                ignitionSource = source.GetComponent<LavaIgnitionSource>();
            if (ignitionSource != null)
            {
                originalHeatEnabled = ignitionSource.heatEnabled;
                originalHeatCollider = ignitionSource.heatCollider;
                originalHeatColliderEnabled = originalHeatCollider != null && originalHeatCollider.enabled;
                originalHeatSurface = ignitionSource.surfaceMesh;
                originalHeatRenderer = ignitionSource.surfaceRenderer;
                ignitionSource.heatEnabled = false;
                if (originalHeatCollider != null && originalHeatCollider != surfaceCollider)
                    originalHeatCollider.enabled = false;
                ignitionSource.heatCollider = surfaceCollider;
                ignitionSource.surfaceMesh = surface;
                ignitionSource.surfaceRenderer = surfaceRenderer;
            }

            // BK retains ownership of its full mesh. Only our separate mesh is edited below.
            source.enabled = false;
            surfaceCollider.enabled = false;
            surfaceCollider.isTrigger = false;
            surfaceCollider.convex = false;
            revealedMesh = new Mesh
            {
                name = source.name + " (progressive flow)",
                hideFlags = HideFlags.DontSave,
                indexFormat = referenceMesh != null ? referenceMesh.indexFormat : UnityEngine.Rendering.IndexFormat.UInt16
            };
            revealedMesh.MarkDynamic();
            InitializeControlledMaterials();
            IsInitialized = true;
            ResetFlow();
        }

        public void ResetFlow()
        {
            if (!cooling) cooling = GetComponent<LavaCoolingSurface>();
            if (cooling) cooling.ResetCooling();
            StoppedByCooling = false;
            CoolingBarrierDistance = residualFrom = float.PositiveInfinity;
            DownstreamCooling01 = FlowPhase = untilCoolingScan = 0f;
            coolingReaches.Clear();
            FlowRate01 = incomingSupply = 1f;
            rateStart = rateTarget = 1f;
            rateElapsed = FlowResponseSeconds;
            coolingDirty = true;
            if (sectionCapacity != null)
                for (int i = 0; i < sectionCapacity.Length; i++) sectionCapacity[i] = prefixCapacity[i] = 1f;
            ApplyFlowPhase();
            SetAudioFlowActive(false, true);
            if (!IsInitialized)
            {
                Initialize();
                return;
            }
            capturedReverse = reverse;
            DistanceTravelled = 0f;
            untilMeshUpdate = 0f;
            ApplyDistance(0f);
        }

        /// <summary>Water/foundation edits invalidate the cached hydraulic cross-sections.</summary>
        public void NotifyCoolingChanged() => coolingDirty = true;

        /// <summary>Completing obsidian forces an immediate supply cut, independent of the mesh cadence.</summary>
        public void RefreshCoolingBarrier()
        {
            if (!canBeStoppedByWater || !IsInitialized || Length <= Epsilon) return;
            if (!cooling) cooling = GetComponent<LavaCoolingSurface>();
            coolingDirty = false;
            untilCoolingScan = .12f;
            if (!cooling || !cooling.HasDirectCooling) return;
            float prefix = 1f;
            for (int i = 0; i < sectionCapacity.Length; i++)
            {
                float distance = Mathf.Min(i * CoolingScanSpacing, Length);
                bool blocked = true;
                float totalHot = 0f, totalWidth = 0f;
                // Sample each real mesh segment, not only its authored columns: a narrow
                // uncooled opening between sparse BK columns must keep the route connected.
                for (int column = 0; column < columnCount - 1; column++)
                {
                    Vector3 left = PositionAtDistance(distance, column / (columnCount - 1f));
                    Vector3 right = PositionAtDistance(distance, (column + 1f) / (columnCount - 1f));
                    float width = Vector3.Distance(left, right);
                    int samples = Mathf.Max(1, Mathf.CeilToInt(width / CoolingWidthSpacing));
                    float segmentHot = 0f;
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        float hot = 1f - cooling.DirectCoolingAt(Vector3.Lerp(left, right, sample / (float)samples));
                        if (hot > 0f) blocked = false;
                        segmentHot += hot * (sample == 0 || sample == samples ? .5f : 1f);
                    }
                    totalHot += segmentHot / samples * width;
                    totalWidth += width;
                }
                sectionCapacity[i] = totalWidth > Epsilon ? Mathf.Clamp01(totalHot / totalWidth) : 1f;
                prefixCapacity[i] = prefix = Mathf.Min(prefix, sectionCapacity[i]);
                if (blocked) CoolingBarrierDistance = Mathf.Min(CoolingBarrierDistance, distance);
            }
            StoppedByCooling = DistanceTravelled + Epsilon >= CoolingBarrierDistance;
            if (StoppedByCooling) DistanceTravelled = Mathf.Max(VisibleDistance, Mathf.Min(DistanceTravelled, CoolingBarrierDistance));
            BeginResidualCooling(StoppedByCooling ? CoolingBarrierDistance : float.PositiveInfinity);
        }

        /// <summary>Advances simulation time; the sequencer owns delays and calling cadence.</summary>
        public void Advance(float deltaTime) => Advance(deltaTime, 1f);

        /// <summary>Zero live supply still ticks cooling on the already visible, stationary lava.</summary>
        public void Advance(float deltaTime, float supply01)
        {
            if (!IsInitialized)
                Initialize();
            if (!IsFinite(deltaTime) || deltaTime <= 0f || Time.timeScale <= 0f)
                return;
            incomingSupply = IsFinite(supply01) ? Mathf.Clamp01(supply01) : 0f;
            untilCoolingScan -= deltaTime;
            if (coolingDirty && untilCoolingScan <= 0f) RefreshCoolingBarrier();
            float validSpeed = IsFinite(speed) ? Mathf.Max(0f, speed) : 0f;
            float desiredRate = incomingSupply * CapacityAt(DistanceTravelled);
            if (StoppedByCooling || incomingSupply <= Epsilon) desiredRate = 0f;
            float phaseStep = AdvanceFlowRate(desiredRate, deltaTime);
            float previousDistance = DistanceTravelled;
            if (!StoppedByCooling && incomingSupply > Epsilon && !HasArrived)
                DistanceTravelled = Mathf.Min(Mathf.Min(Length, CoolingBarrierDistance),
                    DistanceTravelled + validSpeed * phaseStep);
            StoppedByCooling = DistanceTravelled + Epsilon >= CoolingBarrierDistance;
            // Integrating phase, instead of multiplying wall time by the new speed,
            // keeps the BK textures continuous throughout slowing, stopping and pausing.
            if (VisibleDistance > Epsilon || DistanceTravelled > Epsilon)
                FlowPhase += phaseStep;
            ApplyFlowPhase();
            SetAudioFlowActive(VisibleDistance > Epsilon && FlowRate01 > .01f);
            untilMeshUpdate -= deltaTime;
            if (DistanceTravelled > previousDistance + Epsilon &&
                (untilMeshUpdate <= 0f || VisibleDistance <= 0f || HasArrived || StoppedByCooling))
            {
                ApplyDistance(DistanceTravelled);
                untilMeshUpdate = IsFinite(meshUpdateInterval) ? Mathf.Max(0.02f, meshUpdateInterval) : 0.1f;
            }
            if (canBeStoppedByWater && VisibleDistance > Epsilon)
            {
                BeginResidualCooling(incomingSupply <= Epsilon ? 0f : StoppedByCooling ? CoolingBarrierDistance : float.PositiveInfinity);
                foreach (CoolingReach reach in coolingReaches)
                {
                    reach.progress = Mathf.Min(1f, reach.progress + deltaTime / ResidualCoolingSeconds);
                    if (reach.progress >= 1f - .00001f) reach.progress = 1f;
                    if (cooling) cooling.SetDownstreamCooling(reach.from, reach.to, reach.progress);
                }
                if (coolingReaches.Count > 0) DownstreamCooling01 = coolingReaches[coolingReaches.Count - 1].progress;
            }
        }

        float AdvanceFlowRate(float target, float deltaTime)
        {
            if (Mathf.Abs(target - rateTarget) > Epsilon)
            {
                // Every new hydraulic target gets the same 0.6-second transition,
                // including small partial-cooling changes. Retarget from the current
                // rate so repeated water input never jumps the texture phase.
                rateStart = FlowRate01;
                rateTarget = target;
                rateElapsed = 0f;
            }
            float transitionTime = Mathf.Min(deltaTime, Mathf.Max(0f, FlowResponseSeconds - rateElapsed));
            float initialRate = FlowRate01;
            rateElapsed = Mathf.Min(FlowResponseSeconds, rateElapsed + deltaTime);
            FlowRate01 = Mathf.Lerp(rateStart, rateTarget, rateElapsed / FlowResponseSeconds);
            // Exact integral of the linear ramp plus any constant-rate remainder.
            // A large simulation step and small substeps therefore share one phase.
            return (initialRate + FlowRate01) * .5f * transitionTime + rateTarget * (deltaTime - transitionTime);
        }

        void BeginResidualCooling(float from)
        {
            if (from >= residualFrom || from >= VisibleDistance) return;
            // Each newly isolated reach has its own six-second clock. Building a second,
            // earlier barrier must neither reheat nor delay an already cooling reach.
            coolingReaches.Add(new CoolingReach { from = from, to = Mathf.Min(residualFrom, VisibleDistance) });
            residualFrom = from;
            DownstreamCooling01 = 0f;
        }

        float CapacityAt(float distance)
        {
            if (!canBeStoppedByWater || prefixCapacity == null || prefixCapacity.Length == 0) return 1f;
            int index = Mathf.Clamp(Mathf.FloorToInt(distance / CoolingScanSpacing), 0, prefixCapacity.Length - 1);
            return prefixCapacity[index];
        }

        /// <summary>Current feed at a connection, distinct from the historical revealed distance.</summary>
        public float GetSupply01(float progress)
        {
            if (!IsInitialized || !isActiveAndEnabled || !IsFinite(progress)) return 0f;
            float distance = Length * Mathf.Clamp01(progress);
            if (VisibleDistance <= Epsilon || VisibleDistance + .005f < distance || distance + Epsilon >= CoolingBarrierDistance) return 0f;
            return incomingSupply * CapacityAt(distance);
        }

        void InitializeControlledMaterials()
        {
            if (!canBeStoppedByWater || !surfaceRenderer) return;
            Shader shader = Resources.Load<Shader>("LavaCombat/VillageLava");
            if (!shader) return;
            originalMaterials = surfaceRenderer.sharedMaterials;
            controlledMaterials = new Material[originalMaterials.Length];
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                if (!originalMaterials[i]) continue;
                controlledMaterials[i] = new Material(originalMaterials[i])
                { shader = shader, name = originalMaterials[i].name + " (controlled village flow)", hideFlags = HideFlags.DontSave };
            }
            surfaceRenderer.sharedMaterials = controlledMaterials;
            flowProperties = new MaterialPropertyBlock();
            originalProperties = new MaterialPropertyBlock();
            surfaceRenderer.GetPropertyBlock(originalProperties);
        }

        void ApplyFlowPhase()
        {
            if (flowProperties == null || !surfaceRenderer) return;
            surfaceRenderer.GetPropertyBlock(flowProperties);
            flowProperties.SetFloat(FlowPhaseId, FlowPhase);
            surfaceRenderer.SetPropertyBlock(flowProperties);
        }

        private void SetAudioFlowActive(bool active, bool immediate = false)
        {
            audioFlowActive = active && isActiveAndEnabled && bubblingClip != null && flowAudioSource != null;
            if (immediate && flowAudioSource)
            {
                flowAudioSource.Stop();
                flowAudioSource.volume = 0f;
            }
        }

        private void Update()
        {
            if (!flowAudioSource || !bubblingClip) return;
            if (IsInitialized && flowAudioSource.transform != transform) flowAudioSource.transform.position = FrontPosition;
            if (Time.timeScale <= 0f)
            {
                if (!audioPaused && flowAudioSource.isPlaying) flowAudioSource.Pause();
                audioPaused = true;
                return;
            }
            if (audioPaused) { flowAudioSource.UnPause(); audioPaused = false; }
            float target = audioFlowActive ? bubblingVolume * FlowRate01 * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f)) : 0f;
            if (audioFlowActive && !flowAudioSource.isPlaying) flowAudioSource.Play();
            flowAudioSource.volume = Mathf.MoveTowards(flowAudioSource.volume, target, Mathf.Max(.01f, bubblingFadeSpeed) * Time.deltaTime);
            if (!audioFlowActive && flowAudioSource.volume <= .001f && flowAudioSource.isPlaying) flowAudioSource.Stop();
        }

        /// <summary>Immediate deterministic seek, clamped to this route's world-space length.</summary>
        public void SetDistance(float metres)
        {
            if (!IsInitialized)
                Initialize();
            if (float.IsNaN(metres))
                return;
            DistanceTravelled = Mathf.Clamp(metres, 0f, Length);
            ApplyDistance(DistanceTravelled);
            if (canBeStoppedByWater) RefreshCoolingBarrier();
            untilMeshUpdate = IsFinite(meshUpdateInterval) ? Mathf.Max(0.02f, meshUpdateInterval) : 0.1f;
        }

        private Mesh CreateReferenceMesh()
        {
            // Static batching can replace the MeshFilter mesh with a combined buffer. BK's
            // separately retained mesh is the preferred geometry source when it is valid.
            if (IsValidGrid(source.m_Mesh))
                return Instantiate(source.m_Mesh);
            if (IsValidGrid(originalSurfaceMesh))
                return Instantiate(originalSurfaceMesh);

            // Legacy persisted meshes may not match the current row settings. Rebuild only
            // a private generator: calling source.Rebuild would edit its owned/shared mesh.
            GameObject temporary = new GameObject(source.name + " (canonical flow builder)");
            temporary.SetActive(false);
            temporary.hideFlags = HideFlags.HideAndDontSave;
            temporary.transform.SetParent(source.transform, false);
            BK_Lava builder = null;
            Mesh generated = null;
            try
            {
                builder = temporary.AddComponent<BK_Lava>();
                builder.m_Container = source.m_Container;
                builder.m_Width = source.m_Width;
                builder.m_HeightOffset = source.m_HeightOffset;
                builder.m_BorderHeight = source.m_BorderHeight;
                builder.m_LengthSegments = source.m_LengthSegments;
                builder.m_WidthSegments = source.m_WidthSegments;
                builder.UVTiling = source.UVTiling;
                builder.curve = source.curve;
                builder.m_Material = source.m_Material;
                builder.Rebuild();
                generated = builder.m_Mesh;
                columnCount = builder.m_WidthSegments + 1;
                if (!IsValidGrid(generated))
                    throw new InvalidOperationException(source.name + ": canonical BK_Lava fallback did not generate a readable row grid.");
                return Instantiate(generated);
            }
            finally
            {
                // An inactive temporary component may never receive OnDestroy. Explicitly
                // release its generated mesh and detach ownership to avoid leaks/double frees.
                if (builder != null)
                {
                    if (generated == null) generated = builder.m_Mesh;
                    builder.m_Mesh = null;
                    MeshFilter temporaryFilter = builder.GetComponent<MeshFilter>();
                    if (temporaryFilter != null) temporaryFilter.sharedMesh = null;
                }
                DestroyOwnedObject(generated);
                DestroyOwnedObject(temporary);
            }
        }

        private bool IsValidGrid(Mesh mesh)
        {
            if (mesh == null)
                return false;
            if (mesh.vertexCount == 0)
                return true;
            if (!mesh.isReadable || columnCount < 2 || mesh.vertexCount % columnCount != 0 ||
                mesh.vertexCount / columnCount < 2 || mesh.subMeshCount != 1 || mesh.GetTopology(0) != MeshTopology.Triangles)
                return false;

            int rows = mesh.vertexCount / columnCount;
            int[] indices = mesh.triangles;
            if (indices.Length != (rows - 1) * (columnCount - 1) * 6)
                return false;
            int index = 0;
            for (int row = 0; row < rows - 1; row++)
            {
                for (int column = 0; column < columnCount - 1; column++)
                {
                    int current = row * columnCount + column;
                    int next = current + columnCount;
                    if (indices[index++] != current || indices[index++] != next || indices[index++] != current + 1 ||
                        indices[index++] != current + 1 || indices[index++] != next || indices[index++] != next + 1)
                        return false;
                }
            }
            return true;
        }

        private void CacheDistances()
        {
            FullWorldBounds = new Bounds(source.transform.position, Vector3.zero);
            if (fullVertices.Length > 0)
            {
                Bounds bounds = new Bounds(source.transform.TransformPoint(fullVertices[0]), Vector3.zero);
                for (int i = 1; i < fullVertices.Length; i++) bounds.Encapsulate(source.transform.TransformPoint(fullVertices[i]));
                FullWorldBounds = bounds;
            }
            rowCentres = new Vector3[rowCount];
            rowDistances = new float[rowCount];
            Length = 0f;
            for (int row = 0; row < rowCount; row++)
            {
                Vector3 centre = Vector3.zero;
                for (int column = 0; column < columnCount; column++)
                    centre += fullVertices[row * columnCount + column];
                rowCentres[row] = centre / columnCount;
                if (row > 0)
                    Length += Vector3.Distance(source.transform.TransformPoint(rowCentres[row - 1]),
                        source.transform.TransformPoint(rowCentres[row]));
                rowDistances[row] = Length;
            }
            sectionCapacity = new float[Mathf.CeilToInt(Length / CoolingScanSpacing) + 1];
            prefixCapacity = new float[sectionCapacity.Length];
        }

        private void ApplyDistance(float distance)
        {
            VisibleDistance = distance;
            bool visible = Length > Epsilon && distance > Epsilon;
            if (!visible)
            {
                surfaceRenderer.enabled = false;
                surfaceCollider.enabled = false;
                surface.sharedMesh = null;
                surfaceCollider.sharedMesh = null;
                if (ignitionSource != null)
                    ignitionSource.heatEnabled = false;
                return;
            }

            vertices.Clear(); normals.Clear(); tangents.Clear(); uv.Clear(); uv2.Clear(); colors.Clear(); triangles.Clear();
            if (distance >= Length)
            {
                for (int row = 0; row < rowCount; row++)
                    AddRow(row, row, 0f);
            }
            else
            {
                float frontArc = capturedReverse ? Length - distance : distance;
                FindRow(frontArc, out int lower, out float fraction);
                if (capturedReverse)
                {
                    AddRow(lower, lower + 1, fraction);
                    for (int row = lower + 1; row < rowCount; row++)
                        AddRow(row, row, 0f);
                }
                else
                {
                    for (int row = 0; row <= lower; row++)
                        AddRow(row, row, 0f);
                    if (fraction > 0f)
                        AddRow(lower, lower + 1, fraction);
                }
            }

            int visibleRows = vertices.Count / columnCount;
            for (int row = 0; row < visibleRows - 1; row++)
            {
                for (int column = 0; column < columnCount - 1; column++)
                {
                    int current = row * columnCount + column;
                    int next = current + columnCount;
                    triangles.Add(current); triangles.Add(next); triangles.Add(current + 1);
                    triangles.Add(current + 1); triangles.Add(next); triangles.Add(next + 1);
                }
            }
            revealedMesh.Clear();
            revealedMesh.SetVertices(vertices);
            if (normals.Count == vertices.Count) revealedMesh.SetNormals(normals);
            if (tangents.Count == vertices.Count) revealedMesh.SetTangents(tangents);
            if (uv.Count == vertices.Count) revealedMesh.SetUVs(0, uv);
            if (uv2.Count == vertices.Count) revealedMesh.SetUVs(1, uv2);
            if (colors.Count == vertices.Count) revealedMesh.SetColors(colors);
            revealedMesh.SetTriangles(triangles, 0, true);
            if (normals.Count != vertices.Count) revealedMesh.RecalculateNormals();
            if (tangents.Count != vertices.Count && uv.Count == vertices.Count) revealedMesh.RecalculateTangents();
            surface.sharedMesh = revealedMesh;
            surfaceRenderer.enabled = true;
            // This component owns the visible/contact pair. Rebind it directly after every
            // edit, including a reset that detached the collider, rather than depending on
            // an ignition helper's optional surface references to recook our collider.
            surfaceCollider.enabled = true;
            surfaceCollider.sharedMesh = null;
            surfaceCollider.sharedMesh = revealedMesh;
            if (ignitionSource != null)
            {
                ignitionSource.heatCollider = surfaceCollider;
                ignitionSource.surfaceMesh = surface;
                ignitionSource.surfaceRenderer = surfaceRenderer;
                ignitionSource.heatEnabled = isActiveAndEnabled;
            }
            if (cooling) cooling.NotifySurfaceChanged();
        }

        private void AddRow(int first, int second, float fraction)
        {
            for (int column = 0; column < columnCount; column++)
            {
                int a = first * columnCount + column;
                int b = second * columnCount + column;
                vertices.Add(Vector3.LerpUnclamped(fullVertices[a], fullVertices[b], fraction));
                if (fullNormals.Length == fullVertices.Length)
                    normals.Add(Vector3.LerpUnclamped(fullNormals[a], fullNormals[b], fraction).normalized);
                if (fullTangents.Length == fullVertices.Length)
                {
                    Vector4 tangent = Vector4.LerpUnclamped(fullTangents[a], fullTangents[b], fraction);
                    Vector3 direction = new Vector3(tangent.x, tangent.y, tangent.z).normalized;
                    tangents.Add(new Vector4(direction.x, direction.y, direction.z, tangent.w >= 0f ? 1f : -1f));
                }
                if (fullUV.Length == fullVertices.Length)
                    uv.Add(Vector2.LerpUnclamped(fullUV[a], fullUV[b], fraction));
                if (fullUV2.Length == fullVertices.Length)
                    uv2.Add(Vector2.LerpUnclamped(fullUV2[a], fullUV2[b], fraction));
                if (fullColors.Length == fullVertices.Length)
                    colors.Add(Color32.Lerp(fullColors[a], fullColors[b], fraction));
            }
        }

        private void FindRow(float arc, out int lower, out float fraction)
        {
            int left = 0;
            int right = rowCount - 1;
            while (left + 1 < right)
            {
                int middle = (left + right) / 2;
                if (rowDistances[middle] <= arc) left = middle;
                else right = middle;
            }
            lower = Mathf.Min(left, rowCount - 2);
            float segment = rowDistances[lower + 1] - rowDistances[lower];
            fraction = segment > Epsilon ? Mathf.Clamp01((arc - rowDistances[lower]) / segment) : 0f;
        }

        /// <summary>World-space centre of the actual lava mesh, measured from its flow origin.</summary>
        public Vector3 PositionAtDistance(float distance)
        {
            if (source == null) return transform.position;
            if (rowCount == 0) return source.transform.position;
            if (rowCount == 1 || Length <= Epsilon)
                return source.transform.TransformPoint(rowCentres[capturedReverse ? rowCount - 1 : 0]);
            float arc = capturedReverse ? Length - distance : distance;
            FindRow(Mathf.Clamp(arc, 0f, Length), out int row, out float fraction);
            return source.transform.TransformPoint(Vector3.Lerp(rowCentres[row], rowCentres[row + 1], fraction));
        }

        /// <summary>Point on the real bank-to-bank mesh cross-section; 0 and 1 are its edges.</summary>
        public Vector3 PositionAtDistance(float distance, float cross01)
        {
            if (source == null || rowCount < 2 || columnCount < 2) return PositionAtDistance(distance);
            float arc = capturedReverse ? Length - distance : distance;
            FindRow(Mathf.Clamp(arc, 0f, Length), out int row, out float fraction);
            float across = Mathf.Clamp01(cross01) * (columnCount - 1);
            int column = Mathf.Min(Mathf.FloorToInt(across), columnCount - 2);
            Vector3 a = Vector3.Lerp(fullVertices[row * columnCount + column], fullVertices[(row + 1) * columnCount + column], fraction);
            Vector3 b = Vector3.Lerp(fullVertices[row * columnCount + column + 1], fullVertices[(row + 1) * columnCount + column + 1], fraction);
            return source.transform.TransformPoint(Vector3.Lerp(a, b, across - column));
        }

        /// <summary>Nearest world-space route arc, measured in the direction of travel.</summary>
        public float DistanceAtPoint(Vector3 point)
        {
            if (!IsInitialized || rowCount < 2 || !source) return 0f;
            float best = float.PositiveInfinity, arc = 0f;
            for (int row = 0; row < rowCount - 1; row++)
            {
                Vector3 a = source.transform.TransformPoint(rowCentres[row]);
                Vector3 b = source.transform.TransformPoint(rowCentres[row + 1]);
                Vector3 segment = b - a;
                float fraction = segment.sqrMagnitude > Epsilon ? Mathf.Clamp01(Vector3.Dot(point - a, segment) / segment.sqrMagnitude) : 0f;
                float distance = (point - Vector3.Lerp(a, b, fraction)).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                arc = Mathf.Lerp(rowDistances[row], rowDistances[row + 1], fraction);
            }
            return capturedReverse ? Length - arc : arc;
        }

        private void OnEnable()
        {
            if (IsInitialized && ignitionSource != null)
                ignitionSource.heatEnabled = VisibleDistance > Epsilon && Length > Epsilon;
        }

        private void OnDisable()
        {
            audioFlowActive = false;
            audioPaused = false;
            if (flowAudioSource) { flowAudioSource.volume = 0f; flowAudioSource.Stop(); }
            if (IsInitialized && ignitionSource != null)
                ignitionSource.heatEnabled = false;
        }

        private void OnDestroy()
        {
            if (IsInitialized)
            {
                if (surface != null && (surface.sharedMesh == revealedMesh || surface.sharedMesh == null))
                    surface.sharedMesh = originalSurfaceMesh;
                if (surfaceRenderer != null)
                {
                    surfaceRenderer.enabled = originalRendererEnabled;
                    if (originalMaterials != null)
                    {
                        surfaceRenderer.sharedMaterials = originalMaterials;
                        surfaceRenderer.SetPropertyBlock(originalProperties);
                    }
                }
                if (surfaceCollider != null)
                {
                    surfaceCollider.sharedMesh = null;
                    surfaceCollider.convex = originalColliderConvex;
                    surfaceCollider.isTrigger = originalColliderTrigger;
                    surfaceCollider.sharedMesh = originalColliderMesh;
                    surfaceCollider.enabled = originalColliderEnabled;
                }
                if (ignitionSource != null)
                {
                    ignitionSource.heatEnabled = originalHeatEnabled;
                    ignitionSource.heatCollider = originalHeatCollider;
                    ignitionSource.surfaceMesh = originalHeatSurface;
                    ignitionSource.surfaceRenderer = originalHeatRenderer;
                }
                if (originalHeatCollider != null && originalHeatCollider != surfaceCollider)
                    originalHeatCollider.enabled = originalHeatColliderEnabled;
                if (addedCollider && surfaceCollider != null)
                    DestroyOwnedObject(surfaceCollider);
                if (source != null)
                    source.enabled = originalSourceEnabled;
            }
            DestroyOwnedObject(revealedMesh);
            DestroyOwnedObject(referenceMesh);
            if (controlledMaterials != null)
                foreach (Material material in controlledMaterials) DestroyOwnedObject(material);
        }

        private static void DestroyOwnedObject(UnityEngine.Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
