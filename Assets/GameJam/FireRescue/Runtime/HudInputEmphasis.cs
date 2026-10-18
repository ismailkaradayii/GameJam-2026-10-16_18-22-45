using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Presentation only: the mechanic's real input label stays unchanged.</summary>
    public static class HudInputEmphasis
    {
        public static readonly Color Blue = new Color(.35f, .86f, 1f, 1f);
        static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();
        static readonly Regex Bracket = new Regex(@"\[[^\[\]<>\r\n]{1,28}\]", RegexOptions.Compiled);
        static readonly Regex Command = new Regex(@"\b(?<verb>press|hold|tap|release)\s+(?<key>right\s+(?:mouse\s+)?button|left\s+(?:mouse\s+)?button|space|shift|escape|esc|enter|rmb|lmb|[A-Z0-9])\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex NamedKey = new Regex(@"\b(?:RMB|LMB|WASD|SHIFT|SPACE|ESC)\b", RegexOptions.Compiled);
        public static string Format(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Contains("<color=")) return value ?? "";
            if (Cache.TryGetValue(value, out var formatted)) return formatted;
            // Process plain commands before brackets so the markup is never matched a second time.
            formatted = Command.Replace(value, m => m.Groups["verb"].Value + " " + Mark(m.Groups["key"].Value));
            formatted = Bracket.Replace(formatted, m => Mark(m.Value));
            // Standalone mouse / movement instructions also occur in the help page.
            if (formatted == value) formatted = NamedKey.Replace(formatted, m => Mark(m.Value));
            if (Cache.Count >= 128) Cache.Clear();
            Cache[value] = formatted;
            return formatted;
        }
        static string Mark(string key) => "<color=#59DBFF><b>" + key + "</b></color>";
        public static void Apply(Text label)
        {
            if (!label) return;
            label.supportRichText = true;
            string formatted = Format(label.text);
            if (label.text != formatted) label.text = formatted;
        }
    }
}
