using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Responsive first-contact briefing over a still captured from the actual game.</summary>
    public sealed class SpiritIntroductionCard
    {
        public const string ThreatText = "Çatılara sıçrar, dokundukları evleri ateşe verirler. Onları evlere ulaşmadan indir.";
        static readonly Color Paper = new Color(.97f, .95f, .9f);
        static readonly Color Muted = new Color(.72f, .76f, .77f);
        static readonly Color Ember = new Color(1f, .45f, .19f);
        GUIStyle text;
        Font font;

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
                font = Resources.Load<Font>("ASHFALL/UI/Fonts/SourceSans3-Regular");
                text = new GUIStyle(GUI.skin.label) { font = font, wordWrap = true, padding = new RectOffset(), richText = false };
            }
            Fill(new Rect(0, 0, w, h), new Color(.015f, .025f, .035f, .2f));
            Fill(new Rect(0, 0, w, 64), new Color(.015f, .022f, .03f, .96f));
            Label(new Rect(50, 19, w - 100, 28), "ASHFALL     /     İLK TEMAS", 20, Paper, true);
            float x = w * .585f, panelW = w * .375f;
            float top = h * .16f;
            Fill(new Rect(x - 30, top - 24, panelW + 56, 650), new Color(.025f, .04f, .052f, .96f));
            Fill(new Rect(x - 30, top - 24, 4, 650), Ember);
            Label(new Rect(x, top, panelW, 26), "01   /   TEHLİKEYİ TANI", 18, Ember, true);
            Label(new Rect(x - 3, top + 32, panelW, 77), "ATEŞ RUHU", 60, Paper, true);
            Label(new Rect(x, top + 122, panelW - 6, 106), ThreatText, 26, Paper);
            Fill(new Rect(x, top + 246, panelW - 8, 1), new Color(.5f, .59f, .63f, .35f));
            Label(new Rect(x, top + 269, panelW, 25), "02   /   KASABAYI KURTAR", 18, Ember, true);
            string instruction = !player.HasPistol ? "Yatak odasındaki silahı al. Parlayan mavi işareti takip et." :
                player.IsDriving ? ExitKey(player) + " ile araçtan in. Silahını kuşan ve ruhları durdur." : "Silahını kuşan. Ruhlara nişan al ve ateş et.";
            Label(new Rect(x, top + 308, panelW - 6, 72), instruction, 25, Paper, true);
            float column = panelW * .5f;
            Key(new Rect(x, top + 399, column - 12, 32), "1", "Silahı kuşan");
            Key(new Rect(x + column, top + 399, column, 32), "SAĞ TIK", "Nişan al");
            Key(new Rect(x, top + 443, column - 12, 32), "SOL TIK", "Ateş et");
            Key(new Rect(x + column, top + 443, column, 32), ReloadKey(player), "Şarjör değiştir");
            Fill(new Rect(x, top + 501, panelW - 8, 1), new Color(.5f, .59f, .63f, .35f));
            Label(new Rect(x, top + 523, panelW - 6, 78), "Ev yanıyorsa itfaiyenin su topuyla söndür. Tank boşsa göldeki mavi oka git.", 22, Muted);

            // Open corner marks frame the actual airborne enemy without covering its silhouette.
            Rect photo = new Rect(w * .055f, top + 18, w * .465f, 533);
            Corners(photo);
            Label(new Rect(photo.x + 18, photo.y + 16, photo.width - 36, 26), "GÖRÜNTÜ DURDURULDU", 17, Paper, true);
            Fill(new Rect(photo.x + 18, photo.yMax - 55, 230, 34), new Color(.02f, .03f, .04f, .85f));
            Label(new Rect(photo.x + 30, photo.yMax - 51, 210, 28), "HEDEF  /  ATEŞ RUHU", 18, Ember, true);

            Fill(new Rect(0, h - 78, w, 78), new Color(.015f, .022f, .03f, .97f));
            Label(new Rect(50, h - 54, w * .4f, 30), "Hazır olduğunda kasabayı koru.", 22, Muted);
            Label(new Rect(w * .51f, h - 55, w * .45f, 32), canContinue ?
                "SPACE / ENTER   ·   DEVAM ET" : "Tehlikeyi tanı. Oyun seni bekliyor.", 23, canContinue ? Paper : Muted, true, TextAnchor.MiddleRight);
            GUI.matrix = saved; GUI.color = savedColor;
        }
        static string ExitKey(FireRescuePlayer player) => player.contextualInteractions ? player.contextualInteractions.Label(InteractionInput.ExitVehicle) : player.exitVehicleKey.ToString();
        static string ReloadKey(FireRescuePlayer player) => player.retractHoseKey.ToString().ToUpperInvariant();
        void Key(Rect area, string key, string action)
        {
            float width = key.Length > 3 ? 87 : 34;
            Fill(new Rect(area.x, area.y, width, 30), new Color(.15f, .19f, .21f));
            Label(new Rect(area.x, area.y, width, 30), key, 16, Paper, true, TextAnchor.MiddleCenter);
            Label(new Rect(area.x + width + 10, area.y, area.width - width - 10, 32), action, 20, Paper);
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
