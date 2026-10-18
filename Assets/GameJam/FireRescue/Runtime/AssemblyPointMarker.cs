using UnityEngine;

namespace GameJam.FireRescue
{
    public sealed class AssemblyPointMarker : MonoBehaviour
    {
        public EvacuationMission mission;
        public TextMesh label;
        public LineRenderer boundary;
        public Renderer[] slotMarkers;
        public Color waitingColor = new Color(1f, .72f, .18f);
        public Color readyColor = new Color(.25f, 1f, .62f);
        MaterialPropertyBlock boundaryProperties;
        void LateUpdate()
        {
            bool visible = mission && mission.IsActive && !mission.EscortStarted;
            if (boundary) boundary.enabled = visible;
            if (label)
            {
                label.gameObject.SetActive(visible);
                if (visible)
                {
                    label.text = "ASSEMBLY POINT\n" + mission.AssembledCount + " / " + mission.TotalCount +
                        (mission.AllAtAssembly ? "\n" + mission.InteractLabel + " LEAD EVERYONE TO SHELTER" :
                         mission.IsDepositing ? "\nWAIT FOR THE GROUP" :
                         mission.CanDepositGroup ? "\n" + mission.InteractLabel + " LEAVE THE GROUP HERE" : "\nBRING THE GROUPS HERE");
                    label.color = mission.AllAtAssembly ? readyColor : waitingColor;
                    if (mission.player && mission.player.viewCamera)
                        label.transform.rotation = Quaternion.LookRotation(label.transform.position - mission.player.viewCamera.transform.position);
                }
            }
            if (boundary)
            {
                Color color = mission && mission.AllAtAssembly ? readyColor : waitingColor;
                boundary.startColor = boundary.endColor = color;
                if (boundaryProperties == null) boundaryProperties = new MaterialPropertyBlock();
                boundaryProperties.SetColor("_BaseColor", color);
                boundary.SetPropertyBlock(boundaryProperties);
            }
            if (slotMarkers != null) foreach (var marker in slotMarkers) if (marker) marker.enabled = visible;
        }
    }
}
