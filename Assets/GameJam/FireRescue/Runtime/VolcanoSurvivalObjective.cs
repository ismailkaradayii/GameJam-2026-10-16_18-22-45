using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.FireRescue
{
    /// <summary>Evacuate, survive a finite eruption, then extract in the fire truck.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(190)]
    public sealed class VolcanoSurvivalObjective : MonoBehaviour
    {
        public enum MissionState { WaitingForEruption, Fighting, ReturnToTruck, Won, Lost }
        public LavaCreatureEncounter encounter;
        public FireRescuePlayer player;
        public CreatureCombatPlayer vitals;
        public bool showHud = true;
        public MissionState State { get; private set; }
        public bool IsFinished => State == MissionState.Won || State == MissionState.Lost;
        readonly Dictionary<Behaviour, bool> controls = new Dictionary<Behaviour, bool>();
        RescueGameFlow rescueFlow;
        bool bound, previousRecovery, pausedWorld;
        float previousTimeScale = 1f;
        CursorLockMode previousLock;
        bool previousCursor;
        GUIStyle title, body, centered, button;

        void Start() => BindPlayer();
        void Update() => TickObjective();

        void BindPlayer()
        {
            if (!rescueFlow) rescueFlow = FindFirstObjectByType<RescueGameFlow>();
            if (!encounter) encounter = GetComponent<LavaCreatureEncounter>();
            if (!player) player = FindFirstObjectByType<FireRescuePlayer>();
            if (!vitals && player && player.firstPerson) vitals = CreatureCombatPlayer.Ensure(player.firstPerson);
            if (bound || !vitals) return;
            previousRecovery = vitals.allowRecovery;
            vitals.allowRecovery = false;
            vitals.Defeated += Lose;
            bound = true;
        }

        public void TickObjective()
        {
            if (!isActiveAndEnabled) return;
            BindPlayer();
            if (rescueFlow && (rescueFlow.State == RescueGameFlow.GameState.Won || rescueFlow.State == RescueGameFlow.GameState.Lost))
            {
                State = rescueFlow.State == RescueGameFlow.GameState.Won ? MissionState.Won : MissionState.Lost;
                return;
            }
            if (IsFinished || Time.timeScale <= 0f || (rescueFlow && rescueFlow.BlocksGameplay)) return;
            if (!encounter || !encounter.enableWaves) return;
            if (vitals && vitals.Health <= 0f) { Lose(); return; }
            var sequence = encounter.sequence;
            if (!sequence || !sequence.Initialized || sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing) return;
            if (VolcanoCinematic.IsPlayingAny) return;
            bool firesOut = true;
            int standing = 0, total = 0;
            foreach (var house in BurnableBuilding.ActiveBuildings)
            {
                if (!house) continue;
                total++; if (!house.IsBurnedOut) standing++;
                if (house.IsBurning) firesOut = false;
            }
            if (total > 0 && standing == 0) { Finish(MissionState.Lost, RescueGameFlow.LossReason.VillageBurned); return; }
            if (encounter.IsCleared && firesOut && standing > 0) { Finish(MissionState.Won); return; }
            State = encounter.IsCleared && firesOut ? MissionState.ReturnToTruck : MissionState.Fighting;
            // Returning early is safe, but cannot skip the remaining enemies or the rescue.
            if ((!rescueFlow || !rescueFlow.IsConfigured) && State == MissionState.ReturnToTruck && (!sequence.evacuation || sequence.evacuation.IsComplete) &&
                player && player.IsDriving && player.drive) Finish(MissionState.Won);
        }

        void Lose() { if (encounter && encounter.enableWaves && !IsFinished) Finish(MissionState.Lost); }

        void Finish(MissionState result, RescueGameFlow.LossReason reason = RescueGameFlow.LossReason.None)
        {
            if (IsFinished) return;
            if (rescueFlow)
            {
                rescueFlow.FinishMission(result == MissionState.Won, reason);
                if (rescueFlow.HasResult) State = rescueFlow.State == RescueGameFlow.GameState.Won ? MissionState.Won : MissionState.Lost;
                return;
            }
            State = result; if (encounter) encounter.StopEncounter();
            previousTimeScale = Time.timeScale; previousLock = Cursor.lockState; previousCursor = Cursor.visible;
            pausedWorld = true; Time.timeScale = 0f;
            if (player)
            {
                Freeze(player); Freeze(player.firstPerson); Freeze(player.pistol);
                Freeze(player.cannon); Freeze(player.hose); Freeze(player.drive);
            }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        void Freeze(Behaviour component)
        {
            if (!component || controls.ContainsKey(component)) return;
            controls.Add(component, component.enabled); component.enabled = false;
        }

        public void RestartMission() { if (rescueFlow) rescueFlow.RestartMission(); else LoadScene(gameObject.scene.name); }
        public void ReturnToMenu() { if (rescueFlow) rescueFlow.ReturnToMainMenu(); else LoadScene("MainMenu"); }
        void LoadScene(string scene)
        {
            if (!IsFinished || !Application.CanStreamedLevelBeLoaded(scene)) return;
            Time.timeScale = 1f; pausedWorld = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            SceneManager.LoadScene(scene);
        }

        void OnDisable()
        {
            if (bound && vitals) { vitals.Defeated -= Lose; vitals.allowRecovery = previousRecovery; }
            bound = false;
            if (!pausedWorld) return;
            Time.timeScale = previousTimeScale;
            foreach (var item in controls) if (item.Key) item.Key.enabled = item.Value;
            controls.Clear(); pausedWorld = false;
            Cursor.lockState = previousLock; Cursor.visible = previousCursor;
        }

        void OnGUI()
        {
            if (!showHud || !encounter || !encounter.enableWaves || VolcanoCinematic.IsPlayingAny ||
                (rescueFlow && rescueFlow.State != RescueGameFlow.GameState.Playing)) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
                body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                centered = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
                button = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
                title.font = button.font = Ashfall.UI.AshfallTypography.Semibold;
                body.font = centered.font = Ashfall.UI.AshfallTypography.Regular;
                title.normal.textColor = body.normal.textColor = centered.normal.textColor = Color.white;
            }
            var oldMatrix = GUI.matrix; var oldColor = GUI.color; int oldDepth = GUI.depth;
            float scale = Mathf.Clamp(Screen.height / 900f, .65f, 1.8f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            if (IsFinished)
            {
                GUI.depth = -200;
                GUI.color = new Color(.018f, .023f, .03f, .96f);
                GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
                float panelWidth = Mathf.Min(620, width - 36), x = (width - panelWidth) * .5f, y = height * .5f - 170;
                GUI.color = State == MissionState.Won ? new Color(.3f, 1f, .63f) : new Color(1f, .35f, .13f);
                GUI.DrawTexture(new Rect(x, y, panelWidth, 4), Texture2D.whiteTexture);
                GUI.Label(new Rect(x, y + 28, panelWidth, 45), State == MissionState.Won ? "EVACUATION COMPLETE · AREA SECURE" : "MISSION FAILED", centered);
                GUI.color = Color.white;
                int saved = 0, lost = 0;
                foreach (var house in BurnableBuilding.ActiveBuildings) if (house) { if (house.IsBurnedOut) lost++; else saved++; }
                string outcome = State == MissionState.Won ? "7 people safe. Houses saved: " + saved + " · Houses lost: " + lost : "You ran out of health. Avoid lava and stop the spirits.";
                GUI.Label(new Rect(x + 20, y + 90, panelWidth - 40, 60), outcome, centered);
                GUI.Label(new Rect(x, y + 157, panelWidth, 32), "Spirits defeated: " + encounter.DefeatedCount + " / " + encounter.PlannedCount +
                    "    •    Wave: " + encounter.WaveNumber + " / " + encounter.WaveCount, centered);
                float gap = 14, bw = (panelWidth - gap) * .5f;
                if (GUI.Button(new Rect(x, y + 225, bw, 54), "TRY AGAIN", button)) RestartMission();
                if (GUI.Button(new Rect(x + bw + gap, y + 225, bw, 54), "MAIN MENU", button)) ReturnToMenu();
            }
            else if (State != MissionState.WaitingForEruption)
            {
                float x = Mathf.Max(18, width - 334), y = width >= 720 ? 282 : 470;
                GUI.color = new Color(.07f, .035f, .025f, .93f);
                GUI.DrawTexture(new Rect(x, y, 316, 132), Texture2D.whiteTexture);
                GUI.color = new Color(1f, .64f, .22f);
                GUI.Label(new Rect(x + 12, y + 9, 292, 28), State == MissionState.ReturnToTruck ? "FINAL STEP · RETURN TO TRUCK" :
                    "SPIRIT WAVE  " + Mathf.Max(1, encounter.WaveNumber) + " / " + encounter.WaveCount, title);
                GUI.color = Color.white;
                string instruction = State == MissionState.ReturnToTruck ? "Retract the hose and enter the truck at the driver's door." :
                    encounter.IsCleared ? "Spirits defeated. Extinguish the remaining fires!" :
                    encounter.AllWavesReleased ? "Final wave released. Defeat the remaining spirits!" :
                    encounter.SpawnedInWave >= encounter.CurrentWaveSize ? "Next wave: " + Mathf.CeilToInt(encounter.NextWaveIn) + " sec" : "More spirits are emerging from the lava.";
                GUI.Label(new Rect(x + 12, y + 43, 292, 45), instruction, body);
                GUI.Label(new Rect(x + 12, y + 95, 292, 25), "Active: " + encounter.AliveCount + "    Defeated: " + encounter.DefeatedCount + " / " + encounter.PlannedCount, body);
                if (State == MissionState.ReturnToTruck && player && player.drive && player.viewCamera)
                {
                    Vector3 screen = player.viewCamera.WorldToScreenPoint(player.drive.transform.position + Vector3.up * 3f);
                    if (screen.z > 0f)
                        GUI.Label(new Rect(Mathf.Clamp(screen.x / scale - 100, 10, width - 210),
                            Mathf.Clamp((Screen.height - screen.y) / scale - 35, 90, height - 130), 200, 65), "FIRE TRUCK\nComplete the mission", centered);
                }
            }
            GUI.matrix = oldMatrix; GUI.color = oldColor; GUI.depth = oldDepth;
        }
    }
}
