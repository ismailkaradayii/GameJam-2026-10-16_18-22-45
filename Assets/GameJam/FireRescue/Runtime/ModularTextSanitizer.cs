using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ashfall.UI
{
    /// <summary>Keeps text readable when the selected Modular face has no Turkish glyph.</summary>
    public sealed class ModularTextSanitizer : MonoBehaviour
    {
        Text label;
        string lastValue;

        void Awake() => label = GetComponent<Text>();
        void LateUpdate()
        {
            if (!label || label.font != AshfallTypography.Modular || label.text == lastValue) return;
            string normalized = ToAsciiTurkish(label.text);
            if (normalized != label.text) label.text = normalized;
            lastValue = label.text;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void NormalizeLoadedUi()
        {
            AttachToUi();
            SceneManager.sceneLoaded += (_, __) => AttachToUi();
        }

        static void AttachToUi()
        {
            foreach (Text text in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (text.font != AshfallTypography.Modular) continue;
                if (!text.TryGetComponent<ModularTextSanitizer>(out _)) text.gameObject.AddComponent<ModularTextSanitizer>();
            }
        }

        public static string ToAsciiTurkish(string value) => string.IsNullOrEmpty(value) ? value : value
            .Replace('Ç', 'C').Replace('ç', 'c').Replace('Ğ', 'G').Replace('ğ', 'g')
            .Replace('İ', 'I').Replace('ı', 'i').Replace('Ö', 'O').Replace('ö', 'o')
            .Replace('Ş', 'S').Replace('ş', 's').Replace('Ü', 'U').Replace('ü', 'u');
    }
}
