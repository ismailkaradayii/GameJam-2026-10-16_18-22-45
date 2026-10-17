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
            return CanHeat && (!cooling || cooling.IsHotAt(point));
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
                if (building == null || !building.isActiveAndEnabled)
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

            Collider contact = GetContactCollider(building);
            if (contact == null || !contact.enabled || !contact.gameObject.activeInHierarchy)
                return false;

            // Buildings use solid box/capsule/convex colliders. Placing the building first also
            // permits a concave mesh as the lava collider, without igniting across its empty AABB.
            if (!Physics.ComputePenetration(contact, contact.transform.position, contact.transform.rotation,
                    heatCollider, heatCollider.transform.position, heatCollider.transform.rotation,
                    out _, out _))
                return false;

            if (!cooling) cooling = GetComponent<LavaCoolingSurface>();
            if (cooling && !cooling.HasHotContact(contact.bounds)) return false;
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
