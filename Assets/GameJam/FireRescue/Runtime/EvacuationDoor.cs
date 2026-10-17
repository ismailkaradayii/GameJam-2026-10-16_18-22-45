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
        public bool IsOpen { get; private set; }
        void Awake() { if (hinge) closed = hinge.localRotation; }
        public void Open() => IsOpen = true;
        void Update()
        {
            if (!hinge || !IsOpen) return;
            openness = Mathf.MoveTowards(openness, 1f, Time.deltaTime * 1.7f);
            hinge.localRotation = closed * Quaternion.Euler(0f, Mathf.SmoothStep(0, 1, openness) * openAngle, 0f);
        }
    }
}
