using System;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>The authored mission order and the independent preparation deadline.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-60)]
    public sealed class VolcanoMissionDirector : MonoBehaviour
    {
        public enum MissionPhase { Evacuate, Refill, Warning, Cinematic, Defend, Intro, Weapon, WarnVillage, Truck, Ready, Window }
        public enum FlowState { Intro, Preparation, VolcanoCinematic, LavaApproaching, FireSpiritCinematic, VillageDefense, Victory, Defeat }
        public EvacuationMission evacuation;
        public VolcanoEruptionSequence sequence;
        public FireRescuePlayer player;
        public FireHoseSystem tank;
        public VolcanoCinematic cinematic;
        public LavaCreatureEncounter encounter;
        public RescueGameFlow gameFlow;
        [Header("Authored starting room and destinations")]
        public Transform wakePose;
        public Transform standingSpawn;
        public Transform houseExit;
        public BoxCollider startingHouse;
        public WeaponPickup weaponPickup;
        public LakeFillStation fillStation;
        public VillageAlarmButton villageAlarm;
        public VolcanoStorySequence story;
        [Header("Jam timing")]
        [Min(1f)] public float preparationSeconds = 120f;
        [Min(0f)] public float spiritRevealDelay = 1f;
        [Min(1f)] public float exitRadius = 9f;
        [Tooltip("Skip the warning only in scenes without an authored village alarm or bell interaction.")]
        public bool skipVillageWarning = true;
        public MissionPhase Phase { get; private set; } = MissionPhase.Intro;
        public FlowState State { get; private set; } = FlowState.Intro;
        public float PreparationRemaining { get; private set; }
        public bool EruptionTriggered { get; private set; }
        public bool LeftStartingHouse { get; private set; }
        public bool PreparationComplete { get; private set; }
        public bool WarningCompleted { get; private set; }
        public int SavedAtEruption { get; private set; }
        public float WaterAtEruption { get; private set; }
        public event Action ObjectiveChanged;
        bool spiritRevealPlayed, began;
        float firstSpiritSeenAt = -1f;

        void Awake()
        {
            if (!evacuation) evacuation = FindFirstObjectByType<EvacuationMission>();
            if (!sequence) sequence = FindFirstObjectByType<VolcanoEruptionSequence>();
            if (!player) player = FindFirstObjectByType<FireRescuePlayer>();
            if (!gameFlow) gameFlow = FindFirstObjectByType<RescueGameFlow>();
            if (!encounter) encounter = FindFirstObjectByType<LavaCreatureEncounter>();
            if (!tank && player) tank = player.hose;
            if (!story) story = GetComponent<VolcanoStorySequence>();
            PreparationRemaining = Mathf.Max(1f, preparationSeconds);
            if (sequence) { sequence.missionDirector = this; sequence.autoErupt = false; sequence.showHud = false; }
            if (evacuation) { evacuation.startAutomatically = false; evacuation.showHud = false; }
            if (tank) tank.fillingUnlocked = true;
            if (player) player.mission = this;
        }
        void Start()
        {
            began = true;
            if (player && standingSpawn) player.PlaceOnFoot(standingSpawn.position, standingSpawn.eulerAngles.y);
            if (cinematic && wakePose) cinematic.BeginIntro(wakePose);
            else CompleteIntro();
        }
        void Update() => TickMission();
        public void TickMission()
        {
            if (!began || !player || !sequence || !tank) return;
            if (gameFlow && (gameFlow.State == RescueGameFlow.GameState.Won || gameFlow.State == RescueGameFlow.GameState.Lost))
            { State = gameFlow.State == RescueGameFlow.GameState.Won ? FlowState.Victory : FlowState.Defeat; return; }
            if (Time.timeScale <= 0f || (gameFlow && gameFlow.BlocksGameplay)) return;
            if (State == FlowState.Intro)
            {
                if (!cinematic || !cinematic.IsPlaying) CompleteIntro();
                return;
            }
            if (!EruptionTriggered)
            {
                PreparationRemaining = Mathf.Max(0f, PreparationRemaining - Time.deltaTime);
                if (PreparationRemaining <= 2f) sequence.BeginExplosionAudio();
                if (PreparationRemaining <= 0f) TriggerDeadline();
                else TickPreparation();
                return;
            }
            if (cinematic && cinematic.IsPlaying)
            {
                State = cinematic.Kind == VolcanoCinematic.ShotKind.Spirits ? FlowState.FireSpiritCinematic : FlowState.VolcanoCinematic;
                return;
            }
            State = FlowState.LavaApproaching;
            if (encounter && encounter.SpawnedCount > 0)
            {
                if (!spiritRevealPlayed && encounter.LivingCreatures.Count > 0)
                {
                    if (firstSpiritSeenAt < 0) firstSpiritSeenAt = Time.time;
                    // Let the first spirit rise clear of the lava before applying slow motion.
                    if (Time.time - firstSpiritSeenAt < spiritRevealDelay) { SetPhase(MissionPhase.Defend); return; }
                    spiritRevealPlayed = true;
                    var spirit = encounter.LivingCreatures[0];
                    if (cinematic && spirit) cinematic.BeginSpirits(spirit.transform);
                    State = FlowState.FireSpiritCinematic;
                }
                else State = FlowState.VillageDefense;
            }
            SetPhase(MissionPhase.Defend);
        }
        void StartPreparation() { State = FlowState.Preparation; SetPhase(MissionPhase.Weapon); }
        void CompleteIntro()
        {
            if (story && story.isActiveAndEnabled)
            {
                if (!story.OpeningStarted && story.BeginOpening()) SetPhase(MissionPhase.Window);
                if (story.IsOpening) return;
            }
            StartPreparation();
        }
        void TickPreparation()
        {
            if (houseExit && player.HasPistol && !LeftStartingHouse)
            {
                Vector3 feet = player.transform.position;
                bool outside = !startingHouse || !startingHouse.bounds.Contains(feet + Vector3.up * 2f);
                if (outside || EvacuationResident.FlatDistance(feet, houseExit.position) <= exitRadius &&
                    Mathf.Abs(feet.y - houseExit.position.y) < 3f && Vector3.Dot(feet - houseExit.position, houseExit.forward) >= -.5f) LeftStartingHouse = true;
            }
            if (Phase == MissionPhase.Weapon)
            {
                if (!player.HasPistol || (!villageAlarm && !LeftStartingHouse)) return;
                SetPhase(MissionPhase.WarnVillage);
                if (!villageAlarm && skipVillageWarning) CompleteVillageWarning();
            }
            if (Phase == MissionPhase.WarnVillage && villageAlarm && villageAlarm.Activated) CompleteVillageWarning();
            if (Phase == MissionPhase.Evacuate && evacuation && evacuation.IsComplete) SetPhase(MissionPhase.Truck);
            if (Phase == MissionPhase.Truck && player.IsDriving) SetPhase(MissionPhase.Refill);
            if (Phase == MissionPhase.Refill && tank.Tank01 >= .999f)
            { PreparationComplete = true; SetPhase(MissionPhase.Ready); }
        }
        // Future bell interaction calls this method; no church destination is displayed while skipped.
        public void CompleteVillageWarning()
        {
            if (WarningCompleted || Phase != MissionPhase.WarnVillage || (villageAlarm && !villageAlarm.Activated)) return;
            WarningCompleted = true;
            if (evacuation) evacuation.BeginMission();
            SetPhase(MissionPhase.Evacuate);
        }
        public void TriggerDeadline()
        {
            if (EruptionTriggered) return;
            if (story) story.CancelOpening();
            EruptionTriggered = true; PreparationRemaining = 0f;
            SavedAtEruption = evacuation ? evacuation.RescuedCount : 0;
            WaterAtEruption = tank ? tank.TankLitres : 0;
            // Unfinished preparations remain usable. The deadline never waits on a mission.
            if (evacuation && !evacuation.IsActive && !evacuation.IsComplete) evacuation.BeginMission();
            State = FlowState.VolcanoCinematic; SetPhase(MissionPhase.Cinematic);
            sequence.BeginEruption(true);
            sequence.BeginExplosionAudio();
            sequence.NotifyExplosionAudioEvent();
            if (cinematic) cinematic.BeginShot();
        }
        void SetPhase(MissionPhase next)
        {
            if (Phase == next) return;
            Phase = next; ObjectiveChanged?.Invoke();
        }
        public string ObjectiveTitle => EruptionTriggered ? "DEFEND THE VILLAGE" : Phase switch
        {
            MissionPhase.Intro => "ASHFALL",
            MissionPhase.Window => "PENCEREDEN YANARDAĞA BAK",
            MissionPhase.Weapon => villageAlarm ? "TAKE YOUR WEAPON" : "TAKE YOUR WEAPON AND LEAVE THE HOUSE",
            MissionPhase.WarnVillage => villageAlarm ? "ACTIVATE THE VILLAGE ALARM" : "WARN THE VILLAGE",
            MissionPhase.Evacuate => "EVACUATE THE VILLAGERS",
            MissionPhase.Truck => "PREPARE THE FIRE TRUCK",
            MissionPhase.Refill => "DRIVE TO THE LAKE",
            MissionPhase.Ready => story && story.defensePosition ? "LAVIN GELECEĞİ TARAFTA HAZIR BEKLE" : "READY FOR THE ERUPTION",
            _ => "READY FOR THE ERUPTION"
        };
        public string ObjectiveDetail
        {
            get
            {
                if (EruptionTriggered)
                {
                    if (!player.HasPistol) return "Collect the pistol from your bedroom. The village needs you!";
                    if (tank.Tank01 < .05f) return "Shoot the fire spirits. Refill the truck at the BLUE lake marker to fight fires.";
                    return "Shoot the fire spirits. Use the truck's water cannon on burning houses.";
                }
                return Phase switch
                {
                    MissionPhase.Window => "Yatak odasındaki işaretli pencereye yaklaş. Yanardağda bir terslik var...",
                    MissionPhase.Weapon => player.HasPistol ? "Pistol in slot 1. Follow the marker to the front door. E opens doors." : "Pick up the pistol on the bedside cabinet. Press E when nearby.",
                    MissionPhase.WarnVillage => villageAlarm ? "İlk evde kırmızı ışığın altındaki alarm butonuna yaklaş. " +
                        (player.contextualInteractions ? player.contextualInteractions.Label(InteractionInput.Interact) : "E") + " ile bas ve köyü uyar." : "Warn the village to begin the evacuation.",
                    MissionPhase.Evacuate => "Alarmı duyan köylüler dışarı çıkıyor. Q ile 'Buraya gelin!' diye seslen, ardından sığınağa götür.",
                    MissionPhase.Truck => "Find the truck behind your house. Approach the driver's door and press " +
                        (player.contextualInteractions ? player.contextualInteractions.Label(InteractionInput.Interact) : "E") + ".",
                    MissionPhase.Refill => "Park in the BLUE lake ring. Press E to connect the intake and fill the tank.",
                    MissionPhase.Ready => story && story.defensePosition ? "Yolu takip ederek kasabaya dön. İşaretli savunma noktasında lavın gelişine karşı hazır bekle." : "Return to the village. Keep the pistol and water tank ready.",
                    _ => "Wake up. The volcano is stirring."
                };
            }
        }
    }
}
