using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Shared hit target for the pistol and water cannon on an animated lava creature.</summary>
    [DisallowMultipleComponent]
    public sealed class LavaCreatureActor : MonoBehaviour
    {
        public LavaCreatureAutoJump jump;
        [Min(1f)] public float maxHealth = 100f;
        [Min(0f)] public float waterDamagePerLitre = 2f;

        public float Health => initialized ? health : ValidMaximum;
        public float Health01 => Mathf.Clamp01(Health / ValidMaximum);
        public bool IsDefeated { get; private set; }

        float health;
        bool initialized;
        float ValidMaximum => IsFinite(maxHealth) && maxHealth > 0f ? maxHealth : 100f;

        void Awake() => InitializeHealth();

        void InitializeHealth()
        {
            if (initialized) return;
            initialized = true;
            health = ValidMaximum;
            if (!jump) jump = GetComponentInChildren<LavaCreatureAutoJump>(true);
        }

        public void ApplyDamage(float amount)
        {
            if (!isActiveAndEnabled || IsDefeated || !IsFinite(amount) || amount <= 0f) return;
            InitializeHealth();
            health = Mathf.Max(0f, health - amount);
            if (health <= 0f) Defeat();
        }

        public void ApplyWater(float litres)
        {
            if (!IsFinite(litres) || litres <= 0f || !IsFinite(waterDamagePerLitre) || waterDamagePerLitre <= 0f) return;
            // Cap before multiplying so an extreme input cannot overflow and bypass damage.
            ApplyDamage(Mathf.Min(litres, Health / waterDamagePerLitre) * waterDamagePerLitre);
        }

        void Defeat()
        {
            IsDefeated = true;
            // AutoJump.OnDisable stops its coroutine and restores the grounded visual pose.
            if (jump) jump.enabled = false;
            foreach (Animator animator in GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (Collider hitbox in GetComponentsInChildren<Collider>(true)) hitbox.enabled = false;
            foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (Light glow in GetComponentsInChildren<Light>(true)) glow.enabled = false;

            // Property blocks keep the imported materials and other creature instances intact.
            var block = new MaterialPropertyBlock();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                string part = renderer.name.ToLowerInvariant();
                if (renderer is ParticleSystemRenderer || part.Contains("flame") || part.Contains("ember"))
                {
                    renderer.enabled = false;
                    continue;
                }
                renderer.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", Color.black);
                block.SetColor("_BaseColor", new Color(.12f, .13f, .14f, 1f));
                block.SetColor("_Color", new Color(.12f, .13f, .14f, 1f));
                renderer.SetPropertyBlock(block);
                block.Clear();
            }
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
