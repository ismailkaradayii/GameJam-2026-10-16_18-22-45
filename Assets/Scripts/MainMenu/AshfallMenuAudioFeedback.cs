using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ashfall.MainMenu
{
    /// <summary>Plays short UI sounds without replacing or duplicating menu actions.</summary>
    [DisallowMultipleComponent]
    public sealed class AshfallMenuAudioFeedback : MonoBehaviour
    {
        private const string SfxVolumeKey = "Ashfall.SfxVolume";

        [SerializeField] private Button[] targetButtons;
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

        private readonly List<Button> wiredButtons = new List<Button>();

        private void Awake()
        {
            if (hoverClip != null) hoverClip.LoadAudioData();
            if (clickClip != null && clickClip != hoverClip) clickClip.LoadAudioData();

            if (targetButtons == null || targetButtons.Length == 0)
            {
                Canvas canvas = FindFirstObjectByType<Canvas>();
                if (canvas != null)
                    targetButtons = canvas.GetComponentsInChildren<Button>(true);
            }

            if (targetButtons == null) return;
            for (int i = 0; i < targetButtons.Length; i++)
            {
                Button button = targetButtons[i];
                if (button == null || wiredButtons.Contains(button)) continue;
                wiredButtons.Add(button);
                AshfallMenuButtonPointerAudio pointerAudio = button.GetComponent<AshfallMenuButtonPointerAudio>();
                if (pointerAudio == null) pointerAudio = button.gameObject.AddComponent<AshfallMenuButtonPointerAudio>();
                pointerAudio.Configure(hoverClip, volume, this);
                button.onClick.RemoveListener(PlayClick);
                button.onClick.AddListener(PlayClick);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < wiredButtons.Count; i++)
                if (wiredButtons[i] != null) wiredButtons[i].onClick.RemoveListener(PlayClick);
        }

        private void PlayClick() { if (isActiveAndEnabled) Play(clickClip, volume); }

        private static AudioSource activeHover;

        internal static void Play(AudioClip clip, float baseVolume, bool hover = false)
        {
            if (clip == null) return;
            if (hover && activeHover && activeHover.isPlaying) return;
            float sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 1f));
            float volumeScale = Mathf.Clamp01(baseVolume) * sfxVolume * (hover ? .45f : 1f);
            if (volumeScale <= 0f) return;
            var voice = new GameObject("UI Audio One Shot");
            DontDestroyOnLoad(voice);
            var source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.clip = clip; source.volume = volumeScale; source.Play();
            if (hover) activeHover = source;
            voice.AddComponent<AshfallUiSoundLifetime>().Begin(clip.length + .1f);
        }
    }

    internal sealed class AshfallUiSoundLifetime : MonoBehaviour
    {
        public void Begin(float seconds) => StartCoroutine(DisposeAfter(seconds));

        private IEnumerator DisposeAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Destroy(gameObject);
        }
    }

    /// <summary>Small pointer listener attached only to actual buttons.</summary>
    [DisallowMultipleComponent]
    internal sealed class AshfallMenuButtonPointerAudio : MonoBehaviour, IPointerEnterHandler,
        IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private AudioClip hoverClip;
        private float volume;
        private MonoBehaviour owner;
        private Image hoverBackground;
        private Graphic labelGraphic;
        private Coroutine visualRoutine;
        private Color labelRestColor;
        private Vector3 labelRestScale;
        private bool hovered;
        private static AshfallMenuButtonPointerAudio activeVisual;

        public void Configure(AudioClip clip, float playbackVolume, MonoBehaviour feedback)
        {
            owner = feedback;
            hoverClip = clip;
            volume = playbackVolume;
        }

        public void ConfigureVisual(Image background, Graphic label)
        {
            hoverBackground = background;
            labelGraphic = label;
            if (labelGraphic != null)
            {
                labelRestColor = labelGraphic.color;
                labelRestScale = labelGraphic.rectTransform.localScale;
                labelGraphic.raycastTarget = false;
            }
            SetVisualImmediate(false, false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Button button = GetComponent<Button>();
            if (owner && owner.isActiveAndEnabled && button != null && button.IsInteractable())
                AshfallMenuAudioFeedback.Play(hoverClip, volume, true);
            ActivateVisual();
        }

        public void OnPointerExit(PointerEventData eventData) => DeactivateVisual();
        public void OnSelect(BaseEventData eventData) => ActivateVisual();
        public void OnDeselect(BaseEventData eventData) => DeactivateVisual();

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!hovered || labelGraphic == null) return;
            if (visualRoutine != null) StopCoroutine(visualRoutine);
            visualRoutine = StartCoroutine(FadeVisual(true, true));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!hovered) return;
            if (visualRoutine != null) StopCoroutine(visualRoutine);
            visualRoutine = StartCoroutine(FadeVisual(true, false));
        }

        private void ActivateVisual()
        {
            Button button = GetComponent<Button>();
            if (hoverBackground == null || button == null || !button.IsInteractable()) return;
            if (activeVisual != null && activeVisual != this) activeVisual.DeactivateVisual();
            activeVisual = this;
            hovered = true;
            if (visualRoutine != null) StopCoroutine(visualRoutine);
            visualRoutine = StartCoroutine(FadeVisual(true, false));
        }

        private void DeactivateVisual()
        {
            if (activeVisual == this) activeVisual = null;
            hovered = false;
            if (visualRoutine != null) StopCoroutine(visualRoutine);
            if (isActiveAndEnabled) visualRoutine = StartCoroutine(FadeVisual(false, false));
            else SetVisualImmediate(false, false);
        }

        private IEnumerator FadeVisual(bool show, bool pressed)
        {
            float duration = pressed ? 0.07f : 0.16f;
            float elapsed = 0f;
            Color backgroundStart = hoverBackground != null ? hoverBackground.color : Color.clear;
            Color backgroundTarget = pressed ? new Color(1f, 0.7f, 0.45f, 1f)
                : new Color(1f, 1f, 1f, show ? 0.94f : 0f);
            Color labelStart = labelGraphic != null ? labelGraphic.color : Color.white;
            Color labelTarget = show ? new Color(1f, 0.94f, 0.82f, labelRestColor.a) : labelRestColor;
            Vector3 scaleStart = labelGraphic != null ? labelGraphic.rectTransform.localScale : Vector3.one;
            Vector3 scaleTarget = labelRestScale * (pressed ? 0.985f : show ? 1.025f : 1f);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                if (hoverBackground != null) hoverBackground.color = Color.Lerp(backgroundStart, backgroundTarget, t);
                if (labelGraphic != null)
                {
                    labelGraphic.color = Color.Lerp(labelStart, labelTarget, t);
                    labelGraphic.rectTransform.localScale = Vector3.Lerp(scaleStart, scaleTarget, t);
                }
                yield return null;
            }
            SetVisualImmediate(show, pressed);
            visualRoutine = null;
        }

        private void SetVisualImmediate(bool show, bool pressed)
        {
            if (hoverBackground != null)
                hoverBackground.color = pressed ? new Color(1f, 0.7f, 0.45f, 1f) : new Color(1f, 1f, 1f, show ? 0.94f : 0f);
            if (labelGraphic != null)
            {
                labelGraphic.color = show ? new Color(1f, 0.94f, 0.82f, labelRestColor.a) : labelRestColor;
                labelGraphic.rectTransform.localScale = labelRestScale * (pressed ? 0.985f : show ? 1.025f : 1f);
            }
        }

        private void OnDisable()
        {
            if (activeVisual == this) activeVisual = null;
            if (visualRoutine != null) StopCoroutine(visualRoutine);
            visualRoutine = null;
            hovered = false;
            SetVisualImmediate(false, false);
        }
    }
}
