using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameJam.FireRescue
{
    /// <summary>One camera/input lease shared by the wake-up, eruption and spirit reveals.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(300)]
    public sealed class VolcanoCinematic : MonoBehaviour
    {
        public enum ShotKind { Intro, Eruption, Spirits }
        public const float Duration = 10f; // Compatibility for older editor tools.
        public FireRescuePlayer player;
        public Transform crater;
        public Vector3 startOffset = new Vector3(500, 480, -900);
        public Vector3 endOffset = new Vector3(380, 420, -780);
        public AudioClip eruptionSound;
        [Min(.5f)] public float introSeconds = 2.2f;
        [Min(3f)] public float eruptionSeconds = 7f;
        [Range(2f, 3f)] public float spiritSeconds = 2.6f;
        [Range(.1f, 1f)] public float spiritTimeScale = .35f;
        [Min(.1f)] public float transitionSeconds = 1.2f;
        [Min(5f)] public float spiritViewingDistance = 14f;
        [Header("Eruption framing and aftershock")]
        public Vector3 eruptionFocusOffset = new Vector3(0, 130, 0);
        [Range(.05f, 1f)] public float eruptionShakeStrength = .42f;
        [Range(0f, 2f)] public float eruptionShakeDegrees = .8f;
        public VolcanoCameraShake aftermathShake;
        [Min(0f)] public float aftershockSeconds = 8f;
        [Range(0f, .3f)] public float aftershockStrength = .12f;
        public bool IsPlaying { get; private set; }
        public bool HasPlayed { get; private set; }
        public bool HasIntroPlayed { get; private set; }
        public bool HasSpiritsPlayed { get; private set; }
        public ShotKind Kind { get; private set; }
        public float Elapsed { get; private set; }
        public Vector3 FocusPosition => subject ? subject.position + (Kind == ShotKind.Eruption ? eruptionFocusOffset : Vector3.up * 1.5f) : returnPosition;
        public Camera ShotCamera => shot;
        public static bool IsPlayingAny { get; private set; }
        Camera shot;
        Transform subject;
        Vector3 returnPosition, introPosition, frozenVelocity, frozenAngularVelocity;
        Quaternion returnRotation, introRotation;
        float returnFov, shotDuration, previousTimeScale, previousFixedStep;
        bool viewEnabled, handsEnabled, previousKinematic, frozenTruck, ownsSlowMotion;
        CursorLockMode savedLock;
        bool savedCursor;
        GUIStyle caption;
        int beginFrame;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() => IsPlayingAny = false;

        public void BeginIntro(Transform wakePose)
        {
            if (HasIntroPlayed || !wakePose || !Begin(ShotKind.Intro, introSeconds)) return;
            HasIntroPlayed = true;
            introPosition = wakePose.position; introRotation = wakePose.rotation;
            UpdatePose();
        }
        public void BeginShot()
        {
            if (HasPlayed || !crater || !Begin(ShotKind.Eruption, eruptionSeconds)) return;
            HasPlayed = true; subject = crater;
            if (eruptionSound && !FindFirstObjectByType<WindAudioPulse>())
            {
                var audio = shot.gameObject.AddComponent<AudioSource>(); audio.spatialBlend = 0; audio.volume = .85f;
                audio.PlayOneShot(eruptionSound);
            }
            UpdatePose();
        }
        public void BeginSpirits(Transform firstSpirit)
        {
            if (HasSpiritsPlayed || !firstSpirit || !Begin(ShotKind.Spirits, spiritSeconds)) return;
            HasSpiritsPlayed = true; subject = firstSpirit;
            ownsSlowMotion = true;
            Time.timeScale = spiritTimeScale;
            Time.fixedDeltaTime = previousFixedStep * spiritTimeScale;
            UpdatePose();
        }
        bool Begin(ShotKind kind, float seconds)
        {
            if (IsPlaying || !player || !player.viewCamera) return false;
            Kind = kind; shotDuration = Mathf.Max(.5f, seconds);
            var view = player.viewCamera;
            returnPosition = view.transform.position; returnRotation = view.transform.rotation; returnFov = view.fieldOfView;
            viewEnabled = view.enabled;
            handsEnabled = player.firstPerson && player.firstPerson.handsCamera && player.firstPerson.handsCamera.enabled;
            savedLock = Cursor.lockState; savedCursor = Cursor.visible;
            previousTimeScale = Time.timeScale; previousFixedStep = Time.fixedDeltaTime;
            player.SetCinematicLocked(true);
            if (player.IsDriving && player.drive && player.drive.body)
            {
                var body = player.drive.body;
                previousKinematic = body.isKinematic; frozenVelocity = body.linearVelocity; frozenAngularVelocity = body.angularVelocity;
                body.isKinematic = true; frozenTruck = true;
            }
            var go = new GameObject("Mission cinematic camera"); go.transform.SetParent(transform, false);
            shot = go.AddComponent<Camera>(); shot.CopyFrom(view);
            var data = shot.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true; data.volumeLayerMask = view.GetUniversalAdditionalCameraData().volumeLayerMask;
            shot.depth = view.depth + 20; shot.farClipPlane = Mathf.Max(5000, view.farClipPlane);
            shot.transform.SetPositionAndRotation(returnPosition, returnRotation);
            view.enabled = false;
            if (player.firstPerson && player.firstPerson.handsCamera) player.firstPerson.handsCamera.enabled = false;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            Elapsed = 0; beginFrame = Time.frameCount; IsPlaying = IsPlayingAny = true;
            return true;
        }
        void Update()
        {
            // Scene loading can report a long first delta; keep the wake-up visible.
            if (IsPlaying && Time.frameCount > beginFrame) TickShot(Mathf.Min(Time.unscaledDeltaTime, .2f));
        }
        public void TickShot(float dt)
        {
            if (!IsPlaying || dt <= 0 || !float.IsFinite(dt)) return;
            Elapsed = Mathf.Min(shotDuration, Elapsed + dt);
            UpdatePose();
            if (Elapsed >= shotDuration - .0001f) EndShot();
        }
        void LateUpdate() { if (IsPlaying) UpdatePose(); }
        void UpdatePose()
        {
            if (!shot) return;
            if (Kind == ShotKind.Intro)
            {
                float t = Mathf.SmoothStep(0, 1, Elapsed / shotDuration);
                shot.transform.SetPositionAndRotation(Vector3.Lerp(introPosition, returnPosition, t), Quaternion.Slerp(introRotation, returnRotation, t));
                shot.fieldOfView = Mathf.Lerp(returnFov - 9, returnFov, t);
                return;
            }
            Vector3 focus = subject ? subject.position : returnPosition + returnRotation * Vector3.forward * 20;
            Vector3 from, to;
            float zoomStart, zoomEnd;
            if (Kind == ShotKind.Eruption)
            {
                from = focus + startOffset; to = focus + endOffset; focus += eruptionFocusOffset;
                zoomStart = 58; zoomEnd = 46;
            }
            else
            {
                focus += Vector3.up * 1.5f;
                Vector3 direction = Vector3.ProjectOnPlane(returnPosition - focus, Vector3.up).normalized;
                if (direction.sqrMagnitude < .1f) direction = Vector3.back;
                from = focus + direction * spiritViewingDistance + Vector3.up * 5;
                to = focus + direction * (spiritViewingDistance * .75f) + Vector3.up * 3.5f;
                zoomStart = 52; zoomEnd = 39;
            }
            float blendSeconds = Mathf.Min(transitionSeconds, shotDuration * .27f);
            float progress = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(blendSeconds, shotDuration - blendSeconds, Elapsed));
            Vector3 position = Vector3.Lerp(from, to, progress);
            Quaternion rotation = Quaternion.LookRotation(focus - position);
            float fov = Mathf.Lerp(zoomStart, zoomEnd, progress);
            float enter = Mathf.SmoothStep(0, 1, Elapsed / blendSeconds);
            float leave = Mathf.SmoothStep(0, 1, (Elapsed - shotDuration + blendSeconds) / blendSeconds);
            position = Vector3.Lerp(Vector3.Lerp(returnPosition, position, enter), returnPosition, leave);
            rotation = Quaternion.Slerp(Quaternion.Slerp(returnRotation, rotation, enter), returnRotation, leave);
            fov = Mathf.Lerp(Mathf.Lerp(returnFov, fov, enter), returnFov, leave);
            if (Kind == ShotKind.Eruption)
            {
                float pressure = Mathf.Exp(-Mathf.Max(0, Elapsed - 1.3f) * 1.8f) * Mathf.SmoothStep(0, 1, (Elapsed - 1f) / .4f);
                float envelope = (.22f + pressure * .78f) * enter * (1 - leave);
                float n1 = Mathf.PerlinNoise(Elapsed * 11f, 5.7f) * 2 - 1;
                float n2 = Mathf.PerlinNoise(32.6f, Elapsed * 13f) * 2 - 1;
                position += (rotation * new Vector3(n1, n2 * .7f, 0)) * (eruptionShakeStrength * envelope);
                rotation *= Quaternion.Euler(n2 * eruptionShakeDegrees * .45f * envelope, n1 * eruptionShakeDegrees * .3f * envelope, n1 * eruptionShakeDegrees * envelope);
            }
            shot.transform.SetPositionAndRotation(position, rotation); shot.fieldOfView = fov;
        }
        public void EndShot()
        {
            if (!IsPlaying) return;
            IsPlaying = IsPlayingAny = false;
            if (ownsSlowMotion) { Time.timeScale = previousTimeScale; Time.fixedDeltaTime = previousFixedStep; ownsSlowMotion = false; }
            if (player)
            {
                if (frozenTruck && player.drive && player.drive.body)
                {
                    var body = player.drive.body; body.isKinematic = previousKinematic;
                    if (!previousKinematic) { body.linearVelocity = frozenVelocity; body.angularVelocity = frozenAngularVelocity; }
                }
                if (player.viewCamera)
                {
                    player.viewCamera.transform.SetPositionAndRotation(returnPosition, returnRotation);
                    player.viewCamera.fieldOfView = returnFov; player.viewCamera.enabled = viewEnabled;
                }
                player.SetCinematicLocked(false);
                if (player.firstPerson && player.firstPerson.handsCamera) player.firstPerson.handsCamera.enabled = handsEnabled;
            }
            frozenTruck = false;
            // Start a fresh envelope on the real gameplay camera, after returning control.
            // Interrupted shots (restart, defeat, unloading) must not create an aftershock.
            if (Kind == ShotKind.Eruption && Elapsed >= shotDuration - .001f && player &&
                (!player.gameFlow || !player.gameFlow.BlocksGameplay))
            {
                if (!aftermathShake && player.viewCamera) aftermathShake = player.viewCamera.GetComponent<VolcanoCameraShake>();
                if (aftermathShake) aftermathShake.Shake(aftershockSeconds, aftershockStrength);
            }
            Cursor.lockState = savedLock; Cursor.visible = savedCursor;
            if (shot) { shot.enabled = false; Destroy(shot.gameObject); }
        }
        void OnDisable() => EndShot();
        void OnGUI()
        {
            if (!IsPlaying) return;
            int depth = GUI.depth; Color color = GUI.color;
            GUI.depth = -100; GUI.color = Color.black;
            float bar = Screen.height * .085f;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
            if (caption == null) caption = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            caption.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 40f, 14, 30));
            caption.normal.textColor = new Color(1, .83f, .6f);
            GUI.color = Color.white;
            string message = Kind == ShotKind.Intro ? "ASHFALL  |  EVERY SECOND COUNTS" : Kind == ShotKind.Spirits ?
                "DEFEND THE VILLAGE! SHOOT THE FIRE SPIRITS!" : "VOLCANO ERUPTION IN: 00:00  |  THE VOLCANO HAS AWAKENED";
            GUI.Label(new Rect(0, Screen.height - bar, Screen.width, bar), message, caption);
            GUI.color = color; GUI.depth = depth;
        }
    }
}
