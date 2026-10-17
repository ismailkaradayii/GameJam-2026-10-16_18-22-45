using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Render-time camera offsets, applied after gameplay aiming and removed before the next update.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class VolcanoCameraShake : MonoBehaviour
    {
        [Min(0.01f)] public float noiseFrequency = 15f;
        [Min(0f)] public float defaultStrength = 0.35f;
        [Min(0f)] public float rotationDegreesAtDefaultStrength = 0.7f;
        [Range(0f, 1f)] public float aimedStrengthMultiplier = .35f;
        [Range(.5f, 3f)] public float decayExponent = 1.4f;

        public float Intensity { get; private set; }
        public bool IsShaking => duration > 0f && elapsed < duration;

        private float duration;
        private float elapsed;
        private float strength;
        private float noiseTime;
        private float impactDuration, impactElapsed, impactDegrees;
        private bool offsetApplied;
        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;
        private Vector3 appliedLocalPosition;
        private Quaternion appliedLocalRotation;
        private VolcanoCameraShakeRestore restoreHelper;

        private void Awake() => EnsureRestoreHelper();

        private void OnEnable()
        {
            EnsureRestoreHelper();
            restoreHelper.enabled = true;
        }

        private void LateUpdate() => TickShake(Time.deltaTime);

        public void Shake(float shakeDuration, float shakeStrength)
        {
            if (!isActiveAndEnabled || !FinitePositive(shakeDuration) || !FinitePositive(shakeStrength))
                return;
            EnsureRestoreHelper();
            float previousRemaining = Mathf.Max(0f, duration - elapsed);
            float previousIntensity = Intensity;
            if (!IsShaking)
                noiseTime = 0f;
            duration = Mathf.Max(shakeDuration, previousRemaining);
            elapsed = 0f;
            strength = Mathf.Max(shakeStrength, previousIntensity);
            Intensity = strength;
        }

        public void Shake(float shakeDuration) => Shake(shakeDuration, defaultStrength);

        public void ShakeImpact(float shakeDuration, float shakeStrength, float rotationDegrees)
        {
            if (!isActiveAndEnabled || !FinitePositive(shakeDuration) || !FinitePositive(shakeStrength) || !FinitePositive(rotationDegrees)) return;
            Shake(shakeDuration, shakeStrength);
            impactDuration = shakeDuration; impactElapsed = 0f;
            impactDegrees = Mathf.Clamp(rotationDegrees, 0f, 3f);
        }

        /// <summary>Automatic LateUpdate step; also available for deterministic amplitude/cleanup checks.</summary>
        public void TickShake(float deltaTime)
        {
            RestoreOffset();
            if (!isActiveAndEnabled || !IsShaking || deltaTime < 0f ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;
            elapsed = Mathf.Min(duration, elapsed + deltaTime);
            impactElapsed = Mathf.Min(impactDuration, impactElapsed + deltaTime);
            noiseTime += deltaTime;
            float envelope = 1f - Mathf.Clamp01(elapsed / duration);
            Intensity = strength * Mathf.Pow(envelope, decayExponent);
            if (Intensity <= 0f)
                return;

            float phase = noiseTime * Mathf.Max(0.01f, noiseFrequency);
            Vector3 positionNoise = new Vector3(Noise(phase, 17.3f), Noise(phase, 53.7f), Noise(phase, 91.1f));
            // Clamp the vector magnitude so strength is a true maximum displacement in metres.
            var controller = GetComponentInParent<Itfaiye.ItfaiyeFirstPersonController>();
            float aim = controller && controller.pistol ? controller.pistol.AimAmount : 0;
            float renderedIntensity = Intensity * Mathf.Lerp(1f, aimedStrengthMultiplier, aim);
            Vector3 positionOffset = Vector3.ClampMagnitude(positionNoise, 1f) * renderedIntensity;
            float rotationScale = rotationDegreesAtDefaultStrength * renderedIntensity / Mathf.Max(0.01f, defaultStrength);
            Vector3 rotationOffset = new Vector3(Noise(phase, 131.9f) * 0.5f,
                Noise(phase, 171.4f) * 0.5f, Noise(phase, 211.8f)) * rotationScale;
            if (impactDuration > 0f && impactElapsed < impactDuration)
            {
                float kick = Mathf.Pow(1f - impactElapsed / impactDuration, 2f) * impactDegrees;
                rotationOffset += new Vector3(-Mathf.Cos(impactElapsed * 39f), 0f, Mathf.Sin(impactElapsed * 32f) * .7f) * kick;
            }

            baseLocalPosition = transform.localPosition;
            baseLocalRotation = transform.localRotation;
            Vector3 worldOffset = transform.rotation * positionOffset;
            Vector3 localOffset = transform.parent != null ? transform.parent.InverseTransformVector(worldOffset) : worldOffset;
            appliedLocalPosition = baseLocalPosition + localOffset;
            appliedLocalRotation = baseLocalRotation * Quaternion.Euler(rotationOffset);
            transform.SetLocalPositionAndRotation(appliedLocalPosition, appliedLocalRotation);
            offsetApplied = true;
        }

        public void StopShake()
        {
            RestoreOffset();
            duration = 0f;
            elapsed = 0f;
            strength = 0f;
            noiseTime = 0f;
            Intensity = 0f;
            impactDuration = impactElapsed = impactDegrees = 0f;
        }

        internal void RestoreOffset()
        {
            if (!offsetApplied)
                return;
            // Another camera controller may already have replaced either pose component. In that
            // case leave its absolute write intact instead of subtracting an offset it never included.
            if ((transform.localPosition - appliedLocalPosition).sqrMagnitude < 0.00000001f)
                transform.localPosition = baseLocalPosition;
            if (Quaternion.Angle(transform.localRotation, appliedLocalRotation) < 0.001f)
                transform.localRotation = baseLocalRotation;
            offsetApplied = false;
        }

        private void EnsureRestoreHelper()
        {
            if (restoreHelper != null)
                return;
            restoreHelper = gameObject.AddComponent<VolcanoCameraShakeRestore>();
            restoreHelper.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            restoreHelper.owner = this;
        }

        private void OnDisable()
        {
            StopShake();
            if (restoreHelper != null)
                restoreHelper.enabled = false;
        }

        private void OnDestroy()
        {
            if (restoreHelper == null)
                return;
            restoreHelper.owner = null;
            if (Application.isPlaying)
                Destroy(restoreHelper);
            else
                DestroyImmediate(restoreHelper);
        }

        private static float Noise(float time, float seed) => Mathf.PerlinNoise(time + 0.37f, seed) * 2f - 1f;
        private static bool FinitePositive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    // Separate execution order is necessary: the main component runs after camera LateUpdate,
    // while restoration must happen before the player writes its first-person camera in Update.
    [DefaultExecutionOrder(-10000)]
    [AddComponentMenu("")]
    public sealed class VolcanoCameraShakeRestore : MonoBehaviour
    {
        [System.NonSerialized] public VolcanoCameraShake owner;

        private void Update()
        {
            if (owner != null)
                owner.RestoreOffset();
        }
    }
}
