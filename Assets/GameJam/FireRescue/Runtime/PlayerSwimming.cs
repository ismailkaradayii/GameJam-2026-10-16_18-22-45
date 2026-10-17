using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Itfaiye;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController)), DefaultExecutionOrder(120)]
    public sealed class PlayerSwimming : MonoBehaviour
    {
        [Min(.1f)] public float swimSpeed = 2.5f;
        [Min(.1f)] public float fastSwimSpeed = 3.6f;
        [Min(.1f)] public float verticalSpeed = 2f;
        [Min(.1f)] public float waterAcceleration = 4.5f;
        [Header("Pool exit")]
        [Min(.2f)] public float exitReach = 1.65f;
        [Min(.1f)] public float maximumLedgeHeight = .85f;
        [Min(.3f)] public float climbDuration = .7f;
        public bool IsSwimming { get; private set; }
        public bool IsSubmerged { get; private set; }
        public float Blend { get; private set; }
        public float StrokePhase { get; private set; }
        public Vector3 Velocity { get; private set; }
        public WaterSourceVolume Water { get; private set; }
        public Vector3 CameraOffset => new Vector3(0, Mathf.Sin(StrokePhase * 2) * .012f * Blend, 0);
        public int EntryCount { get; private set; }
        public bool IsClimbing => climbLeft > 0;
        public int CompletedClimbs { get; private set; }
        public bool UsesAuthoredAnimations { get; private set; }
        public int HandStrokeCount { get; private set; }
        CharacterController motor;
        ItfaiyeFirstPersonController player;
        FireRescuePlayer coordinator;
        float standingHeight, eyeHeight, nextWake, climbLeft, climbLength, nextClimb;
        int lastStrokeIndex;
        Transform leftHand, rightHand;
        Vector3 standingCenter, climbDestination, climbStart;
        Collider climbSupport;
        bool bodySwimmingParameters, handsSwimmingParameters;
        static readonly int SwimmingId = Animator.StringToHash("Swimming");
        static readonly int SwimSpeedId = Animator.StringToHash("SwimSpeed");
        static readonly int SwimPlaybackId = Animator.StringToHash("SwimPlaybackRate");
        bool previousWeaponAvailable, previousEquipped;
        Volume underwater;
        VolumeProfile profile;
        readonly RaycastHit[] hits = new RaycastHit[24];
        readonly Collider[] overlap = new Collider[24];
        readonly System.Collections.Generic.HashSet<Collider> ignoredSurfaces = new System.Collections.Generic.HashSet<Collider>();

        void Awake()
        {
            motor = GetComponent<CharacterController>(); player = GetComponent<ItfaiyeFirstPersonController>();
            coordinator = GetComponent<FireRescuePlayer>();
            standingHeight = motor.height; standingCenter = motor.center;
            eyeHeight = player && player.playerCamera ? player.playerCamera.transform.localPosition.y : standingHeight * .9f;
            bodySwimmingParameters = HasSwimParameters(player ? player.bodyAnimator : null);
            handsSwimmingParameters = HasSwimParameters(player ? player.handsAnimator : null);
            UsesAuthoredAnimations = bodySwimmingParameters && handsSwimmingParameters;
            if (player && player.handsAnimator && player.handsAnimator.isHuman)
            {
                leftHand = player.handsAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                rightHand = player.handsAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            }
        }

        public bool Simulate(Vector2 input, bool sprint, float vertical, bool captured, float dt, System.Func<Vector3, Vector3> constrain)
        {
            if (!isActiveAndEnabled || !motor || !motor.enabled || dt <= 0) return false;
            float scale = Mathf.Abs(transform.lossyScale.y);
            // A decorative water mesh may have a solid collider. Only this swimmer ignores it;
            // the pool floor/rim and other gameplay queries keep their normal collisions.
            foreach (var water in WaterSourceVolume.ActiveSources)
            {
                if (!water || !water.waterSurface) continue;
                var surfaceCollider = water.waterSurface.GetComponent<Collider>();
                if (surfaceCollider && !surfaceCollider.isTrigger && ignoredSurfaces.Add(surfaceCollider))
                    Physics.IgnoreCollision(motor, surfaceCollider, true);
            }
            if (climbLeft > 0)
            {
                AdvanceClimb(dt, scale);
                UpdateAnimations(dt);
                return true;
            }
            WaterSourceVolume found = null;
            Vector3 chest = transform.position + Vector3.up * ((IsSwimming ? .62f : .90f) * scale);
            foreach (var candidate in WaterSourceVolume.ActiveSources)
                if (candidate && candidate.ContainsIntake(chest)) { found = candidate; break; }
            if (!found && IsSwimming && Water && Water.ContainsSurface(transform.position) &&
                transform.position.y < Water.SurfaceY + .18f * scale && !motor.isGrounded) found = Water;
            if (found && !IsSwimming) Enter(found);
            else if (!found && IsSwimming) Exit();
            else if (found) Water = found;
            Blend = Mathf.MoveTowards(Blend, IsSwimming ? 1 : 0, dt * 5f);
            if (!IsSwimming) { UpdateAnimations(dt); UpdateUnderwater(false, dt); return false; }

            input = captured ? Vector2.ClampMagnitude(input, 1) : Vector2.zero;
            vertical = captured ? Mathf.Clamp(vertical, -1, 1) : 0;
            IsSubmerged = player && player.playerCamera && player.playerCamera.transform.position.y < Water.SurfaceY - .08f;
            if (captured && vertical > .5f && TryClimb(input, scale)) return true;
            float targetY = Water.SurfaceY - eyeHeight * scale + .24f * scale;
            float floatVelocity = Mathf.Clamp((targetY - transform.position.y) * 3.2f, -1.5f, 2.8f);
            Vector3 direction = transform.TransformDirection(new Vector3(input.x, 0, input.y));
            // Looking down while swimming forward dives naturally. At the surface, neutral input floats.
            if (player && Mathf.Abs(player.Pitch) > 20 && input.y > 0)
                direction.y = -Mathf.Sin(player.Pitch * Mathf.Deg2Rad) * input.y;
            float desiredVertical = vertical != 0 ? vertical * verticalSpeed :
                Mathf.Abs(direction.y) > .3f ? direction.y * verticalSpeed : floatVelocity;
            if (transform.position.y >= targetY && desiredVertical > 0 && vertical >= 0) desiredVertical = floatVelocity;
            direction.y = 0;
            Vector3 target = direction * (sprint ? fastSwimSpeed : swimSpeed) + Vector3.up * desiredVertical;
            Velocity = Vector3.MoveTowards(Velocity, target, waterAcceleration * dt);
            Vector3 travel = Velocity * dt;
            if (constrain != null) travel = constrain(travel);
            Vector3 previous = transform.position;
            var collision = motor.Move(travel);
            Velocity = (transform.position - previous) / dt;
            if ((collision & CollisionFlags.Above) != 0 && Velocity.y > 0) Velocity = new Vector3(Velocity.x, 0, Velocity.z);
            if (UsesAuthoredAnimations && player.handsAnimator.GetCurrentAnimatorStateInfo(0).IsName("Swimming"))
                StrokePhase = player.handsAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime * Mathf.PI * 2;
            else StrokePhase += dt * (input.sqrMagnitude > .01f ? (sprint ? 5.8f : 4.5f) : 1.8f);
            if (player && player.handsCamera) player.handsCamera.enabled = true;
            if (player && player.pistol) player.pistol.SetAvailable(false);
            UpdateAnimations(dt); SurfaceMotion(); UpdateUnderwater(IsSubmerged, dt);
            return true;
        }

        void Enter(WaterSourceVolume water)
        {
            Water = water; IsSwimming = true; EntryCount++;
            float fall = motor.velocity.y;
            Velocity = Vector3.ClampMagnitude(motor.velocity, 5f);
            if (player && player.pistol)
            {
                previousWeaponAvailable = player.pistol.Available; previousEquipped = player.pistol.Equipped;
                player.pistol.SetAvailable(false);
            }
            if (coordinator && coordinator.hose && coordinator.hose.IsCarried)
                coordinator.hose.Drop(transform.position + transform.forward * .7f);
            motor.height = .95f; motor.center = Vector3.up * (eyeHeight - .48f);
            water.Effects.Enter(transform.position, fall);
            nextWake = Time.time + .18f;
            lastStrokeIndex = Mathf.FloorToInt(StrokePhase / Mathf.PI + .3f);
            if (player && player.bodyAnimator) player.bodyAnimator.ResetTrigger("Jump");
            if (player && player.handsAnimator) player.handsAnimator.ResetTrigger("Jump");
        }

        void Exit()
        {
            IsSwimming = IsSubmerged = false; Water = null; Velocity = Vector3.zero;
            climbLeft = 0; climbSupport = null;
            motor.height = standingHeight; motor.center = standingCenter;
            UpdateAnimations(0);
            if (player && player.pistol)
            {
                if (coordinator) coordinator.RefreshEquipmentState();
                else player.pistol.SetAvailable(previousWeaponAvailable);
                if (previousEquipped && player.pistol.Available) player.pistol.Equip();
            }
        }

        void SurfaceMotion()
        {
            if (!Water || !player || !player.playerCamera || Mathf.Abs(player.playerCamera.transform.position.y - Water.SurfaceY) > .9f) return;
            float speed = new Vector2(Velocity.x, Velocity.z).magnitude;
            if (Time.time >= nextWake)
            {
                nextWake = Time.time + (speed > .2f ? .22f : .8f);
                Water.Effects.Swim(transform.position - transform.forward * .22f, Velocity, false);
            }
        }

        void LateUpdate()
        {
            if (!IsSwimming || !Water || IsClimbing || Time.deltaTime <= 0 || !player || !player.playerCamera) return;
            float scale = Mathf.Max(.1f, Mathf.Abs(transform.lossyScale.y));
            if (IsSubmerged || Mathf.Abs(player.playerCamera.transform.position.y - Water.SurfaceY) > .9f * scale) return;
            // Evaluate after the animator and the first-person arm pose, once per half stroke.
            float phase = StrokePhase;
            if (UsesAuthoredAnimations && player.handsAnimator.GetCurrentAnimatorStateInfo(0).IsName("Swimming"))
                phase = player.handsAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime * Mathf.PI * 2;
            int index = Mathf.FloorToInt(phase / Mathf.PI + .3f);
            if (index == lastStrokeIndex) return;
            bool advancing = index > lastStrokeIndex && index - lastStrokeIndex <= 2;
            lastStrokeIndex = index;
            if (!advancing) return;
            float side = index % 2 == 0 ? -1 : 1;
            var hand = side < 0 ? leftHand : rightHand;
            Vector3 point = hand ? hand.position : player.playerCamera.transform.position +
                transform.right * (side * .38f * scale) + transform.forward * (.45f * scale);
            // Viewmodel fingers can rise clear of the surface; the paddle still displaces water below them.
            point.y = Water.SurfaceY;
            if (!Water.ContainsSurface(point)) return;
            Vector3 pull = Velocity * .3f - transform.forward * 1.5f + transform.right * (side * .5f);
            Water.Effects.HandStroke(point, pull, Mathf.InverseLerp(0, fastSwimSpeed, Velocity.magnitude));
            HandStrokeCount++;
        }

        bool TryClimb(Vector2 input, float scale)
        {
            if (IsSubmerged || Time.time < nextClimb) return false;
            Vector3 direction = input.sqrMagnitude > .01f ? transform.TransformDirection(new Vector3(input.x, 0, input.y)).normalized : transform.forward;
            float radius = motor.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
            float edgeDistance = DistanceToBoundary(direction);
            if (!Water.rejectDryTerrain && edgeDistance > radius + .55f * scale) return false;
            float reach = exitReach * scale + radius;
            // Sample both the near coping and a full capsule width onto the deck.
            // A single fixed ray could overshoot a narrow edge, land in water, or leave the capsule in the rim.
            for (int probe = 0; probe < 7; probe++)
            {
                float distance = probe < 4 ? radius + (.25f + probe * .35f) * scale : edgeDistance + radius + (probe == 4 ? .12f : probe == 5 ? .4f : .7f) * scale;
                if (distance <= .05f || distance > reach) continue;
                if (!Water.rejectDryTerrain && distance < edgeDistance + radius + .08f * scale) continue;
                Vector3 origin = transform.position + direction * distance;
                origin.y = Water.SurfaceY + maximumLedgeHeight * scale + .1f * scale;
                int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, (maximumLedgeHeight + .6f) * scale, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) continue;
                float nearest = float.PositiveInfinity; RaycastHit ledge = default;
                for (int i = 0; i < count; i++)
                {
                    var h = hits[i];
                    if (Ignored(h.collider) || h.normal.y < .65f || h.distance >= nearest) continue;
                    nearest = h.distance; ledge = h;
                }
                if (float.IsPositiveInfinity(nearest) || ledge.point.y < Water.SurfaceY - .48f * scale ||
                    ledge.point.y > Water.SurfaceY + maximumLedgeHeight * scale) continue;
                if (ledge.point.y < Water.SurfaceY + .03f * scale && Water.ContainsSurface(ledge.point)) continue;
                Vector3 landing = ledge.point + Vector3.up * (.06f * scale);
                if (!StandingFits(landing, scale)) continue;
                climbStart = transform.position; climbDestination = landing; climbSupport = ledge.collider;
                climbLength = Mathf.Max(.3f, climbDuration); climbLeft = climbLength; Velocity = Vector3.zero;
                return true;
            }
            return false;
        }

        float DistanceToBoundary(Vector3 direction)
        {
            var volume = Water.volume;
            Vector3 local = volume.transform.InverseTransformPoint(transform.position) - volume.center;
            Vector3 delta = volume.transform.InverseTransformVector(direction);
            Vector3 half = volume.size * .5f;
            float x = Mathf.Abs(delta.x) > .001f ? ((delta.x > 0 ? half.x : -half.x) - local.x) / delta.x : float.PositiveInfinity;
            float z = Mathf.Abs(delta.z) > .001f ? ((delta.z > 0 ? half.z : -half.z) - local.z) / delta.z : float.PositiveInfinity;
            return Mathf.Max(0, Mathf.Min(x, z));
        }

        bool Ignored(Collider collider) => !collider || collider.transform.IsChildOf(transform) ||
            ignoredSurfaces.Contains(collider) || WaterSourceVolume.IsSurfaceCollider(collider);

        bool StandingFits(Vector3 landing, float scale)
        {
            float radius = motor.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) * .98f;
            Vector3 center = landing + transform.TransformVector(standingCenter);
            float halfLine = Mathf.Max(0, standingHeight * scale * .5f - radius);
            int count = Physics.OverlapCapsuleNonAlloc(center - Vector3.up * halfLine, center + Vector3.up * halfLine,
                radius, overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == overlap.Length) return false;
            for (int i = 0; i < count; i++) if (!Ignored(overlap[i])) return false;
            return true;
        }

        void AdvanceClimb(float dt, float scale)
        {
            if (!climbSupport || !climbSupport.enabled) { CancelClimb(); return; }
            climbLeft = Mathf.Max(0, climbLeft - dt);
            float progress = 1 - climbLeft / climbLength;
            float raisedY = Mathf.Max(climbStart.y, climbDestination.y + .08f * scale);
            Vector3 raisedStart = new Vector3(climbStart.x, raisedY, climbStart.z);
            Vector3 raisedEnd = new Vector3(climbDestination.x, raisedY, climbDestination.z);
            Vector3 desired = progress < .42f ? Vector3.Lerp(climbStart, raisedStart, Mathf.SmoothStep(0, 1, progress / .42f)) :
                progress < .85f ? Vector3.Lerp(raisedStart, raisedEnd, Mathf.SmoothStep(0, 1, (progress - .42f) / .43f)) :
                Vector3.Lerp(raisedEnd, climbDestination, Mathf.SmoothStep(0, 1, (progress - .85f) / .15f));
            Vector3 previous = transform.position;
            var flags = motor.Move(desired - previous); Velocity = (transform.position - previous) / dt;
            if ((flags & CollisionFlags.Above) != 0) { CancelClimb(); return; }
            if (climbLeft > 0) return;
            if (Vector3.Distance(transform.position, climbDestination) <= .12f * scale && StandingFits(climbDestination, scale))
            { CompletedClimbs++; Exit(); }
            else CancelClimb();
        }

        void CancelClimb()
        {
            climbLeft = 0; climbSupport = null; nextClimb = Time.time + .25f; Velocity = Vector3.zero;
        }

        static bool HasSwimParameters(Animator animator)
        {
            if (!animator || !animator.runtimeAnimatorController) return false;
            bool swimming = false, speed = false, playback = false;
            foreach (var p in animator.parameters)
            { swimming |= p.nameHash == SwimmingId; speed |= p.nameHash == SwimSpeedId; playback |= p.nameHash == SwimPlaybackId; }
            return swimming && speed && playback;
        }

        void UpdateAnimations(float dt)
        {
            if (!player) return;
            float speed = new Vector2(Velocity.x, Velocity.z).magnitude;
            float blend = IsClimbing ? 0 : Mathf.InverseLerp(.08f, .65f, speed);
            float playback = Mathf.Lerp(1, 1.2f, Mathf.InverseLerp(swimSpeed, fastSwimSpeed, speed));
            SetAnimation(player.bodyAnimator, bodySwimmingParameters, blend, playback, dt);
            SetAnimation(player.handsAnimator, handsSwimmingParameters, blend, playback, dt);
        }

        void SetAnimation(Animator animator, bool supported, float speed, float playback, float dt)
        {
            if (!animator || !supported) return;
            animator.SetBool(SwimmingId, IsSwimming); animator.SetFloat(SwimPlaybackId, playback);
            if (dt > 0) animator.SetFloat(SwimSpeedId, speed, .12f, dt); else animator.SetFloat(SwimSpeedId, speed);
        }

        void UpdateUnderwater(bool submerged, float dt)
        {
            if (submerged && !underwater)
            {
                var go = new GameObject("Underwater view"); go.transform.SetParent(transform, false);
                underwater = go.AddComponent<Volume>(); underwater.isGlobal = true; underwater.priority = 120; underwater.weight = 0;
                profile = ScriptableObject.CreateInstance<VolumeProfile>(); underwater.sharedProfile = profile;
                var color = profile.Add<ColorAdjustments>(); color.colorFilter.Override(new Color(.55f, .83f, .87f));
                color.postExposure.Override(-.3f); color.contrast.Override(-12f); color.saturation.Override(-14f);
                var vignette = profile.Add<Vignette>(); vignette.intensity.Override(.27f); vignette.smoothness.Override(.7f);
            }
            if (underwater) underwater.weight = Mathf.MoveTowards(underwater.weight, submerged ? 1 : 0, dt * 4f);
        }

        public void ResetSwimming()
        {
            if (IsSwimming) Exit();
            Blend = 0; climbLeft = 0; if (underwater) underwater.weight = 0;
        }
        void RestoreCollisions()
        {
            if (motor) foreach (var surface in ignoredSurfaces) if (surface) Physics.IgnoreCollision(motor, surface, false);
            ignoredSurfaces.Clear();
        }
        void OnDisable() { ResetSwimming(); RestoreCollisions(); }
        void OnDestroy()
        {
            RestoreCollisions();
            if (profile) { foreach (var component in profile.components) if (component) Destroy(component); Destroy(profile); }
        }
    }
}
