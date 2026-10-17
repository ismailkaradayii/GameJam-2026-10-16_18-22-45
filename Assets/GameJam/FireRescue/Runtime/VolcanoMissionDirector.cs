using UnityEngine;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-60)]
    public sealed class VolcanoMissionDirector : MonoBehaviour
    {
        public enum MissionPhase { Evacuate, Refill, Warning, Cinematic, Defend }
        public EvacuationMission evacuation;
        public VolcanoEruptionSequence sequence;
        public FireRescuePlayer player;
        public FireHoseSystem tank;
        public VolcanoCinematic cinematic;
        public MissionPhase Phase { get; private set; }
        public bool PreparationComplete => evacuation && evacuation.IsComplete && Phase >= MissionPhase.Warning;
        GUIStyle title, body;
        RescueHUD rescueHud;

        void Awake()
        {
            rescueHud = FindFirstObjectByType<RescueHUD>();
            if (!evacuation) evacuation = FindFirstObjectByType<EvacuationMission>();
            if (!sequence) sequence = FindFirstObjectByType<VolcanoEruptionSequence>();
            if (!player) player = FindFirstObjectByType<FireRescuePlayer>();
            if (!tank && player) tank = player.hose;
            if (sequence) { sequence.missionDirector = this; sequence.autoErupt = false; sequence.showHud = false; }
            if (tank) tank.fillingUnlocked = false;
        }
        void Update() => TickMission();
        public void TickMission()
        {
            if (!evacuation || !sequence || !tank || Time.timeScale <= 0f || (player && player.GameplaySuspended)) return;
            if (Phase == MissionPhase.Evacuate && evacuation.IsComplete)
            {
                Phase = MissionPhase.Refill;
                tank.fillingUnlocked = true;
                evacuation.showHud = false;
            }
            if (Phase == MissionPhase.Refill && tank.Tank01 >= .999f)
            {
                Phase = MissionPhase.Warning;
                sequence.BeginEruption();
            }
            if (sequence.Stage == VolcanoEruptionSequence.EruptionStage.Erupting) Phase = MissionPhase.Cinematic;
            if (sequence.Stage == VolcanoEruptionSequence.EruptionStage.Flowing) Phase = MissionPhase.Defend;
        }
        void OnGUI()
        {
            if (VolcanoCinematic.IsPlayingAny || !tank || !evacuation || (rescueHud && rescueHud.enabled)) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                body = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
                title.normal.textColor = body.normal.textColor = Color.white;
            }
            float scale = Mathf.Clamp(Screen.height / 900f, .75f, 1.5f);
            var matrix = GUI.matrix; var color = GUI.color;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float w = Screen.width / scale, x = w >= 720 ? w - 334 : 18, y = w >= 720 ? 18 : 210;
            string heading, detail;
            if (Phase == MissionPhase.Evacuate)
            { heading = "1 / 4 · HALKI KURTAR"; detail = "Q ile evlerdeki insanlara seslen.\nYedi kişiyi sığınağa götür."; }
            else if (Phase == MissionPhase.Refill)
            { heading = "2 / 4 · SU DEPOSUNU DOLDUR"; detail = "Tahliye: 7 / 7 ✓   Su: %" + Mathf.FloorToInt(tank.Tank01 * 100) + "\nE: Arka hortumu göle bırak · P: Pompa\nDepo dolunca hortumu R ile topla."; }
            else if (Phase == MissionPhase.Warning || Phase == MissionPhase.Cinematic)
            { heading = "3 / 4 · VOLKAN UYANDI"; detail = "Yer sarsılıyor…\nPatlama başlamak üzere!"; }
            else
            {
                int burning = 0, lost = 0; float remaining = float.PositiveInfinity;
                foreach (var house in BurnableBuilding.ActiveBuildings)
                    if (house) { if (house.IsBurnedOut) lost++; else if (house.IsBurning) { burning++; remaining = Mathf.Min(remaining, house.SecondsUntilCollapse); } }
                heading = "4 / 4 · KASABAYI SAVUN";
                detail = "Yanan ev: " + burning + "   Kül olan: " + lost + "\n" +
                    (burning > 0 ? "En acil ev: " + Mathf.CeilToInt(remaining) + " sn!\n" : "Evleri ve yaklaşan lavları suyla soğut.\n") + "F: İtfaiye · Sağ tık: Su · Sol tık: Ateş";
            }
            GUI.color = new Color(.035f, .055f, .07f, .94f); GUI.DrawTexture(new Rect(x, y, 316, 108), Texture2D.whiteTexture);
            GUI.color = new Color(1, .76f, .36f); GUI.Label(new Rect(x + 12, y + 8, 292, 25), heading, title);
            GUI.color = Color.white; GUI.Label(new Rect(x + 12, y + 36, 292, 66), detail, body);
            GUI.matrix = matrix; GUI.color = color;
        }
    }
}
