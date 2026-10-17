using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace BKPureNature
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BK_Lava : MonoBehaviour
    {
        private const float SegmentReferenceLength = 8f;
        private const float DirectionEpsilon = 0.000001f;

        [SerializeField] public SplineContainer m_Container;

        [HideInInspector] public float m_Width = 1f;
        [HideInInspector] public float m_HeightOffset;
        [HideInInspector] public float m_BorderHeight = 0.2f;
        [HideInInspector] public int m_LengthSegments = 4;
        [HideInInspector] public int m_WidthSegments = 4;
        [HideInInspector] public Vector2 UVTiling = Vector2.one;
        [HideInInspector] public float curve = 1f;
        [HideInInspector] public Material m_Material;

        [HideInInspector] public Mesh m_Mesh;

        private bool m_OwnsMesh;
        private bool m_RebuildRequested = true;
        private Mesh m_VerifiedMesh;
        private Matrix4x4 m_LastContainerMatrix;
        private Matrix4x4 m_LastPathMatrix;
        private Spline m_LastSpline;

        private void Reset()
        {
            FindSplineContainer();
            ClampSettings();
            RequestRebuild();
        }

        private void Awake()
        {
            FindSplineContainer();
            bool meshCreated = EnsureMesh();

            if (meshCreated || m_Mesh.vertexCount == 0)
                Rebuild();
            else
                CacheState(TryGetValidSpline(out Spline spline) ? spline : null);
        }

        private void OnEnable()
        {
            Spline.Changed -= OnSplineChanged;
            Spline.Changed += OnSplineChanged;

            FindSplineContainer();
            bool meshCreated = EnsureMesh();

            if (m_RebuildRequested || meshCreated || m_Mesh.vertexCount == 0 || HaveTransformsChanged())
                Rebuild();
        }

        private void OnDisable()
        {
            Spline.Changed -= OnSplineChanged;
            RequestRebuild();
        }

        private void OnDestroy()
        {
            Spline.Changed -= OnSplineChanged;
            DestroyGeneratedMesh();
        }

        private void OnValidate()
        {
            ClampSettings();
            RequestRebuild();
        }

        private void OnDidApplyAnimationProperties()
        {
            RequestRebuild();
        }

        private void Update()
        {
            if (m_RebuildRequested || HaveTransformsChanged())
                Rebuild();
        }

        public void RequestRebuild()
        {
            m_RebuildRequested = true;
        }

        public void Rebuild()
        {
            m_RebuildRequested = false;
            ClampSettings();
            FindSplineContainer();
            EnsureMesh();

            if (!TryGetValidSpline(out Spline spline))
            {
                ClearMesh();
                CacheState(null);
                return;
            }

            float splineLength = Mathf.Max(0f, m_Container.CalculateLength());
            if (splineLength <= Mathf.Epsilon)
            {
                ClearMesh();
                CacheState(spline);
                return;
            }

            int lengthSegmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt((splineLength / SegmentReferenceLength) * m_LengthSegments));

            int rowCount = lengthSegmentCount + 1;
            int columnCount = m_WidthSegments + 1;
            int vertexCount = rowCount * columnCount;

            Vector3[] centerPositions = new Vector3[rowCount];
            Vector3[] sideDirections = new Vector3[rowCount];
            float[] distances = new float[rowCount];

            Vector3 previousSide = Vector3.right;
            Vector3 previousTangent = Vector3.forward;
            Vector3 previousWorldPosition = Vector3.zero;

            for (int row = 0; row < rowCount; row++)
            {
                float t = row / (float)lengthSegmentCount;
                Vector3 position = (Vector3)spline.EvaluatePosition(t);
                Vector3 tangent = (Vector3)spline.EvaluateTangent(t);

                if (tangent.sqrMagnitude <= DirectionEpsilon)
                {
                    // Linear spline knots have zero endpoint derivatives. Establish
                    // their actual travel direction before choosing a bank orientation;
                    // an arbitrary +Z fallback can turn the entire surface upside down.
                    float sample = Mathf.Max(.0001f, .25f / lengthSegmentCount);
                    tangent = (Vector3)spline.EvaluatePosition(Mathf.Min(1f, t + sample)) -
                        (Vector3)spline.EvaluatePosition(Mathf.Max(0f, t - sample));
                }
                tangent = tangent.sqrMagnitude > DirectionEpsilon ? tangent.normalized : previousTangent;

                Vector3 side = Vector3.Cross(Vector3.up, tangent);
                if (side.sqrMagnitude <= DirectionEpsilon)
                {
                    side = previousSide;
                }
                else
                {
                    side.Normalize();
                    if (row > 0 && Vector3.Dot(side, previousSide) < 0f)
                        side = -side;
                }

                Vector3 worldPosition = m_Container.transform.TransformPoint(position);
                if (row > 0)
                    distances[row] = distances[row - 1] + Vector3.Distance(previousWorldPosition, worldPosition);

                centerPositions[row] = position;
                sideDirections[row] = side;
                previousWorldPosition = worldPosition;
                previousSide = side;
                previousTangent = tangent;
            }

            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            Color32[] colors = new Color32[vertexCount];
            int[] triangles = new int[lengthSegmentCount * m_WidthSegments * 6];

            Matrix4x4 containerToPath =
                transform.worldToLocalMatrix * m_Container.transform.localToWorldMatrix;

            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    float u = column / (float)m_WidthSegments;
                    float lateralOffset = (u - 0.5f) * m_Width;
                    float verticalOffset = m_HeightOffset + EvaluateBorderProfile(u);

                    Vector3 containerLocalPosition =
                        centerPositions[row]
                        + sideDirections[row] * lateralOffset
                        + Vector3.up * verticalOffset;

                    int vertexIndex = row * columnCount + column;
                    vertices[vertexIndex] = containerToPath.MultiplyPoint3x4(containerLocalPosition);
                    uvs[vertexIndex] = new Vector2(
                        u * UVTiling.x * m_Width,
                        distances[row] * UVTiling.y);
                    colors[vertexIndex] = new Color32(0, 0, 0, 255);
                }
            }

            int triangleIndex = 0;
            for (int row = 0; row < lengthSegmentCount; row++)
            {
                int currentRow = row * columnCount;
                int nextRow = (row + 1) * columnCount;

                for (int column = 0; column < m_WidthSegments; column++)
                {
                    int current = currentRow + column;
                    int next = nextRow + column;

                    triangles[triangleIndex++] = current;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = current + 1;

                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = next + 1;
                }
            }

            m_Mesh.Clear();
            m_Mesh.indexFormat = vertexCount > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            m_Mesh.vertices = vertices;
            m_Mesh.uv = uvs;
            m_Mesh.colors32 = colors;
            m_Mesh.triangles = triangles;
            m_Mesh.RecalculateNormals();
            m_Mesh.RecalculateTangents();
            m_Mesh.RecalculateBounds();

            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (m_Material != null && meshRenderer.sharedMaterial != m_Material)
                meshRenderer.sharedMaterial = m_Material;

            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = m_Mesh;
            }

            CacheState(spline);
        }

        private float EvaluateBorderProfile(float normalizedWidth)
        {
            float edgeFactor = Mathf.Abs(normalizedWidth - 0.5f) * 2f;
            float curveFactor = Mathf.Lerp(1f - curve * 2f, 1f, edgeFactor);
            return -m_BorderHeight * curveFactor * edgeFactor;
        }

        private void FindSplineContainer()
        {
            if (m_Container != null)
                return;

            m_Container = GetComponent<SplineContainer>();
            if (m_Container == null)
                m_Container = GetComponentInParent<SplineContainer>();
        }

        private bool TryGetValidSpline(out Spline spline)
        {
            spline = null;

            if (m_Container == null)
                return false;

            spline = m_Container.Spline;
            return spline != null && spline.Count >= 2;
        }

        private void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
        {
            if (m_Container != null && m_Container.Spline == spline)
                RequestRebuild();
        }

        private bool EnsureMesh()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                return false;

            bool needsNewMesh = m_Mesh == null
                || !m_Mesh.isReadable
                || (m_VerifiedMesh != m_Mesh && IsMeshSharedByAnotherLava(m_Mesh));

            if (needsNewMesh)
            {
                m_Mesh = new Mesh
                {
                    name = $"BK_LavaMesh_{GetInstanceID()}"
                };
                m_OwnsMesh = true;
            }

            if (m_VerifiedMesh != m_Mesh)
                m_Mesh.MarkDynamic();

            m_VerifiedMesh = m_Mesh;

            if (meshFilter.sharedMesh != m_Mesh)
                meshFilter.sharedMesh = m_Mesh;

            return needsNewMesh;
        }

        private bool IsMeshSharedByAnotherLava(Mesh mesh)
        {
            if (mesh == null)
                return false;

#if UNITY_2023_1_OR_NEWER
            BK_Lava[] lavaComponents = FindObjectsByType<BK_Lava>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
            BK_Lava[] lavaComponents = FindObjectsOfType<BK_Lava>(true);
#endif

            foreach (BK_Lava lava in lavaComponents)
            {
                if (lava != null && lava != this && lava.m_Mesh == mesh)
                    return true;
            }

            return false;
        }

        private void ClearMesh()
        {
            if (m_Mesh != null)
                m_Mesh.Clear();

            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null && meshCollider.sharedMesh == m_Mesh)
                meshCollider.sharedMesh = null;
        }

        private void DestroyGeneratedMesh()
        {
            if (!m_OwnsMesh || m_Mesh == null)
                return;

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh == m_Mesh)
                meshFilter.sharedMesh = null;

            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null && meshCollider.sharedMesh == m_Mesh)
                meshCollider.sharedMesh = null;

            if (Application.isPlaying)
                Destroy(m_Mesh);
            else
                DestroyImmediate(m_Mesh);

            m_Mesh = null;
            m_VerifiedMesh = null;
            m_OwnsMesh = false;
        }

        private bool HaveTransformsChanged()
        {
            if (m_Container == null)
                return false;

            return m_LastContainerMatrix != m_Container.transform.localToWorldMatrix
                || m_LastPathMatrix != transform.localToWorldMatrix
                || m_LastSpline != m_Container.Spline;
        }

        private void CacheState(Spline spline)
        {
            m_RebuildRequested = false;
            m_LastSpline = spline;
            m_LastPathMatrix = transform.localToWorldMatrix;
            m_LastContainerMatrix = m_Container != null
                ? m_Container.transform.localToWorldMatrix
                : Matrix4x4.identity;
        }

        private void ClampSettings()
        {
            if (!IsFinite(m_Width))
                m_Width = 1f;
            if (!IsFinite(m_HeightOffset))
                m_HeightOffset = 0f;
            if (!IsFinite(m_BorderHeight))
                m_BorderHeight = 0.2f;
            if (!IsFinite(UVTiling.x))
                UVTiling.x = 1f;
            if (!IsFinite(UVTiling.y))
                UVTiling.y = 1f;
            if (!IsFinite(curve))
                curve = 1f;

            m_Width = Mathf.Max(0.1f, m_Width);
            m_BorderHeight = Mathf.Max(0f, m_BorderHeight);
            m_LengthSegments = Mathf.Clamp(m_LengthSegments, 1, 7);
            m_WidthSegments = Mathf.Clamp(m_WidthSegments, 1, 7);
            UVTiling.x = Mathf.Max(0.01f, UVTiling.x);
            UVTiling.y = Mathf.Max(0.01f, UVTiling.y);
            curve = Mathf.Clamp01(curve);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
