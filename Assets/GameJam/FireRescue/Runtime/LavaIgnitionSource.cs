using UnityEngine;
using System.Collections.Generic;

namespace GameJam.FireRescue
{
    /// <summary>Ignites buildings only when their colliders intersect this active lava contact volume.</summary>
    [DisallowMultipleComponent]
    public sealed class LavaIgnitionSource : MonoBehaviour
    {
        [Tooltip("Fit this collider to the actual hot lava, not the bounds of a whole river network.")]
        public Collider heatCollider;
        [Tooltip("Optional generated lava mesh. A replaced mesh is also assigned to the heat MeshCollider.")]
        public MeshFilter surfaceMesh;
        [Tooltip("When present, invisible lava cannot ignite buildings.")]
        public Renderer surfaceRenderer;
        public bool heatEnabled = true;
        [Min(0.05f)] public float scanInterval = 0.25f;
        public LayerMask buildingLayers = ~0;

        private float untilNextScan;
        static readonly List<LavaIgnitionSource> sources = new List<LavaIgnitionSource>();
        public static IReadOnlyList<LavaIgnitionSource> ActiveSources => sources;
        public bool IsHotSurfaceActive => CanHeat;
        LavaCoolingSurface cooling;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSources() => sources.Clear();
        private bool CanHeat => heatEnabled && isActiveAndEnabled && heatCollider != null &&
            heatCollider.enabled && heatCollider.gameObject.activeInHierarchy &&
            (surfaceRenderer == null || (surfaceRenderer.enabled && surfaceRenderer.gameObject.activeInHierarchy));

        private void Reset() => heatCollider = GetComponent<Collider>();

        private void OnEnable()
        {
            if (!sources.Contains(this)) sources.Add(this);
            cooling = GetComponent<LavaCoolingSurface>();
            if (heatCollider == null)
                heatCollider = GetComponent<Collider>();
            if (surfaceMesh == null)
                surfaceMesh = GetComponent<MeshFilter>();
            if (surfaceRenderer == null)
                surfaceRenderer = surfaceMesh != null ? surfaceMesh.GetComponent<Renderer>() : GetComponent<Renderer>();
            RefreshSurfaceMesh();
            untilNextScan = 0f;
        }
        void OnDisable() => sources.Remove(this);
        public bool IsHotAt(Vector3 point)
        {
            if (!cooling) cooling = GetComponent<LavaCoolingSurface>();
            return CanHeat && (!cooling || cooling.IsHotAt(point)) && !LavaCoolingSurface.IsSolidCrustAt(point);
        }

        public bool TouchesHotCollider(Collider contact)
        {
            RefreshSurfaceMesh();
            if (!CanHeat || !contact || !contact.enabled || !contact.gameObject.activeInHierarchy ||
                !heatCollider.bounds.Intersects(contact.bounds)) return false;
            if (!Physics.ComputePenetration(contact, contact.transform.position, contact.transform.rotation,
                heatCollider, heatCollider.transform.position, heatCollider.transform.rotation, out _, out _)) return false;
            return HasHotContact(contact);
        }

        bool HasHotContact(Collider contact)
        {
            Bounds body = contact.bounds, lava = heatCollider.bounds;
            float lowX = Mathf.Max(body.min.x, lava.min.x), highX = Mathf.Min(body.max.x, lava.max.x);
            float lowZ = Mathf.Max(body.min.z, lava.min.z), highZ = Mathf.Min(body.max.z, lava.max.z);
            if (!(heatCollider is MeshCollider mesh) || mesh.convex)
            {
                // Box/convex sources represent a volume, including victims fully submerged
                // below its top face. Surface meshes instead use the precise ray probes below.
                float lowY = Mathf.Max(body.min.y, lava.min.y), highY = Mathf.Min(body.max.y, lava.max.y);
                for (int x = 0; x <= 4; x++) for (int y = 0; y <= 2; y++) for (int z = 0; z <= 4; z++)
                {
                    Vector3 point = new Vector3(Mathf.Lerp(lowX, highX, x / 4f), Mathf.Lerp(lowY, highY, y / 2f), Mathf.Lerp(lowZ, highZ, z / 4f));
                    if ((heatCollider.ClosestPoint(point) - point).sqrMagnitude <= .0025f &&
                        (contact.ClosestPoint(point) - point).sqrMagnitude <= .0025f && IsHotAt(point)) return true;
                }
                return false;
            }
            for (int x = 0; x <= 8; x++) for (int z = 0; z <= 8; z++)
            {
                Vector3 probe = new Vector3(Mathf.Lerp(lowX, highX, x / 8f), lava.max.y + 1f, Mathf.Lerp(lowZ, highZ, z / 8f));
                if (heatCollider.Raycast(new Ray(probe, Vector3.down), out var hit, lava.size.y + 2f) &&
                    (contact.ClosestPoint(hit.point) - hit.point).sqrMagnitude <= .0025f && IsHotAt(hit.point)) return true;
            }
            return false;
        }

        private void Update()
        {
            RefreshSurfaceMesh();
            if (!CanHeat)
                return;
            untilNextScan -= Time.deltaTime;
            if (untilNextScan > 0f)
                return;
            untilNextScan = Mathf.Max(0.05f, scanInterval);
            ScanNow();
        }

        private void OnTriggerEnter(Collider other) => TryIgniteContact(other);
        private void OnTriggerStay(Collider other) => TryIgniteContact(other);

        /// <summary>Periodic overlap also works for stationary colliders with no Rigidbody.</summary>
        public void ScanNow()
        {
            RefreshSurfaceMesh();
            if (!CanHeat)
                return;

            // Iterate the few burnable houses instead of collecting hundreds of scenery rocks
            // inside a winding lava mesh's bounding box, which can overflow a physics hit buffer.
            foreach (BurnableBuilding building in BurnableBuilding.ActiveBuildings)
            {
                // A scan can only start a new fire. In particular, rescued foundations
                // otherwise exhaust every cold-surface ray probe on every lava branch.
                if (building == null || !building.isActiveAndEnabled || building.IsBurning ||
                    building.IsBurnedOut || building.IgnitionBlocked)
                    continue;
                Collider candidate = GetContactCollider(building);
                if (candidate != null && heatCollider.bounds.Intersects(candidate.bounds))
                    TryIgniteContact(candidate);
            }
        }

        public bool TryIgniteContact(Collider other)
        {
            RefreshSurfaceMesh();
            if (!CanHeat || other == null || other == heatCollider || !other.enabled ||
                (buildingLayers.value & (1 << other.gameObject.layer)) == 0)
                return false;
            BurnableBuilding building = other.GetComponentInParent<BurnableBuilding>();
            if (building == null || !building.isActiveAndEnabled)
                return false;
            // Keep the explicit contact query's existing result for a burning house,
            // but reject houses that Ignite cannot affect before any contact physics.
            if (building.IsBurnedOut || (!building.IsBurning && building.IgnitionBlocked))
                return false;

            Collider contact = GetContactCollider(building);
            if (contact == null || !contact.enabled || !contact.gameObject.activeInHierarchy)
                return false;

            // Buildings use solid box/capsule/convex colliders. Placing the building first also
            // permits a concave mesh as the lava collider, without igniting across its empty AABB.
            if (!Physics.ComputePenetration(contact, contact.transform.position, contact.transform.rotation,
                    heatCollider, heatCollider.transform.position, heatCollider.transform.rotation,
                    out _, out _))
                return false;

            if (!HasHotContact(contact)) return false;
            building.Ignite();
            return building.IsBurning;
        }

        private static Collider GetContactCollider(BurnableBuilding building)
        {
            if (building.ignitionBounds != null)
                return building.ignitionBounds;
            // ComputePenetration requires at least one supported convex shape. Never pass two
            // non-convex mesh colliders just because the house already has a detailed mesh.
            Collider candidate = building.GetComponent<Collider>();
            if (candidate is BoxCollider || candidate is SphereCollider || candidate is CapsuleCollider ||
                (candidate is MeshCollider mesh && mesh.convex))
                return candidate;
            return null;
        }

        /// <summary>Use forceRecook after an eruption system edits vertices on an existing mesh instance.</summary>
        public void RefreshSurfaceMesh(bool forceRecook = false)
        {
            if (surfaceMesh == null || !(heatCollider is MeshCollider meshCollider))
                return;
            Mesh currentMesh = surfaceMesh.sharedMesh;
            if (forceRecook || meshCollider.sharedMesh != currentMesh)
            {
                if (forceRecook)
                    meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = currentMesh;
            }
        }
    }
}
