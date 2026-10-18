using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Ashfall.MainMenu
{
    /// <summary>Owns the intro, menu navigation, panel state, and cinematic effects.</summary>
    public sealed class AshfallMenuController : MonoBehaviour
    {
        [Header("Intro UI")]
        [SerializeField] private CanvasGroup titleGroup;
        [SerializeField] private CanvasGroup[] menuGroups;
        [SerializeField] private Button[] menuButtons;
        [SerializeField] private CanvasGroup mainMenuGroup;
        [SerializeField] private CanvasGroup settingsPanel;
        [SerializeField] private CanvasGroup howToPlayPanel;
        [SerializeField] private Button settingsBackButton;
        [SerializeField] private Button howToPlayBackButton;
        [SerializeField] private Selectable settingsInitialSelection;
        [SerializeField] private Selectable howToPlayInitialSelection;

        [Header("Cinematic")]
        [SerializeField] private bool playIntro = true;
        [SerializeField, Min(0f)] private float introDuration = 10f;
        [SerializeField] private ParticleSystem ambientEmbers;
        [SerializeField] private ParticleSystem eruptionSmoke;
        [SerializeField] private ParticleSystem eruptionSparks;
        [SerializeField] private Light eruptionLight;
        [SerializeField] private Image craterGlow;
        [SerializeField] private Transform cameraTransform;
        [SerializeField, Min(0f)] private float shakeMagnitude = 0.12f;
        [SerializeField, Min(0.01f)] private float shakeDuration = 0.65f;
        [SerializeField] private AudioSource ambienceSource;
        [SerializeField] private AudioSource eruptionSource;

        [Header("Navigation")]
        [SerializeField] private string gameplaySceneName = "Volcano_Demo";
        [SerializeField] private VideoClip loadingScreenClip;
        [SerializeField] private AshfallSettingsController settingsController;

        public string GameplaySceneName => gameplaySceneName;

        private Vector3 cameraRestPosition;
        private bool panelOpen;
        private bool panelClosing;
        private CanvasGroup activePanel;
        private GameObject previousSelectedObject;

        private void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            WireButtonCallbacks();
            if (cameraTransform != null) cameraRestPosition = cameraTransform.localPosition;
            if (ambientEmbers != null && !ambientEmbers.isPlaying) ambientEmbers.Play();
            if (ambienceSource != null && ambienceSource.clip != null)
            {
                ambienceSource.loop = true;
                if (!ambienceSource.isPlaying) ambienceSource.Play();
            }
            CloseInstant(settingsPanel);
            CloseInstant(howToPlayPanel);
            SetGroup(titleGroup, 0f, false);
            for (int i = 0; i < menuGroups.Length; i++)
            {
                SetGroup(menuGroups[i], 0f, false);
                if (i < menuButtons.Length && menuButtons[i] != null) menuButtons[i].interactable = false;
            }
            if (eruptionLight != null) eruptionLight.intensity = 0f;
            SetGlow(0f);
        }

        private void WireButtonCallbacks()
        {
            if (menuButtons != null)
            {
                if (menuButtons.Length > 0 && menuButtons[0] != null) { menuButtons[0].onClick.RemoveListener(NewGame); menuButtons[0].onClick.AddListener(NewGame); }
                if (menuButtons.Length > 1 && menuButtons[1] != null) { menuButtons[1].onClick.RemoveListener(OpenSettings); menuButtons[1].onClick.AddListener(OpenSettings); }
                if (menuButtons.Length > 2 && menuButtons[2] != null) { menuButtons[2].onClick.RemoveListener(OpenHowToPlay); menuButtons[2].onClick.AddListener(OpenHowToPlay); }
                if (menuButtons.Length > 3 && menuButtons[3] != null) { menuButtons[3].onClick.RemoveListener(ExitGame); menuButtons[3].onClick.AddListener(ExitGame); }
            }
            if (settingsBackButton != null) { settingsBackButton.onClick.RemoveListener(ClosePanels); settingsBackButton.onClick.AddListener(ClosePanels); }
            if (howToPlayBackButton != null) { howToPlayBackButton.onClick.RemoveListener(ClosePanels); howToPlayBackButton.onClick.AddListener(ClosePanels); }
        }

        private IEnumerator Start()
        {
            if (!playIntro)
            {
                yield return RevealMenu();
                yield break;
            }

            float start = Time.unscaledTime;
            float duration = Mathf.Max(8f, introDuration);
            while (Time.unscaledTime - start < 2f)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / 2f);
                SetGlow(0.035f + t * 0.06f);
                yield return null;
            }

            while (Time.unscaledTime - start < 4f)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start - 2f) / 2f);
                SetGlow(Mathf.Lerp(0.095f, 0.4f, t));
                if (eruptionLight != null) eruptionLight.intensity = Mathf.Lerp(0.15f, 1.2f, t);
                yield return null;
            }

            if (eruptionSmoke != null) eruptionSmoke.Play();
            if (eruptionSparks != null) eruptionSparks.Play();
            if (eruptionSource != null && eruptionSource.clip != null) eruptionSource.Play();
            yield return ShakeCamera();

            float flashStart = Time.unscaledTime;
            while (Time.unscaledTime - flashStart < 1.5f)
            {
                float t = (Time.unscaledTime - flashStart) / 1.5f;
                float pulse = Mathf.Sin(t * Mathf.PI);
                SetGlow(Mathf.Lerp(0.4f, 0.1f, t) + pulse * 0.32f);
                if (eruptionLight != null) eruptionLight.intensity = Mathf.Lerp(1.2f, 0.3f, t) + pulse * 2.5f;
                yield return null;
            }

            float titleWait = Mathf.Max(0f, 6f - (Time.unscaledTime - start));
            if (titleWait > 0f) yield return new WaitForSecondsRealtime(titleWait);
            yield return Fade(titleGroup, 0f, 1f, 0.9f);

            float menuStart = Time.unscaledTime;
            float revealDelay = Mathf.Max(0.16f, (duration - 8f) / Mathf.Max(1, menuGroups.Length));
            for (int i = 0; i < menuGroups.Length; i++)
            {
                if (i < menuButtons.Length && menuButtons[i] != null) menuButtons[i].interactable = true;
                yield return Fade(menuGroups[i], 0f, 1f, 0.38f);
                yield return new WaitForSecondsRealtime(revealDelay);
            }

            float remaining = duration - 8f - (Time.unscaledTime - menuStart);
            if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);
            if (eruptionLight != null) eruptionLight.intensity = 0.22f;
            SetGlow(0.08f);
        }

        public void NewGame()
        {
            if (startingGame) return;
            if (Application.CanStreamedLevelBeLoaded(gameplaySceneName))
            {
                startingGame = true;
                Time.timeScale = 1f;
                StopAllCoroutines();
                if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
                foreach (var button in menuButtons)
                    if (button) button.interactable = false;
                StartCoroutine(LoadGameplayWithFade());
            }
            else
                Debug.LogError($"ASHFALL: Gameplay scene '{gameplaySceneName}' is not included in Build Settings.", this);
        }
        bool startingGame;
        private IEnumerator LoadGameplayWithFade()
        {
            yield return AshfallLoadingScreen.Load(gameplaySceneName, loadingScreenClip);
        }

        public void OpenSettings() => OpenPanel(settingsPanel);
        public void OpenHowToPlay() => OpenPanel(howToPlayPanel);
        public void ClosePanels()
        {
            if (panelOpen && !panelClosing) StartCoroutine(ClosePanelRoutine());
        }

        private void Update()
        {
            if (startingGame) return;
            // Both input backends are enabled in this project. One legacy poll here
            // avoids delivering duplicate ESC actions from two input APIs.
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (settingsController != null && settingsController.TryCancelDialog()) return;
            if (panelOpen) ClosePanels();
        }

        public void ExitGame()
        {
#if UNITY_EDITOR
            Debug.Log("ASHFALL: Exit requested. Application.Quit is ignored in the Unity Editor.", this);
#else
            Application.Quit();
#endif
        }

        private void OpenPanel(CanvasGroup panel)
        {
            if (startingGame) return;
            if (panel == null || panelOpen || (panel != settingsPanel && panel != howToPlayPanel)) return;
            previousSelectedObject = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            panelOpen = true;
            panelClosing = false;
            activePanel = panel;
            SetGroup(settingsPanel, 0f, false);
            SetGroup(howToPlayPanel, 0f, false);
            StartCoroutine(PanelRoutine(panel));
        }

        private IEnumerator PanelRoutine(CanvasGroup panel)
        {
            SetGroup(mainMenuGroup, mainMenuGroup.alpha, false);
            if (settingsController != null) settingsController.Refresh();
            yield return Fade(panel, 0f, 1f, 0.24f);
            SetGroup(panel, 1f, true);
            SelectPanelDefault(panel);
        }

        private IEnumerator ClosePanelRoutine()
        {
            panelClosing = true;
            CanvasGroup closingPanel = activePanel;
            if (closingPanel != null)
            {
                yield return Fade(closingPanel, closingPanel.alpha, 0f, 0.18f);
                SetGroup(closingPanel, 0f, false);
            }
            panelOpen = false;
            panelClosing = false;
            activePanel = null;
            SetGroup(mainMenuGroup, 1f, true);
            if (EventSystem.current != null)
            {
                GameObject selected = previousSelectedObject;
                if (selected == null && menuButtons != null && menuButtons.Length > 0 && menuButtons[0] != null)
                    selected = menuButtons[0].gameObject;
                EventSystem.current.SetSelectedGameObject(selected);
            }
            previousSelectedObject = null;
        }

        private IEnumerator RevealMenu()
        {
            SetGroup(titleGroup, 0f, true);
            yield return Fade(titleGroup, 0f, 1f, 0.5f);
            for (int i = 0; i < menuGroups.Length; i++)
            {
                if (i < menuButtons.Length && menuButtons[i] != null) menuButtons[i].interactable = true;
                yield return Fade(menuGroups[i], 0f, 1f, 0.25f);
            }
        }

        private IEnumerator ShakeCamera()
        {
            if (cameraTransform == null) yield break;
            float elapsed = 0f;
            while (elapsed < shakeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float fade = 1f - Mathf.Clamp01(elapsed / shakeDuration);
                Vector2 offset = Random.insideUnitCircle * shakeMagnitude * fade;
                cameraTransform.localPosition = cameraRestPosition + new Vector3(offset.x, offset.y, 0f);
                yield return null;
            }
            cameraTransform.localPosition = cameraRestPosition;
        }

        private static IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
        {
            if (group == null) yield break;
            group.gameObject.SetActive(true);
            group.interactable = false;
            group.blocksRaycasts = false;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
            }
            group.alpha = to;
            group.interactable = to >= 0.99f;
            group.blocksRaycasts = to >= 0.99f;
        }

        private static void SetGroup(CanvasGroup group, float alpha, bool interactable)
        {
            if (group == null) return;
            group.gameObject.SetActive(interactable || alpha > 0f);
            group.alpha = alpha;
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }

        private static void CloseInstant(CanvasGroup group)
        {
            if (group == null) return;
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            group.gameObject.SetActive(false);
        }

        private void SetGlow(float alpha)
        {
            if (craterGlow == null) return;
            Color color = craterGlow.color;
            color.a = Mathf.Clamp01(alpha);
            craterGlow.color = color;
        }

        private void SelectPanelDefault(CanvasGroup group)
        {
            if (EventSystem.current == null) return;
            Selectable selected = group == settingsPanel ? settingsInitialSelection
                : group == howToPlayPanel ? howToPlayInitialSelection : null;
            if (selected == null || !selected.IsInteractable()) selected = group.GetComponentInChildren<Selectable>(true);
            if (selected != null) EventSystem.current.SetSelectedGameObject(selected.gameObject);
        }
    }
}
