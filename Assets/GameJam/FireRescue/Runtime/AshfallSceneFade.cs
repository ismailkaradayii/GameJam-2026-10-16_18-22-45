using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Unscaled, scene-local curtain. Never changes input or the global time scale.</summary>
    [DisallowMultipleComponent]
    public sealed class AshfallSceneFade : MonoBehaviour
    {
        public CanvasGroup curtain;
        public Canvas canvas;
        public RescueGameFlow flow;
        public Image endingCurtain;
        [Range(.1f, 1f)] public float fadeInSeconds = .55f, fadeOutSeconds = .35f;
        public bool IsCovered => curtain && curtain.alpha > .99f;
        bool covering, opened;
        Material endingMaterial;
        Image flatCurtain;
        static readonly int Progress = Shader.PropertyToID("_Progress");
        void Awake()
        {
            flatCurtain = curtain ? curtain.GetComponent<Image>() : null;
            if (!endingCurtain || !endingCurtain.material) return;
            endingMaterial = new Material(endingCurtain.material) { name = "Ending iris (instance)", hideFlags = HideFlags.DontSave };
            endingCurtain.material = endingMaterial; endingCurtain.gameObject.SetActive(false);
        }
        IEnumerator Start()
        {
            if (!curtain) yield break;
            curtain.alpha = 1;
            yield return Fade(0, fadeInSeconds);
            if (!covering) { opened = true; if (canvas) canvas.sortingOrder = 49; }
        }
        public static IEnumerator Cover()
        {
            var fade = FindFirstObjectByType<AshfallSceneFade>();
            if (!fade || !fade.curtain) yield break;
            fade.covering = true;
            if (fade.flatCurtain) fade.flatCurtain.enabled = true;
            if (fade.endingCurtain) fade.endingCurtain.gameObject.SetActive(false);
            if (fade.canvas) fade.canvas.sortingOrder = 1000;
            fade.curtain.blocksRaycasts = true;
            yield return fade.Fade(1, fade.fadeOutSeconds);
        }
        void LateUpdate()
        {
            if (!opened || covering || !curtain || !flow) return;
            bool ended = flow.HasResult;
            if (endingCurtain && endingMaterial)
            {
                endingCurtain.gameObject.SetActive(ended);
                if (flatCurtain) flatCurtain.enabled = !ended;
                if (ended)
                {
                    curtain.alpha = 1;
                    endingMaterial.SetFloat(Progress, Mathf.SmoothStep(0, 1, flow.EndingProgress));
                    return;
                }
            }
            float target = ended ? flow.State == RescueGameFlow.GameState.Lost ? .4f : .18f : 0;
            curtain.alpha = Mathf.MoveTowards(curtain.alpha, target, Time.unscaledDeltaTime * .9f);
        }
        void OnDestroy() { if (endingMaterial) Destroy(endingMaterial); }
        IEnumerator Fade(float target, float seconds)
        {
            float elapsed = 0, from = curtain.alpha;
            while (elapsed < seconds && curtain)
            {
                if (covering && target == 0) yield break;
                elapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
                curtain.alpha = Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, elapsed / Mathf.Max(.01f, seconds)));
                yield return null;
            }
            if (curtain) curtain.alpha = target;
        }
    }
}
