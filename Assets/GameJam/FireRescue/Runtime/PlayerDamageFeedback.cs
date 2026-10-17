using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Brief damage tint and camera impulse; the overlay never captures UI input.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDamageFeedback : MonoBehaviour
    {
        const float FlashSeconds = .13f, EdgeSeconds = .38f;
        Canvas canvas;
        Image flash, edges;
        Texture2D edgeTexture;
        Sprite edgeSprite;
        CreatureCombatPlayer health;
        FireRescuePlayer coordinator;
        float hitAt = -100f;

        public bool IsVisible => canvas && canvas.enabled;
        public float FlashAlpha => flash ? flash.color.a : 0f;
        public float EdgeAlpha => edges ? edges.color.a : 0f;
        public Canvas OverlayCanvas => canvas;

        public static PlayerDamageFeedback Ensure(CreatureCombatPlayer owner) =>
            owner.GetComponent<PlayerDamageFeedback>() ?? owner.gameObject.AddComponent<PlayerDamageFeedback>();

        void Awake()
        {
            health = GetComponent<CreatureCombatPlayer>();
            coordinator = GetComponent<FireRescuePlayer>();
        }

        public void Play(Camera view)
        {
            if (!isActiveAndEnabled || !view) return;
            if (!canvas) BuildOverlay();
            hitAt = Time.unscaledTime;
            var shake = view.GetComponent<VolcanoCameraShake>() ?? view.gameObject.AddComponent<VolcanoCameraShake>();
            shake.ShakeImpact(.28f, .11f, .95f);
            UpdateVisuals();
        }

        void Update() => UpdateVisuals();

        void UpdateVisuals()
        {
            if (!canvas) return;
            float age = Mathf.Max(0f, Time.unscaledTime - hitAt);
            bool visible = health && health.isActiveAndEnabled && age < EdgeSeconds && Time.timeScale > 0f &&
                !VolcanoCinematic.IsPlayingAny && (!coordinator || !coordinator.IsDriving);
            canvas.enabled = visible;
            float flashFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / FlashSeconds));
            float edgeFade = Mathf.Pow(1f - Mathf.Clamp01(age / EdgeSeconds), 1.65f);
            flash.color = new Color(1f, .025f, .015f, visible ? .19f * flashFade : 0f);
            edges.color = new Color(.9f, .012f, .006f, visible ? .6f * edgeFade : 0f);
        }

        void BuildOverlay()
        {
            var root = new GameObject("Player damage flash (runtime)", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2500;
            var group = root.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            flash = Layer("Brief red impact tint");
            edges = Layer("Fading red damage edges");
            const int size = 128;
            edgeTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Runtime damage edge gradient", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x / (size - 1f) * 2f - 1f), dy = Mathf.Abs(y / (size - 1f) * 2f - 1f);
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.32f, 1f, Mathf.Max(dx, dy)));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(edge * 255));
            }
            edgeTexture.SetPixels32(pixels); edgeTexture.Apply(false, true);
            edgeSprite = Sprite.Create(edgeTexture, new Rect(0, 0, size, size), Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
            edges.sprite = edgeSprite;
        }

        Image Layer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var result = go.GetComponent<Image>(); result.raycastTarget = false; result.color = Color.clear;
            return result;
        }

        void OnDisable() { hitAt = -100f; if (canvas) canvas.enabled = false; }
        void OnDestroy()
        {
            if (canvas) Destroy(canvas.gameObject);
            if (edgeSprite) Destroy(edgeSprite);
            if (edgeTexture) Destroy(edgeTexture);
        }
    }
}
