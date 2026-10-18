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
        bool readyAnnounced;
        bool depositingGroup;

        public int AssembledCount
        {
            get { int n = 0; if (residents != null) foreach (var r in residents) if (r && r.IsAtAssembly) n++; return n; }
        }
        public int FollowingCount
        {
            get { int n = 0; if (residents != null) foreach (var r in residents) if (r && r.IsCalled && !r.IsDead && !r.IsSafe && !r.IsAtAssembly && !r.IsMovingToAssembly) n++; return n; }
        }
        public bool AllAtAssembly => TotalCount > 0 && AssembledCount > 0 && AssembledCount + DeadCount + RescuedCount == TotalCount;
        public bool IsDepositing => depositingGroup;
        public bool PlayerAtAssembly => PlayerTransform && assemblyPoint &&
            EvacuationResident.FlatDistance(PlayerTransform.position, assemblyPoint.position) <= assemblyInteractionDistance &&
            Mathf.Abs(PlayerTransform.position.y - assemblyPoint.position.y) < 3.5f;
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
        public string GroupObjective => IsComplete ? "Herkes sığınakta" : EscortStarted ? "İnsanları sığınağa götür" :
            AllAtAssembly ? "Herkes toplandı" : depositingGroup ? "Grup alana yerleşiyor" : ActiveGroup >= 0 ? "Grubu toplanma alanına getir" : "İnsanları topla";
        public string GroupDetail => IsComplete ? "Tüm insanlar güvende." : EscortStarted ? "Yeşil işareti takip et. Herkesin sığınağa girmesini bekle." :
            AllAtAssembly ? "Toplanma alanında " + InteractLabel + " ile herkesi yanına al. Ardından yeşil işareti takip ederek sığınağa götür." :
            depositingGroup ? "Grup yerleşirken bekle. Sonra sıradaki ünlem işaretli gruba git." :
            ActiveGroup >= 0 ? "Grubu sarı halkaya getir. Alanda " + InteractLabel + " ile burada bırak, sonra diğer grubu al." :
            "Ünlem (!) işaretli gruba yaklaş ve " + InteractLabel + " ile yanına al. Ardından sarı toplanma alanına getir.";
        public string GroupStatus => EscortStarted || IsComplete ? "Sığınakta: " + RescuedCount + " / " + TotalCount :
            "Toplanan: " + AssembledCount + " / " + TotalCount + "  ·  Yanında: " + FollowingCount;
        public string GroupHint => player && player.IsDriving ? InteractLabel + " · Araçtan in" : IsComplete ? "Tüm insanlar güvende" : EscortStarted ? "Yeşil işareti takip et" :
            AllAtAssembly ? PlayerAtAssembly ? InteractLabel + " · Herkesi yanına al" : "Toplanma alanına dön" :
            depositingGroup ? "Grubun yerleşmesini bekle" : ActiveGroup >= 0 ?
                !PlayerAtAssembly ? "Grubu sarı halkaya getir" : CanDepositGroup ? InteractLabel + " · Grubu burada bırak" : "Grubun yanına gelmesini bekle" :
                InteractLabel + " · Ünlem işaretli grubu yanına al";

        void InitializeGroups()
        {
            if (!groupRescue) return;
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
            depositingGroup = false;
            trail.Clear(); trail.Add(PlayerTransform.position);
            Speak("recruit_" + Mathf.Clamp(GroupsDelivered, 0, 2), null, true);
            Speak("reply_" + Mathf.Clamp(resident.rescueGroup, 0, 2), resident.transform);
            return true;
        }

        public bool CanDepositGroup
        {
            get
            {
                if (!CanAct || !PlayerAtAssembly || ActiveGroup < 0 || depositingGroup || EscortStarted) return false;
                bool found = false;
                foreach (var r in residents)
                {
                    if (!r || r.IsDead || r.IsSafe || r.rescueGroup != ActiveGroup) continue;
                    if (!r.IsCalled || r.IsBurning || EvacuationResident.FlatDistance(r.transform.position, assemblyPoint.position) > assemblyGroupDistance ||
                        Mathf.Abs(r.transform.position.y - assemblyPoint.position.y) > 3.5f) return false;
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
            depositingGroup = true;
            foreach (var r in residents)
                if (r && !r.IsDead && !r.IsSafe && r.rescueGroup == ActiveGroup)
                    r.WaitAtAssembly(AssemblySlot(r));
            Speak("wait_" + Mathf.Clamp(GroupsDelivered, 0, 2));
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
                bool any = false, arrived = depositingGroup;
                foreach (var r in residents)
                {
                    if (!r || r.IsDead || r.IsSafe || r.rescueGroup != ActiveGroup) continue;
                    any = true;
                    if (!r.IsAtAssembly) arrived = false;
                }
                if (!any || arrived) { ActiveGroup = -1; depositingGroup = false; if (any) GroupsDelivered++; }
            }
            if (!EscortStarted && AllAtAssembly && !readyAnnounced)
            { readyAnnounced = true; Speak("ready"); }
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
            { label = "SIĞINAK | GRUBU GÖTÜR"; return ShelterOpened ? shelterEntrance : shelterApproach; }
            if (ActiveGroup >= 0 || AllAtAssembly)
            { label = AllAtAssembly ? "TOPLANMA ALANI | " + InteractLabel + ": HERKESİ YANINA AL" : "TOPLANMA ALANI | " + AssembledCount + "/" + TotalCount; return assemblyPoint; }
            Transform nearest = null; float distance = float.PositiveInfinity;
            foreach (var r in residents)
            {
                if (!r || r.IsDead || r.IsCalled || r.IsSafe || r.rescueGroup != NextRecruitGroup) continue;
                float d = EvacuationResident.FlatDistance(r.transform.position, PlayerTransform.position);
                if (d >= distance) continue;
                distance = d; nearest = r.panicGroupAnchor ? r.panicGroupAnchor : r.transform;
            }
            label = "ÜNLEM (!) İŞARETLİ GRUP | " + InteractLabel + ": YANINA AL";
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
