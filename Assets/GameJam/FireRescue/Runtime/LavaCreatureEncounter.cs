using System;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Releases each authored creature once, when visible lava reaches its emergence point.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-75)]
    public sealed class LavaCreatureEncounter : MonoBehaviour
    {
        [Serializable]
        public sealed class Spawn
        {
            public LavaCreatureActor creature;
            public ProgressiveLavaFlow feed;
            [Range(0f, 1f)] public float feedProgress = .6f;
            [Min(0f)] public float delayAfterArrival;
            [NonSerialized] internal bool spawned;
            [NonSerialized] internal float arrivedFor;
        }

        public VolcanoEruptionSequence sequence;
        public Spawn[] spawns = Array.Empty<Spawn>();
        public bool Initialized { get; private set; }
        public int SpawnedCount { get; private set; }

        void Awake() => InitializeEncounter();
        void Update() => TickEncounter(Time.deltaTime);

        public void InitializeEncounter()
        {
            if (Initialized) return;
            Initialized = true;
            SpawnedCount = 0;
            foreach (Spawn spawn in spawns)
            {
                if (spawn == null) continue;
                spawn.spawned = false;
                spawn.arrivedFor = 0f;
                if (spawn.creature && !transform.IsChildOf(spawn.creature.transform))
                    spawn.creature.gameObject.SetActive(false);
            }
        }

        /// <summary>Deterministic update; disable the manager when calling this explicitly in checks.</summary>
        public void TickEncounter(float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            InitializeEncounter();
            if (!sequence || !sequence.Initialized || sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing) return;
            foreach (Spawn spawn in spawns)
            {
                if (spawn == null || spawn.spawned || !spawn.creature || !spawn.feed ||
                    transform.IsChildOf(spawn.creature.transform) || !spawn.feed.IsInitialized ||
                    spawn.feed.Length <= .001f || spawn.feed.VisibleDistance <= 0f ||
                    spawn.feed.VisibleDistance + .005f < spawn.feed.Length * Mathf.Clamp01(spawn.feedProgress)) continue;
                spawn.arrivedFor += deltaTime;
                if (spawn.arrivedFor < Mathf.Max(0f, spawn.delayAfterArrival)) continue;
                spawn.spawned = true;
                SpawnedCount++;
                spawn.creature.gameObject.SetActive(true);
            }
        }
    }
}
