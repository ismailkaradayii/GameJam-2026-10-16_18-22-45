using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Ashfall.MainMenu
{
    /// <summary>Plays the menu video and crossfades its final frame into the first frame at each loop.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class AshfallVideoBackgroundController : MonoBehaviour
    {
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private RawImage videoBackground;
        [SerializeField] private RawImage loopBlendImage;
        [SerializeField, Min(1f)] private float prepareTimeoutSeconds = 8f;
        [SerializeField, Min(0.1f)] private float loopCrossfadeSeconds = 0.8f;

        private bool hasDisplayedFrame;
        private bool waitingForLoopFrame;
        private bool failed;
        private Coroutine blendRoutine;
        private bool started;

        private void Awake()
        {
            if (videoPlayer == null) videoPlayer = GetComponent<VideoPlayer>();
            if (videoBackground != null) videoBackground.enabled = false;
            SetBlendAlpha(0f);
        }

        private void OnEnable()
        {
            if (videoPlayer == null) videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null) return;
            videoPlayer.prepareCompleted += OnPrepared;
            videoPlayer.errorReceived += OnErrorReceived;
            videoPlayer.frameReady += OnFrameReady;
            videoPlayer.loopPointReached += OnLoopPointReached;
            // Script recompiles can disable/enable this component while Play Mode
            // remains active. OnDisable stops the player, so restart preparation
            // here as well instead of relying on Start being called a second time.
            if (Application.isPlaying)
            {
                started = true;
                BeginPlayback();
            }
        }

        private void Start()
        {
            if (started) return;
            started = true;
            BeginPlayback();
        }

        private void BeginPlayback()
        {
            failed = hasDisplayedFrame = waitingForLoopFrame = false;
            if (videoPlayer == null || videoPlayer.clip == null || videoPlayer.targetTexture == null)
            {
                ShowFallback("Video clip or RenderTexture is not assigned.");
                return;
            }

            videoPlayer.playOnAwake = false;
            // The menu animation is intentionally silent; all menu audio comes from the audio settings system.
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.controlledAudioTrackCount = 0;
            // Native looping switches frames immediately. We restart manually to blend the seam.
            videoPlayer.isLooping = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.sendFrameReadyEvents = true;
            videoPlayer.Prepare();
            StartCoroutine(PrepareTimeout());
        }

        private IEnumerator PrepareTimeout()
        {
            yield return new WaitForSecondsRealtime(prepareTimeoutSeconds);
            if (!hasDisplayedFrame && !failed)
                ShowFallback($"Video did not produce a frame within {prepareTimeoutSeconds:0.#} seconds.");
        }

        private void OnPrepared(VideoPlayer source)
        {
            if (!failed) source.Play();
        }

        private void OnLoopPointReached(VideoPlayer source)
        {
            if (failed || !hasDisplayedFrame || waitingForLoopFrame) return;

            // Preserve the final image while the same player seeks and decodes the first frame.
            if (loopBlendImage != null && loopBlendImage.texture is RenderTexture snapshot
                && source.targetTexture != null)
            {
                Graphics.Blit(source.targetTexture, snapshot);
                loopBlendImage.enabled = true;
                SetBlendAlpha(1f);
            }

            source.Pause();
            waitingForLoopFrame = true;
            source.time = 0;
            source.Play();
        }

        private void OnFrameReady(VideoPlayer source, long frameIndex)
        {
            if (failed || frameIndex < 0) return;
            if (!hasDisplayedFrame)
            {
                hasDisplayedFrame = true;
                if (videoBackground != null) videoBackground.enabled = true;
                return;
            }

            if (!waitingForLoopFrame) return;
            waitingForLoopFrame = false;
            if (loopBlendImage == null || loopBlendImage.texture == null)
            {
                if (loopBlendImage != null) loopBlendImage.enabled = false;
                return;
            }
            if (blendRoutine != null) StopCoroutine(blendRoutine);
            blendRoutine = StartCoroutine(CrossfadeLoopSnapshot());
        }

        private IEnumerator CrossfadeLoopSnapshot()
        {
            float elapsed = 0f;
            while (elapsed < loopCrossfadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                SetBlendAlpha(1f - Mathf.Clamp01(elapsed / loopCrossfadeSeconds));
                yield return null;
            }
            SetBlendAlpha(0f);
            if (loopBlendImage != null) loopBlendImage.enabled = false;
            blendRoutine = null;
        }

        private void SetBlendAlpha(float alpha)
        {
            if (loopBlendImage == null) return;
            Color color = loopBlendImage.color;
            color.a = alpha;
            loopBlendImage.color = color;
        }

        private void OnErrorReceived(VideoPlayer source, string message)
        {
            ShowFallback($"VideoPlayer error: {message}");
        }

        private void ShowFallback(string reason)
        {
            if (failed) return;
            failed = true;
            if (videoPlayer != null && videoPlayer.isPlaying) videoPlayer.Stop();
            if (videoBackground != null) videoBackground.enabled = false;
            if (loopBlendImage != null) loopBlendImage.enabled = false;
            Debug.LogWarning($"ASHFALL menu video could not be displayed. The existing dark camera background remains visible. {reason}", this);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            blendRoutine = null;
            if (videoBackground) videoBackground.enabled = false;
            SetBlendAlpha(0f);
            if (loopBlendImage) loopBlendImage.enabled = false;
            if (videoPlayer == null) return;
            videoPlayer.prepareCompleted -= OnPrepared;
            videoPlayer.errorReceived -= OnErrorReceived;
            videoPlayer.frameReady -= OnFrameReady;
            videoPlayer.loopPointReached -= OnLoopPointReached;
            videoPlayer.Stop();
        }
    }
}
