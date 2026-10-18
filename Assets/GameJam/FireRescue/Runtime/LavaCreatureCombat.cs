using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Short, readable windups and committed, dodgeable leaps. No NavMesh bake required.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(90)]
    public sealed class LavaCreatureCombat : MonoBehaviour
    {
        public enum Motion { Emerging, Searching, Windup, Leaping, Recovery, Staggered, Dead, Igniting, RoofLeaping, Settling }
        [Min(1f)] public float noticeDistance = 70f;
        [Min(.1f)] public float hopDistance = 9f;
        [Min(.1f)] public float hopDuration = .42f;
        [Min(.1f)] public float attackRange = 14f;
        [Min(.1f)] public float attackCooldown = 1.8f;
        [Min(0f)] public float attackDamage = 28f;
        [Range(.25f, 1.2f)] public float playerAttackWindup = .4f;
        [Range(.4f, 1.4f)] public float playerPounceDuration = .76f;
        [Range(.4f, 4f)] public float playerPounceHeight = 2.4f;
        [Range(.5f, 3f)] public float playerBlastRadius = 1.6f;
        [Min(.1f)] public float downhillHopDistance = 9f;
        [Min(1f)] public float personalSpace = 2.2f;
        public CreatureCombatPlayer target;
        [Header("Village defense")]
        public bool attackBuildings;
        [Min(.5f)] public float buildingContactDistance = 2.5f;
        [Range(.4f, 1.5f)] public float roofWindupSeconds = .85f;
        [Range(2f, 7f)] public float roofJumpClearance = 3.4f;
        [Min(15f)] public float maximumLifeSeconds = 105f;
        public BurnableBuilding BuildingTarget { get; private set; }
        float lifetime, retaliationUntil, progressClock;
        Vector3 progressPosition;
        readonly SpiritOutdoorNavigation navigation = new SpiritOutdoorNavigation();
        readonly SpiritRoofFlight roofFlight = new SpiritRoofFlight();
        public float RoofAttackCharge => State == Motion.Igniting ? Mathf.Clamp01(elapsed / duration) : 0f;
        public Vector3 RoofImpactPoint => roofFlight.RoofPoint;
        public Collider RoofContactCollider { get; private set; }
        public float RoofFlightProgress => State == Motion.RoofLeaping ? Mathf.Clamp01(elapsed / duration) : 0f;
        public bool PursuingBuilding => attackBuildings && Time.time >= retaliationUntil;
        public int RouteRecoveries { get; private set; }
        public Motion State { get; private set; }
        public int Leaps { get; private set; }
        public int SuccessfulAttacks { get; private set; }
        public bool IsAttacking => attacking && (State == Motion.Windup || State == Motion.Leaping);
        public float AttackCharge => IsAttacking && State == Motion.Windup ? Mathf.Clamp01(elapsed / duration) : 0f;
        public bool IsPlayerPouncing => attacking && State == Motion.Leaping;
        public float PlayerPounceProgress => IsPlayerPouncing ? Mathf.Clamp01(elapsed / duration) : 0f;
        public bool IsIntroductionJump { get; private set; }
        public float IntroductionJumpProgress => IsIntroductionJump && State == Motion.Leaping ? Mathf.Clamp01(elapsed / duration) : 0f;
        public Vector3 IntroductionDirection => direction;
        public Vector3 ReservedLanding => State == Motion.Windup || State == Motion.Leaping ? landing : transform.position;
        public bool IsDescending => descentPath != null && descentIndex < descentPath.Length - 1;
        public float DescentProgress => descentDistances == null || descentDistances.Length < 2 ? 1f :
            descentDistance / Mathf.Max(.001f, descentDistances[descentDistances.Length - 1]);

        LavaCreatureActor actor;
        LavaSpiritVisual spiritVisual;
        LavaCreatureAutoJump jump;
        Animator animator;
        float animatorSpeed = 1f, elapsed, duration, cooldown, acquireAt, bodyOffset = .8f, attackWait;
        float radius = .38f, height = 1.1f, fallbackGroundY;
        bool initialized, attacking, struck, alerted;
        bool playerPounceLive;
        CharacterController ignoredPlayerBody;
        Collider[] contactHitboxes;
        bool[] previousPlayerIgnores;
        bool downhillLeap, emergenceSplash, villageEmergence;
        Vector3[] descentPath;
        float[] descentDistances;
        int descentIndex;
        int detourSide;
        float descentDistance, emergenceDepth = .55f;
        Vector3 start, landing, direction, grounded, roofGroundSurface;
        Vector3 attackGroundSurface, pounceStart, pounceEnd;
        float pounceArcHeight, pounceSeconds;
        readonly RaycastHit[] hits = new RaycastHit[32];
        readonly Collider[] playerContacts = new Collider[64];
        static readonly List<LavaCreatureCombat> crowd = new List<LavaCreatureCombat>();
        static int nextLane;
        int lane;
        LineRenderer attackTell;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCrowd() { crowd.Clear(); nextLane = 0; }
        void OnEnable() { if (!crowd.Contains(this)) { crowd.Add(this); lane = nextLane++; } }

        void Start() => Initialize();
        void Update() => TickCombat(Time.deltaTime);
        void LateUpdate()
        {
            // Animate() moves the rendered core after Update. A visible touch on this final
            // pose must count too, even when the dive has just entered its landing phase.
            if (!playerPounceLive) return;
            if (actor && !actor.IsDefeated && target && !target.VehicleTarget)
            {
                Vector3 core = RoofBodyPosition;
                if (target.SweepBodyContact(core, core, PlayerContactRadius, out _) &&
                    ExplosionCanReach(core, target.ClosestAttackPoint(core)))
                    DetonatePlayerAttack(core, direction, target.ContactBody);
            }
            if (!IsPlayerPouncing && State != Motion.Settling) playerPounceLive = false;
        }

        public void ConfigureVillageEmergence(float depth)
        {
            if (initialized) return;
            villageEmergence = true; emergenceDepth = Mathf.Max(.5f, depth);
            descentPath = null; descentDistances = null;
        }

        /// <summary>Bring an isolated surviving spirit back through a safe lava bank without replacing its health or actor.</summary>
        public bool ReemergeFromLava(Vector3 point, float depth)
        {
            if (!isActiveAndEnabled || !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return false;
            Initialize();
            if (!initialized || !actor || actor.IsDefeated || IsIntroductionJump) return false;
            HideAttackTell(); attacking = struck = downhillLeap = playerPounceLive = false;
            BuildingTarget = null; RoofContactCollider = null;
            navigation.Invalidate(); detourSide = 0;
            descentPath = null; descentDistances = null; descentIndex = 0; descentDistance = 0;
            villageEmergence = true; emergenceSplash = false; emergenceDepth = Mathf.Max(.5f, depth);
            lifetime = progressClock = 0; RouteRecoveries++;
            cooldown = Mathf.Max(cooldown, 1f); retaliationUntil = Time.time + 1.5f; alerted = true;
            jump.TakeCombatControl();
            grounded = GroundAt(point); jump.CombatGrounded(grounded);
            progressPosition = landing = grounded; start = grounded - Vector3.up * emergenceDepth;
            transform.position = start;
            if (animator) animator.speed = animatorSpeed;
            Change(Motion.Emerging, .9f);
            return true;
        }

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
            spiritVisual = GetComponent<LavaSpiritVisual>();
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
            progressPosition = grounded;
            start = grounded - Vector3.up * emergenceDepth;
            landing = grounded;
            transform.position = start;
            Change(Motion.Emerging, IsDescending || villageEmergence ? .9f : .55f);
            AcquireTarget();
            BindPlayerContact();
        }

        void BindPlayerContact()
        {
            var controller = target ? target.ContactBody : null;
            if (controller == ignoredPlayerBody) return;
            RestorePlayerContact();
            if (!controller) return;
            ignoredPlayerBody = controller;
            if (contactHitboxes == null) contactHitboxes = GetComponentsInChildren<Collider>(true);
            previousPlayerIgnores = new bool[contactHitboxes.Length];
            for (int i = 0; i < contactHitboxes.Length; i++)
                if (contactHitboxes[i] && contactHitboxes[i] != controller)
                {
                    previousPlayerIgnores[i] = Physics.GetIgnoreCollision(controller, contactHitboxes[i]);
                    // Weapon raycasts still hit the spirit. Its invisible solid hit capsule
                    // must not push a walking player's controller away before the fire hits.
                    Physics.IgnoreCollision(controller, contactHitboxes[i], true);
                }
        }
        void RestorePlayerContact()
        {
            if (ignoredPlayerBody && contactHitboxes != null && previousPlayerIgnores != null)
                for (int i = 0; i < contactHitboxes.Length; i++)
                    if (contactHitboxes[i] && contactHitboxes[i] != ignoredPlayerBody)
                        Physics.IgnoreCollision(ignoredPlayerBody, contactHitboxes[i], previousPlayerIgnores[i]);
            ignoredPlayerBody = null; previousPlayerIgnores = null;
        }

        /// <summary>A single safe, visible hop for the first-contact camera; normal combat resumes at landing.</summary>
        public bool BeginIntroductionJump()
        {
            Initialize();
            if (!initialized || !actor || actor.IsDefeated || IsDescending) return false;
            grounded = start = GroundAt(transform.position);
            direction = FindBuilding() ? Vector3.ProjectOnPlane(BuildingTarget.transform.position - start, Vector3.up).normalized :
                target ? Vector3.ProjectOnPlane(target.PursuitPosition - start, Vector3.up).normalized : Vector3.forward;
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            attacking = downhillLeap = false; HideAttackTell();
            landing = ChooseLanding(start, direction, Mathf.Min(hopDistance, 4.5f));
            if (FlatDistance(start, landing) > .1f) direction = Vector3.ProjectOnPlane(landing - start, Vector3.up).normalized;
            jump.CombatGrounded(start);
            if (animator) animator.speed = animatorSpeed;
            IsIntroductionJump = true;
            Change(Motion.Windup, .34f);
            return true;
        }

        public void TickCombat(float dt)
        {
            if (!isActiveAndEnabled || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            Initialize();
            if (!initialized || !actor || actor.IsDefeated) return;
            dt = Mathf.Min(dt, .1f);
            if (!IsIntroductionJump && !IsDescending && State != Motion.Emerging) IgniteThermalContacts(RoofBodyPosition, RoofBodyRadius + .22f);
            lifetime += dt;
            if (attackBuildings && lifetime >= maximumLifeSeconds)
            {
                // Stragglers dissipate; no infinite encounter if scenery blocks a hop.
                actor.Dissipate(); return;
            }
            if (!IsIntroductionJump && PursuingBuilding && !IsDescending &&
                (State == Motion.Searching || State == Motion.Recovery) && !PlayerInPounceRange() && TickBuildingContact()) return;
            cooldown = Mathf.Max(0f, cooldown - dt);
            attackWait += dt;
            elapsed += dt;
            if (!target || !target.isActiveAndEnabled) AcquireTarget();
            BindPlayerContact();
            if (State == Motion.Searching || State == Motion.Recovery) Separate(dt);
            switch (State)
            {
                case Motion.Emerging:
                    transform.position = Vector3.Lerp(start, landing, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                    if ((IsDescending || villageEmergence) && !emergenceSplash && elapsed >= duration * .35f)
                    {
                        emergenceSplash = true;
                        LavaCombatEffects.Emerge(landing - Vector3.up * jump.groundClearance);
                    }
                    if (elapsed >= duration) { jump.CombatGrounded(landing); Change(Motion.Searching, 0f); }
                    break;
                case Motion.Searching:
                    bool siege = PursuingBuilding && !PlayerInPounceRange() && FindBuilding();
                    bool hasTarget = target && target.isActiveAndEnabled && target.Health > 0f;
                    Vector3 offset = hasTarget ? target.PursuitPosition - transform.position : Vector3.zero;
                    // A nearby player can intercept the procession, including from a vehicle.
                    if (!siege && IsDescending && hasTarget && Mathf.Abs(offset.y) < 8f &&
                        new Vector2(offset.x, offset.z).sqrMagnitude < 18f * 18f)
                        descentIndex = descentPath.Length - 1;
                    downhillLeap = IsDescending;
                    Vector3 goal = downhillLeap ? DescentGoal() : siege ? BuildingApproach() : (hasTarget ? target.PursuitPosition : transform.position);
                    downhillLeap = IsDescending;
                    if (!downhillLeap && !hasTarget && !siege) return;
                    offset = goal - transform.position;
                    offset.y = 0f;
                    if (!siege && !downhillLeap && !alerted && offset.sqrMagnitude > noticeDistance * noticeDistance) return;
                    alerted = true;
                    attacking = !siege && !downhillLeap && hasTarget && target.CanBeTargeted &&
                        FlatDistance(transform.position, target.ClosestThreatPoint(transform.position)) <= attackRange && cooldown <= 0f && ClaimAttack();
                    if (!siege && !downhillLeap && !attacking && hasTarget)
                    {
                        goal = ApproachPoint(); offset = goal - transform.position; offset.y = 0f;
                    }
                    if (!downhillLeap && !attacking)
                    {
                        goal = navigation.Next(transform.position, goal, radius + .45f);
                        offset = goal - transform.position; offset.y = 0;
                    }
                    if (!attacking && offset.magnitude < .65f) return;
                    direction = offset.sqrMagnitude > .001f ? offset.normalized : Vector3.forward;
                    start = downhillLeap ? transform.position : GroundAt(transform.position);
                    // Commit to the player's position now: strafing can evade the pounce.
                    float step = downhillLeap ? Mathf.Min(downhillHopDistance, offset.magnitude) :
                        Mathf.Min(attacking ? attackRange : hopDistance, Mathf.Max(0f, offset.magnitude - (attacking ? .55f : .1f)));
                    landing = attacking ? GroundAt(target.PursuitPosition) : ChooseLanding(start, direction, step);
                    if (!attacking && FlatDistance(start, landing) < .18f)
                    {
                        navigation.Invalidate(); detourSide = lane % 2 == 0 ? 1 : -1;
                        Change(Motion.Recovery, .18f); break;
                    }
                    if (downhillLeap && FlatDistance(landing, goal) < .4f) landing.y = Mathf.Max(landing.y, goal.y);
                    direction = (landing - start); direction.y = 0f;
                    if (direction.sqrMagnitude > .001f) direction.Normalize();
                    Change(Motion.Windup, attacking ? playerAttackWindup : .055f + lane % 3 * .01f);
                    if (attacking)
                    {
                        attackWait = 0f; struck = false;
                        jump.CombatGrounded(start);
                        attackGroundSurface = RoofVisualBase;
                        pounceEnd = PlayerPounceAim();
                        ShowAttackTell();
                    }
                    break;
                case Motion.Windup:
                    if (attacking)
                    {
                        if (!target || !target.CanBeTargeted)
                        {
                            attacking = false; HideAttackTell(); jump.CombatGrounded(start);
                            Change(Motion.Recovery, .2f); break;
                        }
                        jump.CombatAttackWindup(direction, Mathf.Clamp01(elapsed / duration));
                        transform.position += Vector3.up * (attackGroundSurface.y - RoofVisualBase.y);
                        // Track during the crouch, then commit at release. The old aim was already
                        // stale by half a second before a moving player even saw the launch.
                        pounceEnd = PlayerPounceAim();
                        direction = Vector3.ProjectOnPlane(pounceEnd - RoofBodyPosition, Vector3.up).normalized;
                        landing = GroundAt(target.PursuitPosition);
                    }
                    else jump.CombatWindup(direction, Mathf.Clamp01(elapsed / duration));
                    UpdateAttackTell();
                    if (elapsed < duration) return;
                    struck = false;
                    height = IsIntroductionJump ? 3.4f : attacking ? .42f : downhillLeap ? 1.65f : .95f;
                    if (attacking)
                    {
                        playerPounceLive = true;
                        cooldown = attackCooldown + lane % 4 * .17f;
                        pounceStart = RoofBodyPosition;
                        float reach = Mathf.InverseLerp(3f, attackRange, FlatDistance(pounceStart, pounceEnd));
                        pounceArcHeight = Mathf.Lerp(.8f, playerPounceHeight, reach);
                        pounceSeconds = Mathf.Lerp(.4f, playerPounceDuration, reach);
                        LavaCombatEffects.Launch(attackGroundSurface);
                    }
                    HideAttackTell();
                    jump.BeginCombatJump(direction);
                    Leaps++;
                    Change(Motion.Leaping, IsIntroductionJump ? 1.2f : attacking ? pounceSeconds : downhillLeap ?
                        Mathf.Max(.64f, Mathf.Sqrt(Mathf.Max(0f, start.y - landing.y) / 18f)) : hopDuration);
                    break;
                case Motion.Leaping:
                    if (attacking) { TickPlayerPounce(dt); break; }
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
                    if (IsIntroductionJump)
                        jump.CombatLaunchPose(next, (landing - start) + Vector3.up * (4f * height * (1f - 2f * t)), t);
                    else jump.CombatPose(next, t);
                    if (t < 1f) return;
                    grounded = landing;
                    jump.CombatGrounded(grounded);
                    actor.LandingFeedback(grounded, attacking);
                    IsIntroductionJump = false;
                    Change(Motion.Recovery, .065f + lane % 4 * .015f);
                    break;
                case Motion.Settling:
                    // A dodge or wall impact ends with a fall and a living spirit, never a
                    // timed explosion or another somersault in the middle of its landing.
                    float settle = Mathf.Clamp01(elapsed / duration);
                    jump.CombatLungePose(Vector3.Lerp(start, landing, settle * settle), settle);
                    if (settle >= 1f)
                    { grounded = landing; jump.CombatGrounded(grounded); Change(Motion.Recovery, .16f); }
                    break;
                case Motion.Recovery:
                    if (elapsed >= duration) Change(Motion.Searching, 0f);
                    break;
                case Motion.Igniting:
                    if (!CanAttackBuilding())
                    { CancelRoofWindup(); break; }
                    jump.CombatRoofWindup(direction, Mathf.Clamp01(elapsed / duration));
                    // Squash/stretch happens around the authored head pivot. Anchor the visible
                    // base to the floor so the release cannot stretch down through the ground.
                    transform.position += Vector3.up * (roofGroundSurface.y - RoofVisualBase.y);
                    if (elapsed >= duration)
                    {
                        roofFlight.Plan(BuildingTarget, RoofBodyPosition, roofJumpClearance, RoofBodyRadius);
                        if (!roofFlight.HasRoofCollider) { actor.Dissipate(); break; }
                        jump.BeginCombatJump(direction);
                        LavaCombatEffects.Launch(roofGroundSurface);
                        Leaps++;
                        Change(Motion.RoofLeaping, roofFlight.Duration);
                    }
                    break;
                case Motion.RoofLeaping:
                    TickRoofFlight(dt);
                    break;
                case Motion.Staggered:
                    // Only this enemy pauses for impact; player controls and world time continue.
                    if (elapsed < .065f) return;
                    if (animator) animator.speed = animatorSpeed;
                    float recovery = Mathf.Clamp01((elapsed - .065f) / .23f);
                    // Fast initial displacement, then a soft settle. The impulse reads at once.
                    float recoil = 1f - Mathf.Pow(1f - recovery, 3f);
                    Vector3 recoilPosition = Vector3.Lerp(start, landing, recoil);
                    Vector3 recoilTravel = recoilPosition - transform.position;
                    float recoilClear = ClearDistance(transform.position, recoilTravel.normalized, recoilTravel.magnitude);
                    transform.position += recoilTravel.normalized * recoilClear;
                    jump.RecoverCombatImpact(recoil);
                    if (recovery >= 1f) { grounded = GroundAt(transform.position); transform.position = grounded; jump.CombatGrounded(grounded); Change(Motion.Recovery, .1f); }
                    break;
            }
            if (!IsDescending && State != Motion.Staggered && State != Motion.Igniting && State != Motion.RoofLeaping)
            {
                progressClock += dt;
                if (progressClock >= 2.5f)
                {
                    if (FlatDistance(progressPosition, transform.position) < .6f)
                    { navigation.Invalidate(); detourSide = detourSide == 0 ? 1 : -detourSide; RouteRecoveries++; }
                    progressClock = 0; progressPosition = transform.position;
                }
            }
        }

        bool FindBuilding()
        {
            if (CanTargetBuilding(BuildingTarget)) return true;
            BuildingTarget = null; navigation.Invalidate(); float best = float.PositiveInfinity;
            foreach (var house in BurnableBuilding.ActiveBuildings)
            {
                if (!CanTargetBuilding(house)) continue;
                float score = FlatDistance(transform.position, house.ignitionBounds.ClosestPoint(transform.position));
                if (house.IsBurning) score += 48f;
                foreach (var other in crowd) if (other && other != this && other.PursuingBuilding && other.BuildingTarget == house) score += 24;
                if (score < best) { best = score; BuildingTarget = house; }
            }
            return BuildingTarget;
        }
        bool TickBuildingContact()
        {
            if (!FindBuilding()) return false;
            Vector3 contact = BuildingTarget.ignitionBounds.ClosestPoint(transform.position + Vector3.up * bodyOffset);
            if (FlatDistance(transform.position, contact) > buildingContactDistance || Mathf.Abs(contact.y - transform.position.y) > 8) return false;
            direction = contact - transform.position; direction.y = 0; direction.Normalize();
            grounded = GroundAt(transform.position);
            jump.CombatGrounded(grounded);
            roofGroundSurface = RoofVisualBase;
            RoofContactCollider = null;
            attacking = false; HideAttackTell(); Change(Motion.Igniting, roofWindupSeconds);
            return true;
        }
        static bool CanTargetBuilding(BurnableBuilding house) => house && house.isActiveAndEnabled &&
            house.ignitionBounds && !house.IsBurnedOut && !house.IsRescued && house.MissionFireAllowed &&
            (house.IsBurning ? house.IsDefenseFireTarget : !house.IgnitionBlocked);
        bool CanAttackBuilding() => CanTargetBuilding(BuildingTarget);

        void CancelRoofWindup()
        {
            BuildingTarget = null; navigation.Invalidate();
            jump.CombatGrounded(GroundAt(transform.position));
            Change(Motion.Searching, 0);
        }

        void TickRoofFlight(float dt)
        {
            // A lost/collapsed target cannot receive a remote ignition or redirect a committed leap.
            if (!BuildingTarget || !BuildingTarget.isActiveAndEnabled || BuildingTarget.IsBurnedOut)
            { actor.Dissipate(); return; }
            float fromTime = Mathf.Max(0, elapsed - dt), toTime = Mathf.Min(elapsed, duration);
            int steps = Mathf.Max(1, Mathf.CeilToInt((toTime - fromTime) / .02f));
            for (int i = 1; i <= steps; i++)
            {
                float at = Mathf.Lerp(fromTime, toTime, i / (float)steps);
                Vector3 next = roofFlight.Position(at), velocity = roofFlight.Velocity(at);
                if (roofFlight.Sweep(RoofBodyPosition, next, transform, RoofBodyRadius, out var hit))
                {
                    RoofContactCollider = hit.collider;
                    PlaceRoofBody(hit.point + hit.normal * (RoofBodyRadius + .025f), velocity, at / duration);
                    bool roof = hit.collider.GetComponentInParent<BurnableBuilding>() == BuildingTarget &&
                        hit.normal.y > .25f && velocity.y < 0f;
                    if (roof) ImpactRoof(hit.point, velocity);
                    else actor.Dissipate();
                    return;
                }
                PlaceRoofBody(next, velocity, at / duration);
            }
            if (elapsed < duration) return;
            // No collision means no ignition, even if the target's geometry changed mid-flight.
            actor.Dissipate();
        }

        float RoofBodyRadius => Mathf.Clamp(radius, .24f, .48f);
        float PlayerContactRadius => spiritVisual && spiritVisual.IsBuilt ? Mathf.Max(.24f,
            .46f * Mathf.Max(Mathf.Abs(spiritVisual.VisualRoot.lossyScale.x), Mathf.Abs(spiritVisual.VisualRoot.lossyScale.z))) : RoofBodyRadius;
        Vector3 RoofVisualBase => spiritVisual && spiritVisual.IsBuilt ?
            spiritVisual.VisualRoot.position : transform.position;
        Vector3 RoofBodyPosition => spiritVisual && spiritVisual.IsBuilt ?
            spiritVisual.VisualRoot.TransformPoint(Vector3.up * .65f) : transform.position + Vector3.up * bodyOffset;

        void PlaceRoofBody(Vector3 center, Vector3 velocity, float progress)
        {
            // The authored pivot is above the feet. Sweep the visible core instead, including
            // while it tilts into the dive, so the spirit actually touches the sloping tiles.
            jump.CombatLaunchPose(transform.position, velocity, progress);
            transform.position += center - RoofBodyPosition;
        }

        void ImpactRoof(Vector3 point, Vector3 velocity)
        {
            actor.ConsumeAtBuilding(point, velocity);
            BuildingTarget.IgniteFromRoofImpact(point);
        }
        Vector3 BuildingApproach()
        {
            return SpiritOutdoorNavigation.Exterior(BuildingTarget.ignitionBounds, transform.position,
                Mathf.Max(radius + .65f, buildingContactDistance * .6f));
        }
        public void Stagger(Vector3 shotDirection)
        {
            Initialize();
            if (!initialized || actor.IsDefeated) return;
            // The roof dive is committed. Surviving hits still flash, but cannot pull an airborne
            // spirit through the roof into a grounded recoil pose; lethal hits retire it normally.
            if (State == Motion.RoofLeaping || playerPounceLive) return;
            IsIntroductionJump = false;
            alerted = true;
            retaliationUntil = Time.time + 4.5f; navigation.Invalidate();
            if (IsDescending && target && FlatDistance(transform.position, target.PursuitPosition) < 26f)
                descentIndex = descentPath.Length - 1;
            attacking = false; HideAttackTell();
            cooldown = Mathf.Max(cooldown, .55f);
            shotDirection.y = 0f;
            if (shotDirection.sqrMagnitude < .001f) shotDirection = -direction;
            start = transform.position;
            landing = GroundAt(start + shotDirection.normalized * ClearDistance(start, shotDirection.normalized, 1.3f));
            // Hold the exact struck pose, including a midair flip, before easing upright.
            jump.CaptureCombatImpact();
            if (animator) animator.speed = 0f;
            Change(Motion.Staggered, .295f);
        }

        Vector3 PlayerPounceAim()
        {
            if (target.VehicleTarget) return target.ClosestThreatPoint(RoofBodyPosition);
            var body = target.ContactBody;
            if (!body || !body.enabled) return target.AttackPoint;
            Bounds bounds = body.bounds;
            // Aim through the lower torso, including the village's enlarged player capsule.
            return new Vector3(bounds.center.x, bounds.min.y + Mathf.Clamp(bounds.size.y * .35f, .65f, 1.6f), bounds.center.z);
        }

        void TickPlayerPounce(float dt)
        {
            float fromTime = Mathf.Max(0f, elapsed - dt), toTime = Mathf.Min(elapsed, duration);
            int steps = Mathf.Max(1, Mathf.CeilToInt((toTime - fromTime) / .02f));
            for (int i = 1; i <= steps; i++)
            {
                float t = Mathf.Lerp(fromTime, toTime, i / (float)steps) / duration;
                Vector3 next = Vector3.Lerp(pounceStart, pounceEnd, t) + Vector3.up * (4f * pounceArcHeight * t * (1f - t));
                Vector3 velocity = (pounceEnd - pounceStart) + Vector3.up * (4f * pounceArcHeight * (1f - 2f * t));
                Vector3 previous = RoofBodyPosition;
                float contactAt = 0f;
                bool playerHit = target && !target.VehicleTarget && target.SweepBodyContact(previous, next, PlayerContactRadius, out contactAt);
                bool worldHit = roofFlight.Sweep(previous, next, transform, RoofBodyRadius, out var hit);
                if (playerHit && (!worldHit || IsPlayerTarget(hit.collider) ||
                    hit.distance + .005f >= Vector3.Distance(previous, next) * contactAt))
                {
                    Vector3 center = Vector3.Lerp(previous, next, contactAt);
                    if (ExplosionCanReach(center, target.ClosestAttackPoint(center)))
                    { PlaceRoofBody(center, velocity, t); DetonatePlayerAttack(center, velocity, target.ContactBody); return; }
                }
                if (worldHit)
                {
                    Vector3 center = hit.point + hit.normal * (RoofBodyRadius + .025f);
                    PlaceRoofBody(center, velocity, t);
                    if (IsPlayerTarget(hit.collider)) DetonatePlayerAttack(center, velocity, hit.collider);
                    else RecoverMissedPlayerPounce(false);
                    return;
                }
                PlaceRoofBody(next, velocity, t);
                // A player who steps into an already overlapping cast still makes contact.
                int contacts = Physics.OverlapSphereNonAlloc(next, RoofBodyRadius + .005f, playerContacts,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int contact = 0; contact < contacts; contact++)
                    if (IsPlayerTarget(playerContacts[contact]) && ExplosionCanReach(next, playerContacts[contact].ClosestPoint(next)))
                    { DetonatePlayerAttack(next, velocity, playerContacts[contact]); return; }
            }
            if (elapsed >= duration) RecoverMissedPlayerPounce();
        }

        bool IsPlayerTarget(Collider contact)
        {
            if (!contact || !target || !target.CanBeTargeted) return false;
            var vehicle = target.VehicleTarget;
            return vehicle ? contact.transform.IsChildOf(vehicle.transform) : contact == target.ContactBody;
        }

        void RecoverMissedPlayerPounce(bool canContactWhileLanding = true)
        {
            // A missed dive or a scenery impact leaves a living spirit that can try again.
            // Settle from its current height before resuming the normal attack cooldown.
            // A scenery impact ends the attack; resetting its visual pivot while landing
            // must never turn that stopped dive into a fresh contact behind the obstacle.
            playerPounceLive &= canContactWhileLanding;
            attacking = downhillLeap = false;
            HideAttackTell();
            navigation.Invalidate();
            start = transform.position;
            landing = GroundAt(start);
            height = 0f;
            float fallSeconds = Mathf.Sqrt(2f * Mathf.Max(0f, start.y - landing.y) / 25f);
            Change(Motion.Settling, Mathf.Clamp(fallSeconds, .12f, .6f));
        }

        void DetonatePlayerAttack(Vector3 center, Vector3 velocity, Collider contact)
        {
            // Only a swept hit or a real overlap with the current target can consume the spirit.
            if (struck || actor.IsDefeated || !IsPlayerTarget(contact)) return;
            struck = true;
            // Apply the contact hit before retiring the last enemy can trigger mission events.
            if (target.CanBeAttacked && target.TryTakeDamage(attackDamage, center)) SuccessfulAttacks++;
            IgniteThermalContacts(center, playerBlastRadius);
            actor.Detonate(center, velocity);
        }

        void IgniteThermalContacts(Vector3 center, float reach)
        {
            foreach (var burnable in ThermalDamage.ActiveTargets)
            {
                if (!burnable || !burnable.CanIgnite) continue;
                Vector3 contact = burnable.ClosestPoint(center);
                if ((contact - center).sqrMagnitude > reach * reach || !ExplosionCanReach(center, contact, burnable.transform)) continue;
                if (burnable.IgniteFromSpirit() && target && burnable == target.VehicleTarget) SuccessfulAttacks++;
            }
        }

        bool ExplosionCanReach(Vector3 from, Vector3 contact, Transform thermalTarget = null)
        {
            Vector3 travel = contact - from;
            if (travel.sqrMagnitude < .0001f) return true;
            int count = Physics.RaycastNonAlloc(from, travel.normalized, hits, travel.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var obstacle = hits[i].collider;
                if (!obstacle || obstacle.transform.IsChildOf(transform) ||
                    (thermalTarget && obstacle.transform.IsChildOf(thermalTarget)) ||
                    (target && target.VehicleTarget && (thermalTarget == null || thermalTarget == target.VehicleTarget.transform) &&
                        obstacle.transform.IsChildOf(target.VehicleTarget.transform)) ||
                    (target && obstacle.transform.IsChildOf(target.transform)) ||
                    obstacle.GetComponentInParent<LavaCreatureActor>() || obstacle.GetComponentInParent<LavaIgnitionSource>() ||
                    obstacle.GetComponentInParent<ProgressiveLavaFlow>() || obstacle.GetComponentInParent<LavaCoolingSurface>()) continue;
                return false;
            }
            return true;
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
            for (int i = 0; i < (downhillLeap ? 7 : 13); i++)
            {
                float angle = i == 0 ? 0f : ((i + 1) / 2) * 30f * (i % 2 == 1 ? 1f : -1f);
                Vector3 route = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                float clear = ClearDistance(from, route, distance);
                Vector3 candidate = GroundAt(from + route * clear);
                if (SpiritOutdoorNavigation.Contains(candidate, radius + .2f)) continue;
                if (downhillLeap)
                {
                    // Shorten cliff-edge hops instead of refusing every downhill step.
                    for (int n = 0; n < 4 && candidate.y < from.y - 12f; n++)
                    { clear *= .5f; candidate = GroundAt(from + route * clear); }
                    if (candidate.y > from.y + 2.5f || candidate.y < from.y - 12f) continue;
                }
                else if (Mathf.Abs(candidate.y - from.y) > 1.7f) continue;
                float forwardProgress = Vector3.Dot(Vector3.ProjectOnPlane(candidate - from, Vector3.up), forward);
                float score = downhillLeap ? clear - Mathf.Abs(angle) * .05f - Occupancy(candidate) * 2f :
                    forwardProgress * 1.8f + clear * .25f - Mathf.Abs(angle) * .008f - Occupancy(candidate) * 1.6f;
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

        bool PlayerInPounceRange() => target && target.CanBeTargeted &&
            FlatDistance(transform.position, target.ClosestThreatPoint(transform.position)) <= attackRange &&
            Mathf.Abs(transform.position.y - target.ClosestThreatPoint(transform.position).y) < 5f;

        bool ClaimAttack()
        {
            int attackers = 0;
            foreach (var other in crowd)
            {
                if (!other || other == this || !other.isActiveAndEnabled || other.target != target) continue;
                if (other.PursuingBuilding && other.BuildingTarget && !other.IsAttacking && !other.PlayerInPounceRange()) continue;
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
            float ring = 3.4f + lane % 3 * .45f;
            Vector3 best = center + radial.normalized * ring;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < 12; i++)
            {
                float angle = i == 0 ? 0 : ((i + 1) / 2) * 30f * (i % 2 == 0 ? -1 : 1);
                Vector3 p = center + Quaternion.Euler(0, angle, 0) * radial.normalized * ring;
                p = SpiritOutdoorNavigation.Outside(p, transform.position, radius + .65f);
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
            float groundOffset = start.y - attackGroundSurface.y;
            attackTell.SetPosition(0, attackGroundSurface + Vector3.up * .055f);
            attackTell.SetPosition(1, GroundAt(Vector3.Lerp(start, landing, .55f)) + Vector3.up * (.055f - groundOffset));
            attackTell.SetPosition(2, landing + Vector3.up * (.055f - groundOffset));
            attackTell.startWidth = Mathf.Lerp(.07f, .2f, charge); attackTell.endWidth = .025f;
            attackTell.startColor = new Color(2.2f, .5f + charge * .7f, .08f, .4f + charge * .5f);
            attackTell.endColor = new Color(2.2f, .8f, .12f, .15f + charge * .5f);
        }
        void HideAttackTell() { if (attackTell) attackTell.enabled = false; }

        float ClearDistance(Vector3 from, Vector3 along, float distance)
        {
            if (distance < .001f) return 0f;
            float outdoor = distance;
            if (SpiritOutdoorNavigation.Blocked(from, from + along * distance, radius + .2f))
            {
                float low = 0, high = distance;
                for (int n = 0; n < 9; n++)
                { float middle = (low + high) * .5f; if (SpiritOutdoorNavigation.Blocked(from, from + along * middle, radius + .2f)) high = middle; else low = middle; }
                outdoor = Mathf.Max(0, low - .04f);
            }
            int count = Physics.SphereCastNonAlloc(from + Vector3.up * bodyOffset, radius, along, hits,
                distance + .07f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float result = outdoor;
            for (int i = 0; i < count; i++)
            {
                Collider obstacle = hits[i].collider;
                if (!obstacle || obstacle is TerrainCollider || obstacle.transform.IsChildOf(transform) ||
                    obstacle.GetComponentInParent<LavaCreatureActor>() ||
                    (target && obstacle.transform.IsChildOf(target.transform))) continue;
                // Birth can overlap the advancing lava mesh. Its zero-distance sweep normal is
                // horizontal, so treating it as a wall pins a spirit in place in every direction.
                if (obstacle.GetComponentInParent<ProgressiveLavaFlow>() ||
                    obstacle.GetComponentInParent<LavaIgnitionSource>() ||
                    obstacle.GetComponentInParent<LavaCoolingSurface>()) continue;
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
            RestorePlayerContact();
            crowd.Remove(this); attacking = playerPounceLive = false; HideAttackTell();
            if (animator) animator.speed = animatorSpeed;
            if (actor && actor.IsDefeated) State = Motion.Dead;
        }
    }
}
