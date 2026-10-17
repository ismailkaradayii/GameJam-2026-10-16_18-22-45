using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Shared damage gate prevents a group of pouncing spirits from stacking damage in one frame.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(160)]
    public sealed class CreatureCombatPlayer : MonoBehaviour
    {
        [Min(1f)] public float maximumHealth = 100f;
        public bool allowRecovery = true;
        public event System.Action Defeated;
        public float Health { get; private set; } = 100f;
        public bool CanBeAttacked => isActiveAndEnabled && Health > 0f && Time.timeScale > 0f &&
            (!coordinator || (!coordinator.IsDriving && !coordinator.GameplaySuspended));
        public Vector3 PursuitPosition => coordinator && coordinator.IsDriving && coordinator.drive
            ? coordinator.drive.transform.position : transform.position;
        public Vector3 AttackPoint => body ? body.bounds.center : transform.position + Vector3.up * .9f;
        // Use the actual capsule surface: tall characters can be struck at the legs too.
        public Vector3 ClosestAttackPoint(Vector3 from) => body && body.enabled ? body.ClosestPoint(from) : AttackPoint;
        public int HitsReceived { get; private set; }
        Itfaiye.ItfaiyeFirstPersonController player;
        FireRescuePlayer coordinator;
        CharacterController body;
        VolcanoCameraShake shake;
        Vector3 knockback, safePosition;
        float lastHit = -100f, recoverAt;

        public static CreatureCombatPlayer Ensure(Itfaiye.ItfaiyeFirstPersonController controller)
            => controller.GetComponent<CreatureCombatPlayer>() ?? controller.gameObject.AddComponent<CreatureCombatPlayer>();

        void Awake()
        {
            player = GetComponent<Itfaiye.ItfaiyeFirstPersonController>();
            coordinator = GetComponent<FireRescuePlayer>();
            body = GetComponent<CharacterController>();
            safePosition = transform.position;
            Health = maximumHealth;
            if (player && player.playerCamera)
                shake = player.playerCamera.GetComponent<VolcanoCameraShake>() ?? player.playerCamera.gameObject.AddComponent<VolcanoCameraShake>();
        }

        public bool TryTakeDamage(float damage, Vector3 source)
        {
            if (!CanBeAttacked || !float.IsFinite(damage) || damage <= 0f || Time.time - lastHit < .7f) return false;
            lastHit = Time.time;
            Health = Mathf.Max(0f, Health - damage);
            HitsReceived++;
            Vector3 away = transform.position - source; away.y = 0f;
            knockback = away.normalized * 3.7f;
            if (shake) shake.Shake(.22f, .075f);
            LavaCombatEffects.PlayerHit(AttackPoint);
            if (Health <= 0f) { recoverAt = Time.time + 1.2f; Defeated?.Invoke(); }
            return true;
        }
        // Continuous heat has no knockback or spirit-hit invulnerability window.
        public bool TakeHeatDamage(float damage)
        {
            if (!CanBeAttacked || !float.IsFinite(damage) || damage <= 0) return false;
            Health = Mathf.Max(0, Health - damage); lastHit = Time.time;
            if (Health <= 0) { recoverAt = Time.time + 1.2f; Defeated?.Invoke(); }
            return true;
        }

        void Update()
        {
            if (body && body.enabled && (!coordinator || !coordinator.IsDriving) && knockback.sqrMagnitude > .001f)
                body.Move(knockback * Time.deltaTime);
            knockback = Vector3.Lerp(knockback, Vector3.zero, 1f - Mathf.Exp(-12f * Time.deltaTime));
            if (allowRecovery && Health <= 0f && Time.time >= recoverAt)
            {
                // A short recovery returns the player safely instead of leaving a dead, stuck session.
                bool wasEnabled = body && body.enabled;
                if (body) body.enabled = false;
                transform.position = safePosition;
                if (body) body.enabled = wasEnabled;
                Health = maximumHealth;
                lastHit = Time.time + 1f;
            }
            else if (Health > 0f && Time.time - lastHit > 6f)
                Health = Mathf.MoveTowards(Health, maximumHealth, 6f * Time.deltaTime);
        }

        void OnGUI()
        {
            if (VolcanoCinematic.IsPlayingAny) return;
            if (coordinator && (!coordinator.showHud || coordinator.IsDriving)) return;
            Color old = GUI.color;
            float pulse = Mathf.Clamp01(1f - (Time.time - lastHit) / .48f);
            if (pulse > 0f)
            {
                GUI.color = new Color(1f, .16f, .025f, pulse * .38f);
                float border = Mathf.Max(12f, Screen.height * .025f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, border), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, Screen.height - border, Screen.width, border), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, 0, border, Screen.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(Screen.width - border, 0, border, Screen.height), Texture2D.whiteTexture);
            }
            if (Health < maximumHealth)
            {
                float x = Screen.width * .5f - 70f, y = Screen.height - 102f;
                GUI.color = new Color(.08f, .035f, .025f, .85f);
                GUI.DrawTexture(new Rect(x - 3, y - 3, 146, 12), Texture2D.whiteTexture);
                GUI.color = new Color(1f, .29f, .09f, .95f);
                GUI.DrawTexture(new Rect(x, y, 140f * Mathf.Clamp01(Health / maximumHealth), 6), Texture2D.whiteTexture);
            }
            GUI.color = old;
        }
    }
}
