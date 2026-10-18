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
        [Header("Fire spirit audio")]
        [SerializeField] private AudioClip spiritVoiceClip;
        [Header("Finite eruption waves")]
        public bool enableWaves = true;
        public bool enableFinalObjective = true;
        public int[] waveSizes = { 10 };
        [Min(1f)] public float secondsBetweenWaves = 32f;
        [Min(.1f)] public float secondsBetweenSpawns = 1.8f;
        [Min(1)] public int maximumAlive = 10;
        [Header("Village tactics")]
        [Tooltip("Leave the lava after emerging and use independent outdoor routes.")]
        public bool disperseIntoVillage = true;
        [Range(0f, 1f)] public float buildingAttackShare = .5f;
        [Header("Final-wave stragglers")]
        [Min(1f)] public float strandedSpiritSeconds = 6f;
        [Min(30f)] public float strandedSpiritDistance = 120f;
        public int StragglerRecoveries { get; private set; }
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
        readonly Dictionary<LavaCreatureActor, float> strandedFor = new Dictionary<LavaCreatureActor, float>();
        // Encounter timing remembers arrival at the village inlet, independently of
        // whether the player subsequently dams the visible lava.
        readonly Dictionary<ProgressiveLavaFlow, float> arrivalDistances = new Dictionary<ProgressiveLavaFlow, float>();
        readonly Collider[] emergenceContacts = new Collider[32];
        LavaCreatureActor[] templates;
        bool[] attackOrder;
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
            if (Time.timeScale <= 0f || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            InitializeEncounter();
            RemoveDefeated();
            if (IsStopped) return;
            if (!sequence || !sequence.Initialized || sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing) return;
            TickArrivalClocks(deltaTime);
            if (enableWaves) { TickWaves(deltaTime); RecoverStragglers(deltaTime); return; }
            foreach (Spawn spawn in spawns)
            {
                if (spawn == null || spawn.spawned || !LavaHasArrived(spawn)) continue;
                spawn.arrivedFor += deltaTime;
                if (spawn.arrivedFor < Mathf.Max(0f, spawn.delayAfterArrival)) continue;
                Release(spawn, spawn.creature);
            }
        }

        public void StopEncounter() => IsStopped = true;

        void TickArrivalClocks(float dt)
        {
            if (!disperseIntoVillage) return;
            for (int i = 0; i < spawns.Length; i++)
            {
                var feed = spawns[i]?.feed;
                if (!feed || !feed.IsInitialized || !feed.canBeStoppedByWater) continue;
                bool duplicate = false;
                for (int n = 0; n < i; n++) if (spawns[n]?.feed == feed) { duplicate = true; break; }
                if (duplicate) continue;
                bool started = arrivalDistances.TryGetValue(feed, out float distance);
                if (!started && feed.VisibleDistance <= 0f)
                {
                    var route = Array.Find(sequence.routes, r => r != null && r.flow == feed);
                    if (route?.feed == null || sequence.FlowElapsed < feed.startDelay ||
                        route.feed.VisibleDistance <= 0f || route.feed.VisibleDistance + .005f < route.feed.Length * route.feedProgress) continue;
                }
                // Preserve the authored travel-time offsets, even after an early dam.
                arrivalDistances[feed] = Mathf.Min(feed.Length, Mathf.Max(distance, feed.VisibleDistance) + Mathf.Max(0f, feed.speed) * dt);
            }
        }

        bool LavaHasArrived(Spawn spawn)
        {
            if (spawn?.creature == null || !spawn.feed || transform.IsChildOf(spawn.creature.transform) ||
                !spawn.feed.IsInitialized || spawn.feed.Length <= .001f) return false;
            float arrival = Mathf.Min(spawn.feed.Length, spawn.feed.Length * Mathf.Clamp01(spawn.feedProgress) +
                (spawn.descendFromLava ? EmergenceLavaMargin : 0f));
            float reached = spawn.feed.VisibleDistance;
            if (disperseIntoVillage && spawn.feed.canBeStoppedByWater && arrivalDistances.TryGetValue(spawn.feed, out float timedArrival))
                reached = Mathf.Max(reached, timedArrival);
            return reached > 0f && reached + .005f >= arrival;
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
                if (LavaHasArrived(spawn) && spawn.arrivedFor >= Mathf.Max(0f, spawn.delayAfterArrival) &&
                    (!disperseIntoVillage || !spawn.descendFromLava || TryVillageEmergence(spawn, out _, 1)))
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
            if (!Release(selected, actor))
            {
                if (actor != selected.creature) Destroy(actor.gameObject);
                return;
            }
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

        bool Release(Spawn spawn, LavaCreatureActor actor)
        {
            Vector3 villagePoint = default;
            if (spawn.descendFromLava && disperseIntoVillage && !TryVillageEmergence(spawn, out villagePoint, 1)) return false;
            spawn.spawned = true; SpawnedCount++; living.Add(actor);
            if (spiritVoiceClip)
            {
                var voice = actor.GetComponent<LavaSpiritAudio>();
                if (!voice) voice = actor.gameObject.AddComponent<LavaSpiritAudio>();
                if (!voice.clip) voice.clip = spiritVoiceClip;
            }
            var siege = actor.GetComponent<LavaCreatureCombat>();
            if (!siege) siege = actor.gameObject.AddComponent<LavaCreatureCombat>();
            // Shuffle a fixed roster, rather than rolling each spawn independently:
            // the ten-spirit mission always gets exactly five of each role.
            if (attackOrder == null) attackOrder = CreateAttackOrder(enableWaves ? PlannedCount : spawns.Length, buildingAttackShare);
            siege.attackBuildings = attackOrder[SpawnedCount - 1];
            if (spawn.descendFromLava)
            {
                var path = disperseIntoVillage ? new[] { villagePoint } : DescentPath(spawn);
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
            return true;
        }

        static bool[] CreateAttackOrder(int count, float buildingShare)
        {
            var order = new bool[count];
            int buildings = Mathf.RoundToInt(count * Mathf.Clamp01(buildingShare));
            for (int i = 0; i < buildings; i++) order[i] = true;
            for (int i = count - 1; i > 0; i--)
            {
                int other = UnityEngine.Random.Range(0, i + 1);
                bool role = order[i]; order[i] = order[other]; order[other] = role;
            }
            return order;
        }

        void RecoverStragglers(float dt)
        {
            if (!disperseIntoVillage || !AllWavesReleased || AliveCount == 0 || AliveCount > 2)
            { strandedFor.Clear(); return; }
            if (Time.timeScale <= 0f || VolcanoCinematic.IsPlayingAny) return;
            foreach (var actor in living)
            {
                if (!actor || actor.IsDefeated) continue;
                var combat = actor.GetComponent<LavaCreatureCombat>();
                if (!combat || !combat.isActiveAndEnabled || combat.IsIntroductionJump) continue;
                if (InCombatRange(actor.transform.position, combat.target)) { strandedFor.Remove(actor); continue; }
                strandedFor.TryGetValue(actor, out float seconds);
                seconds += dt; strandedFor[actor] = seconds;
                if (seconds < Mathf.Max(1f, strandedSpiritSeconds)) continue;
                // Reuse this living enemy: recovery must not add a spawn, heal it,
                // count a defeat or let victory skip the final fight.
                bool recovered = false;
                for (int attempt = 0; attempt < spawns.Length * 4; attempt++)
                {
                    var spawn = spawns[(nextSpawnIndex + attempt) % spawns.Length];
                    if (!LavaHasArrived(spawn) || !spawn.descendFromLava) continue;
                    if (!TryVillageEmergence(spawn, out Vector3 point, StragglerRecoveries + attempt + 1, combat.target)) continue;
                    if (!InCombatRange(point, combat.target) || SpiritOutdoorNavigation.Contains(point, 1.5f) ||
                        combat.target && Vector3.Distance(point, combat.target.PursuitPosition) < 18f) continue;
                    bool water = false;
                    foreach (var source in WaterSourceVolume.ActiveSources)
                        if (source && source.ContainsSurface(point) && point.y < source.SurfaceY) { water = true; break; }
                    if (water || !combat.ReemergeFromLava(point, spawn.emergenceDepth)) continue;
                    StragglerRecoveries++; strandedFor.Remove(actor); recovered = true; break;
                }
                // Retry briefly if the lava has not yet exposed a safe return point.
                if (!recovered) strandedFor[actor] = Mathf.Max(0f, strandedSpiritSeconds - 1f);
            }
        }

        bool InCombatRange(Vector3 point, CreatureCombatPlayer target)
        {
            float range = Mathf.Max(30f, strandedSpiritDistance);
            if (target && (target.PursuitPosition - point).sqrMagnitude <= range * range) return true;
            foreach (var house in BurnableBuilding.ActiveBuildings)
                if (house && house.ignitionBounds && house.gameObject.scene == gameObject.scene &&
                    (house.ignitionBounds.ClosestPoint(point) - point).sqrMagnitude <= range * range) return true;
            return false;
        }

        bool TryVillageEmergence(Spawn spawn, out Vector3 point, int variation = 0, CreatureCombatPlayer avoidTarget = null)
        {
            var jump = spawn.creature.GetComponent<LavaCreatureAutoJump>();
            var terrain = jump ? jump.groundTerrain : null;
            float clearance = jump ? jump.groundClearance : .1f;
            float first = spawn.feed.Length * Mathf.Clamp01(spawn.feedProgress);
            float last = Mathf.Min(spawn.feed.VisibleDistance - .2f, spawn.feed.Length * .94f);
            if (last >= first && SampleEmergence(spawn.feed, first, last, terrain, clearance, variation, avoidTarget, false, out point)) return true;
            if (last > 0f && SampleEmergence(spawn.feed, 0f, last, terrain, clearance, variation, avoidTarget, true, out point)) return true;
            // A stopped village inlet must not strand the finite wave. Look just
            // above its junction in the still-hot source, never back at the crater.
            var downstream = spawn.feed;
            for (int depth = 0; sequence && depth < sequence.routes.Length; depth++)
            {
                var route = Array.Find(sequence.routes, r => r != null && r.flow == downstream);
                if (route?.feed == null || route.feed == downstream || !route.feed.IsInitialized) break;
                var upstream = route.feed;
                float end = Mathf.Min(upstream.VisibleDistance - .2f, upstream.Length * Mathf.Clamp01(route.feedProgress));
                if (end > 0f && SampleEmergence(upstream, Mathf.Max(0, end - 24f), end, terrain, clearance, variation, avoidTarget, true, out point)) return true;
                downstream = upstream;
            }
            point = default;
            return false;
        }

        bool SampleEmergence(ProgressiveLavaFlow feed, float first, float last, Terrain terrain, float clearance,
            int variation, CreatureCombatPlayer avoidTarget, bool requireVillage, out Vector3 point)
        {
            var heat = feed.ignitionSource ? feed.ignitionSource : feed.GetComponent<LavaIgnitionSource>();
            for (int attempt = 0; attempt < 32; attempt++)
            {
                // Start at the downstream end for the narrow safe bank at the village inlet.
                float t = requireVillage ? 1f - (attempt / 4) / 8f : Mathf.Repeat((SpawnedCount + variation) * .381966f + attempt * .173f, 1f);
                float distance = Mathf.Lerp(first, last, t);
                float cross = .2f + ((attempt + variation) % 4) * .2f;
                point = feed.PositionAtDistance(distance, cross);
                if (heat && (!heat.IsHotSurfaceActive || !heat.heatCollider.Raycast(new Ray(point + Vector3.up, Vector3.down), out var hit, 2f) || !heat.IsHotAt(hit.point))) continue;
                if (feed.GetSupply01(distance / Mathf.Max(.001f, feed.Length)) <= 0f) continue;
                if (terrain) point.y = terrain.SampleHeight(point) + terrain.transform.position.y + clearance;
                if (SpiritOutdoorNavigation.Contains(point, 1.5f)) continue;
                if (!HasEmergenceClearance(point)) continue;
                if (requireVillage && !InCombatRange(point, avoidTarget)) continue;
                if (avoidTarget && Vector3.Distance(point, avoidTarget.PursuitPosition) < 18f) continue;
                bool water = false;
                foreach (var source in WaterSourceVolume.ActiveSources)
                    if (source && source.ContainsSurface(point) && point.y < source.SurfaceY) { water = true; break; }
                if (water) continue;
                return true;
            }
            point = default;
            return false;
        }

        bool HasEmergenceClearance(Vector3 point)
        {
            // House footprints alone do not cover fences, rocks and roadside props.
            // Reject a birth already embedded in one of those solid obstacles.
            int count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * .65f,
                point + Vector3.up * 1.5f, .5f, emergenceContacts, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count >= emergenceContacts.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var obstacle = emergenceContacts[i];
                if (!obstacle || obstacle is TerrainCollider || obstacle.GetComponentInParent<LavaCreatureActor>() ||
                    obstacle.GetComponentInParent<ProgressiveLavaFlow>() || obstacle.GetComponentInParent<LavaIgnitionSource>() ||
                    obstacle.GetComponentInParent<LavaCoolingSurface>()) continue;
                return false;
            }
            return true;
        }

        void RemoveDefeated()
        {
            for (int i = living.Count - 1; i >= 0; i--)
            {
                var actor = living[i];
                if (actor && !actor.IsDefeated) continue;
                living.RemoveAt(i);
                if (!actor) continue;
                if (!actor.ReachedBuilding && !actor.Dissipated && !actor.SelfDetonated) DefeatedCount++;
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
