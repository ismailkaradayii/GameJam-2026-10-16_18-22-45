using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    // The three recruitment cues share one recording; departure has its own voice line.
    [DisallowMultipleComponent]
    public sealed class RescueDialogue : MonoBehaviour
    {
        [Serializable] public sealed class Line
        {
            public string id;
            [TextArea(2, 5)] public string text;
            public bool villager;
        }
        public EvacuationMission mission;
        public Line[] lines = CreateDefaultLines();
        public AudioClip recruitVoice, shelterVoice;
        [Range(0f, 1f)] public float voiceVolume = .9f;
        public string CurrentId { get; private set; }
        public int PlayedLines { get; private set; }
        public bool IsShowingLine => !string.IsNullOrEmpty(CurrentId) && (paused ? pausedAt : Time.time) < endsAt;
        readonly Queue<Line> pending = new Queue<Line>();
        float endsAt, pausedAt;
        string currentSubtitle;
        bool paused, voicePaused;
        AudioSource voice;

        public static Line[] CreateDefaultLines() => new[]
        {
            new Line { id = "recruit_0", text = "Come here, follow me." },
            new Line { id = "recruit_1", text = "Come here, follow me." },
            new Line { id = "recruit_2", text = "Come here, follow me." },
            new Line { id = "depart", text = "Follow me. I'll take you to the shelter." },
        };

        void Awake()
        {
            if (!mission) mission = GetComponent<EvacuationMission>();
            // Older scenes contain long exchanges. Keep only the four player cues,
            // with stable IDs shared by subtitles and the recorded player calls.
            lines = CreateDefaultLines();
            LoadVoiceClips();
        }
        public void LoadVoiceClips()
        {
            if (!recruitVoice) recruitVoice = Resources.Load<AudioClip>("ASHFALL/Audio/Rescue/comehere");
            if (!shelterVoice) shelterVoice = Resources.Load<AudioClip>("ASHFALL/Audio/Rescue/shelter");
        }
        public void Enqueue(string id, bool replace = false)
        {
            if (id != "recruit_0" && id != "recruit_1" && id != "recruit_2" && id != "depart") return;
            Line line = Array.Find(lines, l => l != null && l.id == id && !l.villager);
            if (line == null) return;
            if (replace) { StopVoice(); pending.Clear(); endsAt = 0; CurrentId = null; currentSubtitle = null; paused = false; }
            pending.Enqueue(line);
        }
        void Update()
        {
            if (!mission) return;
            if (voice) voice.volume = voiceVolume * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f));
            bool shouldPause = Time.timeScale <= 0f || VolcanoCinematic.IsPlayingAny || mission.player && mission.player.GameplaySuspended;
            if (shouldPause)
            {
                if (!paused)
                {
                    pausedAt = Time.time; paused = true;
                    if (voice && voice.isPlaying) { voice.Pause(); voicePaused = true; }
                }
                return;
            }
            if (paused)
            {
                float delay = Mathf.Max(0f, Time.time - pausedAt);
                endsAt += delay; paused = false;
                if (voice && voicePaused) { voice.UnPause(); voicePaused = false; }
                if (IsShowingLine && !string.IsNullOrEmpty(currentSubtitle)) mission.ShowDialogue(currentSubtitle, endsAt - Time.time);
            }
            if (IsShowingLine) return;
            StopVoice();
            CurrentId = null; currentSubtitle = null;
            if (pending.Count > 0)
            {
                var line = pending.Dequeue(); CurrentId = line.id;
                var clip = line.id == "depart" ? shelterVoice : recruitVoice;
                float duration = Mathf.Max(2.5f, line.text.Length * .055f);
                if (clip) duration = Mathf.Max(duration, clip.length);
                endsAt = Time.time + duration + .3f;
                currentSubtitle = line.text;
                mission.ShowDialogue(currentSubtitle, duration + .3f);
                if (clip)
                {
                    if (!voice)
                    {
                        var emitter = new GameObject("Rescue dialogue voice"); emitter.transform.SetParent(transform, false);
                        voice = emitter.AddComponent<AudioSource>();
                        voice.playOnAwake = false; voice.loop = false; voice.spatialBlend = 0;
                        voice.priority = 40; voice.dopplerLevel = 0; voice.reverbZoneMix = 0;
                    }
                    voice.volume = voiceVolume * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f));
                    voice.clip = clip; voice.Play();
                }
                PlayedLines++;
                return;
            }
        }
        void StopVoice()
        {
            if (voice) { voice.Stop(); voice.clip = null; }
            voicePaused = false;
        }
        void OnDisable() { StopVoice(); pending.Clear(); CurrentId = null; currentSubtitle = null; endsAt = 0; paused = false; }
    }
}
