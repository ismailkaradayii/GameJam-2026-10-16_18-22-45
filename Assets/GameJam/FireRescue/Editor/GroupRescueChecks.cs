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
        public string method = "Saved-scene NPCs use their real agents, doors, animations, next-group exclamation marker, contextual E coordinator and shelter volume. The player is guided by a temporary NavMesh fixture. Opening cinematic and preparation deadline are suspended only in this test. Q is injected through the Input System. No manual playthrough is claimed.";
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
        public int wallCrossings, rescued;
        public float elapsedSeconds;
    }
    readonly Report report = new Report();
    readonly Dictionary<EvacuationResident, Vector3> previous = new Dictionary<EvacuationResident, Vector3>();
    readonly Dictionary<EvacuationResident, Vector3> waiting = new Dictionary<EvacuationResident, Vector3>();
    readonly HashSet<EvacuationResident> displaced = new HashSet<EvacuationResident>();
    EvacuationMission mission;
    VolcanoMissionDirector director;
    FireRescuePlayer player;
    NavMeshAgent leader;
    RescueGroupMarker marker;
    Keyboard testKeyboard, oldKeyboard;
    InputSettings.BackgroundBehavior oldInput;
    EditorWindow game;
    bool oldBackground, finished, reloadLocked;
    int phase, groupIndex, calledBefore, stagedBefore;
    float phaseAt, began, nextSave;
    string pausedSubtitle;
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
            CheckThat("recorded dialogue is removed and written dialogue is restored", NoSpeech() && !AssetDatabase.IsValidFolder("Assets/GameJam/FireRescue/Audio/RescueDialogue") &&
                mission.dialogue && mission.dialogue.lines.Length == 16 && mission.dialogue.lines.All(l => !string.IsNullOrEmpty(l.text)));
            CheckThat("one exclamation marker is configured", marker && marker.symbol && marker.symbol.text == "!" && Object.FindObjectsByType<RescueGroupMarker>(FindObjectsSortMode.None).Length == 1);
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
                    CheckThat("alarm keeps its written dialogue and clear objectives", NoSpeech() && mission.dialogue.PlayedLines > 0 && !string.IsNullOrEmpty(mission.CurrentMessage) && mission.GroupDetail.Contains("Ünlem (!)") && mission.GroupDetail.Contains("toplanma alanına"));
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
                        mission.Speak("panic_0", candidate.transform, true);
                        Next(21); return;
                    }
                    Recruit(candidate); break;
                case 21:
                    if (age < .25f) return;
                    pausedSubtitle = mission.CurrentMessage;
                    Time.timeScale = 0f;
                    CheckThat("pause rejects group recruitment", !mission.TryRecruitGroup(GroupLeader()));
                    Next(20); break;
                case 20:
                    if (age < .3f) return;
                    CheckThat("pause preserves written dialogue without recorded speech", NoSpeech() && !string.IsNullOrEmpty(pausedSubtitle) && mission.CurrentMessage == pausedSubtitle);
                    Time.timeScale = 1f;
                    var first = mission.residents.Where(r => r.rescueGroup == 0 && mission.CanRecruitGroup(r))
                        .OrderBy(r => EvacuationResident.FlatDistance(r.transform.position, player.transform.position)).FirstOrDefault();
                    if (!first) { Next(2); return; }
                    Recruit(first); break;
                case 3:
                    if (!mission.CanDepositGroup) return;
                    CheckThat("group marker hides while leading group " + (groupIndex + 1), !marker.IsVisible && mission.NextRecruitGroup == -1);
                    stagedBefore = mission.AssembledCount;
                    UseContext(ContextualInteractionKind.AssemblyPoint, mission.assemblyPoint, null);
                    CheckThat("group " + (groupIndex + 1) + " E orders physical staging", mission.IsDepositing && mission.AssembledCount == stagedBefore);
                    CheckThat("depositing does not count as shelter rescue", mission.RescuedCount == 0 && !mission.IsComplete);
                    Next(4); break;
                case 4:
                    if (mission.ActiveGroup >= 0) return;
                    int expected = groupIndex == 0 ? 2 : groupIndex == 1 ? 4 : 7;
                    CheckThat("assembly count reaches " + expected + " only on real arrival", mission.AssembledCount == expected && mission.residents.Where(r => r.IsAtAssembly).All(r =>
                        EvacuationResident.FlatDistance(r.transform.position, mission.AssemblySlot(r).position) < 1.4f));
                    CheckThat("previous groups wait while the player collects group " + (groupIndex + 1), waiting.All(p => Vector3.Distance(p.Key.transform.position, p.Value) < .3f));
                    foreach (var r in mission.residents.Where(r => r.IsAtAssembly)) waiting[r] = r.transform.position;
                    Capture("0" + (groupIndex + 2) + "-assembly-" + expected, mission.assemblyPoint.position + new Vector3(17, 15, -22), mission.assemblyPoint.position + Vector3.up * 3f);
                    if (groupIndex < 2)
                    {
                        CheckThat("partial assembly cannot start shelter escort " + expected, !mission.AllAtAssembly && !mission.EscortStarted);
                        groupIndex++; MoveToGroup(); Next(2);
                    }
                    else
                    {
                        CheckThat("seven assembled survivors await explicit departure", mission.AllAtAssembly && !mission.EscortStarted && !mission.ShelterOpened && mission.RescuedCount == 0);
                        leader.SetDestination(mission.assemblyPoint.position + new Vector3(0, 0, -6)); Next(5);
                    }
                    break;
                case 5:
                    if (!NearLeader(mission.assemblyPoint.position + new Vector3(0, 0, -6), 1f)) return;
                    UseContext(ContextualInteractionKind.AssemblyPoint, mission.assemblyPoint, null);
                    CheckThat("final E takes all seven from assembly", mission.EscortStarted && mission.FollowingCount == 7 && mission.AssembledCount == 0);
                    CheckThat("departure is idempotent", !mission.TryAssemblyInteraction());
                    CheckThat("escort keeps guidance and its written departure line", mission.GroupObjective == "İnsanları sığınağa götür" && mission.GroupDetail.Contains("Yeşil") &&
                        mission.dialogue.lines.Any(l => l.id == "depart" && l.text.Contains("hazırladığım sığınağa")) && !marker.IsVisible);
                    leader.SetDestination(mission.shelterApproach.position); Next(6); break;
                case 6:
                    if (!NearLeader(mission.shelterApproach.position, 2f)) return;
                    CheckThat("shelter opens after the assembled group is escorted", mission.ShelterOpened && mission.EscortStarted);
                    Capture("05-shelter-escort", mission.shelterApproach.position + new Vector3(13, 12, -20), mission.shelterApproach.position + Vector3.up * 3);
                    leader.SetDestination(mission.residents[5].safePlace.position); Next(7); break;
                case 7:
                    if (!mission.IsComplete) return;
                    CheckThat("all seven physically reach the shelter safe area", mission.RescuedCount == 7 && mission.residents.All(r => r.IsSafe && mission.IsInsideShelter(r.transform.position)));
                    int rescued = mission.RescuedCount; mission.ResidentArrived(mission.residents[0]);
                    CheckThat("duplicate arrival cannot increase rescue count", mission.RescuedCount == rescued);
                    CheckThat("residents never pass through walls", report.wallCrossings == 0, "torso-height physics sweep for every moving frame");
                    Capture("06-safely-inside", mission.shelterEntrance.position + new Vector3(6, 10, -4), mission.residents[4].transform.position + Vector3.up * 3);
                    Next(8); break;
                case 8:
                    if (age < .7f) return;
                    var hud = Object.FindFirstObjectByType<RescueHUD>();
                    CheckThat("completion shows written dialogue without recorded speech", NoSpeech() && mission.dialogue.CurrentId == "safe" && mission.CurrentMessage.Contains("güvendesiniz") &&
                        hud && hud.subtitleText && hud.subtitleText.gameObject.activeInHierarchy && hud.subtitleText.text.Contains("güvendesiniz"));
                    var ending = Object.FindFirstObjectByType<ShelterReturnCinematic>();
                    if (ending)
                    {
                        CheckThat("written closing narration and optional recorded voices remain intact", ending.thoughts.Length == 6 && ending.thoughts.All(t => !string.IsNullOrEmpty(t.text) && (!t.voice || t.Duration >= t.voice.length)) &&
                            !string.IsNullOrEmpty(ending.arrivalThought.text) && (!ending.arrivalThought.voice || ending.arrivalThought.Duration >= ending.arrivalThought.voice.length));
                        CheckThat("existing closing sequence can begin after group evacuation", ending.Begin(player.gameFlow));
                        var firstThought = ending.thoughts[0];
                        var closingVoice = ending.GetComponents<AudioSource>().FirstOrDefault(s => s.clip == firstThought.voice);
                        CheckThat("closing preserves its written line and plays an assigned voice in 2D", ending.CurrentLine == firstThought.text &&
                            (!firstThought.voice || closingVoice && closingVoice.isPlaying && closingVoice.spatialBlend == 0));
                        ending.Cancel();
                    }
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
        var other = mission.residents.FirstOrDefault(r => !r.IsCalled && !r.IsDead);
        if (other) CheckThat("group " + (groupIndex + 1) + " must be deposited before recruiting another", !mission.TryRecruitGroup(other));
        CheckThat("waiting people remain at assembly during collection " + (groupIndex + 1), waiting.All(p => Vector3.Distance(p.Key.transform.position, p.Value) < .3f));
        leader.SetDestination(mission.assemblyPoint.position + new Vector3(0, 0, -6));
        Next(3);
    }
    bool NoSpeech() => Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).All(s => s.name != "Rescue dialogue voice" &&
        (!s.clip || !AssetDatabase.GetAssetPath(s.clip).StartsWith("Assets/GameJam/FireRescue/Audio/RescueDialogue/", StringComparison.Ordinal)));
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
    void MoveToGroup() => leader.SetDestination(GroupLeader().panicGroupAnchor.position);
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
        Quaternion labelRotation = assembly && assembly.label ? assembly.label.transform.rotation : Quaternion.identity;
        if (assembly && assembly.label) assembly.label.transform.rotation = Quaternion.LookRotation(assembly.label.transform.position - eye);
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
            if (assembly && assembly.label) assembly.label.transform.rotation = labelRotation;
            if (group) { group.transform.rotation = groupRotation; group.transform.localScale = groupScale; }
            camera.targetTexture = null; RenderTexture.active = old; target.Release(); Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(go);
        }
    }
}

static class GroupRescuePlayerFixture
{
    public static GameObject PlayerBodyObject(this FireRescuePlayer player) => player.playerBody ? player.playerBody.gameObject : player.gameObject;
}
