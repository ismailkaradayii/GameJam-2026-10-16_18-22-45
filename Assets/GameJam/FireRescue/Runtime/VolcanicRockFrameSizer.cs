using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Fits a sliced volcanic frame to its visible content and reflows a changing description.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class VolcanicRockFrameSizer : MonoBehaviour
    {
        public RectTransform frame;
        [Min(0)] public float authoredWidth;
        [Min(0)] public float minWidth = 120f;
        [Min(0)] public float minHeight = 56f;
        [Min(0)] public float horizontalPadding = 28f;
        [Min(0)] public float bottomPadding = 24f;
        [Range(.1f, 1f)] public float maxWidthOfParent = .94f;
        [Range(.1f, 1f)] public float maxHeightOfParent = .9f;
        public Text[] widthFitTexts = System.Array.Empty<Text>();
        public Text flowText;
        public RectTransform[] flowFollowing = System.Array.Empty<RectTransform>();

        readonly Dictionary<RectTransform, float> originalTextHeights = new Dictionary<RectTransform, float>();
        readonly Dictionary<RectTransform, Vector2> originalFlowPositions = new Dictionary<RectTransform, Vector2>();
        readonly Vector3[] corners = new Vector3[4];
        string lastLayoutKey;

        void OnEnable()
        {
            if (!frame) frame = transform as RectTransform;
            if (frame && authoredWidth <= 0) authoredWidth = frame.rect.width;
            CaptureFlowPositions();
            lastLayoutKey = null;
        }

        public void SetVerticalFlow(Text expandingText, params RectTransform[] following)
        {
            flowText = expandingText;
            flowFollowing = following ?? System.Array.Empty<RectTransform>();
            CaptureFlowPositions();
            lastLayoutKey = null;
        }

        void LateUpdate()
        {
            if (!frame || !frame.parent) return;
            string layoutKey = BuildLayoutKey();
            if (layoutKey == lastLayoutKey) return;
            lastLayoutKey = layoutKey;

            Canvas.ForceUpdateCanvases();
            FitSingleLineLabels();
            FitWrappedText();
            ReflowFollowingContent();
            FitFrameBounds();
        }

        string BuildLayoutKey()
        {
            unchecked
            {
                int hash = Screen.width * 397 ^ Screen.height;
                hash = hash * 31 + frame.rect.width.GetHashCode();
                hash = hash * 31 + frame.rect.height.GetHashCode();
                if (frame.parent is RectTransform parentRect)
                {
                    hash = hash * 31 + parentRect.rect.width.GetHashCode();
                    hash = hash * 31 + parentRect.rect.height.GetHashCode();
                }
                var labels = frame.GetComponentsInChildren<Text>(true);
                for (int i = 0; i < labels.Length; i++)
                {
                    var label = labels[i];
                    if (!label || !label.gameObject.activeInHierarchy) continue;
                    hash = hash * 31 + (label.text != null ? label.text.GetHashCode() : 0);
                    hash = hash * 31 + label.fontSize;
                    hash = hash * 31 + (label.font ? label.font.GetInstanceID() : 0);
                    hash = hash * 31 + label.lineSpacing.GetHashCode();
                    hash = hash * 31 + label.rectTransform.rect.width.GetHashCode();
                }
                return hash.ToString();
            }
        }

        void FitSingleLineLabels()
        {
            if (widthFitTexts == null || widthFitTexts.Length == 0) return;
            float maxFrameWidth = Mathf.Max(minWidth, frame.parent as RectTransform
                ? ((RectTransform)frame.parent).rect.width * maxWidthOfParent
                : frame.rect.width);
            float availableTextWidth = Mathf.Max(24f, maxFrameWidth - horizontalPadding * 2f);
            for (int i = 0; i < widthFitTexts.Length; i++)
            {
                var label = widthFitTexts[i];
                if (!label || !label.gameObject.activeInHierarchy) continue;
                float preferred = Mathf.Max(24f, label.preferredWidth);
                label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    Mathf.Min(preferred, availableTextWidth));
            }
        }

        void FitWrappedText()
        {
            var labels = frame.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                if (!label || !label.gameObject.activeInHierarchy) continue;
                var rect = label.rectTransform;
                if (!originalTextHeights.TryGetValue(rect, out float minimumHeight))
                {
                    minimumHeight = rect.rect.height;
                    originalTextHeights.Add(rect, minimumHeight);
                }
                if (label.horizontalOverflow == HorizontalWrapMode.Wrap)
                {
                    float preferredHeight = Mathf.Ceil(label.preferredHeight);
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(minimumHeight, preferredHeight));
                }
            }
        }

        void CaptureFlowPositions()
        {
            if (flowFollowing == null) return;
            for (int i = 0; i < flowFollowing.Length; i++)
            {
                var item = flowFollowing[i];
                if (item && !originalFlowPositions.ContainsKey(item)) originalFlowPositions.Add(item, item.anchoredPosition);
            }
        }

        void ReflowFollowingContent()
        {
            if (!flowText) return;
            var flowRect = flowText.rectTransform;
            if (!originalTextHeights.TryGetValue(flowRect, out float originalHeight)) return;
            float extraHeight = Mathf.Max(0f, flowRect.rect.height - originalHeight);
            if (flowFollowing == null) return;
            for (int i = 0; i < flowFollowing.Length; i++)
            {
                var item = flowFollowing[i];
                if (!item || !originalFlowPositions.TryGetValue(item, out Vector2 originalPosition)) continue;
                item.anchoredPosition = originalPosition + Vector2.down * extraHeight;
            }
        }

        void FitFrameBounds()
        {
            var parentRect = frame.parent as RectTransform;
            float availableWidth = parentRect ? parentRect.rect.width * maxWidthOfParent : frame.rect.width;
            float availableHeight = parentRect ? parentRect.rect.height * maxHeightOfParent : frame.rect.height;
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float top = float.NegativeInfinity, bottom = float.PositiveInfinity;

            var graphics = frame.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (!graphic || graphic.rectTransform == frame || !graphic.gameObject.activeInHierarchy || IsFrameCore(graphic.transform)) continue;
                // Decorative stretch layers must not measure their own parent and grow it each frame.
                if (!(graphic is Text) && !graphic.GetComponent<Selectable>() && !graphic.name.EndsWith("Track")) continue;
                var rect = graphic.rectTransform;
                rect.GetWorldCorners(corners);
                for (int c = 0; c < corners.Length; c++)
                {
                    Vector3 local = frame.InverseTransformPoint(corners[c]);
                    float x = local.x + frame.rect.width * frame.pivot.x;
                    float y = local.y - frame.rect.height * (1f - frame.pivot.y);
                    left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                    top = Mathf.Max(top, y); bottom = Mathf.Min(bottom, y);
                }
            }

            if (float.IsInfinity(right) || float.IsInfinity(bottom)) return;
            bool fitWidth = widthFitTexts != null && widthFitTexts.Length > 0;
            float desiredWidth = fitWidth ? Mathf.Max(minWidth, right + horizontalPadding) : Mathf.Max(minWidth, authoredWidth);
            float desiredHeight = Mathf.Max(minHeight, -bottom + bottomPadding);
            desiredWidth = Mathf.Min(desiredWidth, Mathf.Max(minWidth, availableWidth));
            desiredHeight = Mathf.Min(desiredHeight, Mathf.Max(minHeight, availableHeight));
            if (Mathf.Abs(frame.rect.width - desiredWidth) > .5f || Mathf.Abs(frame.rect.height - desiredHeight) > .5f)
                frame.sizeDelta = new Vector2(desiredWidth, desiredHeight);
        }

        static bool IsFrameCore(Transform candidate)
        {
            for (Transform current = candidate; current; current = current.parent)
            {
                if (current.name == "ObsidianCore") return true;
                if (current.GetComponent<VolcanicRockFrameSizer>()) break;
            }
            return false;
        }
    }
}
