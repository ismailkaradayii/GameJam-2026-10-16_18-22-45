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
        public enum GameState { Playing, Paused, Won }
        public BurnableBuilding[] targetBuildings = Array.Empty<BurnableBuilding>();
        public FireRescuePlayer player;
        public string mainMenuSceneName = "MainMenu";
        public const int RequiredHouses = 5;
        public GameState State { get; private set; } = GameState.Playing;
        public int RescuedCount => rescued.Count;
        public int TargetCount => targets.Count;
        public bool BlocksGameplay => State != GameState.Playing || returningToMenu;
        public bool IsConfigured => targets.Count == RequiredHouses;
        public bool ReturningToMenu => returningToMenu;
        public event Action<int, int> ProgressChanged;
        public event Action<GameState> StateChanged;
        readonly HashSet<BurnableBuilding> targets = new HashSet<BurnableBuilding>();
        readonly HashSet<BurnableBuilding> rescued = new HashSet<BurnableBuilding>();
        bool completionPending, returningToMenu, ownsPause;
        float previousTimeScale = 1f;

        void Awake()
        {
            State = GameState.Playing;
            rescued.Clear(); targets.Clear();
            completionPending = returningToMenu = ownsPause = false;
            if (player) player.gameFlow = this;
            foreach (var house in targetBuildings) if (house) targets.Add(house);
            if (!IsConfigured) Debug.LogError("Rescue objective requires exactly five distinct authored town houses.", this);
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
            foreach (var house in targets) if (house) house.Rescued -= OnHouseRescued;
            ReleasePause();
        }
        void OnDestroy() => ReleasePause();
        void OnHouseRescued(BurnableBuilding house)
        {
            if (!IsConfigured || !house || !house.IsRescued || !targets.Contains(house) || !rescued.Add(house)) return;
            ProgressChanged?.Invoke(RescuedCount, TargetCount);
            if (RescuedCount == RequiredHouses) completionPending = true;
        }
        void Update()
        {
            // Water hits happen in cannon LateUpdate; finalize on the next Update, after that tick has finished.
            if (completionPending && State != GameState.Won && !returningToMenu)
            {
                completionPending = false;
                Suspend(GameState.Won);
                return;
            }
            if (!Application.isFocused || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (State == GameState.Playing) Pause();
            else if (State == GameState.Paused) Resume();
        }
        public void Pause()
        {
            if (State != GameState.Playing || returningToMenu || completionPending) return;
            Suspend(GameState.Paused);
        }
        void Suspend(GameState nextState)
        {
            if (!ownsPause) previousTimeScale = Time.timeScale > 0 ? Time.timeScale : 1f;
            ownsPause = true; State = nextState;
            if (player) player.SetGameplaySuspended(true);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
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
        public void ReturnToMainMenu()
        {
            if (returningToMenu) return;
            if (!Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            {
                Debug.LogError("Rescue menu scene is missing from Build Settings: " + mainMenuSceneName, this);
                return;
            }
            returningToMenu = true;
            if (player) player.SetGameplaySuspended(true);
            ownsPause = false; Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
        }
        void ReleasePause()
        {
            if (!ownsPause) return;
            Time.timeScale = previousTimeScale > 0 ? previousTimeScale : 1f;
            ownsPause = false;
        }
    }
}
