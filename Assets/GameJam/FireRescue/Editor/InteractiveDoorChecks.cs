using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Explicit isolated physics/API checks; does not change or save the authored scene.</summary>
public static class InteractiveDoorChecks
{
    [Serializable] sealed class Check { public string name; public bool passed; public string evidence; }
    [Serializable] sealed class Report
    {
        public string status = "running", error;
        public string method = "Isolated clone of the authored leaf, real physics queries and player E-route API; no OS keyboard automation.";
        public List<Check> checks = new List<Check>();
    }
    static Report report;
    static GameObject fixture;
    static InteractiveDoor door;
    static FireRescuePlayer player;
    static Vector3 origin;

    [MenuItem("GameJam/Environment/Check opendoor (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            throw new InvalidOperationException("Run in unpaused Volcano_Demo Play Mode, after configuring opendoor.");
        float timeScale = Time.timeScale;
        CursorLockMode cursor = Cursor.lockState; bool cursorVisible = Cursor.visible;
        InteractiveDoor authored = null;
        report = new Report();
        try
        {
            authored = Object.FindObjectsByType<InteractiveDoor>(FindObjectsSortMode.None).Single(d => d.name == "opendoor");
            Require("authored_hinge_and_motion", authored.leafCollider && Mathf.Abs(Mathf.Abs(authored.openAngle) - 90f) < .01f && Mathf.Abs(authored.travelSeconds - .65f) < .001f,
                "angle=" + authored.openAngle + "; seconds=" + authored.travelSeconds);
            Require("moving_leaf_not_static", !authored.gameObject.isStatic && authored.GetComponent<Rigidbody>().isKinematic,
                "leaf collider=" + authored.leafCollider.GetType().Name);
            Time.timeScale = 1f;
            // Exercise the real scene before the isolated fixture: final furniture, jambs and
            // every existing wall remain present. Do not ignore or move any of those colliders.
            Require("authored_scene_starts_closed", !authored.IsOpen && !authored.IsMoving && authored.OpenAmount < .001f,
                "actual opendoor in " + authored.gameObject.scene.name);
            Require("authored_scene_open_arc_clear", authored.TryToggle(), BlockerEvidence(authored));
            StepDoor(authored, .65f);
            Require("authored_scene_fully_opens", authored.IsOpen && !authored.IsMoving && !authored.LastBlocked,
                "actual scene open=" + authored.OpenAmount + "; " + BlockerEvidence(authored));
            Require("authored_scene_close_arc_clear", authored.TryToggle(), BlockerEvidence(authored));
            StepDoor(authored, .65f);
            Require("authored_scene_returns_closed", !authored.IsOpen && !authored.IsMoving && authored.OpenAmount < .001f &&
                Quaternion.Angle(authored.transform.localRotation, authored.ClosedLocalRotation) < .001f,
                "actual frame and final decoration retained; " + BlockerEvidence(authored));
            fixture = new GameObject("Door verification fixture"); origin = new Vector3(20000, 100, 20000);
            var clone = Object.Instantiate(authored.gameObject, origin, Quaternion.identity, fixture.transform);
            clone.name = "Door test clone"; clone.transform.localScale = authored.transform.lossyScale;
            door = clone.GetComponent<InteractiveDoor>(); door.enabled = false;
            door.ConfigureClosedPose(Quaternion.identity, clone.GetComponent<BoxCollider>()); door.enabled = true;
            var actor = new GameObject("Door interaction test actor"); actor.SetActive(false); actor.transform.SetParent(fixture.transform);
            var controller = actor.AddComponent<CharacterController>(); controller.height = 1.8f; controller.radius = .3f; controller.center = Vector3.up * .9f;
            var cameraObject = new GameObject("Door test camera"); cameraObject.transform.SetParent(actor.transform);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            player = actor.AddComponent<FireRescuePlayer>(); player.startDriving = false; player.lockCursorOnStart = false;
            player.showHud = false; player.playerBody = controller; player.viewCamera = camera;
            player.hand = new GameObject("Test hand").transform; player.hand.SetParent(actor.transform, false); player.hand.localPosition = Vector3.up;
            actor.SetActive(true); player.enabled = false;
            Physics.SyncTransforms();

            AimFromSide(-1);
            Require("closed_door_from_front", player.TryGetDoorTarget(out var front) && front == door && player.InteractionPrompt == "E  Kapıyı aç", "front center ray");
            AimFromSide(1);
            Require("closed_door_from_back", player.TryGetDoorTarget(out var back) && back == door, "back center ray");
            Vector3 savedPosition = camera.transform.position;
            camera.transform.position += (camera.transform.position - door.leafCollider.bounds.center).normalized * 3f;
            Require("three_metre_reach", !player.TryGetDoorTarget(out _), "camera moved beyond3m");
            camera.transform.position = savedPosition;

            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Solid ray obstruction";
            blocker.transform.SetParent(fixture.transform); blocker.transform.localScale = Vector3.one * .35f;
            blocker.transform.position = camera.transform.position + camera.transform.forward;
            Physics.SyncTransforms();
            Require("wall_blocks_interaction", !player.TryGetDoorTarget(out _) && !player.Interact() && !door.IsMoving, "nearest solid cube blocks the door");
            blocker.GetComponent<Collider>().isTrigger = true; Physics.SyncTransforms();
            Require("trigger_does_not_hide_door", player.TryGetDoorTarget(out var throughTrigger) && throughTrigger == door, "trigger ignored, solid leaf selected");
            Object.DestroyImmediate(blocker);

            // Stand opposite the arc while moving it; keep the real CharacterController active.
            AimFromSide(door.openAngle > 0 ? -1 : 1);
            Vector3 knobClosed = door.transform.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("door_knob")).position;
            Require("E_route_opens", player.Interact() && door.IsMoving, "same public route called by the E key");
            Step(.325f);
            Require("halfway_in_half_duration", door.OpenAmount > .45f && door.OpenAmount < .55f, "open=" + door.OpenAmount);
            float frozen = door.OpenAmount; Quaternion frozenRotation = door.transform.rotation;
            Time.timeScale = 0;
            door.TickMotion(1f);
            Require("paused_motion_and_toggle_blocked", !door.TryToggle() && Mathf.Abs(door.OpenAmount - frozen) < .00001f && Quaternion.Angle(frozenRotation, door.transform.rotation) < .001f, "scaled time zero");
            Time.timeScale = 1; Step(.4f);
            Require("ninety_degree_open_with_handles", door.IsOpen && !door.IsMoving && Quaternion.Angle(Quaternion.identity, door.transform.rotation) > 89.9f &&
                Vector3.Distance(knobClosed, door.transform.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("door_knob")).position) > .5f, "open=" + door.OpenAmount);
            Require("closed_passage_now_clear", !door.leafCollider.Raycast(new Ray(origin + new Vector3(-2.5f, door.leafCollider.center.y * door.transform.lossyScale.y, door.leafCollider.center.z * door.transform.lossyScale.z),
                Vector3.right), out _, 5f), "ray through closed leaf center no longer touches the moving collider");

            // Put a solid object midway through the closing arc, away from the start and final poses.
            blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Closing arc blocker"; blocker.transform.SetParent(fixture.transform);
            blocker.transform.localScale = Vector3.one * .35f;
            Vector3 centerLocal = Vector3.Scale(door.leafCollider.center, door.transform.lossyScale);
            blocker.transform.position = origin + Quaternion.Euler(0, door.openAngle * .5f, 0) * centerLocal;
            Physics.SyncTransforms();
            Require("closing_arc_blocks_object", !door.TryToggle() && door.LastBlocked && door.IsOpen, "solid object in swept arc");
            Object.DestroyImmediate(blocker);
            Vector3 actorBefore = player.transform.position;
            player.playerBody.enabled = false;
            player.transform.position = origin + Quaternion.Euler(0, door.openAngle * .5f, 0) * centerLocal - Vector3.up * .9f;
            player.playerBody.enabled = true; Physics.SyncTransforms();
            Require("closing_arc_blocks_player", !door.TryToggle() && door.LastBlocked && door.IsOpen, "real CharacterController in swept arc");
            player.playerBody.enabled = false; player.transform.position = actorBefore; player.playerBody.enabled = true; Physics.SyncTransforms();
            Require("unblocked_close_starts", door.TryToggle(), "closing arc clear"); Step(.7f);
            Require("returns_to_authored_closed_pose", !door.IsOpen && !door.IsMoving && door.OpenAmount < .001f && Quaternion.Angle(door.transform.rotation, Quaternion.identity) < .001f, "closed after full travel");

            AimFromSide(door.openAngle > 0 ? -1 : 1);
            player.SetGameplaySuspended(true);
            Require("pause_and_win_gate_E", !player.Interact() && !player.TryGetDoorTarget(out _) && !door.IsMoving, "shared gameplay suspension gate");
            player.SetGameplaySuspended(false);
            Require("resume_frame_E_gate", !player.Interact() && !door.IsMoving, "resume release guard prevents stale interaction");
            // This synchronous API fixture has no rendered input frames. Clear only the release guard,
            // then verify E priorities, leaving real input/pause integration to the game coordinator.
            typeof(FireRescuePlayer).GetField("waitForMouseRelease", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(player, false);
            var hoseObject = new GameObject("Test intake"); hoseObject.transform.SetParent(fixture.transform);
            var hose = hoseObject.AddComponent<FireHoseSystem>(); hose.enabled = false;
            hose.hoseSocket = new GameObject("Socket").transform; hose.hoseSocket.SetParent(hoseObject.transform); hose.hoseSocket.position = player.hand.position;
            hose.nozzle = new GameObject("Nozzle").transform; hose.nozzle.SetParent(hoseObject.transform); hose.nozzle.position = hose.hoseSocket.position;
            player.hose = hose;
            Require("visible_door_precedes_nearby_hose", player.Interact() && door.IsMoving && !hose.IsCarried && player.InteractionPrompt == door.Prompt, "door and socket both eligible");
            door.ConfigureClosedPose(Quaternion.identity, door.leafCollider); Physics.SyncTransforms();
            Require("fixture_can_carry_intake", hose.TryPickup(player.hand), "real hose pickup API");
            Require("carried_hose_precedes_door_prompt", player.InteractionPrompt.Contains("hortum ucunu bırak") && !player.TryGetDoorTarget(out _), "carried intake owns E");
            Require("E_drops_without_opening", player.Interact() && !hose.IsCarried && hose.IsDeployed && !door.IsMoving, "intake drop through real camera surface ray");
            hose.Retract(); camera.transform.rotation = Quaternion.LookRotation(Vector3.up);
            Require("hose_pickup_without_target_door", player.Interact() && hose.IsCarried && !door.IsMoving, "nearby socket fallback");
            hose.Drop(hose.hoseSocket.position);
            Require("deployed_nozzle_at_socket_has_matching_E_R_prompt", player.InteractionPrompt.Contains("E  Hortum ucunu al") &&
                player.InteractionPrompt.Contains("R Hortumu topla"), "deployed nozzle and rear socket both within reach");
            Require("deployed_nozzle_at_socket_E_picks_up", player.Interact() && hose.IsCarried && !door.IsMoving,
                "displayed E action matches coordinator behavior; R remains separate");
            hose.Retract(); player.hose = null; AimFromSide(door.openAngle > 0 ? -1 : 1);
            Require("prepare_reloaded_door", door.TryToggle(), "open before re-instantiating"); Step(.7f);
            var reloaded = Object.Instantiate(door.gameObject, origin + Vector3.forward * 20, door.transform.rotation, fixture.transform).GetComponent<InteractiveDoor>();
            Require("new_instance_resets_closed", !reloaded.IsOpen && !reloaded.IsMoving && reloaded.OpenAmount == 0 && Quaternion.Angle(reloaded.transform.localRotation, reloaded.ClosedLocalRotation) < .001f,
                "Awake uses saved closed pose, no static state");
            report.status = "passed";
        }
        catch (Exception error) { report.status = "failed"; report.error = error.ToString(); Debug.LogException(error); }
        finally
        {
            if (fixture) Object.DestroyImmediate(fixture);
            if (authored) authored.ConfigureClosedPose(authored.ClosedLocalRotation, authored.leafCollider);
            Physics.SyncTransforms();
            Time.timeScale = timeScale; Cursor.lockState = cursor; Cursor.visible = cursorVisible;
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/InteractiveDoorChecks.json", JsonUtility.ToJson(report, true));
            Debug.Log("INTERACTIVE_DOOR_CHECKS: " + report.status + "; assertions=" + report.checks.Count);
        }
    }

    static void AimFromSide(int side)
    {
        Vector3 center = origin + Vector3.Scale(door.leafCollider.center, door.transform.lossyScale);
        Vector3 cameraPoint = center + Vector3.right * (side * 2.4f);
        player.playerBody.enabled = false; player.transform.position = cameraPoint - Vector3.up * 1.65f; player.playerBody.enabled = true;
        player.viewCamera.transform.SetPositionAndRotation(cameraPoint, Quaternion.LookRotation(center - cameraPoint));
        Physics.SyncTransforms();
    }
    static void Step(float seconds) => StepDoor(door, seconds);
    static void StepDoor(InteractiveDoor target, float seconds)
    {
        while (seconds > .00001f) { float delta = Mathf.Min(.01f, seconds); target.TickMotion(delta); Physics.SyncTransforms(); seconds -= delta; }
    }
    static string BlockerEvidence(InteractiveDoor target) => target.LastBlockingCollider
        ? "blocker=" + target.LastBlockingCollider.name + "; depth=" + target.LastBlockingDepth + "; angle=" + target.LastBlockingAngle
        : "no blocking collider";
    static void Require(string name, bool passed, string evidence)
    {
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
        if (!passed) throw new InvalidOperationException(name + ": " + evidence);
    }
}
