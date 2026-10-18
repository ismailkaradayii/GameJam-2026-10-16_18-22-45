using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    // Text dialogue only. Spoken recordings and their AudioSources are intentionally absent.
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
        public string CurrentId { get; private set; }
        public int PlayedLines { get; private set; }
        public bool IsShowingLine => !string.IsNullOrEmpty(CurrentId) && (paused ? pausedAt : Time.time) < endsAt;
        readonly Queue<Line> pending = new Queue<Line>();
        float endsAt, pausedAt;
        string currentSubtitle;
        bool paused;

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
            // with stable IDs for recordings that may be added later.
            lines = CreateDefaultLines();
        }
        public void Enqueue(string id, bool replace = false)
        {
            if (id != "recruit_0" && id != "recruit_1" && id != "recruit_2" && id != "depart") return;
            Line line = Array.Find(lines, l => l != null && l.id == id && !l.villager);
            if (line == null) return;
            if (replace) { pending.Clear(); endsAt = 0; CurrentId = null; currentSubtitle = null; paused = false; }
            pending.Enqueue(line);
        }
        void Update()
        {
            if (!mission) return;
            bool shouldPause = Time.timeScale <= 0f || VolcanoCinematic.IsPlayingAny || mission.player && mission.player.GameplaySuspended;
            if (shouldPause)
            {
                if (!paused) { pausedAt = Time.time; paused = true; }
                return;
            }
            if (paused)
            {
                float delay = Mathf.Max(0f, Time.time - pausedAt);
                endsAt += delay; paused = false;
                if (IsShowingLine && !string.IsNullOrEmpty(currentSubtitle)) mission.ShowDialogue(currentSubtitle, endsAt - Time.time);
            }
            if (IsShowingLine) return;
            CurrentId = null; currentSubtitle = null;
            if (pending.Count > 0)
            {
                var line = pending.Dequeue(); CurrentId = line.id;
                float duration = Mathf.Max(2.5f, line.text.Length * .055f);
                endsAt = Time.time + duration + .3f;
                currentSubtitle = line.text;
                mission.ShowDialogue(currentSubtitle, duration + .3f);
                PlayedLines++;
                return;
            }
        }
        void OnDisable() { pending.Clear(); CurrentId = null; currentSubtitle = null; endsAt = 0; paused = false; }
    }
}
