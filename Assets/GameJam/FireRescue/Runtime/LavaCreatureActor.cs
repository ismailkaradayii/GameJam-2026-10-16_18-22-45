using System.Collections;
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
        public AudioClip bulletImpactClip;

        [Header("Death feedback")]
        [Tooltip("Delay between the poff sound and its small fire cloud.")]
        [Range(.04f, .3f)] public float deathFireDelay = .12f;
        [Range(.4f, 1.5f)] public float deathFireSize = 1f;
        [Range(0f, 1f)] public float deathPoffVolume = .85f;

        public float Health => initialized ? health : ValidMaximum;
        public float Health01 => Mathf.Clamp01(Health / ValidMaximum);
        public bool IsDefeated { get; private set; }
        public bool ReachedBuilding { get; private set; }
        public bool Dissipated { get; private set; }
        public bool SelfDetonated { get; private set; }
        public void Dissipate()
        {
            if (IsDefeated) return;
            InitializeHealth(); Dissipated = true; health = 0; Defeat(transform.position, Vector3.up);
        }
        public void ConsumeAtBuilding()
            => ConsumeAtBuilding(transform.position, Vector3.up);

        public void ConsumeAtBuilding(Vector3 point, Vector3 direction)
        {
            if (IsDefeated) return;
            InitializeHealth(); ReachedBuilding = true; health = 0;
            Defeat(point, direction);
        }

        public void Detonate(Vector3 point, Vector3 direction)
        {
            if (IsDefeated) return;
            InitializeHealth(); SelfDetonated = true; health = 0;
            Defeat(point, direction);
        }

        float health;
        bool initialized;
        LavaCreatureCombat combat;
        LavaSpiritVisual spiritVisual;
        Renderer[] bodyRenderers;
        MaterialPropertyBlock[] originalBlocks;
        MaterialPropertyBlock flashBlock;
        float flashUntil, nextWaterFeedback;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        float ValidMaximum => IsFinite(maxHealth) && maxHealth > 0f ? maxHealth : 100f;

        void Awake() => InitializeHealth();

        void InitializeHealth()
        {
            if (initialized) return;
            initialized = true;
            flashBlock = new MaterialPropertyBlock();
            health = ValidMaximum;
            if (!jump) jump = GetComponentInChildren<LavaCreatureAutoJump>(true);
            if (Application.isPlaying && jump)
            {
                combat = GetComponent<LavaCreatureCombat>() ?? gameObject.AddComponent<LavaCreatureCombat>();
                spiritVisual = LavaSpiritVisual.Ensure(this);
            }
            bodyRenderers = GetComponentsInChildren<Renderer>(true);
            originalBlocks = new MaterialPropertyBlock[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                originalBlocks[i] = new MaterialPropertyBlock();
                bodyRenderers[i].GetPropertyBlock(originalBlocks[i]);
            }
        }

        public void ApplyDamage(float amount)
            => ReceiveDamage(amount, transform.position + Vector3.up * .8f, Vector3.forward, false);

        public void ApplyBulletDamage(float amount, Vector3 point, Vector3 direction)
            => ReceiveDamage(amount, point, direction, true);

        void ReceiveDamage(float amount, Vector3 point, Vector3 direction, bool bullet)
        {
            if (!isActiveAndEnabled || IsDefeated || !IsFinite(amount) || amount <= 0f) return;
            InitializeHealth();
            health = Mathf.Max(0f, health - amount);
            if (Application.isPlaying && (bullet || Time.time >= nextWaterFeedback))
            {
                nextWaterFeedback = Time.time + .13f;
                flashUntil = Time.time + .095f;
                if (spiritVisual && spiritVisual.IsBuilt) spiritVisual.Impact(point, direction);
                LavaCombatEffects.Hit(point, direction, bullet, bulletImpactClip);
                if (bullet && combat && health > 0f) combat.Stagger(direction);
            }
            if (health <= 0f) Defeat(point, direction);
        }

        public void ApplyWater(float litres)
        {
            if (!IsFinite(litres) || litres <= 0f || !IsFinite(waterDamagePerLitre) || waterDamagePerLitre <= 0f) return;
            // Cap before multiplying so an extreme input cannot overflow and bypass damage.
            ApplyDamage(Mathf.Min(litres, Health / waterDamagePerLitre) * waterDamagePerLitre);
        }

        void LateUpdate()
        {
            if (spiritVisual && spiritVisual.IsBuilt) return;
            if (IsDefeated || bodyRenderers == null || flashUntil <= 0f) return;
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                var renderer = bodyRenderers[i];
                if (!renderer || renderer is ParticleSystemRenderer) continue;
                if (Time.time < flashUntil)
                {
                    renderer.GetPropertyBlock(flashBlock);
                    flashBlock.SetColor(EmissionId, new Color(5f, 2.7f, .8f));
                    // Rock materials can have emission disabled; their albedo must flash too.
                    flashBlock.SetColor("_BaseColor", new Color(1f, .78f, .42f));
                    flashBlock.SetColor("_Color", new Color(1f, .78f, .42f));
                    renderer.SetPropertyBlock(flashBlock);
                }
                else if (flashUntil > 0f) renderer.SetPropertyBlock(originalBlocks[i]);
            }
            if (Time.time >= flashUntil) flashUntil = 0f;
        }

        public void LandingFeedback(Vector3 position, bool attack)
        {
            if (Application.isPlaying && !IsDefeated)
            {
                if (spiritVisual) spiritVisual.Land();
                LavaCombatEffects.Landing(position, attack);
            }
        }

        void Defeat(Vector3 point, Vector3 direction)
        {
            IsDefeated = true;
            if (combat) combat.enabled = false;
            foreach (Collider hitbox in GetComponentsInChildren<Collider>(true)) hitbox.enabled = false;
            foreach (Animator animator in GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            if (Application.isPlaying && spiritVisual && spiritVisual.IsBuilt && !ReachedBuilding && !SelfDetonated)
                StartCoroutine(FinishDeath(point, direction));
            else HideAndShatter(direction);
        }

        IEnumerator FinishDeath(Vector3 point, Vector3 direction)
        {
            // Damage and collision end immediately; only the struck spirit holds its last pose.
            spiritVisual.BeginDeath(point, direction);
            yield return new WaitForSeconds(.11f);
            HideAndShatter(direction);
        }

        void HideAndShatter(Vector3 direction)
        {
            // Capture the current airborne pose before AutoJump restores its ground transform.
            if (Application.isPlaying) LavaCombatEffects.Shatter(this, direction);
            if (spiritVisual) spiritVisual.enabled = false;
            // AutoJump.OnDisable stops its coroutine and restores the grounded visual pose.
            if (jump) jump.enabled = false;
            foreach (Animator animator in GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (Collider hitbox in GetComponentsInChildren<Collider>(true)) hitbox.enabled = false;
            foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (Light glow in GetComponentsInChildren<Light>(true)) glow.enabled = false;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
