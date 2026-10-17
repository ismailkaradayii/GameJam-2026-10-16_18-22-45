using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Short, readable windups and committed, dodgeable leaps. No NavMesh bake required.</summary>
    [DisallowMultipleComponent]
    public sealed class LavaCreatureCombat : MonoBehaviour
    {
        public enum Motion { Emerging, Searching, Windup, Leaping, Recovery, Staggered, Dead }
        [Min(1f)] public float noticeDistance = 70f;
        [Min(.1f)] public float hopDistance = 4.6f;
        [Min(.1f)] public float hopDuration = .58f;
        [Min(.1f)] public float attackRange = 5.6f;
        [Min(.1f)] public float attackCooldown = 2.4f;
        [Min(0f)] public float attackDamage = 14f;
        [Min(.1f)] public float downhillHopDistance = 7.2f;
        [Min(1f)] public float personalSpace = 2.2f;
        public CreatureCombatPlayer target;
        public Motion State { get; private set; }
        public int Leaps { get; private set; }
        public int SuccessfulAttacks { get; private set; }
        public bool IsAttacking => attacking && (State == Motion.Windup || State == Motion.Leaping);
        public float AttackCharge => IsAttacking && State == Motion.Windup ? Mathf.Clamp01(elapsed / duration) : 0f;
        public Vector3 ReservedLanding => State == Motion.Windup || State == Motion.Leaping ? landing : transform.position;
        public bool IsDescending => descentPath != null && descentIndex < descentPath.Length - 1;
        public float DescentProgress => descentDistances == null || descentDistances.Length < 2 ? 1f :
            descentDistance / Mathf.Max(.001f, descentDistances[descentDistances.Length - 1]);

        LavaCreatureActor actor;
        LavaCreatureAutoJump jump;
        Animator animator;
        float animatorSpeed = 1f, elapsed, duration, cooldown, acquireAt, bodyOffset = .8f, attackWait;
        float radius = .38f, height = 1.1f, fallbackGroundY;
        bool initialized, attacking, struck, alerted;
        bool downhillLeap, emergenceSplash;
        Vector3[] descentPath;
        float[] descentDistances;
        int descentIndex;
        int detourSide;
        float descentDistance, emergenceDepth = .55f;
        Vector3 start, landing, direction, grounded;
        readonly RaycastHit[] hits = new RaycastHit[32];
        static readonly List<LavaCreatureCombat> crowd = new List<LavaCreatureCombat>();
        static int nextLane;
        int lane;
        LineRenderer attackTell;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCrowd() { crowd.Clear(); nextLane = 0; }
        void OnEnable() { if (!crowd.Contains(this)) { crowd.Add(this); lane = nextLane++; } }

        void Start() => Initialize();
        void Update() => TickCombat(Time.deltaTime);

        // The encounter supplies mesh samples before activating this actor, so the first rendered
        // frame is already under the lava and movement stays on the authored mountain channel.
        public void ConfigureLavaDescent(Vector3[] path, float depth)
        {
            if (initialized || path == null || path.Length < 2) return;
            descentPath = path;
            descentDistances = new float[path.Length];
            for (int i = 1; i < path.Length; i++)
                descentDistances[i] = descentDistances[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            descentIndex = 0; descentDistance = 0f;
            emergenceDepth = Mathf.Max(.5f, depth);
        }

        void Initialize()
        {
            if (initialized) return;
            actor = GetComponent<LavaCreatureActor>();
            jump = GetComponent<LavaCreatureAutoJump>();
            if (!actor || !jump) { enabled = false; return; }
            initialized = true;
            fallbackGroundY = transform.position.y;
            jump.TakeCombatControl();
            // The encounter explicitly assigns its player, even when the lava emerges far away.
            alerted = jump.Target != null;
            animator = GetComponentInChildren<Animator>();
            if (animator) animatorSpeed = animator.speed;
            var hitbox = GetComponentInChildren<CapsuleCollider>();
            if (hitbox)
            {
                bodyOffset = hitbox.bounds.center.y - transform.position.y;
                radius = Mathf.Clamp(Mathf.Min(hitbox.bounds.extents.x, hitbox.bounds.extents.z), .24f, .75f);
            }
            grounded = IsDescending ? descentPath[0] : GroundAt(transform.position);
            start = grounded - Vector3.up * emergenceDepth;
            landing = grounded;
            transform.position = start;
            Change(Motion.Emerging, IsDescending ? .9f : .55f);
            AcquireTarget();
        }

        public void TickCombat(float dt)
        {
            if (!isActiveAndEnabled || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            Initialize();
            if (!initialized || !actor || actor.IsDefeated) return;
            dt = Mathf.Min(dt, .1f);
            cooldown = Mathf.Max(0f, cooldown - dt);
            attackWait += dt;
            elapsed += dt;
            if (!target || !target.isActiveAndEnabled) AcquireTarget();
            if (State == Motion.Searching || State == Motion.Recovery) Separate(dt);
            switch (State)
            {
                case Motion.Emerging:
                    transform.position = Vector3.Lerp(start, landing, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                    if (IsDescending && !emergenceSplash && elapsed >= duration * .35f)
                    {
                        emergenceSplash = true;
                        LavaCombatEffects.Emerge(landing - Vector3.up * jump.groundClearance);
                    }
                    if (elapsed >= duration) { jump.CombatGrounded(landing); Change(Motion.Searching, 0f); }
                    break;
                case Motion.Searching:
                    bool hasTarget = target && target.isActiveAndEnabled && target.Health > 0f;
                    Vector3 offset = hasTarget ? target.PursuitPosition - transform.position : Vector3.zero;
                    // A nearby player can intercept the procession, including from a vehicle.
                    if (IsDescending && hasTarget && Mathf.Abs(offset.y) < 8f &&
                        new Vector2(offset.x, offset.z).sqrMagnitude < 18f * 18f)
                        descentIndex = descentPath.Length - 1;
                    downhillLeap = IsDescending;
                    Vector3 goal = downhillLeap ? DescentGoal() : (hasTarget ? target.PursuitPosition : transform.position);
                    downhillLeap = IsDescending;
                    if (!downhillLeap && !hasTarget) return;
                    offset = goal - transform.position;
                    offset.y = 0f;
                    if (!downhillLeap && !alerted && offset.sqrMagnitude > noticeDistance * noticeDistance) return;
                    alerted = true;
                    attacking = !downhillLeap && hasTarget && target.CanBeAttacked && offset.magnitude <= attackRange && cooldown <= 0f && ClaimAttack();
                    if (!downhillLeap && !attacking && hasTarget)
                    {
                        goal = ApproachPoint(); offset = goal - transform.position; offset.y = 0f;
                    }
                    if (!attacking && offset.magnitude < .65f) return;
                    direction = offset.sqrMagnitude > .001f ? offset.normalized : Vector3.forward;
                    start = downhillLeap ? transform.position : GroundAt(transform.position);
                    // Commit to the player's position now: strafing can evade the pounce.
                    float step = downhillLeap ? Mathf.Min(downhillHopDistance, offset.magnitude) :
                        Mathf.Min(attacking ? attackRange : hopDistance, Mathf.Max(0f, offset.magnitude - (attacking ? .55f : .1f)));
                    landing = ChooseLanding(start, direction, step);
                    if (downhillLeap && FlatDistance(landing, goal) < .4f) landing.y = Mathf.Max(landing.y, goal.y);
                    direction = (landing - start); direction.y = 0f;
                    if (direction.sqrMagnitude > .001f) direction.Normalize();
                    Change(Motion.Windup, attacking ? .46f : .13f + lane % 3 * .025f);
                    if (attacking) { attackWait = 0f; ShowAttackTell(); }
                    break;
                case Motion.Windup:
                    jump.CombatWindup(direction, Mathf.Clamp01(elapsed / duration));
                    UpdateAttackTell();
                    if (elapsed < duration) return;
                    struck = false;
                    height = attacking ? .42f : downhillLeap ? 1.65f : .95f;
                    if (attacking) cooldown = attackCooldown + lane % 4 * .17f;
                    HideAttackTell();
                    jump.BeginCombatJump(direction);
                    Leaps++;
                    Change(Motion.Leaping, attacking ? .32f : downhillLeap ?
                        Mathf.Max(.64f, Mathf.Sqrt(Mathf.Max(0f, start.y - landing.y) / 18f)) : hopDuration);
                    break;
                case Motion.Leaping:
                    float t = Mathf.Clamp01(elapsed / duration);
                    Vector3 next = Vector3.Lerp(start, landing, t) + Vector3.up * (4f * height * t * (1f - t));
                    // Curved terrain may rise above a straight endpoint arc. Keep feet above it.
                    next.y = Mathf.Max(next.y, GroundAt(next).y + 4f * height * t * (1f - t));
                    // Sweep every step as well as the planned path, including against moving props.
                    Vector3 travel = next - transform.position;
                    float clear = ClearDistance(transform.position, travel.normalized, travel.magnitude);
                    if (clear + .015f < travel.magnitude)
                    {
                        next = transform.position + travel.normalized * clear;
                        landing = GroundAt(next);
                        t = 1f;
                        next = landing;
                    }
                    if (attacking) jump.CombatLungePose(next, t);
                    else jump.CombatPose(next, t);
                    if (attacking && !struck && t > .35f) TryStrike();
                    if (t < 1f) return;
                    grounded = landing;
                    jump.CombatGrounded(grounded);
                    actor.LandingFeedback(grounded, attacking);
                    Change(Motion.Recovery, attacking ? .62f : .18f + lane % 4 * .07f);
                    break;
                case Motion.Recovery:
                    if (elapsed >= duration) Change(Motion.Searching, 0f);
                    break;
                case Motion.Staggered:
                    // Only this enemy pauses for impact; player controls and world time continue.
                    if (elapsed < .065f) return;
                    if (animator) animator.speed = animatorSpeed;
                    float recovery = Mathf.Clamp01((elapsed - .065f) / .23f);
                    // Fast initial displacement, then a soft settle. The impulse reads at once.
                    float recoil = 1f - Mathf.Pow(1f - recovery, 3f);
                    transform.position = Vector3.Lerp(start, landing, recoil);
                    jump.RecoverCombatImpact(recoil);
                    if (recovery >= 1f) { grounded = landing; jump.CombatGrounded(grounded); Change(Motion.Recovery, .1f); }
                    break;
            }
        }

        public void Stagger(Vector3 shotDirection)
        {
            Initialize();
            if (!initialized || actor.IsDefeated) return;
            alerted = true;
            attacking = false; HideAttackTell();
            cooldown = Mathf.Max(cooldown, .55f);
            shotDirection.y = 0f;
            if (shotDirection.sqrMagnitude < .001f) shotDirection = -direction;
            start = transform.position;
            landing = GroundAt(start + shotDirection.normalized * ClearDistance(start, shotDirection.normalized, .95f));
            // Hold the exact struck pose, including a midair flip, before easing upright.
            jump.CaptureCombatImpact();
            if (animator) animator.speed = 0f;
            Change(Motion.Staggered, .295f);
        }

        void TryStrike()
        {
            if (!target || !target.CanBeAttacked) return;
            Vector3 center = transform.position + Vector3.up * bodyOffset;
            Vector3 contact = target.ClosestAttackPoint(center);
            if (Vector3.Distance(center, contact) > 1.05f) return;
            Vector3 delta = contact - center;
            if (ClearDistance(transform.position, delta.normalized, delta.magnitude) + .05f < delta.magnitude) return;
            struck = true;
            if (target.TryTakeDamage(attackDamage, transform.position)) SuccessfulAttacks++;
        }

        void AcquireTarget()
        {
            if (target && target.isActiveAndEnabled) return;
            if (Time.time < acquireAt) return;
            acquireAt = Time.time + .5f;
            var player = jump && jump.Target ? jump.Target.GetComponentInParent<Itfaiye.ItfaiyeFirstPersonController>() : null;
            if (!player) player = FindFirstObjectByType<Itfaiye.ItfaiyeFirstPersonController>();
            if (player) target = CreatureCombatPlayer.Ensure(player);
        }

        Vector3 ChooseLanding(Vector3 from, Vector3 forward, float distance)
        {
            Vector3 best = from;
            float bestScore = float.NegativeInfinity;
            float bestAngle = 0f;
            // A fan of collision probes routes repeated hops around buildings and boulders.
            for (int i = 0; i < (downhillLeap ? 7 : 9); i++)
            {
                float angle = i == 0 ? 0f : ((i + 1) / 2) * 30f * (i % 2 == 1 ? 1f : -1f);
                Vector3 route = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                float clear = ClearDistance(from, route, distance);
                Vector3 candidate = GroundAt(from + route * clear);
                if (downhillLeap)
                {
                    // Shorten cliff-edge hops instead of refusing every downhill step.
                    for (int n = 0; n < 4 && candidate.y < from.y - 12f; n++)
                    { clear *= .5f; candidate = GroundAt(from + route * clear); }
                    if (candidate.y > from.y + 2.5f || candidate.y < from.y - 12f) continue;
                }
                else if (Mathf.Abs(candidate.y - from.y) > 1.7f) continue;
                float score = clear - Mathf.Abs(angle) * (downhillLeap ? .05f : .012f) - Occupancy(candidate) * 5f;
                // Remember the chosen side of an obstruction. A slight backward diagonal is
                // necessary when the target direction points back into a long wall.
                if (!downhillLeap && detourSide != 0 && angle * detourSide < 0f) score -= 1.5f;
                if (score > bestScore) { bestScore = score; best = candidate; bestAngle = angle; }
            }
            if (!downhillLeap) detourSide = Mathf.Abs(bestAngle) < 1f ? 0 : (bestAngle > 0f ? 1 : -1);
            return best;
        }

        Vector3 DescentGoal()
        {
            Vector3 position = transform.position;
            float nearest = float.PositiveInfinity;
            int end = Mathf.Min(descentPath.Length - 1, descentIndex + 8);
            // Project onto a small forward window; do not jump to another side of a winding river.
            for (int i = descentIndex; i < end; i++)
            {
                Vector3 a = descentPath[i], delta = descentPath[i + 1] - a;
                Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
                Vector3 flatOffset = position - a; flatOffset.y = 0f;
                float fraction = Mathf.Clamp01(Vector3.Dot(flatOffset, flatDelta) / Mathf.Max(.0001f, flatDelta.sqrMagnitude));
                float arc = Mathf.Lerp(descentDistances[i], descentDistances[i + 1], fraction);
                if (arc + .05f < descentDistance) continue;
                float distance = FlatDistance(position, a + delta * fraction);
                if (distance >= nearest) continue;
                nearest = distance; descentDistance = arc; descentIndex = i;
            }
            if (FlatDistance(position, descentPath[descentPath.Length - 1]) < 1.5f &&
                descentDistance > descentDistances[descentDistances.Length - 1] - downhillHopDistance)
            {
                descentIndex = descentPath.Length - 1;
                descentDistance = descentDistances[descentDistances.Length - 1];
                return target ? target.PursuitPosition : position;
            }
            float lookAhead = descentDistance + downhillHopDistance;
            for (int i = descentIndex; i < descentPath.Length - 1; i++)
                if (descentDistances[i + 1] >= lookAhead)
                    return Vector3.Lerp(descentPath[i], descentPath[i + 1],
                        Mathf.InverseLerp(descentDistances[i], descentDistances[i + 1], lookAhead));
            return descentPath[descentPath.Length - 1];
        }

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        bool ClaimAttack()
        {
            int attackers = 0;
            foreach (var other in crowd)
            {
                if (!other || other == this || !other.isActiveAndEnabled || other.target != target) continue;
                if (other.IsAttacking)
                {
                    attackers++;
                    if (other.State == Motion.Windup && other.elapsed < .3f) return false;
                }
                else if (other.State == Motion.Searching && other.cooldown <= 0f &&
                    FlatDistance(other.transform.position, target.PursuitPosition) <= other.attackRange &&
                    other.attackWait > attackWait + .1f) return false;
            }
            return attackers < 2;
        }

        Vector3 ApproachPoint()
        {
            Vector3 center = target.PursuitPosition;
            Vector3 radial = transform.position - center; radial.y = 0f;
            if (radial.sqrMagnitude < .1f) radial = Quaternion.Euler(0, lane * 137.508f, 0) * Vector3.forward;
            float ring = 4.1f + lane % 3 * .42f;
            Vector3 best = center + radial.normalized * ring;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < 12; i++)
            {
                float angle = i == 0 ? 0 : ((i + 1) / 2) * 30f * (i % 2 == 0 ? -1 : 1);
                Vector3 p = center + Quaternion.Euler(0, angle, 0) * radial.normalized * ring;
                float score = FlatDistance(p, transform.position) + Occupancy(p) * 9f;
                if (score < bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        float Occupancy(Vector3 position)
        {
            float penalty = 0f;
            foreach (var other in crowd)
            {
                if (!other || other == this || !other.isActiveAndEnabled || (other.actor && other.actor.IsDefeated)) continue;
                if (Mathf.Abs(other.ReservedLanding.y - position.y) > 4f) continue;
                float distance = Mathf.Min(FlatDistance(position, other.ReservedLanding), FlatDistance(position, other.transform.position));
                penalty += Mathf.Max(0f, personalSpace - distance);
            }
            return penalty;
        }

        void Separate(float dt)
        {
            Vector3 push = Vector3.zero;
            foreach (var other in crowd)
            {
                if (!other || other == this || !other.isActiveAndEnabled || (other.actor && other.actor.IsDefeated) ||
                    Mathf.Abs(other.transform.position.y - transform.position.y) > 2.5f) continue;
                Vector3 away = transform.position - other.transform.position; away.y = 0;
                float distance = away.magnitude;
                if (distance >= personalSpace) continue;
                if (distance < .01f) away = Quaternion.Euler(0, lane * 137.508f, 0) * Vector3.forward;
                push += away.normalized * (personalSpace - distance);
            }
            if (push.sqrMagnitude < .001f) return;
            float length = Mathf.Min(push.magnitude * 2f * dt, 2.8f * dt);
            Vector3 p = GroundAt(transform.position + push.normalized * ClearDistance(transform.position, push.normalized, length));
            transform.position = grounded = p;
            jump.CombatGrounded(p);
        }

        void ShowAttackTell()
        {
            if (!attackTell)
            {
                var go = new GameObject("Committed lunge warning"); go.transform.SetParent(transform, false);
                attackTell = go.AddComponent<LineRenderer>();
                attackTell.sharedMaterial = Resources.Load<Material>("LavaCombat/ImpactRing");
                attackTell.useWorldSpace = true; attackTell.positionCount = 3;
                attackTell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                attackTell.receiveShadows = false;
            }
            attackTell.enabled = true; UpdateAttackTell();
        }
        void UpdateAttackTell()
        {
            if (!attackTell || !attackTell.enabled) return;
            float charge = AttackCharge;
            attackTell.SetPosition(0, start + Vector3.up * .12f);
            attackTell.SetPosition(1, GroundAt(Vector3.Lerp(start, landing, .55f)) + Vector3.up * .12f);
            attackTell.SetPosition(2, landing + Vector3.up * .12f);
            attackTell.startWidth = Mathf.Lerp(.07f, .2f, charge); attackTell.endWidth = .025f;
            attackTell.startColor = new Color(2.2f, .5f + charge * .7f, .08f, .4f + charge * .5f);
            attackTell.endColor = new Color(2.2f, .8f, .12f, .15f + charge * .5f);
        }
        void HideAttackTell() { if (attackTell) attackTell.enabled = false; }

        float ClearDistance(Vector3 from, Vector3 along, float distance)
        {
            if (distance < .001f) return 0f;
            int count = Physics.SphereCastNonAlloc(from + Vector3.up * bodyOffset, radius, along, hits,
                distance + .07f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float result = distance;
            for (int i = 0; i < count; i++)
            {
                Collider obstacle = hits[i].collider;
                if (!obstacle || obstacle is TerrainCollider || obstacle.transform.IsChildOf(transform) ||
                    obstacle.GetComponentInParent<LavaCreatureActor>() ||
                    (target && obstacle.transform.IsChildOf(target.transform))) continue;
                // Floor contacts are not walls, including the flat test/training floor.
                if (hits[i].normal.y > .65f) continue;
                result = Mathf.Min(result, Mathf.Max(0f, hits[i].distance - .07f));
            }
            return result;
        }

        Vector3 GroundAt(Vector3 point)
        {
            Terrain terrain = jump ? jump.groundTerrain : null;
            if (terrain && terrain.terrainData)
            {
                Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
                point.x = Mathf.Clamp(point.x, origin.x + radius, origin.x + size.x - radius);
                point.z = Mathf.Clamp(point.z, origin.z + radius, origin.z + size.z - radius);
                point.y = terrain.SampleHeight(point) + origin.y + jump.groundClearance;
            }
            else point.y = fallbackGroundY;
            return point;
        }

        void Change(Motion state, float seconds) { State = state; elapsed = 0f; duration = seconds; }
        void OnDisable()
        {
            crowd.Remove(this); attacking = false; HideAttackTell();
            if (animator) animator.speed = animatorSpeed;
            if (actor && actor.IsDefeated) State = Motion.Dead;
        }
    }
}
