using UnityEngine;
using UnityEngine.AI;

namespace GameJam.FireRescue
{
    public sealed partial class EvacuationMission
    {
        [Header("Small group rescue")]
        public bool groupRescue = true;
        public Transform assemblyPoint;
        public Transform[] assemblySlots = new Transform[0];
        [Min(1f)] public float groupInteractionDistance = 6f;
        [Min(1f)] public float assemblyInteractionDistance = 10f;
        [Min(1f)] public float assemblyGroupDistance = 17f;
        public int ActiveGroup { get; private set; } = -1;
        public bool EscortStarted { get; private set; }
        public int GroupsDelivered { get; private set; }
        public RescueDialogue dialogue;
        public const float AssemblyExteriorReach = 3f;
        AssemblyPointMarker assemblyMarker;

        public int AssembledCount
        {
            get { int n = 0; if (residents != null) foreach (var r in residents) if (r && r.IsAtAssembly) n++; return n; }
        }
        public int FollowingCount
        {
            get { int n = 0; if (residents != null) foreach (var r in residents) if (r && r.IsCalled && !r.IsDead && !r.IsSafe && !r.IsAtAssembly && !r.IsMovingToAssembly) n++; return n; }
        }
        public bool AllAtAssembly => TotalCount > 0 && AssembledCount > 0 && AssembledCount + DeadCount + RescuedCount == TotalCount;
        public bool IsDepositing
        {
            get { if (residents != null) foreach (var r in residents) if (r && r.IsMovingToAssembly) return true; return false; }
        }
        public float AssemblyBoundaryRadius => assemblyMarker && assemblyMarker.GroundRadius > 0f ?
            assemblyMarker.GroundRadius : Mathf.Max(0f, assemblyInteractionDistance - 1f); // Legacy reach included a one-metre margin.
        public bool PlayerAtAssembly
        {
            get
            {
                if (!PlayerTransform || !assemblyPoint) return false;
                Vector3 position = PlayerTransform.position;
                float bodyRadius = 0f;
                if (player.playerBody)
                {
                    var body = player.playerBody;
                    Vector3 scale = body.transform.lossyScale;
                    position = body.transform.TransformPoint(body.center);
                    bodyRadius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                }
                // Allow a clear gap outside the yellow markings, measured from the
                // scaled player's body edge rather than the centre between their feet.
                return EvacuationResident.FlatDistance(position, assemblyPoint.position) <= AssemblyBoundaryRadius + bodyRadius + AssemblyExteriorReach + .0001f &&
                    Mathf.Abs(PlayerTransform.position.y - assemblyPoint.position.y) < 3.5f;
            }
        }
        public float GroupProgress => TotalCount == 0 ? 0f : EscortStarted || IsComplete ?
            .5f + .5f * RescuedCount / TotalCount : .5f * AssembledCount / TotalCount;
        public string InteractLabel => player && player.contextualInteractions ? player.contextualInteractions.Label(InteractionInput.Interact) : "E";
        public int NextRecruitGroup
        {
            get
            {
                if (ActiveGroup >= 0 || EscortStarted || IsComplete || residents == null) return -1;
                int next = int.MaxValue;
                foreach (var r in residents)
                    if (r && !r.IsCalled && !r.IsDead && !r.IsSafe) next = Mathf.Min(next, r.rescueGroup);
                return next == int.MaxValue ? -1 : next;
            }
        }
        public string GroupObjective => IsComplete ? "Everyone is sheltered" : EscortStarted ? "Lead everyone to the shelter" :
            AllAtAssembly ? "Everyone is assembled" : ActiveGroup >= 0 ? "Bring the group to the assembly point" :
            IsDepositing && NextRecruitGroup < 0 ? "The group is assembling" : "Gather the townspeople";
        public string GroupDetail => IsComplete ? "Everyone is safe." : EscortStarted ? "Follow the green marker. Wait for everyone to enter the shelter." :
            AllAtAssembly ? "Press " + InteractLabel + " at the assembly point to gather everyone, then follow the green marker to the shelter." :
            ActiveGroup >= 0 ? "Come within 3 m of the yellow boundary. Press " + InteractLabel + " to send the group in, then find the next group." :
            IsDepositing ? NextRecruitGroup >= 0 ? "The group is heading to its places. Find the next group marked (!)." : "The last group is heading to its places. Gather everyone here when they arrive." :
            "Approach the group marked (!) and press " + InteractLabel + " to recruit them. Bring them to the yellow assembly point.";
        public string GroupStatus => EscortStarted || IsComplete ? "Sheltered: " + RescuedCount + " / " + TotalCount :
            "Assembled: " + AssembledCount + " / " + TotalCount + "  ·  Following: " + FollowingCount;
        public string GroupHint => player && player.IsDriving ? InteractLabel + " · Exit the truck" : IsComplete ? "Everyone is safe" : EscortStarted ? "Follow the green marker" :
            AllAtAssembly ? PlayerAtAssembly ? InteractLabel + " · Lead everyone to the shelter" : "Return to the assembly point" :
            ActiveGroup >= 0 ?
                !PlayerAtAssembly ? "Come within 3 m of the yellow boundary" : CanDepositGroup ? InteractLabel + " · Send the group in" : "Help the group before leaving them here" :
                IsDepositing && NextRecruitGroup < 0 ? "The last group is taking its places" :
                InteractLabel + " · Recruit the group marked (!)";

        void InitializeGroups()
        {
            if (!groupRescue) return;
            assemblyMarker = GetComponentInChildren<AssemblyPointMarker>(true);
            villageWideCall = false;
            callAllAlarmedVillagers = false;
            callKey = UnityEngine.InputSystem.Key.None;
            alarmTriggersPanic = true;
            if (!dialogue) dialogue = GetComponent<RescueDialogue>();
            if (dialogue) dialogue.mission = this;
        }

        bool CanAct => groupRescue && IsActive && !IsComplete && PlayerTransform && player &&
            !player.IsDriving && !player.GameplaySuspended && !player.InteractionBusy && Time.timeScale > 0f;

        public bool CanRecruitGroup(EvacuationResident resident)
        {
            if (!(CanAct && ActiveGroup < 0 && !EscortStarted && resident && resident.mission == this &&
                resident.rescueGroup == NextRecruitGroup && !resident.IsCalled && !resident.IsDead && !resident.IsBurning && resident.State == EvacuationResident.ResidentState.Scattering &&
                EvacuationResident.FlatDistance(PlayerTransform.position, resident.transform.position) <= groupInteractionDistance &&
                Mathf.Abs(PlayerTransform.position.y - resident.transform.position.y) < 3.5f)) return false;
            foreach (var r in residents)
                if (r && r.rescueGroup == resident.rescueGroup && !r.IsDead && r.IsBurning) return false;
            return true;
        }

        public bool TryRecruitGroup(EvacuationResident resident)
        {
            if (!CanRecruitGroup(resident)) return false;
            // The action API also checks visibility, so callers cannot recruit through a wall.
            Vector3 from = player.viewCamera ? player.viewCamera.transform.position : PlayerTransform.position + Vector3.up * 2f;
            Vector3 to = resident.transform.position + Vector3.up * 2.4f;
            if (Physics.Linecast(from, to, out var obstruction, ~0, QueryTriggerInteraction.Ignore) &&
                !obstruction.transform.IsChildOf(resident.transform) && !obstruction.transform.IsChildOf(PlayerTransform)) return false;
            int count = 0;
            foreach (var r in residents)
                if (r && r.rescueGroup == resident.rescueGroup && !r.IsDead && !r.IsSafe && r.CallOutside()) count++;
            if (count == 0) return false;
            ActiveGroup = resident.rescueGroup;
            trail.Clear(); trail.Add(PlayerTransform.position);
            Speak("recruit_" + Mathf.Clamp(GroupsDelivered, 0, 2), null, true);
            return true;
        }

        public bool CanDepositGroup
        {
            get
            {
                if (!CanAct || !PlayerAtAssembly || ActiveGroup < 0 || EscortStarted) return false;
                bool found = false;
                foreach (var r in residents)
                {
                    if (!r || r.IsDead || r.IsSafe || r.rescueGroup != ActiveGroup) continue;
                    if (!r.IsCalled || r.IsBurning || !AssemblySlot(r)) return false;
                    found = true;
                }
                return found;
            }
        }

        public bool TryAssemblyInteraction()
        {
            if (!CanAct || !PlayerAtAssembly) return false;
            if (AllAtAssembly && !EscortStarted)
            {
                EscortStarted = true; ActiveGroup = -1;
                trail.Clear(); trail.Add(PlayerTransform.position);
                foreach (var r in residents) if (r && r.IsAtAssembly) r.ResumeFollowing();
                Speak("depart", null, true);
                return true;
            }
            if (!CanDepositGroup) return false;
            foreach (var r in residents)
                if (r && !r.IsDead && !r.IsSafe && r.rescueGroup == ActiveGroup)
                    r.WaitAtAssembly(AssemblySlot(r));
            // Hand off the group immediately. Arrival is still counted at each real waiting place,
            // but the player is free to collect the next group while these residents walk in.
            ActiveGroup = -1;
            GroupsDelivered++;
            return true;
        }

        public Transform AssemblySlot(EvacuationResident resident)
        {
            if (resident && resident.formationIndex >= 0 && resident.formationIndex < assemblySlots.Length && assemblySlots[resident.formationIndex])
                return assemblySlots[resident.formationIndex];
            return assemblyPoint;
        }

        void TickGroups()
        {
            if (ActiveGroup >= 0)
            {
                bool any = false;
                foreach (var r in residents)
                {
                    if (!r || r.IsDead || r.IsSafe || r.rescueGroup != ActiveGroup) continue;
                    any = true;
                }
                if (!any) ActiveGroup = -1;
            }
        }

        public void Speak(string id, Transform speaker = null, bool replace = false)
        {
            if (dialogue) dialogue.Enqueue(id, replace);
        }
        public void ShowDialogue(string text, float duration) => Say(text, duration);

        public int FollowIndex(EvacuationResident resident)
        {
            int index = 0;
            foreach (var r in residents)
            {
                if (r == resident) return index;
                if (r && r.IsCalled && !r.IsDead && !r.IsSafe && !r.IsAtAssembly && !r.IsMovingToAssembly) index++;
            }
            return index;
        }

        public Transform GroupNavigationTarget(out string label)
        {
            if (EscortStarted)
            { label = "SHELTER | LEAD THE GROUP"; return ShelterOpened ? shelterEntrance : shelterApproach; }
            if (ActiveGroup >= 0 || AllAtAssembly || IsDepositing && NextRecruitGroup < 0)
            { label = AllAtAssembly ? "ASSEMBLY POINT | " + InteractLabel + ": LEAD EVERYONE TO SHELTER" : "ASSEMBLY POINT | " + AssembledCount + "/" + TotalCount; return assemblyPoint; }
            Transform nearest = null; float distance = float.PositiveInfinity;
            foreach (var r in residents)
            {
                if (!r || r.IsDead || r.IsCalled || r.IsSafe || r.rescueGroup != NextRecruitGroup) continue;
                float d = EvacuationResident.FlatDistance(r.transform.position, PlayerTransform.position);
                if (d >= distance) continue;
                distance = d; nearest = r.panicGroupAnchor ? r.panicGroupAnchor : r.transform;
            }
            label = "GROUP MARKED (!) | " + InteractLabel + ": RECRUIT";
            return nearest;
        }

        void DrawGroupMarkers(float scale, float width, float height)
        {
            if (ActiveGroup < 0 && !AllAtAssembly && !EscortStarted) return;
            var target = GroupNavigationTarget(out string label);
            if (target) Marker(target.position + Vector3.up * 3f, label, EscortStarted ? new Color(.35f, 1, .7f) : new Color(1f, .78f, .25f), scale, width, height);
        }
    }
}
