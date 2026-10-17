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
    internal sealed class AshfallMenuButtonPointerAudio : MonoBehaviour, IPointerEnterHandler
    {
        private AudioClip hoverClip;
        private float volume;
        private MonoBehaviour owner;

        public void Configure(AudioClip clip, float playbackVolume, MonoBehaviour feedback)
        {
            owner = feedback;
            hoverClip = clip;
            volume = playbackVolume;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Button button = GetComponent<Button>();
            if (owner && owner.isActiveAndEnabled && button != null && button.IsInteractable())
                AshfallMenuAudioFeedback.Play(hoverClip, volume, true);
        }
    }
}
