using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>A guided connection point for the existing physical intake/pump/tank.</summary>
    [DisallowMultipleComponent]
    public sealed class LakeFillStation : MonoBehaviour
    {
        public FireRescuePlayer player;
        public WaterSourceVolume water;
        public Transform intakePoint;
        [Min(2f)] public float parkingRadius = 13f;
        [Min(.1f)] public float maximumFillSpeedKph = 2f;
        public bool IsFilling { get; private set; }
        public bool InRange => player && player.drive &&
            EvacuationResident.FlatDistance(player.drive.transform.position, transform.position) <= parkingRadius &&
            Mathf.Abs(player.drive.transform.position.y - transform.position.y) < 5f;
        public bool CanFill => InRange && player.IsDriving && !player.GameplaySuspended && player.drive.SpeedKph <= maximumFillSpeedKph &&
            player.hose && player.hose.Tank01 < .999f && water && intakePoint && water.ContainsIntake(intakePoint.position) && player.hose.CanReach(intakePoint.position);
        public bool Interact()
        {
            if (IsFilling) { Cancel(); return true; }
            if (!CanFill) return false;
            player.hose.Drop(intakePoint.position);
            if (!player.hose.PumpEnabled) player.hose.TogglePump();
            IsFilling = player.hose.IsPumping;
            if (!IsFilling) player.hose.Retract();
            return IsFilling;
        }
        void Update()
        {
            if (!IsFilling || !player || !player.hose) return;
            if (!InRange || player.hose.Tank01 >= 1f || !player.IsDriving) { Cancel(); return; }
            player.drive.SetInput(0, 0, true);
            bool shouldPump = !player.GameplaySuspended;
            if (player.hose.PumpEnabled != shouldPump) player.hose.TogglePump();
        }
        public void Cancel()
        {
            if (IsFilling && player && player.hose) player.hose.Retract();
            IsFilling = false;
        }
        void OnDisable() => Cancel();
    }
}
