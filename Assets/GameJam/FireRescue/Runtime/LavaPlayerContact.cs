using UnityEngine;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-100)]
    public sealed class LavaPlayerContact : MonoBehaviour
    {
        public float damagePerSecond = 18f;
        public bool InHotLava { get; private set; }
        public float MovementMultiplier => InHotLava ? .5f : 1f;
        CharacterController body;
        FireRescuePlayer player;
        CreatureCombatPlayer health;
        void Awake()
        {
            body = GetComponent<CharacterController>(); player = GetComponent<FireRescuePlayer>();
            health = GetComponent<CreatureCombatPlayer>();
            if (!health) health = gameObject.AddComponent<CreatureCombatPlayer>();
        }
        void Update() => RefreshCollisionPassThrough();
        public void RefreshCollisionPassThrough()
        {
            if (!body || !body.enabled) return;
            // Keep accurate raycast surfaces for the water cannon, but let feet pass into liquid.
            // Reapply after the sequence enables/rebuilds a growing mesh collider.
            foreach (var source in LavaIgnitionSource.ActiveSources)
                if (source && source.heatCollider && source.heatCollider.enabled)
                    Physics.IgnoreCollision(body, source.heatCollider, true);
        }
        void LateUpdate() => TickContact(Time.deltaTime);
        public void TickContact(float dt)
        {
            InHotLava = false;
            if (!body || !body.enabled || !health || (player && (player.IsDriving || player.CinematicLocked))) return;
            Vector3 feet = new Vector3(body.bounds.center.x, body.bounds.min.y, body.bounds.center.z);
            foreach (var source in LavaIgnitionSource.ActiveSources)
            {
                if (!source || !source.IsHotSurfaceActive) continue;
                Bounds bounds = source.heatCollider.bounds;
                if (feet.x < bounds.min.x || feet.x > bounds.max.x || feet.z < bounds.min.z || feet.z > bounds.max.z) continue;
                var ray = new Ray(new Vector3(feet.x, bounds.max.y + 2, feet.z), Vector3.down);
                if (!source.heatCollider.Raycast(ray, out var hit, bounds.size.y + 5)) continue;
                if (feet.y > hit.point.y + .18f || feet.y < hit.point.y - body.bounds.size.y - .5f || !source.IsHotAt(hit.point)) continue;
                InHotLava = true; break;
            }
            if (InHotLava && dt > 0 && float.IsFinite(dt)) health.TakeHeatDamage(damagePerSecond * dt);
        }
        void OnDisable()
        {
            InHotLava = false;
            if (!body) return;
            foreach (var source in LavaIgnitionSource.ActiveSources)
                if (source && source.heatCollider) Physics.IgnoreCollision(body, source.heatCollider, false);
        }
    }
}
