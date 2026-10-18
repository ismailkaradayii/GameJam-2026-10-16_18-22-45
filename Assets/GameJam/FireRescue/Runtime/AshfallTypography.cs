using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace Ashfall.UI
{
    /// <summary>Shared, restrained type scale for authored menu and mission UI.</summary>
    public static class AshfallTypography
    {
        public enum Role { Heading, Body, Label, Caption, Button, Number }
        public static readonly Color Paper = new Color(.93f, .915f, .88f);
        public static readonly Color Secondary = new Color(.74f, .735f, .71f);
        static Font modular, regular, semibold, compact;
        static TMP_FontAsset bodySdf, displaySdf;

        public static Font Regular => regular ? regular : (regular = Resources.Load<Font>("ASHFALL/UI/Fonts/Barlow-Medium"));
        public static Font Semibold => semibold ? semibold : (semibold = Resources.Load<Font>("ASHFALL/UI/Fonts/ChakraPetch-SemiBold"));
        public static Font Compact => compact ? compact : (compact = Resources.Load<Font>("ASHFALL/UI/Fonts/Barlow-SemiBold"));
        // Retained for older authoring tools and the legacy font's sanitizer.
        public static Font Modular => modular ? modular : (modular = Resources.Load<Font>("ASHFALL/UI/Fonts/Modular"));
        public static TMP_FontAsset BodySdf => bodySdf ? bodySdf : (bodySdf = Resources.Load<TMP_FontAsset>("ASHFALL/UI/Fonts/Barlow SDF"));
        public static TMP_FontAsset DisplaySdf => displaySdf ? displaySdf : (displaySdf = Resources.Load<TMP_FontAsset>("ASHFALL/UI/Fonts/Chakra SDF"));

        public static Font Face(Role role) => role == Role.Body ? Regular :
            role == Role.Caption || role == Role.Label ? Compact : Semibold;
        public static bool UsesBodyFace(TMP_Text label)
        {
            string name = label.name.ToLowerInvariant();
            if (name == "text" && label.transform.parent) name += " " + label.transform.parent.name.ToLowerInvariant();
            return name.Contains("detail") || name.Contains("condition") || name.Contains("event") || name.Contains("description") || name.Contains("body");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSceneSubscription() => SceneManager.sceneLoaded -= SceneLoaded;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
            ApplyLoadedFonts();
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode) => ApplyLoadedFonts();
        public static void ApplyLoadedFonts()
        {
            foreach (var label in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // Arrow glyphs use their existing symbol font.
                if (label.name == "Direction" || label.name == "Arrow") continue;
                label.font = Face(Infer(label)); label.fontStyle = FontStyle.Normal;
            }
            foreach (var label in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var font = UsesBodyFace(label) ? BodySdf : DisplaySdf;
                if (font) { label.font = font; label.fontSharedMaterial = font.material; label.fontStyle = FontStyles.Normal; }
            }
        }

        public static Role Infer(Text label)
        {
            string name = label.name.ToLowerInvariant();
            if (name == "text" && label.transform.parent) name += " " + label.transform.parent.name.ToLowerInvariant();
            if (label.GetComponentInParent<Button>()) return Role.Button;
            if (name.Contains("subtitle") || name.Contains("description") || name.Contains("detail") || name.Contains("body") || name.Contains("message") || name.Contains("hint")) return Role.Body;
            if (name.Contains("kicker") || name.Contains("brand") || name.Contains("section") || name.Contains("header")) return Role.Caption;
            if (name.Contains("title") || label.fontSize >= 28) return Role.Heading;
            if (name.Contains("value") || name.Contains("litres") || name.Contains("stats")) return Role.Number;
            return Role.Label;
        }

        public static void Apply(Text label, Role role, int size = 0)
        {
            if (!label) return;
            label.font = Face(role);
            if (!label.font) label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontStyle = FontStyle.Normal;
            int minimum = role == Role.Caption ? 14 : role == Role.Body ? 17 : role == Role.Button ? 20 : 18;
            label.fontSize = Mathf.Max(minimum, size > 0 ? size : label.fontSize);
            label.lineSpacing = role == Role.Body ? 1.15f : 1.05f;
            label.color = role == Role.Body || role == Role.Caption ? Secondary : Paper;
            label.resizeTextForBestFit = false;
            label.raycastTarget = false;
            foreach (var effect in label.GetComponents<Shadow>()) effect.enabled = false;
        }

        public static string NaturalCase(string value)
        {
            if (!string.IsNullOrEmpty(value)) value = value.Replace("ASHFALL", "VOLKANOVSKI");
            if (string.IsNullOrEmpty(value) || value.Length == 1 || value.StartsWith("VOLKANOVSKI") || value == "WASD" || value == "W A S D" ||
                value == "SHIFT" || value == "SPACE" || value == "ESC" || value == "RMB" || value == "LMB" || value == "LMB / RMB" || value == "PC") return value;
            foreach (char letter in value) if (char.IsLower(letter)) return value;
            var culture = CultureInfo.InvariantCulture;
            string result = value.ToLower(culture);
            return char.ToUpper(result[0], culture) + result.Substring(1);
        }
    }
}
