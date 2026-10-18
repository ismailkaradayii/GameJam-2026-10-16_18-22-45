using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a lava creature jump forever without player input.
/// Attach this component to the non-animated outer/root GameObject so the
/// Animator can animate the child model without competing for its position.
/// </summary>
[DisallowMultipleComponent]
public sealed class LavaCreatureAutoJump : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Animator on this object or one of its children. Found automatically when empty.")]
    [SerializeField] private Animator animator;
    [Tooltip("Complete visual model rotated during the somersault. Uses the Animator transform when empty.")]
    [SerializeField] private Transform visualRoot;
    [Tooltip("Player or destination approached one bounce at a time. Leave empty for vertical jumps.")]
    [SerializeField] private Transform target;
    [Tooltip("Optional gameplay terrain. When assigned, jump landings follow its surface instead of a fixed height.")]
    public Terrain groundTerrain;
    [Min(0f)] public float groundClearance = 0.02f;

    [Header("Timing")]
    [Min(0f)] [SerializeField] private float minimumWaitTime = 1.5f;
    [Min(0f)] [SerializeField] private float maximumWaitTime = 3.5f;
    [Min(0.05f)] [SerializeField] private float jumpDuration = 0.8f;
    [SerializeField] private bool initialRandomDelay = true;

    [Header("Movement")]
    [Min(0f)] [SerializeField] private float jumpHeight = 1.5f;
    [Min(0f)] [SerializeField] private float forwardDistance = 2f;
    [Min(0f)] [SerializeField] private float stoppingDistance = 1.25f;

    [Header("Forward Somersault")]
    [Min(0)] [SerializeField] private int somersaultCount = 1;
    [Range(0f, 1f)] [SerializeField] private float fireballTuckAmount = 0.85f;
    [Range(0f, 0.5f)] [SerializeField] private float fireballStartTime = 0.15f;
    [Range(0.5f, 1f)] [SerializeField] private float fireballEndTime = 0.85f;
    [Range(0.5f, 1f)] [SerializeField] private float midairFireballScale = 0.9f;
    [Min(1f)] [SerializeField] private float flameIntensity = 2f;
    [Tooltip("Optional ember system. Emission is enabled only during the fireball phase.")]
    [SerializeField] private ParticleSystem emberParticles;
    [Min(0f)] [SerializeField] private float particleIntensity = 18f;

    [Header("Optional Emergence")]
    [SerializeField] private bool emergeOnEnable;
    [Min(0f)] [SerializeField] private float emergenceDepth = 1f;
    [Min(0.05f)] [SerializeField] private float emergenceDuration = 0.8f;

    private static readonly int JumpTrigger = Animator.StringToHash("Jump");
    private static readonly int IdleState = Animator.StringToHash("Base Layer.Idle");
    private static readonly int JumpState = Animator.StringToHash("Base Layer.Jump");

    private Coroutine jumpLoop;
    private Vector3 groundPosition;
    private bool hasJumpTrigger;
    private bool hasIdleState;
    private bool hasJumpState;
    private bool animatorInitialized;
    private Quaternion visualRestLocalRotation;
    private Vector3 visualRestLocalScale;
    private Quaternion impactRotation;
    private Vector3 impactScale;
    private readonly List<Renderer> flameRenderers = new List<Renderer>();
    private readonly List<Color> flameBaseEmission = new List<Color>();
    private MaterialPropertyBlock emissionBlock;
    private bool combatControlled;

    // Gameplay actors use the same authored flip/tuck without competing coroutines.
    public Transform Target => target;
    public void TakeCombatControl()
    {
        combatControlled = true;
        if (jumpLoop != null) StopCoroutine(jumpLoop);
        jumpLoop = null;
        InitializeAnimator();
        groundPosition = transform.position;
        RestoreVisualTransform();
    }

    public void CombatWindup(Vector3 direction, float amount)
    {
        FaceCombatDirection(direction);
        if (visualRoot != null)
            visualRoot.localScale = Vector3.Scale(visualRestLocalScale,
                new Vector3(1f + .16f * amount, 1f + .16f * amount, 1f - .28f * amount));
        SetFireballEffects(amount * 1.3f);
    }

    public void BeginCombatJump(Vector3 direction)
    {
        FaceCombatDirection(direction);
        PlayJump();
    }

    public void CombatRoofWindup(Vector3 direction, float amount)
    {
        FaceCombatDirection(direction);
        float load = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount / .82f));
        float release = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.82f, 1f, amount));
        if (visualRoot)
        {
            // Authored +Z is up: visibly compress into the floor, then snap into the launch.
            visualRoot.localScale = Vector3.Scale(visualRestLocalScale,
                new Vector3(1f + .32f * load - .43f * release, 1f + .22f * load - .3f * release,
                    1f - .52f * load + 1.12f * release));
            visualRoot.localRotation = visualRestLocalRotation * Quaternion.AngleAxis(10f * load - 24f * release, Vector3.right);
        }
        SetFireballEffects(load * 2f);
    }

    public void CombatAttackWindup(Vector3 direction, float amount)
    {
        CombatRoofWindup(direction, amount);
        if (!visualRoot) return;
        float load = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount / .82f));
        visualRoot.localRotation *= Quaternion.AngleAxis(-8f * load, Vector3.right);
    }

    public void CombatLaunchPose(Vector3 position, Vector3 velocity, float progress)
    {
        transform.position = position;
        FaceCombatDirection(velocity);
        if (!visualRoot) return;
        float pitch = Mathf.Atan2(velocity.y, new Vector2(velocity.x, velocity.z).magnitude) * Mathf.Rad2Deg;
        float thrust = 1f - Mathf.Clamp01(progress / .3f);
        visualRoot.localRotation = visualRestLocalRotation * Quaternion.AngleAxis(-pitch, Vector3.right);
        visualRoot.localScale = Vector3.Scale(visualRestLocalScale, new Vector3(.88f, 1.12f + .4f * thrust, .88f));
        SetFireballEffects(2f);
    }

    public void CombatPose(Vector3 position, float progress)
    {
        transform.position = position;
        if (visualRoot == null) return;
        float ball = CalculateFireballAmount(progress);
        visualRoot.localRotation = visualRestLocalRotation * Quaternion.AngleAxis(-360f * somersaultCount * progress, Vector3.right);
        visualRoot.localScale = Vector3.Scale(visualRestLocalScale,
            new Vector3(1f + .2f * ball, 1f + .2f * ball, 1f - .38f * ball)) * Mathf.Lerp(1f, midairFireballScale, ball);
        SetFireballEffects(ball * 1.5f);
    }

    public void CombatLungePose(Vector3 position, float progress)
    {
        transform.position = position;
        if (!visualRoot) return;
        float drive = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
        visualRoot.localRotation = visualRestLocalRotation * Quaternion.AngleAxis(-26f * drive, Vector3.right);
        // Local -Y faces forward in the authored FBX. Keep the face visible during the attack.
        visualRoot.localScale = Vector3.Scale(visualRestLocalScale,
            new Vector3(1f - .1f * drive, 1f + .38f * drive, 1f - .16f * drive));
        SetFireballEffects(drive * 1.8f);
    }

    public void CombatGrounded(Vector3 position)
    {
        groundPosition = position;
        transform.position = position;
        RestoreVisualTransform();
        PlayIdle();
    }

    public void CaptureCombatImpact()
    {
        if (visualRoot == null) return;
        impactRotation = visualRoot.localRotation;
        impactScale = visualRoot.localScale;
    }

    public void RecoverCombatImpact(float amount)
    {
        if (visualRoot == null) return;
        visualRoot.localRotation = Quaternion.Slerp(impactRotation, visualRestLocalRotation, amount);
        visualRoot.localScale = Vector3.Lerp(impactScale, visualRestLocalScale, amount);
    }

    void FaceCombatDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > .0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up) * ModelBasisCorrection;
    }

    // Inspected FBX basis: the face points along local -Y and the head/up axis
    // is local +Z. This correction maps that basis to Unity forward/up.
    private static readonly Quaternion ModelBasisCorrection = Quaternion.Inverse(
        Quaternion.LookRotation(Vector3.down, Vector3.forward));

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator == null)
            return;

        animator.applyRootMotion = false;
        if (visualRoot == null)
            visualRoot = FindVisualRoot();

        visualRestLocalRotation = visualRoot.localRotation;
        visualRestLocalScale = visualRoot.localScale;
        CacheFlameRenderers();
    }

    private void OnEnable()
    {
        groundPosition = transform.position;
        if (TryTerrainHeight(groundPosition, out float surfaceY))
            groundPosition.y = surfaceY;
        transform.position = groundPosition;
        if (!combatControlled && jumpLoop == null)
            jumpLoop = StartCoroutine(AutoJumpLoop());
    }

    private void OnDisable()
    {
        if (jumpLoop != null)
        {
            StopCoroutine(jumpLoop);
            jumpLoop = null;
        }

        // Always leave the creature exactly at its cached ground point.
        transform.position = groundPosition;
        RestoreVisualTransform();
        PlayIdle();
    }

    private void OnValidate()
    {
        maximumWaitTime = Mathf.Max(maximumWaitTime, minimumWaitTime);
        jumpDuration = Mathf.Max(0.05f, jumpDuration);
        emergenceDuration = Mathf.Max(0.05f, emergenceDuration);
        jumpHeight = Mathf.Max(0f, jumpHeight);
        forwardDistance = Mathf.Max(0f, forwardDistance);
        stoppingDistance = Mathf.Max(0f, stoppingDistance);
        somersaultCount = Mathf.Max(0, somersaultCount);
        fireballEndTime = Mathf.Max(fireballStartTime + 0.05f, fireballEndTime);
        flameIntensity = Mathf.Max(1f, flameIntensity);
        particleIntensity = Mathf.Max(0f, particleIntensity);
        emergenceDepth = Mathf.Max(0f, emergenceDepth);
    }

    private IEnumerator AutoJumpLoop()
    {
        // During an Editor domain reload the nested FBX Animator can be restored
        // one frame after this wrapper. Waiting avoids querying it while its
        // controller is temporarily unavailable.
        yield return null;
        InitializeAnimator();
        PlayIdle();

        if (emergeOnEnable)
            yield return MoveFromBelowLava();

        if (initialRandomDelay)
            yield return new WaitForSeconds(RandomWaitTime());

        while (enabled)
        {
            if (HasReachedTarget())
            {
                PlayIdle();
                yield return new WaitForSeconds(0.25f);
                continue;
            }

            yield return PerformJump();
            yield return new WaitForSeconds(RandomWaitTime());
        }
    }

    private IEnumerator PerformJump()
    {
        Vector3 startPosition = groundPosition;
        Vector3 landingPosition = CalculateLandingPosition(startPosition, out Vector3 travelDirection);

        // Face the actual model forward axis (-Y), not Transform.forward. This
        // is what distinguishes a frontflip from moving backward while rotating.
        if (travelDirection.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(travelDirection, Vector3.up) * ModelBasisCorrection;

        Quaternion visualStartLocalRotation = visualRoot != null ? visualRoot.localRotation : Quaternion.identity;
        Vector3 visualStartLocalScale = visualRoot != null ? visualRoot.localScale : Vector3.one;

        PlayJump();
        float elapsed = 0f;

        while (elapsed < jumpDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / jumpDuration);
            // 4t(1-t) is a smooth parabola: zero at both ends, one at the apex.
            float verticalOffset = 4f * jumpHeight * progress * (1f - progress);
            Vector3 horizontalPosition = Vector3.Lerp(startPosition, landingPosition, progress);
            transform.position = horizontalPosition + Vector3.up * verticalOffset;

            if (visualRoot != null)
            {
                // The inspected model uses local X as its left-right axis,
                // local -Y as forward and local +Z as up. Rotating the dedicated
                // pivot around local +X moves +Z (head) toward -Y (forward).
                // Keeping this entirely local prevents the parent's FBX axis
                // correction from introducing a sideways roll.
                // The inspected model's forward direction is local -Y. A
                // negative local-X rotation carries the head forward toward
                // -Y, producing a frontflip instead of a backflip.
                float angle = -360f * somersaultCount * progress;
                visualRoot.localRotation = visualStartLocalRotation
                    * Quaternion.AngleAxis(angle, Vector3.right);

                float fireballAmount = CalculateFireballAmount(progress);
                float tuck = fireballAmount * fireballTuckAmount;
                float uniformScale = Mathf.Lerp(1f, midairFireballScale, fireballAmount);

                // In the inspected FBX, local Z is character height. Compress
                // that axis and widen X/Y to create a round lava-ball silhouette.
                Vector3 fireballShape = new Vector3(
                    1f + 0.2f * tuck,
                    1f + 0.2f * tuck,
                    1f - 0.38f * tuck);
                visualRoot.localScale = Vector3.Scale(visualStartLocalScale, fireballShape) * uniformScale;
                SetFireballEffects(fireballAmount);
            }
            yield return null;
        }

        groundPosition = landingPosition;
        transform.position = groundPosition;
        RestoreVisualTransform();
        SetFireballEffects(0f);
        PlayIdle();
    }

    private Vector3 CalculateLandingPosition(Vector3 startPosition, out Vector3 travelDirection)
    {
        travelDirection = Vector3.zero;
        if (target == null || forwardDistance <= 0f)
            return startPosition;

        Vector3 toTarget = target.position - startPosition;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (distance <= stoppingDistance || distance <= 0.0001f)
            return startPosition;

        travelDirection = toTarget / distance;
        float step = Mathf.Min(forwardDistance, distance - stoppingDistance);
        Vector3 landingPosition = startPosition + travelDirection * step;
        if (groundTerrain != null)
        {
            // Stay on the playable terrain instead of continuing at a stale height beyond its edge.
            if (!TryTerrainHeight(landingPosition, out float surfaceY)) return startPosition;
            landingPosition.y = surfaceY;
        }
        return landingPosition;
    }

    private bool TryTerrainHeight(Vector3 position, out float height)
    {
        height = position.y;
        if (groundTerrain == null || groundTerrain.terrainData == null) return false;
        Vector3 terrainOrigin = groundTerrain.transform.position;
        Vector3 terrainSize = groundTerrain.terrainData.size;
        Vector3 local = position - terrainOrigin;
        if (local.x < 0f || local.z < 0f || local.x > terrainSize.x || local.z > terrainSize.z) return false;
        height = groundTerrain.SampleHeight(position) + terrainOrigin.y + Mathf.Max(0f, groundClearance);
        return true;
    }

    private bool HasReachedTarget()
    {
        if (target == null) return false;
        Vector3 offset = target.position - groundPosition;
        offset.y = 0f;
        return offset.sqrMagnitude <= stoppingDistance * stoppingDistance;
    }

    private IEnumerator MoveFromBelowLava()
    {
        Vector3 submergedPosition = groundPosition - Vector3.up * emergenceDepth;
        transform.position = submergedPosition;
        PlayJump();

        float elapsed = 0f;
        while (elapsed < emergenceDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / emergenceDuration));
            transform.position = Vector3.LerpUnclamped(submergedPosition, groundPosition, progress);
            yield return null;
        }

        transform.position = groundPosition;
        PlayIdle();
    }

    private float RandomWaitTime()
    {
        return Random.Range(minimumWaitTime, Mathf.Max(minimumWaitTime, maximumWaitTime));
    }

    private void PlayJump()
    {
        if (animator == null) return;

        if (hasJumpTrigger)
        {
            animator.ResetTrigger(JumpTrigger);
            animator.SetTrigger(JumpTrigger);
        }
        else if (hasJumpState)
        {
            animator.CrossFadeInFixedTime(JumpState, 0.08f, 0, 0f);
        }
    }

    private void PlayIdle()
    {
        if (animator != null && animator.isActiveAndEnabled && hasIdleState)
            animator.CrossFadeInFixedTime(IdleState, 0.1f);
    }

    private bool HasParameter(int nameHash, AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == nameHash && parameter.type == type)
                return true;
        }
        return false;
    }

    private void InitializeAnimator()
    {
        if (animatorInitialized) return;

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator == null)
        {
            Debug.LogWarning($"{name}: LavaCreatureAutoJump could not find an Animator. Movement will still run, but animations will not play.", this);
            return;
        }

        animator.applyRootMotion = false;
        if (visualRoot == null)
            visualRoot = FindVisualRoot();
        visualRestLocalRotation = visualRoot.localRotation;
        visualRestLocalScale = visualRoot.localScale;
        CacheFlameRenderers();
        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"{name}: The Animator has no Runtime Animator Controller. Movement will run without animation.", this);
            return;
        }

        hasJumpTrigger = HasParameter(JumpTrigger, AnimatorControllerParameterType.Trigger);
        hasIdleState = animator.HasState(0, IdleState);
        hasJumpState = animator.HasState(0, JumpState);
        animatorInitialized = true;

        if (!hasJumpTrigger && !hasJumpState)
            Debug.LogWarning($"{name}: Animator needs either a 'Jump' trigger or a state named 'Jump'.", this);
        if (!hasIdleState)
            Debug.LogWarning($"{name}: Animator has no state named 'Idle'.", this);
    }

    private void RestoreVisualTransform()
    {
        if (visualRoot == null) return;
        visualRoot.localRotation = visualRestLocalRotation;
        visualRoot.localScale = visualRestLocalScale;
        SetFireballEffects(0f);
    }

    private Transform FindVisualRoot()
    {
        if (animator == null) return null;

        // The dedicated pivot is intentionally outside the animated FBX.
        // Animator may change bones/model transforms, but it cannot overwrite
        // the procedural somersault rotation on this parent.
        Transform parent = animator.transform.parent;
        if (parent != null && parent.name == "SomersaultVisual")
            return parent;

        return animator.transform;
    }

    private float CalculateFireballAmount(float progress)
    {
        float enter = Mathf.InverseLerp(0f, Mathf.Max(0.01f, fireballStartTime), progress);
        float exit = 1f - Mathf.InverseLerp(fireballEndTime, 1f, progress);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(enter, exit)));
    }

    private void CacheFlameRenderers()
    {
        flameRenderers.Clear();
        flameBaseEmission.Clear();
        if (visualRoot == null) return;

        foreach (Renderer candidate in visualRoot.GetComponentsInChildren<Renderer>(true))
        {
            string objectName = candidate.name.ToLowerInvariant();
            if (!objectName.Contains("flame") && !objectName.Contains("ember")) continue;

            flameRenderers.Add(candidate);
            Material material = candidate.sharedMaterial;
            flameBaseEmission.Add(material != null && material.HasProperty("_EmissionColor")
                ? material.GetColor("_EmissionColor")
                : Color.black);
        }
        emissionBlock ??= new MaterialPropertyBlock();
    }

    private void SetFireballEffects(float amount)
    {
        for (int i = 0; i < flameRenderers.Count; i++)
        {
            Renderer flame = flameRenderers[i];
            if (flame == null) continue;
            flame.GetPropertyBlock(emissionBlock);
            emissionBlock.SetColor("_EmissionColor", flameBaseEmission[i] * Mathf.Lerp(1f, flameIntensity, amount));
            flame.SetPropertyBlock(emissionBlock);
        }

        if (emberParticles == null) return;
        ParticleSystem.EmissionModule emission = emberParticles.emission;
        emission.rateOverTimeMultiplier = particleIntensity * amount;
        if (amount > 0.01f && !emberParticles.isPlaying)
            emberParticles.Play();
        else if (amount <= 0.01f && emberParticles.isPlaying)
            emberParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
