using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>A single, deferred notification when the visible objective changes.</summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveCardFeedback : MonoBehaviour
    {
        public int NotificationCount { get; private set; }
        public float AnimationAge { get; private set; }
        RectTransform card;
        CanvasGroup group;
        Text heading, kicker;
        Vector2 restPosition;
        Vector3 restScale;
        Color headingColor, kickerColor;
        string shownTitle, restKicker;
        AudioSource sound;
        AudioClip chime;
        bool audioPaused;

        public void Configure(RectTransform panel, CanvasGroup visibility, Text title, Text caption)
        {
            card = panel; group = visibility; heading = title; kicker = caption;
            restPosition = card.anchoredPosition; restScale = card.localScale;
            headingColor = heading.color;
            if (kicker) { restKicker = kicker.text; kickerColor = kicker.color; }
            sound = gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false; sound.loop = false; sound.spatialBlend = 0;
            sound.priority = 40;
            chime = CreateChime(); sound.clip = chime;
        }

        public void TickFeedback(string title, bool visible, float deltaTime)
        {
            if (!card || !heading || !sound) return;
            if (!visible)
            {
                if (sound.isPlaying) { sound.Pause(); audioPaused = true; }
                return;
            }
            // Titles encode the evacuation substeps too. Live counters never retrigger this.
            if (!string.IsNullOrEmpty(title) && shownTitle != title)
            {
                shownTitle = title; AnimationAge = 0; NotificationCount++;
                sound.Stop(); audioPaused = false;
                sound.volume = .28f * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f));
                sound.Play();
            }
            else
            {
                AnimationAge += Mathf.Max(0, deltaTime);
                if (audioPaused) { sound.UnPause(); audioPaused = false; }
            }
            float entry = Mathf.Clamp01(AnimationAge / .55f);
            float ease = 1f - Mathf.Pow(1f - entry, 3);
            float distance = card.rect.width + Mathf.Abs(restPosition.x) + 48f;
            card.anchoredPosition = restPosition + Vector2.left * distance * (1f - ease);
            card.localScale = restScale * (1f + .025f * Mathf.Sin(entry * Mathf.PI));
            if (group) group.alpha = Mathf.Clamp01(AnimationAge / .18f);
            float attention = Mathf.Clamp01(1f - AnimationAge / 2.2f);
            float pulse = attention * (.65f + .35f * Mathf.Sin(AnimationAge * 9f));
            heading.color = Color.Lerp(headingColor, new Color(1f, .8f, .36f, 1), pulse);
            if (kicker)
            {
                kicker.text = AnimationAge < 2.2f ? "NEW OBJECTIVE" : restKicker;
                kicker.color = Color.Lerp(kickerColor, Color.white, pulse);
            }
        }

        static AudioClip CreateChime()
        {
            const int rate = 24000;
            const float duration = .32f;
            var samples = new float[Mathf.CeilToInt(rate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                samples[i] = Note(t, 880f, .17f) + Note(t - .105f, 1174.66f, .205f);
            }
            var clip = AudioClip.Create("Objective notification chime", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
        static float Note(float t, float frequency, float duration)
        {
            if (t < 0 || t >= duration) return 0;
            float envelope = Mathf.Min(1f, t / .008f) * Mathf.Pow(1f - t / duration, 2);
            return .42f * envelope * (Mathf.Sin(2 * Mathf.PI * frequency * t) + .12f * Mathf.Sin(4 * Mathf.PI * frequency * t));
        }
        void OnDisable()
        {
            if (sound) sound.Stop();
            audioPaused = false;
        }
        void OnDestroy() { if (chime) Destroy(chime); }
    }
}
