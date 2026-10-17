using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Ashfall.UI
{
    /// <summary>Shared, restrained type scale for authored menu and mission UI.</summary>
    public static class AshfallTypography
    {
        public enum Role { Heading, Body, Label, Caption, Button, Number }
        public static readonly Color Paper = new Color(.93f, .915f, .88f);
        public static readonly Color Secondary = new Color(.74f, .735f, .71f);
        static Font regular, semibold;
        public static Font Regular => regular ? regular : (regular = Resources.Load<Font>("ASHFALL/UI/Fonts/SourceSans3-Regular"));
        public static Font Semibold => semibold ? semibold : (semibold = Resources.Load<Font>("ASHFALL/UI/Fonts/SourceSans3-Semibold"));

        public static Role Infer(Text label)
        {
            string name = label.name.ToLowerInvariant();
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
            bool strong = role == Role.Heading || role == Role.Button || role == Role.Label;
            label.font = strong ? Semibold : Regular;
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
            if (string.IsNullOrEmpty(value) || value.Length == 1 || value == "ASHFALL" || value == "WASD" || value == "W A S D" ||
                value == "SHIFT" || value == "SPACE" || value == "ESC" || value == "RMB" || value == "LMB" || value == "LMB / RMB" || value == "PC") return value;
            foreach (char letter in value) if (char.IsLower(letter)) return value;
            if (value.StartsWith("ASHFALL")) return value.Replace("GÖREV", "Görev").Replace("BEKLEME NOKTASI", "Bekleme noktası").Replace("RAPORU", "raporu");
            const string english = "|NEW GAME|SETTINGS|HOW TO PLAY|EXIT|AUDIO|GRAPHICS|MASTER VOLUME|MUSIC VOLUME|SOUND EFFECTS|RESOLUTION|DISPLAY MODE|QUALITY|V-SYNC|FRAME RATE LIMIT|RESTORE DEFAULTS|BACK|CONFIRM|CANCEL|KEEP|REVERT|OK|";
            var culture = english.Contains("|" + value + "|") ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("tr-TR");
            string result = value.ToLower(culture);
            return char.ToUpper(result[0], culture) + result.Substring(1);
        }
    }
}
