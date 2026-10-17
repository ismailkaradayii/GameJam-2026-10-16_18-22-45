using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Transfers the scene pistol into the existing digit-1 weapon slot.</summary>
    [DisallowMultipleComponent]
    public sealed class WeaponPickup : MonoBehaviour
    {
        public FireRescuePlayer player;
        public GameObject visual;
        [Min(.5f)] public float pickupDistance = 6.5f;
        public bool IsCollected { get; private set; }
        public event System.Action<WeaponPickup> Collected;
        readonly RaycastHit[] sightHits = new RaycastHit[32];
        public bool CanPickUp
        {
            get
            {
                if (IsCollected || !player || player.IsDriving || player.GameplaySuspended || !player.viewCamera) return false;
                Vector3 origin = player.viewCamera.transform.position, delta = transform.position - origin;
                if (delta.sqrMagnitude > pickupDistance * pickupDistance) return false;
                // A cabinet can own the final few centimetres of the ray, but a wall cannot.
                int count = Physics.RaycastNonAlloc(origin, delta.normalized, sightHits, Mathf.Max(0, delta.magnitude - .45f), ~0, QueryTriggerInteraction.Ignore);
                if (count == sightHits.Length) return false;
                for (int i = 0; i < count; i++)
                    if (!sightHits[i].transform.IsChildOf(player.transform) && !sightHits[i].transform.IsChildOf(transform)) return false;
                return true;
            }
        }
        public bool TryPickUp()
        {
            if (!CanPickUp || !player.AcquirePistol()) return false;
            IsCollected = true;
            var polish = player.GetComponent<PlayerPolishEffects>();
            if (polish) polish.PlayPickup(transform.position);
            if (visual) visual.SetActive(false);
            foreach (var light in GetComponentsInChildren<Light>()) light.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Collected?.Invoke(this);
            return true;
        }
    }
}
