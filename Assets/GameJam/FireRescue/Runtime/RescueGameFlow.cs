using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GameJam.FireRescue
{
    /// <summary>One scene's five-house rescue objective, pause state and return-to-menu flow.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-200)]
    public sealed class RescueGameFlow : MonoBehaviour
    {
        public enum GameState { Playing, Paused, Won, Lost, ReturningToShelter }
        public enum LossReason { None, PlayerDefeated, AllVillagersLost, VillageBurned, RequiredHouseLost }
        public BurnableBuilding[] targetBuildings = Array.Empty<BurnableBuilding>();
        [Tooltip("Optional short mission: only these houses can burn, and each must be extinguished to win.")]
        public BurnableBuilding[] defenseFireBuildings = Array.Empty<BurnableBuilding>();
        public FireRescuePlayer player;
        public ShelterReturnCinematic victoryCinematic;
        public string mainMenuSceneName = "MainMenu";
        [Header("Result presentation")]
        [Min(0f)] public float resultDelaySeconds = 2.4f;
        public const int RequiredHouses = 5;
        public GameState State { get; private set; } = GameState.Playing;
        public LossReason DefeatReason { get; private set; }
        public int RescuedCount => rescued.Count;
        public int TargetCount => targets.Count;
        public int RequiredFireCount => fireTargets.Count;
        public bool HasFixedFireObjective => fireTargets.Count > 0;
        public bool FireObjectiveComplete => !HasFixedFireObjective || rescued.IsSupersetOf(fireTargets);
        public bool BlocksGameplay => State != GameState.Playing || returningToMenu;
        public bool IsConfigured => targets.Count > 0;
        public bool ReturningToMenu => returningToMenu;
        public bool HasResult => State == GameState.Won || State == GameState.Lost;
        public bool IsVictoryCinematic => State == GameState.ReturningToShelter;
        public float EndingProgress => HasResult ? Mathf.Clamp01((Time.unscaledTime - finishedAt) / Mathf.Max(.01f, resultDelaySeconds)) : 0f;
        public bool ResultReady => HasResult && (resultDelaySeconds <= 0 || EndingProgress >= 1f);
        public float ResultRevealElapsed => HasResult ? Mathf.Max(0, Time.unscaledTime - finishedAt - Mathf.Max(0, resultDelaySeconds)) : 0f;
        public event Action<int, int> ProgressChanged;
        public event Action<GameState> StateChanged;
        readonly HashSet<BurnableBuilding> targets = new HashSet<BurnableBuilding>();
        readonly HashSet<BurnableBuilding> rescued = new HashSet<BurnableBuilding>();
        readonly HashSet<BurnableBuilding> fireTargets = new HashSet<BurnableBuilding>();
        LavaCreatureEncounter encounter;
        VolcanoSurvivalObjective survival;
        EvacuationMission evacuation;
        bool completionPending, returningToMenu, ownsPause;
        float previousTimeScale = 1f;
        float finishedAt;
        bool resultCursorReleased;

        void Awake()
        {
            survival = FindFirstObjectByType<VolcanoSurvivalObjective>();
            evacuation = FindFirstObjectByType<EvacuationMission>();
            State = GameState.Playing;
            DefeatReason = LossReason.None;
            rescued.Clear(); targets.Clear();
            completionPending = returningToMenu = ownsPause = false;
            if (player) player.gameFlow = this;
            foreach (var house in targetBuildings) if (house) targets.Add(house);
            fireTargets.Clear();
            foreach (var house in defenseFireBuildings) if (house && targets.Contains(house)) fireTargets.Add(house);
            if (HasFixedFireObjective)
                foreach (var house in FindObjectsByType<BurnableBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (house.gameObject.scene == gameObject.scene)
                    {
                        house.IsDefenseFireTarget = fireTargets.Contains(house);
                        house.MissionFireAllowed = house.IsDefenseFireTarget;
                    }
            if (!IsConfigured) Debug.LogError("Rescue objective needs authored town houses.", this);
        }
        void OnEnable()
        {
            foreach (var house in targets)
            {
                house.Rescued += OnHouseRescued;
                if (house.IsRescued) OnHouseRescued(house);
            }
        }
        void OnDisable()
        {
            if (victoryCinematic) victoryCinematic.Cancel();
            foreach (var house in targets) if (house) house.Rescued -= OnHouseRescued;
            ReleasePause();
        }
        void OnDestroy() => ReleasePause();
        void OnHouseRescued(BurnableBuilding house)
        {
            if (!IsConfigured || !house || !house.IsRescued || !targets.Contains(house) || !rescued.Add(house)) return;
            ProgressChanged?.Invoke(RescuedCount, TargetCount);
            // The finite defense objective owns victory; extinguishing houses cannot skip enemies.
            if (!FindFirstObjectByType<VolcanoMissionDirector>() && RescuedCount == TargetCount) completionPending = true;
        }
        void Update()
        {
            if (HasResult)
            {
                if (ResultReady && !resultCursorReleased)
                { resultCursorReleased = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                return;
            }
            if (returningToMenu) return;
            if (State == GameState.Playing)
            {
                TickDefenseFires();
                var loss = VillageLossReason();
                if (loss != LossReason.None) { FinishMission(false, loss); return; }
            }
            if (survival && survival.IsFinished) return;
            // Water hits happen in cannon LateUpdate; finalize on the next Update, after that tick has finished.
            if (completionPending && State == GameState.Playing)
            {
                FinishMission(true);
                return;
            }
            if (!Application.isFocused || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (State == GameState.Playing) Pause();
            else if (State == GameState.Paused) Resume();
        }
        public void TickDefenseFires()
        {
            if (!HasFixedFireObjective || BlocksGameplay || Time.timeScale <= 0f) return;
            if (!encounter) encounter = FindFirstObjectByType<LavaCreatureEncounter>();
            if (!encounter || !encounter.AllWavesReleased) return;
            foreach (var actor in encounter.LivingCreatures)
                if (actor && !actor.IsDefeated && actor.TryGetComponent<LavaCreatureCombat>(out var combat) && combat.attackBuildings) return;
            // Let the house attackers ignite their targets. If they were all intercepted,
            // guarantee the three fire objectives without waiting for an impossible impact.
            // Ignite respects wet protection and never replenishes an existing fire.
            foreach (var house in fireTargets) if (house) house.Ignite();
        }
        public void Pause()
        {
            if (BlocksGameplay || completionPending || (player && player.CinematicLocked)) return;
            Suspend(GameState.Paused);
        }
        void Suspend(GameState nextState)
        {
            if (!ownsPause) previousTimeScale = Time.timeScale > 0 ? Time.timeScale : 1f;
            ownsPause = true; State = nextState;
            if (HasResult) { finishedAt = Time.unscaledTime; resultCursorReleased = false; }
            if (player) player.SetGameplaySuspended(true);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = !HasResult;
            StateChanged?.Invoke(State);
        }
        public void Resume()
        {
            if (State != GameState.Paused || returningToMenu || completionPending) return;
            State = GameState.Playing;
            ReleasePause();
            if (player) player.SetGameplaySuspended(false);
            StateChanged?.Invoke(State);
        }
        LossReason VillageLossReason()
        {
            foreach (var house in fireTargets)
                if (house && house.IsBurnedOut) return LossReason.RequiredHouseLost;
            // A sheltered survivor keeps the evacuation viable; an empty roster is not a defeat.
            if (evacuation && evacuation.TotalCount > 0 && evacuation.DeadCount == evacuation.TotalCount)
                return LossReason.AllVillagersLost;
            int houses = 0;
            foreach (var house in targets)
            {
                if (!house) continue;
                houses++;
                if (!house.IsBurnedOut) return LossReason.None;
            }
            return houses > 0 ? LossReason.VillageBurned : LossReason.None;
        }
        public void FinishMission(bool success, LossReason reason = LossReason.None)
        {
            if (HasResult || returningToMenu || IsVictoryCinematic && success) return;
            // Thermal damage may kill the last villager later in the same frame as a victory check.
            var villageLoss = VillageLossReason();
            if (villageLoss != LossReason.None) { success = false; reason = villageLoss; }
            DefeatReason = success ? LossReason.None : reason == LossReason.None ? LossReason.PlayerDefeated : reason;
            if (!success && victoryCinematic) victoryCinematic.Cancel();
            var cinematic = FindFirstObjectByType<VolcanoCinematic>();
            if (cinematic) cinematic.EndShot();
            var encounter = FindFirstObjectByType<LavaCreatureEncounter>();
            if (encounter) encounter.StopEncounter();
            completionPending = false;
            if (success && victoryCinematic && victoryCinematic.isActiveAndEnabled)
            {
                ReleasePause();
                if (player) player.SetGameplaySuspended(false);
                State = GameState.ReturningToShelter;
                if (victoryCinematic.Begin(this)) { StateChanged?.Invoke(State); return; }
            }
            Suspend(success ? GameState.Won : GameState.Lost);
        }
        internal void CompleteVictoryCinematic(ShelterReturnCinematic sequence)
        {
            if (!IsVictoryCinematic || returningToMenu || sequence != victoryCinematic || !sequence.HasCompleted) return;
            Suspend(GameState.Won);
        }
        public void RestartMission()
        {
            if (returningToMenu) return;
            returningToMenu = true; ownsPause = false; Time.timeScale = 1f;
            if (victoryCinematic) victoryCinematic.Cancel();
            var cinematic = FindFirstObjectByType<VolcanoCinematic>();
            if (cinematic) cinematic.EndShot();
            if (player) player.SetGameplaySuspended(true);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            StartCoroutine(LoadWithFade(gameObject.scene.name));
        }
        public void ReturnToMainMenu()
        {
            if (returningToMenu) return;
            if (!Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            {
                Debug.LogError("Rescue menu scene is missing from Build Settings: " + mainMenuSceneName, this);
                return;
            }
            returningToMenu = true;
            if (victoryCinematic) victoryCinematic.Cancel();
            if (player) player.SetGameplaySuspended(true);
            ownsPause = false; Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            StartCoroutine(LoadWithFade(mainMenuSceneName));
        }
        System.Collections.IEnumerator LoadWithFade(string scene)
        {
            yield return AshfallSceneFade.Cover();
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }
        void ReleasePause()
        {
            if (!ownsPause) return;
            Time.timeScale = previousTimeScale > 0 ? previousTimeScale : 1f;
            ownsPause = false;
        }
    }
}
