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
        [Tooltip("Optional damage feedback camera. Uses the first-person camera when empty.")]
        public Camera damageCamera;
        public event System.Action Defeated;
        public float Health { get; private set; } = 100f;
        public bool CanBeAttacked => isActiveAndEnabled && Health > 0f && Time.timeScale > 0f &&
            (!coordinator || (!coordinator.IsDriving && !coordinator.GameplaySuspended));
        public Vector3 PursuitPosition => coordinator && coordinator.IsDriving && coordinator.drive
            ? coordinator.drive.transform.position : transform.position;
        public ThermalDamage VehicleTarget => coordinator && coordinator.IsDriving && coordinator.drive ? coordinator.drive.Thermal : null;
        public bool CanBeTargeted => CanBeAttacked || (isActiveAndEnabled && Time.timeScale > 0f && coordinator &&
            !coordinator.GameplaySuspended && VehicleTarget && VehicleTarget.CanIgnite);
        public Vector3 ClosestThreatPoint(Vector3 from) => VehicleTarget ? VehicleTarget.ClosestPoint(from) : ClosestAttackPoint(from);
        public CharacterController ContactBody => coordinator && coordinator.playerBody ? coordinator.playerBody : body;
        public Vector3 AttackPoint => ContactBody ? ContactBody.bounds.center : transform.position + Vector3.up * .9f;
        // Use the actual capsule surface: tall characters can be struck at the legs too.
        public Vector3 ClosestAttackPoint(Vector3 from) => ContactBody && ContactBody.enabled ? ContactBody.ClosestPoint(from) : AttackPoint;
        public int HitsReceived { get; private set; }
        Itfaiye.ItfaiyeFirstPersonController player;
        FireRescuePlayer coordinator;
        CharacterController body;
        PlayerDamageFeedback feedback;
        Vector3 knockback, safePosition;
        float lastHit = -100f, lastSpiritHit = -100f, recoverAt, nextHeatFeedback;

        public static CreatureCombatPlayer Ensure(Itfaiye.ItfaiyeFirstPersonController controller)
            => controller.GetComponent<CreatureCombatPlayer>() ?? controller.gameObject.AddComponent<CreatureCombatPlayer>();

        void Awake()
        {
            player = GetComponent<Itfaiye.ItfaiyeFirstPersonController>();
            coordinator = GetComponent<FireRescuePlayer>();
            body = GetComponent<CharacterController>();
            safePosition = transform.position;
            Health = maximumHealth;
        }

        /// <summary>Sweep against the live controller geometry, including between physics updates.</summary>
        public bool SweepBodyContact(Vector3 from, Vector3 to, float spiritRadius, out float fraction)
        {
            fraction = 1f;
            var capsule = ContactBody;
            if (!CanBeAttacked || !capsule || !capsule.enabled || !float.IsFinite(spiritRadius) || spiritRadius <= 0f) return false;
            Vector3 scale = capsule.transform.lossyScale, axis = capsule.transform.up;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfLine = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * .5f - radius);
            Vector3 center = capsule.transform.TransformPoint(capsule.center);
            float combined = radius + spiritRadius;
            Vector3 offset = from - center, travel = to - from;
            float along = Vector3.Dot(offset, axis);
            if ((offset - axis * Mathf.Clamp(along, -halfLine, halfLine)).sqrMagnitude <= combined * combined)
            { fraction = 0f; return true; }
            bool hit = false;
            Vector3 radial = offset - axis * along, across = travel - axis * Vector3.Dot(travel, axis);
            float a = across.sqrMagnitude, b = Vector3.Dot(radial, across), c = radial.sqrMagnitude - combined * combined;
            float discriminant = b * b - a * c;
            if (a > .0000001f && discriminant >= 0f)
            {
                float enter = (-b - Mathf.Sqrt(discriminant)) / a;
                float height = along + Vector3.Dot(travel, axis) * enter;
                if (enter >= 0f && enter <= fraction && Mathf.Abs(height) <= halfLine)
                { fraction = enter; hit = true; }
            }
            hit |= SweepSphere(offset - axis * halfLine, travel, combined, ref fraction);
            hit |= SweepSphere(offset + axis * halfLine, travel, combined, ref fraction);
            return hit;
        }

        static bool SweepSphere(Vector3 offset, Vector3 travel, float radius, ref float fraction)
        {
            float a = travel.sqrMagnitude;
            if (a <= .0000001f) return false;
            float b = Vector3.Dot(offset, travel), c = offset.sqrMagnitude - radius * radius;
            float discriminant = b * b - a * c;
            if (discriminant < 0f) return false;
            float enter = (-b - Mathf.Sqrt(discriminant)) / a;
            if (enter < 0f || enter > fraction) return false;
            fraction = enter; return true;
        }

        public bool TryTakeDamage(float damage, Vector3 source)
        {
            if (!CanBeAttacked || !float.IsFinite(damage) || damage <= 0f || Time.time - lastSpiritHit < .7f) return false;
            lastHit = lastSpiritHit = Time.time;
            Health = Mathf.Max(0f, Health - damage);
            HitsReceived++;
            Vector3 away = transform.position - source; away.y = 0f;
            knockback = away.normalized * 3.7f;
            ShowDamageFeedback();
            LavaCombatEffects.PlayerHit(AttackPoint);
            if (Health <= 0f) { recoverAt = Time.time + 1.2f; Defeated?.Invoke(); }
            return true;
        }
        // Continuous heat has no knockback or spirit-hit invulnerability window.
        public bool TakeHeatDamage(float damage)
        {
            if (!CanBeAttacked || !float.IsFinite(damage) || damage <= 0) return false;
            Health = Mathf.Max(0, Health - damage); lastHit = Time.time;
            if (Time.unscaledTime >= nextHeatFeedback)
            { nextHeatFeedback = Time.unscaledTime + .65f; ShowDamageFeedback(); }
            if (Health <= 0) { recoverAt = Time.time + 1.2f; Defeated?.Invoke(); }
            return true;
        }

        void ShowDamageFeedback()
        {
            // Resolve at the hit, after runtime player/camera setup and control handoffs.
            if (!player) player = GetComponent<Itfaiye.ItfaiyeFirstPersonController>();
            if (!coordinator) coordinator = GetComponent<FireRescuePlayer>();
            Camera view = damageCamera ? damageCamera : player && player.playerCamera ? player.playerCamera : coordinator ? coordinator.viewCamera : null;
            if (!view) return;
            if (!feedback) feedback = PlayerDamageFeedback.Ensure(this);
            feedback.Play(view);
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
                lastHit = lastSpiritHit = Time.time + 1f;
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
            if (pulse > 0f && !feedback)
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
