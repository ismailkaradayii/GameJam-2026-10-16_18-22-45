using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class GroupRescueChecks
{
    const string Pending = "GroupRescueChecks.Pending";
    static GroupRescueChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                EditorApplication.delayCall += () => new GameObject("Small group evacuation checks").AddComponent<GroupRescueProbe>().Begin();
            }
        };
    }
    [MenuItem("GameJam/Final Loop/Check small group rescue")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        Directory.CreateDirectory(GroupRescueSetup.Folder);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isDirty) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        File.WriteAllText(GroupRescueSetup.Folder + "/checks.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
}

[DefaultExecutionOrder(1000)]
public sealed class GroupRescueProbe : MonoBehaviour
{
    [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
    [Serializable] public sealed class Report
    {
        public string status = "RUNNING";
        public string method = "Saved-scene NPCs use their real agents, doors, animations, next-group exclamation marker, contextual E coordinator and shelter volume. The player is guided by a temporary NavMesh fixture. Boundary probes measure clearance from the actual controller bounds and both feet outside the authored yellow markings; E and Q are injected through the Input System. Opening cinematic and preparation deadline are suspended only in this test. No manual playthrough is claimed.";
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
        public int wallCrossings, rescued;
        public float elapsedSeconds;
    }
    readonly Report report = new Report();
    readonly Dictionary<EvacuationResident, Vector3> previous = new Dictionary<EvacuationResident, Vector3>();
    readonly Dictionary<EvacuationResident, Vector3> waiting = new Dictionary<EvacuationResident, Vector3>();
    readonly HashSet<EvacuationResident> displaced = new HashSet<EvacuationResident>();
    readonly List<string> voicedCues = new List<string>();
    EvacuationMission mission;
    VolcanoMissionDirector director;
    FireRescuePlayer player;
    NavMeshAgent leader;
    RescueGroupMarker marker;
    Keyboard testKeyboard, oldKeyboard;
    InputSettings.BackgroundBehavior oldInput;
    EditorWindow game;
    bool oldBackground, finished, reloadLocked;
    int phase, groupIndex, calledBefore, stagedBefore, spokenAtCompletion;
    int observedVoiceLines, pausedVoiceSample = -1;
    float phaseAt, began, nextSave;
    string pausedSubtitle;
    EvacuationResident[] depositingResidents;
    Vector3[] handoffPositions;
    float playerBodyRadius;
    Transform[] playerFeet;
    double deadline;

    public void Begin()
    {
        try
        {
            mission = Object.FindFirstObjectByType<EvacuationMission>(); player = mission.player;
            marker = Object.FindFirstObjectByType<RescueGroupMarker>();
            EditorApplication.LockReloadAssemblies(); reloadLocked = true;
            director = Object.FindFirstObjectByType<VolcanoMissionDirector>();
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            oldInput = InputSystem.settings.backgroundBehavior; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.logMessageReceived += Log;
            game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); game.Focus();
            if (director.cinematic) { director.cinematic.EndShot(); director.cinematic.enabled = false; }
            if (director.story) { director.story.CancelOpening(); director.story.enabled = false; }
            director.enabled = false;
            Backing(director, "Phase", VolcanoMissionDirector.MissionPhase.Evacuate);
            Backing(director, "State", VolcanoMissionDirector.FlowState.Preparation);
            player.SetCinematicLocked(false); player.SetGameplaySuspended(false); player.SetDriving(false);
            typeof(FireRescuePlayer).GetField("waitForMouseRelease", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, false);
            player.PlaceOnFoot(OnMesh(new Vector3(-14, 1, -554)), 90f);
            Physics.SyncTransforms();
            Bounds bodyBounds = player.playerBody.bounds;
            playerBodyRadius = Mathf.Max(bodyBounds.extents.x, bodyBounds.extents.z);
            Require(playerBodyRadius > 0f && EvacuationResident.FlatDistance(bodyBounds.center, player.transform.position) < .001f,
                "measure the saved player's actual centred physics footprint before disabling movement");
            playerFeet = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes }
                .Select(b => player.firstPerson.bodyAnimator.GetBoneTransform(b)).ToArray();
            Require(playerFeet.All(t => t), "both foot and toe bones are available for outside-clearance checks");
            player.playerBody.enabled = false; player.enabled = false; player.firstPerson.enabled = false;
            leader = player.PlayerBodyObject().AddComponent<NavMeshAgent>();
            leader.radius = .7f; leader.height = 5.5f; leader.acceleration = 35; leader.angularSpeed = 540;
            leader.speed = 12; leader.stoppingDistance = .5f; leader.avoidancePriority = 1;
            Require(leader.Warp(OnMesh(player.transform.position)), "fixture starts on existing village navigation");
            oldKeyboard = Keyboard.current; testKeyboard = InputSystem.AddDevice<Keyboard>(); testKeyboard.MakeCurrent();
            Cursor.lockState = CursorLockMode.Locked;
            Time.timeScale = 1; began = Time.time; deadline = EditorApplication.timeSinceStartup + 300;
            foreach (var r in mission.residents) previous[r] = r.transform.position;
            CheckThat("seven existing villagers form groups of two, two and three", mission.residents.Length == 7 &&
                mission.residents.GroupBy(r => r.rescueGroup).OrderBy(g => g.Key).Select(g => g.Count()).SequenceEqual(new[] { 2, 2, 3 }));
            CheckThat("three recruitment cues share comehere and departure uses shelter", CanonicalDialogue() && VoiceBindings());
            CheckThat("both downloaded recordings decode to non-silent audio", HasDecodedAudio(mission.dialogue.recruitVoice) && HasDecodedAudio(mission.dialogue.shelterVoice),
                "comehere=" + mission.dialogue.recruitVoice.length + "s shelter=" + mission.dialogue.shelterVoice.length + "s");
            CheckThat("one exclamation marker is configured", marker && marker.symbol && marker.symbol.text == "!" && Object.FindObjectsByType<RescueGroupMarker>(FindObjectsSortMode.None).Length == 1);
            CheckThat("exclamation has an open outline and no opaque background card", !marker.backdrop && marker.accents.Length >= 3 &&
                !marker.transform.Find("Marker background"));
            var assemblySign = Object.FindFirstObjectByType<AssemblyPointMarker>();
            CheckThat("assembly lettering is fixed on a two-sided ground sign", assemblySign && assemblySign.label && assemblySign.reverseLabel &&
                assemblySign.label.transform.IsChildOf(assemblySign.transform.Find("Freestanding assembly sign")) &&
                assemblySign.label.transform.position.y - mission.assemblyPoint.position.y < 5f && assemblySign.slotMarkers.Length == 7);
            CheckThat("Q and global alarm recruitment are disabled", mission.callKey == Key.None && !mission.villageWideCall && !mission.callAllAlarmedVillagers && !mission.Shout());
            CheckThat("inactive mission rejects recruitment", !mission.TryRecruitGroup(mission.residents[0]));
            mission.BeginMission();
            CheckThat("alarm panics all villagers without recruiting them", mission.residents.All(r => r.HasHeardAlarm) && mission.CalledCount == 0);
            CheckThat("shelter stays closed before assembly", !mission.ShelterOpened && mission.residents.All(r => !mission.CanEnterShelter(r)));
            CheckThat("cannot depart or deposit remotely", !mission.TryAssemblyInteraction());
            Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    void Update()
    {
        if (finished || !mission) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(Snapshot());
            if (Time.unscaledTime >= nextSave) { Save(); nextSave = Time.unscaledTime + 2; }
            Observe();
            ObserveVoice();
            float age = Time.unscaledTime - phaseAt;
            switch (phase)
            {
                case 0:
                    if (mission.residents.Any(r => r.State != EvacuationResident.ResidentState.Scattering ||
                        EvacuationResident.FlatDistance(r.transform.position, r.panicGroupAnchor.position) > 8f)) return;
                    CheckThat("all seven leave through doors and panic in their outdoor groups", mission.residents.All(r => r.PanicRuns > 0) && mission.CalledCount == 0);
                    var crowd = mission.transform.Find("Crowd Panic Ambience").GetComponent<AudioSource>();
                    CheckThat("original panic crowd ambience plays after alarm", crowd.enabled && crowd.isPlaying && crowd.clip && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(crowd.clip)) == "1b30f0540d4880a45a5bb22be6d99d3d");
                    CheckThat("only the first group has the exclamation marker", marker.IsVisible && marker.CurrentGroup == 0 && mission.NextRecruitGroup == 0);
                    Capture("01-outdoor-groups", new Vector3(46, 20, -622), new Vector3(85, 3, -574));
                    CheckThat("far E action cannot recruit a group", !mission.TryRecruitGroup(mission.residents[0]));
                    Vector3 original = leader.transform.position;
                    var later = mission.residents.First(r => r.rescueGroup == 1);
                    Require(leader.Warp(OnMesh(later.transform.position)), "fixture approaches a later group");
                    CheckThat("a nearby later group cannot be recruited out of order", EvacuationResident.FlatDistance(player.transform.position, later.transform.position) < 6f && !mission.TryRecruitGroup(later));
                    Require(leader.Warp(OnMesh(original)), "fixture returns to its route");
                    InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.Q)); Next(1); break;
                case 1:
                    if (age < .25f) return;
                    InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
                    CheckThat("real Q key input recruits nobody", mission.CalledCount == 0 && mission.ActiveGroup == -1);
                    CheckThat("alarm keeps rescue voice silent until a group is recruited", VoiceIsIdle() && mission.dialogue.PlayedLines == 0 && string.IsNullOrEmpty(mission.CurrentMessage) &&
                        PendingLines().Length == 0 && mission.GroupObjective == "Gather the townspeople" && mission.GroupDetail.Contains("group marked (!)") && mission.GroupDetail.Contains("yellow assembly point"));
                    MoveToGroup(); Next(2); break;
                case 2:
                    if (!NearLeader(GroupLeader().panicGroupAnchor.position, 2f)) return;
                    var candidate = mission.residents.Where(r => r.rescueGroup == groupIndex && mission.CanRecruitGroup(r))
                        .OrderBy(r => EvacuationResident.FlatDistance(r.transform.position, player.transform.position)).FirstOrDefault();
                    if (!candidate) return;
                    CheckThat("exclamation points to group " + (groupIndex + 1), marker.IsVisible && marker.CurrentGroup == groupIndex && mission.NextRecruitGroup == groupIndex);
                    if (groupIndex == 0)
                    {
                        Capture("00-next-group-marker", candidate.panicGroupAnchor.position + new Vector3(12, 11, -16), candidate.panicGroupAnchor.position + Vector3.up * 4);
                        foreach (var obsolete in new[] { "dispatch", "panic_0", "reply_0", "wait_0", "ready", "safe" }) mission.Speak(obsolete, candidate.transform, true);
                        CheckThat("old dispatch and villager exchanges cannot enter the dialogue queue", mission.dialogue.PlayedLines == 0 &&
                            string.IsNullOrEmpty(mission.CurrentMessage) && PendingLines().Length == 0 && CanonicalDialogue());
                        // Pause the actual first recruitment, without manufacturing an extra call.
                        Recruit(candidate);
                        Next(21); return;
                    }
                    Recruit(candidate); break;
                case 21:
                    if (age < .25f) return;
                    pausedSubtitle = mission.CurrentMessage;
                    CheckThat("the first recruitment plays comehere with its subtitle", pausedSubtitle == "Come here, follow me." && mission.dialogue.CurrentId == "recruit_0" &&
                        PendingLines().Length == 0 && Voice() && Voice().isPlaying && Voice().clip == mission.dialogue.recruitVoice);
                    Time.timeScale = 0f;
                    CheckThat("pause rejects group recruitment", !mission.TryRecruitGroup(GroupLeader()));
                    Next(20); break;
                case 20:
                    if (age < .15f) return;
                    if (pausedVoiceSample < 0) { pausedVoiceSample = Voice().timeSamples; return; }
                    if (age < .45f) return;
                    CheckThat("pause freezes both the voice sample and its subtitle", Voice() && !Voice().isPlaying && Voice().timeSamples == pausedVoiceSample &&
                        !string.IsNullOrEmpty(pausedSubtitle) && mission.CurrentMessage == pausedSubtitle);
                    Time.timeScale = 1f;
                    Next(22); break;
                case 22:
                    if (age < .15f) return;
                    CheckThat("resume continues the same recording without replaying the cue", Voice() && Voice().isPlaying && Voice().timeSamples > pausedVoiceSample &&
                        mission.dialogue.CurrentId == "recruit_0" && mission.dialogue.PlayedLines == 1 && voicedCues.Count == 1);
                    Next(31); break;
                case 31:
                    if (age < .15f) return;
                    CheckThat("group " + (groupIndex + 1) + " hears exactly the short recruitment cue without a villager reply", mission.dialogue.CurrentId == "recruit_" + groupIndex &&
                        mission.CurrentMessage == "Come here, follow me." && PendingLines().Length == 0 && CanonicalDialogue() &&
                        Voice() && Voice().isPlaying && Voice().clip == mission.dialogue.recruitVoice);
                    if (groupIndex == 0)
                    {
                        foreach (var lagging in mission.residents.Where(r => r.rescueGroup == 0))
                        { lagging.enabled = false; lagging.Agent.isStopped = true; }
                        leader.SetDestination(mission.assemblyPoint.position + Vector3.back * (mission.AssemblyBoundaryRadius + playerBodyRadius + 4.4f));
                        Next(32);
                    }
                    else
                    {
                        leader.SetDestination(mission.assemblyPoint.position + Vector3.back * (mission.AssemblyBoundaryRadius + playerBodyRadius + 3.9f));
                        Next(3);
                    }
                    break;
                case 32:
                    if (!NearLeader(mission.assemblyPoint.position + Vector3.back * (mission.AssemblyBoundaryRadius + playerBodyRadius + 4.4f), .8f)) return;
                    PlaceOutsideBoundary(4.1f);
                    CheckThat("assembly reach measures the visible outer yellow markings", mission.AssemblyBoundaryRadius > 8.7f && mission.AssemblyBoundaryRadius < 8.9f);
                    CheckThat("4.1 metres of body clearance outside the yellow boundary rejects delivery", !mission.PlayerAtAssembly && !mission.CanDepositGroup && !mission.TryAssemblyInteraction(), BoundaryEvidence());
                    EnableFootInput();
                    InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.E));
                    Next(33); break;
                case 33:
                    if (age < .1f) return;
                    InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
                    player.enabled = false;
                    CheckThat("real E beyond the four-metre body-clearance limit leaves the group following", mission.ActiveGroup == 0 && mission.GroupsDelivered == 0 &&
                        mission.residents.Where(r => r.rescueGroup == 0).All(r => r.State == EvacuationResident.ResidentState.Following), BoundaryEvidence());
                    PlaceOutsideBoundary(4f);
                    CheckThat("four metres of clear space beyond the player's body allows delivery", mission.PlayerAtAssembly && mission.CanDepositGroup, BoundaryEvidence());
                    PlaceOutsideBoundary(3.9f);
                    CheckThat("delivery is available with both feet well outside the yellow ring", BodyBoundaryClearance() > 3.8f && FootBoundaryClearance() > 2.5f && mission.CanDepositGroup, BoundaryEvidence());
                    Next(34); break;
                case 34:
                    if (age < .1f) return;
                    Next(3); break;
                case 3:
                    if (!mission.CanDepositGroup) return;
                    // Hold the automated leader outside for every key probe. Its navigation
                    // deceleration/avoidance must not carry the fixture across the line while E is processed.
                    PlaceOutsideBoundary(3.9f);
                    CheckThat("group marker hides while leading group " + (groupIndex + 1), !marker.IsVisible && mission.NextRecruitGroup == -1);
                    stagedBefore = mission.AssembledCount;
                    depositingResidents = mission.residents.Where(r => r.rescueGroup == groupIndex).ToArray();
                    handoffPositions = depositingResidents.Select(r => r.transform.position).ToArray();
                    if (groupIndex == 0)
                        CheckThat("E is ready while followers are still beyond the old distance gate", depositingResidents.Any(r =>
                            EvacuationResident.FlatDistance(r.transform.position, mission.assemblyPoint.position) > mission.assemblyGroupDistance));
                    foreach (var r in depositingResidents) r.enabled = true;
                    Time.timeScale = 0f;
                    CheckThat("pause rejects assembly handoff", !mission.TryAssemblyInteraction());
                    Time.timeScale = 1f;
                    leader.isStopped = true;
                    EnableFootInput();
                    InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.E));
                    Next(36); break;
                case 36:
                    if (age < .1f) return;
                    InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
                    player.enabled = false;
                    CheckThat("real E hands off group " + (groupIndex + 1) + " at 3.9 metres outside the yellow ring", mission.ActiveGroup == -1 && BodyBoundaryClearance() > 3.8f && FootBoundaryClearance() > 2.5f, BoundaryEvidence());
                    CheckThat("group " + (groupIndex + 1) + " E orders physical staging", mission.IsDepositing && mission.AssembledCount == stagedBefore);
                    CheckThat("handoff immediately frees the player to find the next group " + (groupIndex + 1), mission.ActiveGroup == -1 &&
                        mission.GroupsDelivered == groupIndex + 1 && mission.NextRecruitGroup == (groupIndex < 2 ? groupIndex + 1 : -1));
                    CheckThat("handoff starts routes without teleporting group " + (groupIndex + 1), depositingResidents.Select((r, i) =>
                        r.IsMovingToAssembly && !r.Agent.isStopped && Vector3.Distance(r.transform.position, handoffPositions[i]) <= r.runningSpeed * (age + .2f) &&
                        EvacuationResident.FlatDistance(r.Agent.destination, mission.AssemblySlot(r).position) < .5f).All(v => v));
                    if (groupIndex < 2)
                        CheckThat("repeated E cannot hand off the same group twice", !mission.TryAssemblyInteraction() && mission.GroupsDelivered == groupIndex + 1);
                    CheckThat("depositing does not count as shelter rescue", mission.RescuedCount == 0 && !mission.IsComplete);
                    if (groupIndex == 1) MoveToNextGroupDuringArrival();
                    if (groupIndex == 2)
                    {
                        CheckThat("early departure can be requested while the final group walks in", mission.TryAssemblyInteraction() && mission.DepartureRequested && !mission.EscortStarted);
                        PlaceOutsideBoundary(4.1f);
                        typeof(EvacuationMission).GetMethod("TickGroups", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mission, null);
                        CheckThat("leaving the assembly range cancels a pending departure", !mission.DepartureRequested && !mission.EscortStarted);
                        PlaceOutsideBoundary(3.9f);
                        Next(37); break;
                    }
                    Next(4); break;
                case 37:
                    if (age < .1f) return;
                    EnableFootInput();
                    InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.E));
                    Next(38); break;
                case 38:
                    if (age < .1f) return;
                    InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
                    player.enabled = false;
                    CheckThat("one real E remembers the final departure before everyone is ready", mission.DepartureRequested && !mission.EscortStarted && mission.GroupsDelivered == 3);
                    Next(4); break;
                case 4:
                    if (groupIndex == 2)
                    {
                        if (!mission.EscortStarted) return;
                        CheckThat("the queued departure starts without another E", !mission.DepartureRequested && mission.FollowingCount == 7 && mission.AssembledCount == 0);
                        Next(5); break;
                    }
                    int expected = groupIndex == 0 ? 2 : groupIndex == 1 ? 4 : 7;
                    if (mission.AssembledCount != expected) return;
                    if (groupIndex == 0)
                        CheckThat("the first group enters and settles while the player stays well outside", BodyBoundaryClearance() > 3.8f && FootBoundaryClearance() > 2.5f &&
                            mission.residents.Where(r => r.rescueGroup == 0).All(r => r.IsAtAssembly), BoundaryEvidence());
                    CheckThat("assembly count reaches " + expected + " only on real arrival", mission.AssembledCount == expected && mission.residents.Where(r => r.IsAtAssembly).All(r =>
                        EvacuationResident.FlatDistance(r.transform.position, mission.AssemblySlot(r).position) < 1.4f));
                    CheckThat("previous groups wait while the player collects group " + (groupIndex + 1), waiting.All(p => Vector3.Distance(p.Key.transform.position, p.Value) < .3f));
                    foreach (var r in mission.residents.Where(r => r.IsAtAssembly)) waiting[r] = r.transform.position;
                    Capture("0" + (groupIndex + 2) + "-assembly-" + expected, mission.assemblyPoint.position + new Vector3(17, 15, -22), mission.assemblyPoint.position + Vector3.up * 3f);
                    if (groupIndex == 0)
                        Capture("07-ground-sign", mission.assemblyPoint.position + new Vector3(1, 5.8f, -6), mission.assemblyPoint.position + new Vector3(0, 3.6f, 7.5f));
                    if (groupIndex < 2)
                    {
                        CheckThat("partial assembly cannot start shelter escort " + expected, !mission.AllAtAssembly && !mission.EscortStarted);
                        groupIndex++; MoveToGroup(); Next(2);
                    }
                    break;
                case 5:
                    CheckThat("final E takes all seven from assembly", mission.EscortStarted && mission.FollowingCount == 7 && mission.AssembledCount == 0);
                    CheckThat("departure is idempotent", !mission.TryAssemblyInteraction());
                    CheckThat("escort keeps English guidance and its single departure cue", mission.GroupObjective == "Lead everyone to the shelter" && mission.GroupDetail.Contains("green marker") &&
                        (PendingLines().Length == 1 && PendingLines()[0].id == "depart" || mission.dialogue.CurrentId == "depart") && !marker.IsVisible);
                    ResumeLeader(); leader.SetDestination(mission.shelterApproach.position); Next(6); break;
                case 6:
                    if (!NearLeader(mission.shelterApproach.position, 2f)) return;
                    CheckThat("shelter opens after the assembled group is escorted", mission.ShelterOpened && mission.EscortStarted);
                    CheckThat("departure speech adds no NPC replies or extra exchanges", PendingLines().Length == 0 && CanonicalDialogue());
                    Capture("05-shelter-escort", mission.shelterApproach.position + new Vector3(13, 12, -20), mission.shelterApproach.position + Vector3.up * 3);
                    leader.SetDestination(mission.residents[5].safePlace.position); Next(7); break;
                case 7:
                    if (!mission.IsComplete) return;
                    CheckThat("all seven physically reach the shelter safe area", mission.RescuedCount == 7 && mission.residents.All(r => r.IsSafe && mission.IsInsideShelter(r.transform.position)));
                    int rescued = mission.RescuedCount; mission.ResidentArrived(mission.residents[0]);
                    CheckThat("duplicate arrival cannot increase rescue count", mission.RescuedCount == rescued);
                    CheckThat("residents never pass through walls", report.wallCrossings == 0, "torso-height physics sweep for every moving frame");
                    spokenAtCompletion = mission.dialogue.PlayedLines;
                    Capture("06-safely-inside", mission.shelterEntrance.position + new Vector3(6, 10, -4), mission.residents[4].transform.position + Vector3.up * 3);
                    Next(8); break;
                case 8:
                    if (age < .7f) return;
                    CheckThat("completion adds no extra safe speech or NPC replies", VoiceIsIdle() && mission.dialogue.CurrentId != "safe" &&
                        mission.dialogue.PlayedLines == spokenAtCompletion && PendingLines().Length == 0 && CanonicalDialogue() &&
                        mission.GroupObjective == "Everyone is sheltered" && mission.GroupDetail == "Everyone is safe.");
                    CheckThat("the real evacuation plays exactly three comehere calls and one shelter departure", mission.dialogue.PlayedLines == 4 &&
                        voicedCues.SequenceEqual(new[] { "recruit_0", "recruit_1", "recruit_2", "depart" }), string.Join(", ", voicedCues));
                    var ending = Object.FindFirstObjectByType<ShelterReturnCinematic>();
                    if (ending)
                    {
                        CheckThat("English closing narration and optional recorded voices remain intact", ending.thoughts.Length == 6 && ending.thoughts.All(t => !string.IsNullOrEmpty(t.text) && IsEnglish(t.text) && (!t.voice || t.Duration >= t.voice.length)) &&
                            ending.thoughts[0].text.StartsWith("It's over...", StringComparison.Ordinal) && ending.arrivalThought.text.StartsWith("There they are...", StringComparison.Ordinal) &&
                            IsEnglish(ending.arrivalThought.text) && (!ending.arrivalThought.voice || ending.arrivalThought.Duration >= ending.arrivalThought.voice.length));
                        CheckThat("existing closing sequence can begin after group evacuation", ending.Begin(player.gameFlow));
                        // Closing narration starts after its authored relocation fade.
                        Next(23); return;
                    }
                    report.rescued = mission.RescuedCount; report.elapsedSeconds = Time.time - began;
                    Finish(null); break;
                case 23:
                    var closing = Object.FindFirstObjectByType<ShelterReturnCinematic>();
                    if (closing.IsRelocating || closing.ThoughtIndex < 0) return;
                    var firstThought = closing.thoughts[0];
                    var closingVoice = closing.GetComponents<AudioSource>().FirstOrDefault(s => s.clip == firstThought.voice);
                    CheckThat("closing preserves its written line and plays an assigned voice in 2D after its fade", closing.CurrentLine == firstThought.text &&
                        (!firstThought.voice || closingVoice && closingVoice.isPlaying && closingVoice.spatialBlend == 0));
                    closing.Cancel();
                    report.rescued = mission.RescuedCount; report.elapsedSeconds = Time.time - began;
                    Finish(null); break;
            }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    void Recruit(EvacuationResident resident)
    {
        calledBefore = mission.CalledCount;
        UseContext(ContextualInteractionKind.Resident, resident.transform, resident);
        int size = groupIndex == 2 ? 3 : 2;
        CheckThat("E recruits only group " + (groupIndex + 1) + " of " + size, mission.ActiveGroup == groupIndex && mission.CalledCount - calledBefore == size);
        CheckThat("repeating recruitment cannot replay group " + (groupIndex + 1) + " voice", !mission.TryRecruitGroup(resident));
        var other = mission.residents.FirstOrDefault(r => !r.IsCalled && !r.IsDead);
        if (other) CheckThat("group " + (groupIndex + 1) + " must be deposited before recruiting another", !mission.TryRecruitGroup(other));
        CheckThat("waiting people remain at assembly during collection " + (groupIndex + 1), waiting.All(p => Vector3.Distance(p.Key.transform.position, p.Value) < .3f));
        var pending = PendingLines();
        CheckThat("recruiting group " + (groupIndex + 1) + " queues one player cue and no NPC response", CanonicalDialogue() && pending.Length == 1 &&
            pending[0].id == "recruit_" + groupIndex && pending[0].text == "Come here, follow me." && !pending[0].villager);
        Next(31);
    }
    bool CanonicalDialogue() => mission.dialogue && mission.dialogue.lines != null && mission.dialogue.lines.Length == 4 &&
        mission.dialogue.lines.Select(l => l.id).SequenceEqual(new[] { "recruit_0", "recruit_1", "recruit_2", "depart" }) &&
        mission.dialogue.lines.All(l => !l.villager && l.text == (l.id == "depart" ? "Follow me. I'll take you to the shelter." : "Come here, follow me."));
    RescueDialogue.Line[] PendingLines() => ((Queue<RescueDialogue.Line>)typeof(RescueDialogue).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mission.dialogue)).ToArray();
    static bool IsEnglish(string value) => !string.IsNullOrEmpty(value) && value.IndexOfAny("çğıöşüÇĞİÖŞÜ".ToCharArray()) < 0;
    AudioSource Voice()
    {
        var emitter = mission.dialogue.transform.Find("Rescue dialogue voice");
        return emitter ? emitter.GetComponent<AudioSource>() : null;
    }
    bool VoiceIsIdle() => !Voice() || !Voice().isPlaying;
    bool VoiceBindings() => mission.dialogue.recruitVoice && mission.dialogue.shelterVoice &&
        mission.dialogue.recruitVoice == Resources.Load<AudioClip>("ASHFALL/Audio/Rescue/comehere") &&
        mission.dialogue.shelterVoice == Resources.Load<AudioClip>("ASHFALL/Audio/Rescue/shelter");
    static bool HasDecodedAudio(AudioClip clip)
    {
        if (!clip || clip.length < .5f || clip.samples < 8192 || clip.loadState != AudioDataLoadState.Loaded) return false;
        var samples = new float[4096];
        return clip.GetData(samples, clip.samples / 2) && samples.Any(s => Mathf.Abs(s) > .0001f);
    }
    void ObserveVoice()
    {
        var dialogue = mission.dialogue; var voice = Voice();
        if (dialogue.PlayedLines <= observedVoiceLines || !voice || !voice.isPlaying) return;
        var expected = dialogue.CurrentId == "depart" ? dialogue.shelterVoice : dialogue.recruitVoice;
        CheckThat("cue " + dialogue.CurrentId + " plays its assigned recording once in 2D", voice.clip == expected &&
            voice.spatialBlend == 0 && !voice.loop && voice.pitch == 1, AssetDatabase.GetAssetPath(voice.clip));
        voicedCues.Add(dialogue.CurrentId); observedVoiceLines = dialogue.PlayedLines;
    }
    void UseContext(ContextualInteractionKind kind, Transform anchor, EvacuationResident resident)
    {
        player.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Vector3 focus = anchor.position + Vector3.up * (kind == ContextualInteractionKind.Resident ? 1.65f : 3f);
        player.viewCamera.transform.position = player.transform.position + Vector3.up * 5.8f;
        player.viewCamera.transform.rotation = Quaternion.LookRotation(focus - player.viewCamera.transform.position);
        Physics.SyncTransforms();
        var ui = player.contextualInteractions; ui.RefreshSelection();
        var target = ui.CurrentTarget;
        Require(target && target.kind == kind && (!resident || target.resident == resident), "E prompt selects " + kind + " rather than " + (target ? target.kind.ToString() : "nothing") + " " + Snapshot());
        Require(ui.CurrentOffer.input == InteractionInput.Interact && !ui.CurrentOffer.blocked, "E offer is available " + kind);
        Require(player.Interact(), "existing player E coordinator executes " + kind);
        player.enabled = false;
    }
    EvacuationResident GroupLeader() => mission.residents.First(r => r.rescueGroup == groupIndex);
    void ResumeLeader() { leader.nextPosition = leader.transform.position; leader.updatePosition = true; leader.isStopped = false; }
    void MoveToGroup() { ResumeLeader(); leader.SetDestination(GroupLeader().panicGroupAnchor.position); }
    void MoveToNextGroupDuringArrival() { ResumeLeader(); leader.SetDestination(mission.residents.First(r => r.rescueGroup == groupIndex + 1).panicGroupAnchor.position); }
    float BodyBoundaryClearance() => EvacuationResident.FlatDistance(player.transform.position, mission.assemblyPoint.position) - playerBodyRadius - mission.AssemblyBoundaryRadius;
    float FootBoundaryClearance() => playerFeet.Min(t => EvacuationResident.FlatDistance(t.position, mission.assemblyPoint.position) - mission.AssemblyBoundaryRadius);
    string BoundaryEvidence() => "bodyRadius=" + playerBodyRadius.ToString("F3") + " bodyClearance=" + BodyBoundaryClearance().ToString("F3") +
        " nearestFootOrToeClearance=" + FootBoundaryClearance().ToString("F3") + " activeGroup=" + mission.ActiveGroup + " delivered=" + mission.GroupsDelivered;
    void PlaceOutsideBoundary(float distance)
    {
        leader.updatePosition = true;
        Require(leader.Warp(OnMesh(mission.assemblyPoint.position + Vector3.back * (mission.AssemblyBoundaryRadius + playerBodyRadius + distance))), "boundary input fixture stays on village navigation");
        leader.ResetPath(); leader.isStopped = true; leader.velocity = Vector3.zero; leader.updatePosition = false;
        Require(Mathf.Abs(BodyBoundaryClearance() - distance) < .001f, "boundary probe has the requested measured body clearance");
    }
    void EnableFootInput()
    {
        typeof(FireRescuePlayer).GetField("focused", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, true);
        typeof(FireRescuePlayer).GetField("waitForMouseRelease", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, false);
        Cursor.lockState = CursorLockMode.Locked;
        player.viewCamera.transform.rotation = Quaternion.LookRotation(Vector3.back);
        player.enabled = true;
    }
    bool NearLeader(Vector3 point, float radius) => !leader.pathPending && EvacuationResident.FlatDistance(leader.transform.position, point) < radius;
    static Vector3 OnMesh(Vector3 p)
    {
        if (NavMesh.SamplePosition(p, out var hit, 8f, NavMesh.AllAreas)) return hit.position;
        throw new InvalidOperationException("No existing navigation near " + p);
    }
    static void Backing(object obj, string name, object value) => obj.GetType().GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
    void Observe()
    {
        foreach (var r in mission.residents)
        {
            Vector3 a = previous[r], b = r.transform.position; previous[r] = b;
            if (r.IsAtAssembly && waiting.TryGetValue(r, out var parked) && Vector3.Distance(parked, b) > .3f && displaced.Add(r))
                File.AppendAllText(GroupRescueSetup.Folder + "/waiting-diagnostic.txt", r.name + " parked=" + parked + " now=" + b + " slot=" + mission.AssemblySlot(r).position +
                    " stopped=" + r.Agent.isStopped + " updatePosition=" + r.Agent.updatePosition + " next=" + r.Agent.nextPosition + " onMesh=" + r.Agent.isOnNavMesh +
                    " velocity=" + r.Agent.velocity + " animator=" + r.animator.transform.name + " rootMotion=" + r.animator.applyRootMotion + " parent=" + r.transform.parent.position + "\n");
            if (Vector3.Distance(a, b) < .015f) continue;
            Vector3 raised = Vector3.up * r.Agent.height * r.transform.lossyScale.y * .55f;
            foreach (var hit in Physics.RaycastAll(a + raised, b - a, Vector3.Distance(a, b), ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.GetComponentInParent<EvacuationResident>() && !hit.collider.GetComponentInParent<FireRescuePlayer>())
                { report.wallCrossings++; File.AppendAllText(GroupRescueSetup.Folder + "/wall-crossings.txt", r.name + " " + hit.collider.name + " " + a + " -> " + b + "\n"); }
        }
    }
    void CheckThat(string name, bool passed, string evidence = "")
    {
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
        Require(passed, name);
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("FAIL: " + message); }
    void Next(int value) { phase = value; phaseAt = Time.unscaledTime; deadline = EditorApplication.timeSinceStartup + 100; Save(); }
    string Snapshot() => "phase=" + phase + " group=" + groupIndex + " called=" + mission.CalledCount + " staged=" + mission.AssembledCount +
        " safe=" + mission.RescuedCount + " leader=" + (leader ? leader.transform.position.ToString() + " path=" + leader.pathStatus + " remaining=" + leader.remainingDistance : "none") + "\n" +
        string.Join("\n", mission.residents.Select(r => r.name + " " + r.State + " " + r.transform.position + " path=" + r.Agent.pathStatus + " remaining=" + r.Agent.remainingDistance));
    void Save()
    {
        File.WriteAllText(GroupRescueSetup.Folder + "/checks.json", JsonUtility.ToJson(report, true));
        if (mission) File.WriteAllText(GroupRescueSetup.Folder + "/progress.txt", Snapshot());
    }
    void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(text); }
    void Finish(string error)
    {
        if (finished) return; finished = true;
        if (error != null) report.errors.Add(error);
        report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        Save(); Application.logMessageReceived -= Log;
        Time.timeScale = 1; Application.runInBackground = oldBackground;
        InputSystem.settings.backgroundBehavior = oldInput;
        if (testKeyboard != null) InputSystem.RemoveDevice(testKeyboard);
        if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
        if (reloadLocked) { EditorApplication.UnlockReloadAssemblies(); reloadLocked = false; }
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
    void OnDisable() { if (!finished) Finish("Play Mode interrupted."); }
    static void Capture(string name, Vector3 eye, Vector3 targetPoint)
    {
        var go = new GameObject("Group rescue review camera"); var camera = go.AddComponent<Camera>(); camera.enabled = false;
        camera.transform.position = eye; camera.transform.LookAt(targetPoint); camera.fieldOfView = 70; camera.farClipPlane = 2200;
        var assembly = Object.FindFirstObjectByType<AssemblyPointMarker>();
        var group = Object.FindFirstObjectByType<RescueGroupMarker>();
        Quaternion groupRotation = group ? group.transform.rotation : Quaternion.identity;
        Vector3 groupScale = group ? group.transform.localScale : Vector3.one;
        if (group)
        {
            group.transform.rotation = Quaternion.LookRotation(group.transform.position - eye);
            group.transform.localScale = Vector3.one * Mathf.Clamp(Vector3.Distance(group.transform.position, eye) * .025f, 1f, 2.5f);
        }
        var target = new RenderTexture(1440, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var image = new Texture2D(1440, 900, TextureFormat.RGB24, false); var old = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); image.Apply();
            File.WriteAllBytes(GroupRescueSetup.Folder + "/" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            if (group) { group.transform.rotation = groupRotation; group.transform.localScale = groupScale; }
            camera.targetTexture = null; RenderTexture.active = old; target.Release(); Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(go);
        }
    }
}

static class GroupRescuePlayerFixture
{
    public static GameObject PlayerBodyObject(this FireRescuePlayer player) => player.playerBody ? player.playerBody.gameObject : player.gameObject;
}
