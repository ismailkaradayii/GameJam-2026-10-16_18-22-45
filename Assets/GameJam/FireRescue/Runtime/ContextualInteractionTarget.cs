using UnityEngine;

namespace GameJam.FireRescue
{
    public enum ContextualInteractionKind
    {
        Pistol, Door, HouseExit, TruckEntry, TruckExit, Resident, CallHouse, Shelter,
        LakeFill, HosePickup, HoseDrop, HoseSocket, BuildingFire, VillageAlarm
    }
    public enum InteractionInput { None, Interact, ExitVehicle, CallVillagers, RetractHose, Pump, Spray }

    public struct InteractionOffer
    {
        public ContextualInteractionTarget target;
        public string title, detail;
        public InteractionInput input;
        public bool blocked, showProgress;
        public float progress;
    }

    /// <summary>Lightweight scene anchor. One shared Canvas presents all of these targets.</summary>
    [DisallowMultipleComponent]
    public class ContextualInteractionTarget : MonoBehaviour
    {
        public ContextualInteractionKind kind;
        public Transform anchor, visibilityRoot;
        public Vector3 localPoint;
        [Min(1f)] public float maximumViewDistance = 15f;
        [Range(5f, 85f)] public float maximumLookAngle = 30f;
        [Range(0f, 1f)] public float priority = .5f;
        [Min(0f)] public float labelHeight = .35f;
        public InteractiveDoor door;
        public EvacuationResident resident;
        public BurnableBuilding building;
        public VillageAlarmButton alarm;

        public virtual Vector3 FocusPosition(ContextualInteractionUI context)
        {
            if (kind == ContextualInteractionKind.Door && door && door.leafCollider)
                return door.leafCollider.bounds.center;
            if (kind == ContextualInteractionKind.BuildingFire && building && building.ignitionBounds && context.player.viewCamera)
                return building.ignitionBounds.ClosestPoint(context.player.viewCamera.transform.position);
            return (anchor ? anchor : transform).TransformPoint(localPoint);
        }

        // A new mechanic can subclass this provider without adding another UI or input loop.
        public virtual bool TryDescribe(ContextualInteractionUI context, out InteractionOffer offer)
            => context.Describe(this, out offer);
    }
}
