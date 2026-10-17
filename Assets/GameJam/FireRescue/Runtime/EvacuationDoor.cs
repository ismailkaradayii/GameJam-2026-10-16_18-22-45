using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>The authored door leaf swings about its actual hinge while residents react.</summary>
    public sealed class EvacuationDoor : MonoBehaviour
    {
        public Transform hinge;
        public float openAngle = -105f;
        Quaternion closed;
        float openness;
        bool waitingForSafeDoor;
        float nextDoorAttempt;
        public bool IsOpen { get; private set; }
        void Awake() { if (hinge) closed = hinge.localRotation; }
        public void Open()
        {
            IsOpen = true;
            var door = hinge ? hinge.GetComponent<InteractiveDoor>() : null;
            waitingForSafeDoor = door && !door.IsOpen;
            if (waitingForSafeDoor && !door.IsMoving) { door.TryToggle(); nextDoorAttempt = Time.time + .25f; }
        }
        void Update()
        {
            if (!hinge || !IsOpen) return;
            var door = hinge.GetComponent<InteractiveDoor>();
            if (door)
            {
                if (door.IsOpen) waitingForSafeDoor = false;
                if (waitingForSafeDoor && !door.IsMoving && Time.timeScale > 0 && Time.time >= nextDoorAttempt)
                { door.TryToggle(); nextDoorAttempt = Time.time + .25f; }
                return;
            }
            openness = Mathf.MoveTowards(openness, 1f, Time.deltaTime * 1.7f);
            hinge.localRotation = closed * Quaternion.Euler(0f, Mathf.SmoothStep(0, 1, openness) * openAngle, 0f);
        }
    }
}
