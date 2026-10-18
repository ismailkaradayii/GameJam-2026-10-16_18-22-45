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
        [SerializeField, Min(0f)] private float introDuration = 4.5f;
        [SerializeField, Min(0f)] private float preLogoDelay = 2.25f;
        [SerializeField, Min(0.05f)] private float logoFadeInDuration = 0.8f;
        [SerializeField, Min(0f)] private float logoHoldDuration = 0.45f;
        [SerializeField, Min(0.05f)] private float menuFadeDuration = 0.32f;
        [SerializeField, Min(0f)] private float menuStaggerDelay = 0.08f;
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
        private Material volcanicTextMaterial;
        private bool panelOpen;
        private bool panelClosing;
        private CanvasGroup activePanel;
        private GameObject previousSelectedObject;

        private void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            NormalizeMenuButtonImages();
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

        private void NormalizeMenuButtonImages()
        {
            if (mainMenuGroup != null)
            {
                RectTransform menuRect = mainMenuGroup.GetComponent<RectTransform>();
                menuRect.anchorMin = new Vector2(0.055f, 0.12f);
                menuRect.anchorMax = new Vector2(0.43f, 0.60f);
                menuRect.offsetMin = Vector2.zero;
                menuRect.offsetMax = Vector2.zero;
            }
            if (titleGroup != null)
            {
                RectTransform logoRect = titleGroup.GetComponent<RectTransform>();
                logoRect.anchorMin = new Vector2(0.04f, 0.61f);
                logoRect.anchorMax = new Vector2(0.64f, 0.96f);
                logoRect.offsetMin = Vector2.zero;
                logoRect.offsetMax = Vector2.zero;
            }
            if (menuButtons == null) return;
            string[] menuLabels = { "NEW GAME", "SETTINGS", "HOW TO PLAY", "EXIT" };
            for (int buttonIndex = 0; buttonIndex < menuButtons.Length; buttonIndex++)
            {
                Button button = menuButtons[buttonIndex];
                if (button == null) continue;
                Image plate = button.GetComponent<Image>();
                if (plate == null || plate.sprite == null) continue;
                // The supplied artwork is a complete volcanic plate. Slicing its
                // large source borders into a compact menu row collapses the image
                // into scattered fragments, so always render the full sprite.
                plate.type = Image.Type.Simple;
                plate.preserveAspect = false;
                plate.fillCenter = true;
                plate.color = new Color(1f, 1f, 1f, 0f);
                plate.raycastTarget = true;
                button.targetGraphic = plate;
                button.transition = Selectable.Transition.None;

                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    if (buttonIndex < menuLabels.Length) label.text = menuLabels[buttonIndex];
                    // Keep the authored lava-rock display face on the four primary
                    // menu labels without changing typography elsewhere in the UI.
                    Font volcanicFace = Ashfall.UI.AshfallTypography.MainMenuDisplay;
                    if (volcanicFace != null) label.font = volcanicFace;
                    ApplyVolcanicTextMaterial(label, plate.sprite.texture);
                    label.fontStyle = FontStyle.Normal;
                    label.alignment = TextAnchor.MiddleLeft;
                    label.resizeTextForBestFit = false;
                    label.fontSize = 48;
                    RectTransform labelRect = label.rectTransform;
                    labelRect.anchorMin = Vector2.zero;
                    labelRect.anchorMax = Vector2.one;
                    labelRect.pivot = new Vector2(0.5f, 0.5f);
                    labelRect.offsetMin = new Vector2(112f, 14f);
                    labelRect.offsetMax = new Vector2(-72f, -14f);
                    labelRect.localScale = Vector3.one;
                }

                AshfallMenuButtonPointerAudio pointer = button.GetComponent<AshfallMenuButtonPointerAudio>();
                if (pointer == null) pointer = button.gameObject.AddComponent<AshfallMenuButtonPointerAudio>();
                pointer.ConfigureVisual(plate, label);

                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 0.88f, 0.72f, 1f);
                colors.pressedColor = new Color(0.78f, 0.5f, 0.32f, 1f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0.48f, 0.43f, 0.4f, 0.78f);
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.1f;
                button.colors = colors;
            }
        }

        private void ApplyVolcanicTextMaterial(Text label, Texture lavaTexture)
        {
            if (volcanicTextMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("ASHFALL/UI/Shaders/VolcanicText");
                if (shader == null)
                {
                    Debug.LogWarning("ASHFALL: Volcanic text shader could not be loaded; menu text will use its fallback color.", this);
                    return;
                }
                volcanicTextMaterial = new Material(shader)
                {
                    name = "Volcanovski Lava Text (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                volcanicTextMaterial.SetFloat("_Brightness", 1.45f);
                volcanicTextMaterial.SetFloat("_EdgeCutoff", 0.18f);
            }
            if (lavaTexture != null) volcanicTextMaterial.SetTexture("_LavaTex", lavaTexture);
            label.material = volcanicTextMaterial;
        }

        private void OnDestroy()
        {
            if (volcanicTextMaterial != null) Destroy(volcanicTextMaterial);
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
            // Runtime font initializers also run between Awake and Start. Reapply
            // the authored main-menu face here so entering Play Mode or returning
            // to this scene cannot restore the generic UI font.
            NormalizeMenuButtonImages();

            if (!playIntro)
            {
                yield return RevealMenu();
                yield break;
            }

            // Let the background establish itself before presenting the menu brand.
            // The VideoPlayer remains completely independent and keeps running.
            if (preLogoDelay > 0f) yield return new WaitForSecondsRealtime(preLogoDelay);
            yield return Fade(titleGroup, 0f, 1f, logoFadeInDuration);
            float requestedHold = Mathf.Max(logoHoldDuration,
                introDuration - preLogoDelay - logoFadeInDuration);
            if (requestedHold > 0f) yield return new WaitForSecondsRealtime(requestedHold);

            for (int i = 0; i < menuGroups.Length; i++)
            {
                if (i < menuButtons.Length && menuButtons[i] != null) menuButtons[i].interactable = true;
                yield return Fade(menuGroups[i], 0f, 1f, menuFadeDuration);
                if (menuStaggerDelay > 0f) yield return new WaitForSecondsRealtime(menuStaggerDelay);
            }
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
