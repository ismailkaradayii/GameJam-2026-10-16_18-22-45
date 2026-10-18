using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Responsive first-contact briefing over a still captured from the actual game.</summary>
    public sealed class SpiritIntroductionCard
    {
        public const string ThreatText = "They leap onto rooftops and set houses on fire. Stop them before they reach the houses.";
        static readonly Color Paper = new Color(.97f, .95f, .9f);
        static readonly Color Muted = new Color(.72f, .76f, .77f);
        static readonly Color Ember = new Color(1f, .45f, .19f);
        // Use the first and third mice from the supplied sheet without altering its pixels.
        static readonly Rect LeftMouseRegion = new Rect(15f / 700f, 13f / 211f, 108f / 700f, 168f / 211f);
        static readonly Rect RightMouseRegion = new Rect(295f / 700f, 13f / 211f, 108f / 700f, 168f / 211f);
        GUIStyle text;
        Font font;
        Texture2D mouseButtons;

        public void Draw(Texture2D frame, FireRescuePlayer player, bool canContinue)
        {
            Matrix4x4 saved = GUI.matrix;
            Color savedColor = GUI.color;
            GUI.color = Color.white;
            if (frame) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), frame, ScaleMode.StretchToFill);
            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
            float w = Screen.width / scale, h = Screen.height / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            if (text == null)
            {
                font = Ashfall.UI.AshfallTypography.Regular;
                text = new GUIStyle(GUI.skin.label) { font = font, wordWrap = true, padding = new RectOffset(), richText = false };
                mouseButtons = Resources.Load<Texture2D>("ASHFALL/UI/Icons/TutorialMouseButtons");
            }
            Fill(new Rect(0, 0, w, h), new Color(.015f, .025f, .035f, .2f));
            Fill(new Rect(0, 0, w, 64), new Color(.015f, .022f, .03f, .96f));
            Label(new Rect(50, 19, w - 100, 28), "VOLKANOVSKI     /     FIRST CONTACT", 20, Paper, true);
            float x = w * .585f, panelW = w * .375f;
            float top = h * .16f;
            Fill(new Rect(x - 30, top - 24, panelW + 56, 650), new Color(.025f, .04f, .052f, .96f));
            Fill(new Rect(x - 30, top - 24, 4, 650), Ember);
            Label(new Rect(x, top, panelW, 26), "01   /   KNOW THE THREAT", 18, Ember, true);
            Label(new Rect(x - 3, top + 32, panelW, 77), "FIRE SPIRIT", 60, Paper, true);
            Label(new Rect(x, top + 122, panelW - 6, 106), ThreatText, 26, Paper);
            Fill(new Rect(x, top + 246, panelW - 8, 1), new Color(.5f, .59f, .63f, .35f));
            Label(new Rect(x, top + 269, panelW, 25), "02   /   SAVE THE TOWN", 18, Ember, true);
            if (canContinue) DismissKeys(new Rect(x, top + 308, panelW - 6, 32), "Close briefing");
            else Label(new Rect(x, top + 308, panelW - 6, 32), "Know the threat before continuing.", 20, Muted);
            string instruction = !player.HasPistol ? "Pick up the weapon in the bedroom. Follow the glowing blue marker." :
                "Equip your weapon. Aim at the spirits and fire.";
            Label(new Rect(x, top + 350, panelW - 6, 48), instruction, 20, Paper, true);
            float column = panelW * .5f;
            Key(new Rect(x, top + 399, column - 12, 32), "1", "Equip weapon");
            Key(new Rect(x + column, top + 399, column, 32), "RMB", "Aim", RightMouseRegion);
            Key(new Rect(x, top + 443, column - 12, 32), "LMB", "Fire", LeftMouseRegion);
            Key(new Rect(x + column, top + 443, column, 32), ReloadKey(player), "Reload");
            Fill(new Rect(x, top + 501, panelW - 8, 1), new Color(.5f, .59f, .63f, .35f));
            Label(new Rect(x, top + 523, panelW - 6, 78), "Use the fire truck's water cannon on burning houses. If the tank is empty, follow the blue arrow to the lake.", 22, Muted);

            // Open corner marks frame the actual airborne enemy without covering its silhouette.
            Rect photo = new Rect(w * .055f, top + 18, w * .465f, 533);
            Corners(photo);
            Label(new Rect(photo.x + 18, photo.y + 16, photo.width - 36, 26), "FRAME PAUSED", 17, Paper, true);
            Fill(new Rect(photo.x + 18, photo.yMax - 55, 230, 34), new Color(.02f, .03f, .04f, .85f));
            Label(new Rect(photo.x + 30, photo.yMax - 51, 210, 28), "TARGET  /  FIRE SPIRIT", 18, Ember, true);

            Fill(new Rect(0, h - 78, w, 78), new Color(.015f, .022f, .03f, .97f));
            Label(new Rect(50, h - 54, w * .4f, 30), "Protect the town when you're ready.", 22, Muted);
            if (canContinue) DismissKeys(new Rect(w * .96f - 310, h - 55, 310, 32), "Continue");
            else Label(new Rect(w * .51f, h - 55, w * .45f, 32), "Know the threat. Continue when you're ready.", 23, Muted, true, TextAnchor.MiddleRight);
            GUI.matrix = saved; GUI.color = savedColor;
        }
        static string ReloadKey(FireRescuePlayer player) => player.retractHoseKey.ToString().ToUpperInvariant();
        void DismissKeys(Rect area, string action)
        {
            Key(new Rect(area.x, area.y, 64, area.height), "ESC", "/");
            Key(new Rect(area.x + 70, area.y, area.width - 70, area.height), "LMB", action, LeftMouseRegion);
        }
        void Key(Rect area, string key, string action, Rect? mouseRegion = null)
        {
            float width = key.Length > 3 ? 87 : mouseRegion.HasValue ? 46 : 34;
            Fill(new Rect(area.x, area.y, width, 30), new Color(.15f, .19f, .21f));
            Label(new Rect(area.x, area.y, width, 30), key, 16, Paper, true, TextAnchor.MiddleCenter);
            float actionX = area.x + width + 10;
            if (mouseRegion.HasValue && mouseButtons)
            {
                const float iconHeight = 32f;
                const float iconWidth = iconHeight * 108f / 168f;
                GUI.DrawTextureWithTexCoords(new Rect(actionX, area.y - 1, iconWidth, iconHeight), mouseButtons, mouseRegion.Value);
                actionX += iconWidth + 10;
            }
            Label(new Rect(actionX, area.y, area.xMax - actionX, 32), action, 20, Paper);
        }
        void Label(Rect rect, string value, int size, Color tint, bool bold = false, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            text.fontSize = size; text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.normal.textColor = tint; text.alignment = alignment;
            GUI.color = Color.white; GUI.Label(rect, value, text);
        }
        static void Fill(Rect rect, Color tint) { GUI.color = tint; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }
        static void Corners(Rect r)
        {
            Color tint = new Color(.93f, .95f, .94f, .7f);
            foreach (Vector2 p in new[] { r.min, new Vector2(r.xMax, r.y), new Vector2(r.x, r.yMax), r.max })
            {
                bool right = p.x > r.center.x, bottom = p.y > r.center.y;
                Fill(new Rect(p.x - (right ? 32 : 0), p.y - (bottom ? 2 : 0), 32, 2), tint);
                Fill(new Rect(p.x - (right ? 2 : 0), p.y - (bottom ? 32 : 0), 2, 32), tint);
            }
        }
    }
}
