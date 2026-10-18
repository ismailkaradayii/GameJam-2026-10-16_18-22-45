using UnityEngine;

namespace GameJam.FireRescue
{
    public sealed class AssemblyPointMarker : MonoBehaviour
    {
        public EvacuationMission mission;
        public TextMesh label;
        public LineRenderer boundary;
        public Renderer[] slotMarkers;
        public TextMesh reverseLabel;
        public Renderer[] occupancyLights;
        public Color waitingColor = new Color(1f, .72f, .18f);
        public Color readyColor = new Color(.25f, 1f, .62f);
        MaterialPropertyBlock boundaryProperties;
        MaterialPropertyBlock markerProperties;
        float groundRadius = -1f;

        // The outer ground inlays are part of the visible yellow boundary. Measure them
        // in world metres so interaction reach follows the authored ring, not a separate radius.
        public float GroundRadius
        {
            get
            {
                if (groundRadius >= 0f) return groundRadius;
                if (!mission || !mission.assemblyPoint || !boundary || boundary.positionCount < 3) return 0f;
                Vector3 center = mission.assemblyPoint.position;
                float radius = 0f;
                for (int i = 0; i < boundary.positionCount; i++)
                {
                    Vector3 point = boundary.GetPosition(i);
                    if (!boundary.useWorldSpace) point = boundary.transform.TransformPoint(point);
                    radius = Mathf.Max(radius, EvacuationResident.FlatDistance(center, point));
                }
                radius += boundary.widthMultiplier * .5f;
                var inlays = transform.Find("Grounded amber wayfinding");
                if (inlays)
                    foreach (var renderer in inlays.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Bounds bounds = renderer.localBounds;
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                            radius = Mathf.Max(radius, EvacuationResident.FlatDistance(center, renderer.transform.TransformPoint(corner)));
                        }
                    }
                return groundRadius = radius;
            }
        }

        void LateUpdate()
        {
            bool visible = mission && mission.IsActive && !mission.EscortStarted;
            if (boundary) boundary.enabled = visible;
            if (label)
            {
                // This lettering belongs to a freestanding, two-sided sign, never a billboard.
                label.gameObject.SetActive(true);
                label.text = !mission ? "GATHER HERE" : mission.EscortStarted ? "FOLLOW THE GROUP TO SHELTER" :
                    mission.AssembledCount + " / " + mission.TotalCount + "  ASSEMBLED\n" +
                    (mission.AllAtAssembly ? mission.InteractLabel + "  LEAD EVERYONE TO SHELTER" :
                     mission.CanDepositGroup ? mission.InteractLabel + "  LEAVE THE GROUP HERE" :
                     mission.IsDepositing ? "THE GROUP IS TAKING ITS PLACES" : "BRING THE GROUPS HERE");
                label.color = mission && mission.AllAtAssembly ? readyColor : new Color(.95f, .91f, .77f);
                if (reverseLabel) { reverseLabel.text = label.text; reverseLabel.color = label.color; }
            }
            if (boundary)
            {
                Color color = mission && mission.AllAtAssembly ? readyColor : waitingColor;
                boundary.startColor = boundary.endColor = color;
                if (boundaryProperties == null) boundaryProperties = new MaterialPropertyBlock();
                boundaryProperties.SetColor("_BaseColor", color);
                boundary.SetPropertyBlock(boundaryProperties);
            }
            if (markerProperties == null) markerProperties = new MaterialPropertyBlock();
            if (slotMarkers != null) for (int i = 0; i < slotMarkers.Length; i++)
            {
                bool occupied = false;
                if (mission && mission.residents != null)
                    foreach (var resident in mission.residents)
                        if (resident && resident.formationIndex == i && resident.IsAtAssembly) { occupied = true; break; }
                Color color = occupied ? readyColor : waitingColor * .7f;
                color.a = 1f;
                markerProperties.SetColor("_BaseColor", color);
                if (slotMarkers[i]) { slotMarkers[i].enabled = visible; slotMarkers[i].SetPropertyBlock(markerProperties); }
                if (occupancyLights != null && i < occupancyLights.Length && occupancyLights[i])
                    occupancyLights[i].SetPropertyBlock(markerProperties);
            }
        }
    }
}
