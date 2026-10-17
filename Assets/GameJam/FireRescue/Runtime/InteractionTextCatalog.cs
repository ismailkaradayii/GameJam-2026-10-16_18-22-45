using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Shared presentation copy. It never changes mission state or invents an interaction.</summary>
    [CreateAssetMenu(menuName = "GameJam/Interaction text catalog")]
    public sealed class InteractionTextCatalog : ScriptableObject
    {
        public string takePistol = "Take pistol";
        public string pistolSlot = "Add to slot 1";
        public string openDoor = "Open door", closeDoor = "Close door";
        public string blockedDoor = "Door blocked. Step back.";
        public string leaveHouse = "Leave house", takePistolFirst = "Take the pistol before leaving";
        public string walkOutside = "Walk outside through the door";
        public string enterTruck = "Enter truck", exitTruck = "Exit truck";
        public string stopBeforeExit = "Slow below 20 km/h before exiting";
        public string drivingHint = "WASD to drive · {0} to spray water";
        public string clearExit = "Exit blocked. Park in an open area.";
        public string callVillagers = "Call villagers", leadVillager = "Lead villager to shelter";
        public string leadToShelter = "Lead to shelter", following = "Following you";
        public string callCoolingDown = "Rest your voice. You can call again shortly.";
        public string shelter = "Shelter", shelterAutomatic = "Villagers are rescued when they enter";
        public string fillTank = "Fill water tank", fillingTank = "Filling tank";
        public string disconnect = "Disconnect", stopBeforeFill = "Stop in the blue ring to refill";
        public string tankFull = "Tank full", returnToVillage = "Return to the village";
        public string intakeOutOfWater = "Hose intake is not in water";
        public string hoseTooFar = "Move the truck closer to the water";
        public string takeHose = "Take intake hose", dropHose = "Drop hose";
        public string putIntakeInWater = "Place the intake in water";
        public string retractHose = "Retract hose", enablePump = "Start pump", disablePump = "Stop pump";
        public string extinguish = "Extinguish fire", useWaterCannon = "Aim at the fire and hold to spray";
        public string pistolCollected = "Pistol collected", residentSaved = "+1 villager rescued!";
        public string objectiveComplete = "Objective complete!", fireExtinguished = "Fire extinguished!";
        public string activateAlarm = "Activate village alarm", alarmNeedsPistol = "Take the pistol first";
        public string alarmGetCloser = "Move closer to the button", alarmReloading = "Wait until reloading is complete";
        public string alarmActivated = "Village alerted!";
    }
}
