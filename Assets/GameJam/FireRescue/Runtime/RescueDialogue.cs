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
        readonly HashSet<int> panicHeard = new HashSet<int>();
        float endsAt, nextPanic, pausedAt;
        string currentSubtitle;
        bool paused;

        public static Line[] CreateDefaultLines() => new[]
        {
            new Line { id = "dispatch", text = "Önce insanları toplanma alanına getirmeliyim. Sonra hep birlikte sığınağa gideceğiz.", villager = false },
            new Line { id = "recruit_0", text = "Hey! Bana bakın! Bana güvenin, sizi buradan çıkaracağım. Birbirinizden ayrılmayın, beni takip edin!", villager = false },
            new Line { id = "reply_0", text = "Çok korkuyoruz! Ne olur bizi burada bırakma!", villager = true },
            new Line { id = "recruit_1", text = "Buradayım! Sakin olun. Sizi kurtaracağım. Yanımdan ayrılmayın, toplanma alanına gidiyoruz!", villager = false },
            new Line { id = "reply_1", text = "Tamam, sana güveniyoruz! Hadi, gidelim!", villager = true },
            new Line { id = "recruit_2", text = "Hepiniz benimle gelin! Kimseyi geride bırakmayacağım. Birlikte gidersek kurtulacağız!", villager = false },
            new Line { id = "reply_2", text = "Bizi de götür! Lütfen, acele edelim!", villager = true },
            new Line { id = "wait_0", text = "Burada bekleyin. Birbirinizden ayrılmayın! Diğerlerini de alıp geleceğim.", villager = false },
            new Line { id = "wait_1", text = "Tamam, burada güvendesiniz. Son grubu da getiriyorum. Beni burada bekleyin!", villager = false },
            new Line { id = "wait_2", text = "Herkes burada. Derin bir nefes alın. Az sonra birlikte hareket edeceğiz.", villager = false },
            new Line { id = "ready", text = "Herkesi topladım. Şimdi onları sığınağa götürmeliyim.", villager = false },
            new Line { id = "depart", text = "Sizi bu günler için hazırladığım sığınağa götüreceğim. Hepiniz orada güvende olacaksınız. Hadi! Birbirinize göz kulak olun, beni takip edin!", villager = false },
            new Line { id = "safe", text = "Başardık... Artık güvendesiniz. Burada kalın. Ben dışarıdaki yangınları durduracağım.", villager = false },
            new Line { id = "panic_0", text = "Yardım edin! Ne yapacağız biz? Buradan nasıl çıkacağız?", villager = true },
            new Line { id = "panic_1", text = "Yer sallanıyor! Birisi yardım etsin!", villager = true },
            new Line { id = "panic_2", text = "Lütfen! Bizi duyan var mı? Çok korkuyorum!", villager = true },
        };

        void Awake()
        {
            if (!mission) mission = GetComponent<EvacuationMission>();
            nextPanic = Time.time + 4f;
        }
        public void Enqueue(string id, bool replace = false)
        {
            Line line = Array.Find(lines, l => l != null && l.id == id);
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
                endsAt += delay; nextPanic += delay; paused = false;
                if (IsShowingLine && !string.IsNullOrEmpty(currentSubtitle)) mission.ShowDialogue(currentSubtitle, endsAt - Time.time);
            }
            if (IsShowingLine) return;
            CurrentId = null; currentSubtitle = null;
            if (pending.Count > 0)
            {
                var line = pending.Dequeue(); CurrentId = line.id;
                float duration = Mathf.Max(2.5f, line.text.Length * .055f);
                endsAt = Time.time + duration + .3f;
                currentSubtitle = (line.villager ? "KÖYLÜ: " : "SEN: ") + line.text;
                mission.ShowDialogue(currentSubtitle, duration + .3f);
                PlayedLines++; nextPanic = Time.time + duration + 8f;
                return;
            }
            if (!mission.IsActive || mission.EscortStarted || !mission.PlayerTransform || Time.time < nextPanic) return;
            EvacuationResident nearby = null; float nearest = 24f;
            foreach (var r in mission.residents)
            {
                if (!r || r.IsCalled || r.IsDead || r.IsBurning || r.State != EvacuationResident.ResidentState.Scattering || panicHeard.Contains(r.rescueGroup)) continue;
                float distance = EvacuationResident.FlatDistance(r.transform.position, mission.PlayerTransform.position);
                if (distance < nearest) { nearest = distance; nearby = r; }
            }
            if (nearby) { panicHeard.Add(nearby.rescueGroup); Enqueue("panic_" + Mathf.Clamp(nearby.rescueGroup, 0, 2)); }
        }
        void OnDisable() { pending.Clear(); CurrentId = null; currentSubtitle = null; endsAt = 0; paused = false; }
    }
}
