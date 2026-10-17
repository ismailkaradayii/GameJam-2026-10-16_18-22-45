using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>A finite water body. The intake must be inside this volume and below its surface.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WaterSourceVolume : MonoBehaviour
    {
        public BoxCollider volume;
        [Tooltip("Optional water plane. Its world Y position is the water surface.")]
        public Transform waterSurface;
        public float surfaceY;
        [Min(0f)] public float surfaceTolerance = 0.1f;
        [Tooltip("Reject parts of the box where Unity terrain rises out of the water.")]
        public bool rejectDryTerrain = true;

        private static readonly List<WaterSourceVolume> Sources = new List<WaterSourceVolume>();
        static Terrain[] cachedTerrains;
        static int terrainCacheFrame = -1;
        public static IReadOnlyList<WaterSourceVolume> ActiveSources => Sources;
        public float SurfaceY => waterSurface != null ? waterSurface.position.y : surfaceY;
        WaterSurfaceInteraction surfaceEffects;
        public WaterSurfaceInteraction Effects
        {
            get
            {
                if (!surfaceEffects) surfaceEffects = GetComponent<WaterSurfaceInteraction>();
                if (!surfaceEffects) surfaceEffects = gameObject.AddComponent<WaterSurfaceInteraction>();
                return surfaceEffects;
            }
        }

        public bool ContainsSurface(Vector3 point)
        {
            point.y = SurfaceY - .025f;
            return ContainsIntake(point);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSources() { Sources.Clear(); cachedTerrains = null; terrainCacheFrame = -1; }

        public static bool IsSurfaceCollider(Collider collider)
        {
            if (!collider) return false;
            for (int i = 0; i < Sources.Count; i++)
                if (Sources[i] && Sources[i].waterSurface && collider.transform == Sources[i].waterSurface) return true;
            return false;
        }

        private void Reset()
        {
            volume = GetComponent<BoxCollider>();
            volume.isTrigger = true;
            surfaceY = transform.position.y;
        }

        private void OnEnable()
        {
            if (volume == null)
                volume = GetComponent<BoxCollider>();
            if (!Sources.Contains(this))
                Sources.Add(this);
        }

        private void OnDisable() => Sources.Remove(this);

        public bool ContainsIntake(Vector3 point)
        {
            if (!isActiveAndEnabled || volume == null || !volume.enabled ||
                point.y > SurfaceY + surfaceTolerance)
                return false;

            // Test in the collider's own coordinates, so a rotated lake is not enlarged to its world AABB.
            Vector3 local = volume.transform.InverseTransformPoint(point) - volume.center;
            Vector3 half = volume.size * 0.5f;
            if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y || Mathf.Abs(local.z) > half.z)
                return false;

            if (rejectDryTerrain)
            {
                // activeTerrains returns an array. Reuse it across all ripple vertices and ballistic samples this frame.
                if (cachedTerrains == null || terrainCacheFrame != Time.frameCount)
                { cachedTerrains = Terrain.activeTerrains; terrainCacheFrame = Time.frameCount; }
                foreach (Terrain terrain in cachedTerrains)
                {
                    if (terrain == null || terrain.terrainData == null)
                        continue;
                    Vector3 offset = point - terrain.transform.position;
                    Vector3 size = terrain.terrainData.size;
                    if (offset.x < 0f || offset.z < 0f || offset.x > size.x || offset.z > size.z)
                        continue;
                    float groundY = terrain.SampleHeight(point) + terrain.transform.position.y;
                    if (groundY >= SurfaceY - 0.02f || point.y < groundY - 0.1f)
                        return false;
                }
            }

            return true;
        }

        public static bool TryFindSource(Vector3 intake, out WaterSourceVolume source)
        {
            for (int i = 0; i < Sources.Count; i++)
            {
                WaterSourceVolume candidate = Sources[i];
                if (candidate != null && candidate.ContainsIntake(intake))
                {
                    source = candidate;
                    return true;
                }
            }
            source = null;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (volume == null)
                return;
            Gizmos.color = new Color(0.1f, 0.7f, 1f, 0.7f);
            Gizmos.matrix = volume.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(volume.center, volume.size);
        }
    }
}
