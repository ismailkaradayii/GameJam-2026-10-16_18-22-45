using System.Collections;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Unscaled, scene-local curtain. Never changes input or the global time scale.</summary>
    [DisallowMultipleComponent]
    public sealed class AshfallSceneFade : MonoBehaviour
    {
        public CanvasGroup curtain;
        public Canvas canvas;
        public RescueGameFlow flow;
        [Range(.1f, 1f)] public float fadeInSeconds = .55f, fadeOutSeconds = .35f;
        public bool IsCovered => curtain && curtain.alpha > .99f;
        bool covering, opened;
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
            if (fade.canvas) fade.canvas.sortingOrder = 1000;
            fade.curtain.blocksRaycasts = true;
            yield return fade.Fade(1, fade.fadeOutSeconds);
        }
        void LateUpdate()
        {
            if (!opened || covering || !curtain || !flow) return;
            bool ended = flow.State == RescueGameFlow.GameState.Won || flow.State == RescueGameFlow.GameState.Lost;
            float target = ended ? flow.State == RescueGameFlow.GameState.Lost ? .4f : .18f : 0;
            curtain.alpha = Mathf.MoveTowards(curtain.alpha, target, Time.unscaledDeltaTime * .9f);
        }
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
