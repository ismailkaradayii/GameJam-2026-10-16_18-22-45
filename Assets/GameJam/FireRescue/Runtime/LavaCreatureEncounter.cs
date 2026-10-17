using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Lava-gated waves, built from pristine copies of the authored mountain spirits.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-75)]
    public sealed class LavaCreatureEncounter : MonoBehaviour
    {
        public const float EmergenceLavaMargin = 4f;
        [Serializable]
        public sealed class Spawn
        {
            public LavaCreatureActor creature;
            public ProgressiveLavaFlow feed;
            [Range(0f, 1f)] public float feedProgress = .6f;
            [Min(0f)] public float delayAfterArrival;
            [Tooltip("Emerge from this feed's surface, then hop along it and the connected flows below.")]
            public bool descendFromLava;
            public ProgressiveLavaFlow[] downhillFlows = Array.Empty<ProgressiveLavaFlow>();
            [Range(0f, 1f)] public float descentEndProgress = 1f;
            [Min(.5f)] public float emergenceDepth = 2.8f;
            [NonSerialized] internal bool spawned;
            [NonSerialized] internal float arrivedFor;
        }

        public VolcanoEruptionSequence sequence;
        public Spawn[] spawns = Array.Empty<Spawn>();
        [Header("Finite eruption waves")]
        public bool enableWaves = true;
        public bool enableFinalObjective = true;
        public int[] waveSizes = { 3, 4, 5, 6, 7 };
        [Min(1f)] public float secondsBetweenWaves = 32f;
        [Min(.1f)] public float secondsBetweenSpawns = 1.8f;
        [Min(1)] public int maximumAlive = 10;
        [Header("Village tactics")]
        [Tooltip("Leave the lava after emerging and use independent outdoor routes.")]
        public bool disperseIntoVillage = true;
        [Range(0f, 1f)] public float buildingAttackShare = .6f;
        public bool Initialized { get; private set; }
        public int SpawnedCount { get; private set; }
        public int DefeatedCount { get; private set; }
        public int WaveNumber { get; private set; }
        public int AliveCount => living.Count;
        public int WaveCount => waveSizes == null ? 0 : waveSizes.Length;
        public int SpawnedInWave => spawnedInWave;
        public int CurrentWaveSize => WaveNumber > 0 && WaveNumber <= WaveCount ? Mathf.Max(1, waveSizes[WaveNumber - 1]) : 0;
        public int PlannedCount { get { int n = 0; if (waveSizes != null) foreach (int size in waveSizes) n += Mathf.Max(1, size); return n; } }
        public bool AllWavesReleased => enableWaves && WaveCount > 0 && WaveNumber == WaveCount && spawnedInWave >= CurrentWaveSize;
        public bool IsCleared => AllWavesReleased && AliveCount == 0;
        public bool IsStopped { get; private set; }
        public float NextWaveIn => Mathf.Max(0f, nextWaveAt - flowingFor);
        public float WaveAge => flowingFor - waveBeganAt;
        public IReadOnlyList<LavaCreatureActor> LivingCreatures => living;
        readonly List<LavaCreatureActor> living = new List<LavaCreatureActor>();
        LavaCreatureActor[] templates;
        GameObject templateRoot;
        int spawnedInWave, nextSpawnIndex;
        float flowingFor, nextWaveAt, spawnCooldown, waveBeganAt;

        void Awake()
        {
            InitializeEncounter();
            if (Application.isPlaying && enableWaves && enableFinalObjective && !GetComponent<VolcanoSurvivalObjective>())
                gameObject.AddComponent<VolcanoSurvivalObjective>().encounter = this;
        }
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
            RemoveDefeated();
            if (IsStopped) return;
            if (!sequence || !sequence.Initialized || sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing) return;
            if (enableWaves) { TickWaves(deltaTime); return; }
            foreach (Spawn spawn in spawns)
            {
                if (spawn == null || spawn.spawned || !LavaHasArrived(spawn)) continue;
                spawn.arrivedFor += deltaTime;
                if (spawn.arrivedFor < Mathf.Max(0f, spawn.delayAfterArrival)) continue;
                Release(spawn, spawn.creature);
            }
        }

        public void StopEncounter() => IsStopped = true;

        bool LavaHasArrived(Spawn spawn)
        {
            if (spawn?.creature == null || !spawn.feed || transform.IsChildOf(spawn.creature.transform) ||
                !spawn.feed.IsInitialized || spawn.feed.Length <= .001f || spawn.feed.VisibleDistance <= 0f) return false;
            float arrival = Mathf.Min(spawn.feed.Length, spawn.feed.Length * Mathf.Clamp01(spawn.feedProgress) +
                (spawn.descendFromLava ? EmergenceLavaMargin : 0f));
            return spawn.feed.VisibleDistance + .005f >= arrival;
        }

        void TickWaves(float dt)
        {
            if (WaveCount == 0 || spawns.Length == 0 || AllWavesReleased) return;
            flowingFor += dt;
            foreach (var spawn in spawns) if (LavaHasArrived(spawn)) spawn.arrivedFor += dt;
            if (AliveCount >= Mathf.Max(1, maximumAlive)) return;
            int index = -1;
            for (int n = 0; n < spawns.Length; n++)
            {
                int candidate = (nextSpawnIndex + n) % spawns.Length;
                var spawn = spawns[candidate];
                if (LavaHasArrived(spawn) && spawn.arrivedFor >= Mathf.Max(0f, spawn.delayAfterArrival))
                { index = candidate; break; }
            }
            if (index < 0) return;
            if (WaveNumber == 0 || spawnedInWave >= CurrentWaveSize)
            {
                if (WaveNumber > 0 && flowingFor < nextWaveAt) return;
                WaveNumber++; spawnedInWave = 0; spawnCooldown = 0f;
                waveBeganAt = flowingFor; nextWaveAt = flowingFor + Mathf.Max(1f, secondsBetweenWaves);
            }
            spawnCooldown -= dt;
            if (spawnCooldown > 0f) return;
            EnsureTemplates();
            Spawn selected = spawns[index];
            LavaCreatureActor actor = selected.spawned
                ? Instantiate(templates[index], transform) : selected.creature;
            actor.name = "Lava Spirit Â· Wave " + WaveNumber + " Â· " + (spawnedInWave + 1);
            Release(selected, actor);
            spawnedInWave++; nextSpawnIndex = (index + 1) % spawns.Length;
            // Never accumulate a burst while the living limit or lava gate is holding a wave.
            spawnCooldown = Mathf.Max(.1f, secondsBetweenSpawns);
        }

        void EnsureTemplates()
        {
            if (templates != null) return;
            templateRoot = new GameObject("Pristine lava wave templates");
            templateRoot.transform.SetParent(transform, false); templateRoot.SetActive(false);
            templates = new LavaCreatureActor[spawns.Length];
            for (int i = 0; i < spawns.Length; i++)
                if (spawns[i]?.creature)
                {
                    templates[i] = Instantiate(spawns[i].creature, templateRoot.transform);
                    templates[i].gameObject.SetActive(false);
                }
        }

        void Release(Spawn spawn, LavaCreatureActor actor)
        {
            spawn.spawned = true; SpawnedCount++; living.Add(actor);
            var siege = actor.GetComponent<LavaCreatureCombat>();
            if (!siege) siege = actor.gameObject.AddComponent<LavaCreatureCombat>();
            // A repeating low-discrepancy pattern guarantees both roles even in a three-spirit wave.
            siege.attackBuildings = Mathf.Repeat((SpawnedCount - 1) * .618034f, 1f) < buildingAttackShare;
            if (spawn.descendFromLava)
            {
                var path = disperseIntoVillage ? VillageEmergence(spawn) : DescentPath(spawn);
                // Spread each procession across the lava channel instead of using one footprint.
                float lane = disperseIntoVillage ? 0 : ((SpawnedCount - 1) / Mathf.Max(1, spawns.Length) % 3 - 1) * 1.35f;
                var originalPath = (Vector3[])path.Clone();
                var sourceJump = spawn.creature.GetComponent<LavaCreatureAutoJump>();
                for (int i = 0; i < path.Length; i++)
                {
                    Vector3 forward = originalPath[Mathf.Min(i + 1, path.Length - 1)] - originalPath[Mathf.Max(0, i - 1)];
                    Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
                    path[i] += side * lane;
                    var terrain = sourceJump ? sourceJump.groundTerrain : null;
                    if (terrain) path[i].y = Mathf.Max(path[i].y, terrain.SampleHeight(path[i]) + terrain.transform.position.y + sourceJump.groundClearance);
                }
                var combat = actor.GetComponent<LavaCreatureCombat>();
                if (!combat) combat = actor.gameObject.AddComponent<LavaCreatureCombat>();
                if (disperseIntoVillage) combat.ConfigureVillageEmergence(spawn.emergenceDepth);
                else combat.ConfigureLavaDescent(path, spawn.emergenceDepth);
                if (path.Length > 0) actor.transform.position = path[0];
            }
            actor.gameObject.SetActive(true);
        }

        Vector3[] VillageEmergence(Spawn spawn)
        {
            var jump = spawn.creature.GetComponent<LavaCreatureAutoJump>();
            var terrain = jump ? jump.groundTerrain : null;
            float clearance = jump ? jump.groundClearance : .1f;
            float first = spawn.feed.Length * Mathf.Clamp01(spawn.feedProgress);
            float last = Mathf.Max(first, Mathf.Min(spawn.feed.VisibleDistance - EmergenceLavaMargin, spawn.feed.Length * .94f));
            Vector3 origin = spawn.feed.PositionAtDistance(first);
            // Sample along the live lava front and alternate banks. Reject house interiors and water.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                float distance = Mathf.Lerp(first, last, Mathf.Repeat(SpawnedCount * .381966f + attempt * .173f, 1));
                Vector3 center = spawn.feed.PositionAtDistance(distance);
                Vector3 forward = spawn.feed.PositionAtDistance(Mathf.Min(spawn.feed.Length, distance + 2)) -
                    spawn.feed.PositionAtDistance(Mathf.Max(0, distance - 2));
                Vector3 side = Vector3.Cross(Vector3.up, forward).normalized * ((SpawnedCount + attempt) % 2 == 0 ? 1 : -1);
                Vector3 point = center + side * (1.4f + attempt % 4 * 1.1f);
                if (terrain) point.y = terrain.SampleHeight(point) + terrain.transform.position.y + clearance;
                if (SpiritOutdoorNavigation.Contains(point, 1.5f)) continue;
                bool water = false;
                foreach (var source in WaterSourceVolume.ActiveSources)
                    if (source && source.ContainsSurface(point) && point.y < source.SurfaceY) { water = true; break; }
                if (water) continue;
                origin = point; break;
            }
            origin = SpiritOutdoorNavigation.Outside(origin, origin, 1.6f);
            if (terrain) origin.y = terrain.SampleHeight(origin) + terrain.transform.position.y + clearance;
            return new[] { origin };
        }

        void RemoveDefeated()
        {
            for (int i = living.Count - 1; i >= 0; i--)
            {
                var actor = living[i];
                if (actor && !actor.IsDefeated) continue;
                living.RemoveAt(i);
                if (!actor) continue;
                if (!actor.ReachedBuilding && !actor.Dissipated) DefeatedCount++;
                bool authored = Array.Exists(spawns, s => s != null && s.creature == actor);
                if (!authored) Destroy(actor.gameObject, 2f);
            }
        }

        static Vector3[] DescentPath(Spawn spawn)
        {
            var points = new List<Vector3>();
            var flows = new List<ProgressiveLavaFlow> { spawn.feed };
            if (spawn.downhillFlows != null)
                foreach (var flow in spawn.downhillFlows) if (flow && !flows.Contains(flow)) flows.Add(flow);
            var jump = spawn.creature.GetComponent<LavaCreatureAutoJump>();
            float clearance = jump ? jump.groundClearance : 0f;
            for (int i = 0; i < flows.Count; i++)
            {
                var flow = flows[i];
                if (!flow.IsInitialized || flow.Length <= .001f) continue;
                float from = i == 0 ? flow.Length * Mathf.Clamp01(spawn.feedProgress) : 0f;
                float to = flow.Length * (i == flows.Count - 1 ? Mathf.Clamp01(spawn.descentEndProgress) : 1f);
                int segments = Mathf.Max(1, Mathf.CeilToInt((to - from) / 3f));
                for (int n = 0; n <= segments; n++)
                {
                    Vector3 point = flow.PositionAtDistance(Mathf.Lerp(from, Mathf.Max(from, to), n / (float)segments));
                    if (jump && jump.groundTerrain)
                        point.y = Mathf.Max(point.y, jump.groundTerrain.SampleHeight(point) + jump.groundTerrain.transform.position.y);
                    point.y += clearance;
                    if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], point) > .1f) points.Add(point);
                }
            }
            return points.ToArray();
        }
    }
}
