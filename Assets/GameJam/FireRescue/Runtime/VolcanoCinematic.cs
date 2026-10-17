using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>A ten-second unbroken aerial reveal; the sequence owns its clock.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(300)]
    public sealed class VolcanoCinematic : MonoBehaviour
    {
        public const float Duration = 10f;
        public FireRescuePlayer player;
        public Transform crater;
        public Vector3 startOffset = new Vector3(500, 480, -900);
        public Vector3 endOffset = new Vector3(380, 420, -780);
        public AudioClip eruptionSound;
        public bool IsPlaying { get; private set; }
        public bool HasPlayed { get; private set; }
        public float Elapsed { get; private set; }
        public Camera ShotCamera => shot;
        public static bool IsPlayingAny { get; private set; }
        Camera shot;
        AudioSource audioSource;
        bool viewEnabled, handsEnabled;
        CursorLockMode savedLock;
        bool savedCursor;
        GUIStyle caption;
        VolumeProfile grading;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() => IsPlayingAny = false;

        public void BeginShot()
        {
            if (IsPlaying || HasPlayed || !crater || !player || !player.viewCamera) return;
            var go = new GameObject("Eruption cinematic camera");
            go.transform.SetParent(transform, false);
            shot = go.AddComponent<Camera>(); shot.CopyFrom(player.viewCamera);
            var cameraData = shot.GetUniversalAdditionalCameraData(); cameraData.renderPostProcessing = true; cameraData.volumeLayerMask = ~0;
            var volume = go.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 1000;
            grading = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = grading;
            var color = grading.Add<ColorAdjustments>(); color.postExposure.Override(1.65f);
            var bloom = grading.Add<Bloom>(); bloom.intensity.Override(.35f); bloom.threshold.Override(1.15f); bloom.scatter.Override(.65f);
            var vignette = grading.Add<Vignette>(); vignette.intensity.Override(.16f); vignette.smoothness.Override(.7f);
            shot.cullingMask = player.viewCamera.cullingMask;
            shot.depth = player.viewCamera.depth + 20; shot.nearClipPlane = 1; shot.farClipPlane = 5000;
            shot.fieldOfView = 54;
            viewEnabled = player.viewCamera.enabled;
            handsEnabled = player.firstPerson && player.firstPerson.handsCamera && player.firstPerson.handsCamera.enabled;
            player.viewCamera.enabled = false;
            if (player.firstPerson && player.firstPerson.handsCamera) player.firstPerson.handsCamera.enabled = false;
            savedLock = Cursor.lockState; savedCursor = Cursor.visible;
            player.SetCinematicLocked(true);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            Elapsed = 0; HasPlayed = IsPlaying = IsPlayingAny = true;
            audioSource = go.AddComponent<AudioSource>(); audioSource.spatialBlend = 0; audioSource.volume = .85f;
            if (eruptionSound) audioSource.PlayOneShot(eruptionSound);
            UpdatePose();
        }
        public void TickShot(float dt)
        {
            if (!IsPlaying || dt <= 0 || !float.IsFinite(dt)) return;
            Elapsed = Mathf.Min(Duration, Elapsed + dt);
            UpdatePose();
            if (Elapsed >= Duration - .0001f) EndShot();
        }
        void LateUpdate() { if (IsPlaying) UpdatePose(); }
        void UpdatePose()
        {
            if (!shot || !crater) return;
            float t = Mathf.SmoothStep(0, 1, Elapsed / Duration);
            Vector3 position = crater.position + Vector3.Lerp(startOffset, endOffset, t);
            Vector3 focus = crater.position + Vector3.up * Mathf.Lerp(140, 180, t);
            float tremor = Mathf.Exp(-Elapsed * .37f) * .34f + (Elapsed > 3.8f && Elapsed < 5.3f ? .12f : 0);
            position += new Vector3(Mathf.Sin(Elapsed * 37), Mathf.Sin(Elapsed * 29), 0) * tremor;
            shot.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
            shot.fieldOfView = Mathf.Lerp(54, 47, t);
        }
        public void EndShot()
        {
            if (!IsPlaying) return;
            IsPlaying = IsPlayingAny = false;
            if (player)
            {
                if (player.viewCamera) player.viewCamera.enabled = viewEnabled;
                player.SetCinematicLocked(false);
                if (player.firstPerson && player.firstPerson.handsCamera) player.firstPerson.handsCamera.enabled = handsEnabled;
            }
            Cursor.lockState = savedLock; Cursor.visible = savedCursor;
            if (shot) { shot.enabled = false; Destroy(shot.gameObject); }
            if (grading) Destroy(grading);
        }
        void OnDisable() => EndShot();
        void OnGUI()
        {
            if (!IsPlaying) return;
            int depth = GUI.depth; Color color = GUI.color;
            GUI.depth = -100; GUI.color = Color.black;
            float bar = Screen.height * .10f;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
            if (caption == null) { caption = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 21, fontStyle = FontStyle.Bold }; caption.normal.textColor = new Color(1, .82f, .59f); }
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01((Elapsed - 1.8f) / 1.5f));
            GUI.Label(new Rect(0, Screen.height - bar, Screen.width, bar), Elapsed < 6.5f ? "KÜL VE ATEŞ" : "Halk güvende. Şimdi kasabayı koru.", caption);
            GUI.color = color; GUI.depth = depth;
        }
    }
}
