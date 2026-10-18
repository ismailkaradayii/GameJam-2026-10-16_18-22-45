using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises the authored music and gameplay audio in an unsaved Play Mode fixture.</summary>
[InitializeOnLoad]
public static class VolcanoMusicChecks
{
    public const string Folder = "Logs/VolcanoMusic";
    const string Pending = "VolcanoMusicChecks.Pending";

    static VolcanoMusicChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                EditorApplication.delayCall += () => new GameObject("Adaptive volcano music checks").AddComponent<VolcanoMusicProbe>().Begin();
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending + ".Dirty", false))
            {
                var scene = SceneManager.GetSceneByPath(SessionState.GetString(Pending + ".Scene", ""));
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
                SessionState.EraseBool(Pending + ".Dirty");
                SessionState.EraseString(Pending + ".Scene");
            }
        };
    }

    [MenuItem("GameJam/Final Loop/Check adaptive volcano music")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Use idle Edit Mode with Volcano_Demo open.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Open the authored Volcano_Demo scene first.");
        if (scene.isDirty)
        {
            Directory.CreateDirectory("Library/VolcanoMusicReview");
            if (!EditorSceneManager.SaveScene(scene, "Library/VolcanoMusicReview/unsaved-preserved-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true))
                throw new InvalidOperationException("Could not preserve the unsaved scene as a separate copy.");
        }
        SessionState.SetBool(Pending + ".Dirty", scene.isDirty);
        SessionState.SetString(Pending + ".Scene", scene.path);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/checks.json", "{\"status\":\"STARTING\",\"startedUtc\":\"" + DateTime.UtcNow.ToString("O") + "\"}");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
}

[DefaultExecutionOrder(1000)]
public sealed class VolcanoMusicProbe : MonoBehaviour
{
    [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
    [Serializable] public sealed class Report
    {
        public string status = "RUNNING", startedUtc, finishedUtc;
        public string method = "Saved-scene music and actual gameplay audio on a temporary remote flat surface. Actual pistol Fire/Reload, truck WheelColliders and SetInput, cannon emission, written dialogue, the siren contact callback, mission deadline and eruption stages are exercised. Opening and unrelated mission progression are suspended only in Play Mode. No source scene or AudioClip is changed, no manual listening or full mission playthrough is claimed.";
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
        public float elapsedSeconds;
    }

    struct SoundState
    {
        public AudioSource source;
        public AudioClip clip;
        public float volume, pitch, blend;
        public int priority;
        public bool mute, enabled, loop, playing;
    }

    readonly Report report = new Report();
    VolcanoMusicDirector music;
    VolcanoMissionDirector mission;
    FireRescuePlayer player;
    RescueGameFlow flow;
    AudioSource engine, spray;
    bool finished, prepared, reloadLocked, previousBackground, hadMusic, hadSfx;
    float previousTimeScale, previousMusic, previousSfx, began;
    double deadline;

    public void Begin()
    {
        report.startedUtc = DateTime.UtcNow.ToString("O");
        began = Time.realtimeSinceStartup;
        deadline = EditorApplication.timeSinceStartup + 150;
        Application.logMessageReceived += Log;
        EditorApplication.update += Watch;
        EditorApplication.playModeStateChanged += PlayState;
        try
        {
            EditorApplication.LockReloadAssemblies(); reloadLocked = true;
            previousBackground = Application.runInBackground;
            previousTimeScale = Time.timeScale;
            hadMusic = PlayerPrefs.HasKey("Ashfall.MusicVolume");
            hadSfx = PlayerPrefs.HasKey("Ashfall.SfxVolume");
            previousMusic = PlayerPrefs.GetFloat("Ashfall.MusicVolume", 1);
            previousSfx = PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1);
            prepared = true;
            Application.runInBackground = true; Time.timeScale = 1;
            PlayerPrefs.SetFloat("Ashfall.MusicVolume", 1);
            PlayerPrefs.SetFloat("Ashfall.SfxVolume", 1);
            music = Object.FindFirstObjectByType<VolcanoMusicDirector>();
            mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
            Require(music && mission && mission.player, "The authored music director and mission are required.");
            player = mission.player; flow = mission.gameFlow;
            CheckThat("one authored director binds the two different recordings", Object.FindObjectsByType<VolcanoMusicDirector>(FindObjectsSortMode.None).Length == 1 &&
                music.mission == mission && music.passage && music.horizon && music.passage != music.horizon &&
                music.passage.name.IndexOf("Passage", StringComparison.OrdinalIgnoreCase) >= 0 &&
                music.horizon.name.IndexOf("Horizon", StringComparison.OrdinalIgnoreCase) >= 0,
                music.passage ? music.passage.name + " / " + music.horizon.name : "missing clips");
            mission.enabled = false;
            if (mission.story) { mission.story.CancelOpening(); mission.story.enabled = false; }
            if (mission.cinematic) { mission.cinematic.EndShot(); mission.cinematic.enabled = false; }
            mission.sequence.enabled = false;
            if (flow) flow.enabled = false;
            var survival = Object.FindFirstObjectByType<VolcanoSurvivalObjective>();
            if (survival) survival.enabled = false;
            if (mission.encounter) mission.encounter.enabled = false;
            if (mission.evacuation) mission.evacuation.enabled = false;
            if (mission.evacuation && mission.evacuation.dialogue) mission.evacuation.dialogue.enabled = false;
            player.SetCinematicLocked(false); player.SetGameplaySuspended(false); player.SetDriving(false);
            player.enabled = false;
            player.firstPerson.enabled = false;
            player.firstPerson.SetInputCaptured(true);
            player.pistol.SetExternalInput(true, false, false, false, false, false);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Unsaved music check ground";
            floor.transform.position = new Vector3(12000, 299.5f, 12000);
            floor.transform.localScale = new Vector3(500, 1, 500);
            player.PlaceOnFoot(new Vector3(11965, 300.2f, 11965), 0);
            player.viewCamera.transform.rotation = Quaternion.Euler(-25, 0, 0);
            player.drive.body.position = new Vector3(12000, 306, 12000);
            player.drive.body.rotation = Quaternion.identity;
            player.drive.body.linearVelocity = player.drive.body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            engine = Source(player.drive.GetComponent<FireTruckEngineAudio>(), "engineSource");
            spray = Source(player.cannon.GetComponent<FireTruckSprayAudio>(), "spraySource");
            StartCoroutine(Guarded(Checks()));
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    IEnumerator Checks()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(3.5f, music.fadeInSeconds + .5f));
        CheckThat("Passage plays before eruption while Horizon is silent", !music.IsHorizon && music.PassageSource.isPlaying &&
            music.PassageSource.clip == music.passage && music.PassageSource.volume > .02f && music.HorizonSource.volume <= .001f, Mix());
        CheckThat("both music emitters are looping 2D at normal pitch with low voice priority", new[] { music.PassageSource, music.HorizonSource }.All(s =>
            s && s.loop && !s.playOnAwake && s.spatialBlend == 0 && Mathf.Abs(s.pitch - 1) < .001f && s.priority >= 200));
        CheckSfxUntouched("idle music mixing leaves all gameplay audio source settings unchanged");
        float idleVolume = music.PassageSource.volume;

        // The actual semi-automatic pistol emits its production shot audio.
        player.AcquirePistol(); player.firstPerson.SetInputCaptured(true);
        player.pistol.SetExternalInput(true, false, false, false, false, false);
        yield return new WaitForSecondsRealtime(.65f);
        int shots = player.pistol.ShotsFired;
        CheckThat("a real pistol shot is accepted", player.pistol.Fire());
        float shotUntil = Time.realtimeSinceStartup + 1.2f;
        while (player.pistol.ShotsFired == shots && Time.realtimeSinceStartup < shotUntil) yield return null;
        yield return new WaitForSecondsRealtime(.14f);
        CheckThat("real shot ducks music without reducing weapon feedback volume", player.pistol.ShotsFired == shots + 1 &&
            music.TargetDuck <= music.actionDuck + .01f && music.CurrentDuck < .8f && music.PassageSource.volume < idleVolume * .85f &&
            player.pistol.Feedback && player.pistol.Feedback.shotVolume > 0,
            Mix() + "; shots=" + player.pistol.ShotsFired);
        CheckSfxUntouched("shot duck changes only the music sources");
        yield return new WaitForSecondsRealtime(.15f);
        CheckThat("the physical magazine reload starts", player.pistol.Reload());
        yield return new WaitForSecondsRealtime(.25f);
        CheckThat("reload sound retains space in the mix", player.pistol.IsReloading && music.TargetDuck <= music.actionDuck + .01f, Mix());
        yield return new WaitForSecondsRealtime(player.pistol.reloadDuration + music.duckReleaseSeconds * 4 + .5f);
        CheckThat("music recovers smoothly after the weapon becomes quiet", !player.pistol.IsReloading && music.CurrentDuck > .92f &&
            music.PassageSource.volume > idleVolume * .85f, Mix());

        var dialogue = mission.evacuation.dialogue;
        dialogue.enabled = true; dialogue.Enqueue("recruit_0", true);
        yield return new WaitForSecondsRealtime(.3f);
        CheckThat("actual rescue dialogue takes priority over music", dialogue.IsShowingLine && dialogue.CurrentId == "recruit_0" &&
            mission.evacuation.CurrentMessage == "Come here, follow me." && music.TargetDuck <= music.speechDuck + .01f &&
            music.PassageSource.volume < idleVolume * .6f, Mix());
        dialogue.enabled = false;
        yield return new WaitForSecondsRealtime(music.duckReleaseSeconds * 4 + .25f);

        player.SetDriving(true);
        float drivingUntil = Time.realtimeSinceStartup + 2;
        while (Time.realtimeSinceStartup < drivingUntil) { player.drive.SetInput(.55f, 0, false); yield return null; }
        CheckThat("physical truck driving and the original engine remain audible", player.drive.IsDriven && player.drive.SpeedKph > 5 &&
            engine && engine.isPlaying && engine.volume > .01f && music.TargetDuck <= .81f && music.CurrentDuck < .9f,
            Mix() + "; speed=" + player.drive.SpeedKph.ToString("F1") + "; engine=" + (engine ? engine.volume.ToString("F3") : "missing"));
        CheckSfxUntouched("driving duck leaves the engine source unchanged");
        player.drive.SetInput(0, 0, true);
        player.SetDriving(false);
        player.hose.SetTankLitres(500);
        player.cannon.SetSpraying(true);
        yield return new WaitForSecondsRealtime(.4f);
        CheckThat("actual cannon water and spray audio duck the background", player.cannon.IsSpraying && spray && spray.isPlaying &&
            spray.volume > .01f && music.TargetDuck <= music.waterDuck + .01f, Mix());
        CheckSfxUntouched("water duck leaves the spray source unchanged");
        player.cannon.SetSpraying(false);

        // Invoke the same contact callback that the physical palm strike uses.
        var alarm = mission.villageAlarm;
        typeof(VillageAlarmButton).GetMethod("Strike", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(alarm, null);
        yield return new WaitForSecondsRealtime(.4f);
        CheckThat("authored village sirens play and take priority over the score", alarm.SirenRunning && alarm.villageSirens.Any(s => s && s.isPlaying && s.volume > 0) &&
            music.TargetDuck <= music.alarmDuck + .01f, Mix());
        CheckSfxUntouched("siren duck leaves the alarm emitters unchanged");
        alarm.enabled = false;
        yield return new WaitForSecondsRealtime(music.duckReleaseSeconds * 4 + .25f);

        mission.TriggerDeadline();
        if (mission.cinematic) mission.cinematic.EndShot();
        yield return new WaitForSecondsRealtime(.3f);
        CheckThat("warning and pre-impact explosion keep Passage until the visible eruption", mission.EruptionTriggered &&
            mission.sequence.Stage == VolcanoEruptionSequence.EruptionStage.Warning && !music.IsHorizon &&
            music.PassageSource.isPlaying && music.HorizonSource.volume <= .001f, Mix());
        CheckThat("the real explosion sound is protected by the strongest music duck", music.TargetDuck <= music.explosionDuck + .01f, Mix());
        CheckSfxUntouched("explosion duck leaves the explosion source unchanged");
        mission.sequence.TickSequence(mission.sequence.warningSeconds + .1f);
        yield return new WaitForSecondsRealtime(.45f);
        CheckThat("visible eruption crossfades both real recordings", music.IsHorizon && music.HorizonSource.isPlaying &&
            music.HorizonSource.volume > .001f && music.PassageSource.isPlaying && music.PassageSource.volume > .001f,
            Mix() + "; stage=" + mission.sequence.Stage);
        yield return new WaitForSecondsRealtime(music.crossfadeSeconds + .5f);
        CheckThat("Horizon takes over completely after the crossfade", music.IsHorizon && music.HorizonSource.isPlaying &&
            music.HorizonSource.volume > .001f && music.PassageSource.volume <= .001f, Mix());

        flow.Pause();
        yield return new WaitForSecondsRealtime(.2f);
        float pausedTime = music.HorizonSource.time;
        yield return new WaitForSecondsRealtime(.45f);
        CheckThat("pause freezes music playback without advancing or rewinding the track", flow.State == RescueGameFlow.GameState.Paused &&
            !music.PassageSource.isPlaying && !music.HorizonSource.isPlaying && Mathf.Abs(music.HorizonSource.time - pausedTime) < .05f);
        flow.Resume();
        yield return new WaitForSecondsRealtime(.25f);
        CheckThat("resume continues Horizon from its paused position", music.HorizonSource.isPlaying &&
            music.HorizonSource.time >= pausedTime && music.HorizonSource.time < pausedTime + 1.5f);

        // Hold the same duck while testing gain, independently of effect volumes.
        float full = music.HorizonSource.volume;
        PlayerPrefs.SetFloat("Ashfall.MusicVolume", .5f);
        music.TickMusic(.00001f);
        CheckThat("music slider applies half gain while SFX preference stays intact", Mathf.Abs(music.HorizonSource.volume - full * .5f) < .012f &&
            PlayerPrefs.GetFloat("Ashfall.SfxVolume") == 1, Mix());
        CheckSfxUntouched("music slider never changes gameplay SFX gain");
        PlayerPrefs.SetFloat("Ashfall.MusicVolume", 0);
        music.TickMusic(.00001f);
        CheckThat("music mute silences both tracks while the SFX slider stays unchanged", music.PassageSource.volume == 0 &&
            music.HorizonSource.volume == 0 && PlayerPrefs.GetFloat("Ashfall.SfxVolume") == 1);
        PlayerPrefs.SetFloat("Ashfall.MusicVolume", 1);
        PlayerPrefs.SetFloat("Ashfall.SfxVolume", .35f);
        music.TickMusic(.00001f);
        CheckThat("mix reads and preserves the independent SFX preference", PlayerPrefs.GetFloat("Ashfall.SfxVolume") == .35f &&
            music.HorizonSource.volume > 0);
        CheckSfxUntouched("SFX preference changes are consumed without rewriting effect sources");
        PlayerPrefs.SetFloat("Ashfall.SfxVolume", 1);

        // A result freezes gameplay; the subdued score should continue at normal pitch.
        flow.FinishMission(false);
        yield return new WaitForSecondsRealtime(.5f);
        CheckThat("result screen keeps subdued music playing despite frozen gameplay time", flow.HasResult && Time.timeScale == 0 &&
            music.HorizonSource.isPlaying && music.HorizonSource.volume > 0 && music.HorizonSource.pitch == 1, Mix());
        Finish(null);
    }

    void CheckSfxUntouched(string name)
    {
        var states = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(s => s != music.PassageSource && s != music.HorizonSource).Select(s => new SoundState
            {
                source = s, clip = s.clip, volume = s.volume, pitch = s.pitch, blend = s.spatialBlend,
                priority = s.priority, mute = s.mute, enabled = s.enabled, loop = s.loop, playing = s.isPlaying
            }).ToArray();
        music.TickMusic(.03f);
        var changed = states.Where(s => s.source && (s.source.clip != s.clip || s.source.volume != s.volume || s.source.pitch != s.pitch ||
            s.source.spatialBlend != s.blend || s.source.priority != s.priority || s.source.mute != s.mute ||
            s.source.enabled != s.enabled || s.source.loop != s.loop || s.source.isPlaying != s.playing)).Select(s => s.source.name).ToArray();
        CheckThat(name, changed.Length == 0, "observed " + states.Length + " effect sources; modified=" + string.Join(", ", changed));
    }

    string Mix() => "track=" + (music.IsHorizon ? "Horizon" : "Passage") + "; reason=" + music.MixReason +
        "; duck=" + music.CurrentDuck.ToString("F3") + "/" + music.TargetDuck.ToString("F3") +
        "; volumes=" + music.PassageSource.volume.ToString("F4") + "/" + music.HorizonSource.volume.ToString("F4");

    static AudioSource Source(Component owner, string field) => owner ?
        owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner) as AudioSource : null;

    IEnumerator Guarded(IEnumerator sequence)
    {
        while (!finished)
        {
            object current;
            try { if (!sequence.MoveNext()) yield break; current = sequence.Current; }
            catch (Exception ex) { Finish(ex.ToString()); yield break; }
            yield return current;
        }
    }

    void Watch()
    {
        if (!finished && EditorApplication.timeSinceStartup > deadline) Finish("Music checks exceeded their wall-clock deadline.");
    }

    void PlayState(PlayModeStateChange state)
    {
        if (!finished && state == PlayModeStateChange.ExitingPlayMode) Finish("Play Mode was interrupted.", false);
    }

    void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        { report.errors.Add(message); Save(); }
    }

    void CheckThat(string name, bool passed, string evidence = "")
    {
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
        Save();
        Require(passed, name + ": " + evidence);
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    void Save()
    {
        report.elapsedSeconds = Time.realtimeSinceStartup - began;
        Directory.CreateDirectory(VolcanoMusicChecks.Folder);
        File.WriteAllText(VolcanoMusicChecks.Folder + "/checks.json", JsonUtility.ToJson(report, true));
    }

    void Finish(string error, bool exitPlay = true)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= Log;
        EditorApplication.update -= Watch;
        EditorApplication.playModeStateChanged -= PlayState;
        if (!string.IsNullOrEmpty(error)) report.errors.Add(error);
        report.status = string.IsNullOrEmpty(error) && report.errors.Count == 0 ? "COMPLETED" : "FAILED";
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        if (prepared)
        {
            Application.runInBackground = previousBackground; Time.timeScale = previousTimeScale;
            if (hadMusic) PlayerPrefs.SetFloat("Ashfall.MusicVolume", previousMusic); else PlayerPrefs.DeleteKey("Ashfall.MusicVolume");
            if (hadSfx) PlayerPrefs.SetFloat("Ashfall.SfxVolume", previousSfx); else PlayerPrefs.DeleteKey("Ashfall.SfxVolume");
        }
        Save();
        if (reloadLocked) { EditorApplication.UnlockReloadAssemblies(); reloadLocked = false; }
        if (exitPlay && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
