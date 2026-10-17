using System;
using System.Collections.Generic;
using System.IO;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;

public static class FireCombatChecks
{
    [Serializable]
    private sealed class Check
    {
        public string name;
        public bool passed;
        public string evidence;
    }

    [Serializable]
    private sealed class Report
    {
        public bool allPassed;
        public string finishedUtc;
        public string error;
        public List<Check> checks = new List<Check>();
    }

    [MenuItem("GameJam/Fire Rescue/Check fire and water cannon (Play Mode)", true)]
    private static bool CanRun() => EditorApplication.isPlaying;

    [MenuItem("GameJam/Fire Rescue/Check fire and water cannon (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("Fire combat checks require Play Mode.");
        var report = new Report();
        var objects = new List<GameObject>();
        Mesh wallMesh = null;
        Mesh lavaMesh = null;
        Vector3 origin = new Vector3(15000f, 200f, 15000f);
        try
        {
            BurnableBuilding fire = CreateBuilding(objects, "Fire test target", origin);
            fire.Ignite();
            fire.ApplyWater(40f);
            Add(report, "proportional_water_cooling", Near(fire.Heat, 0.6f), "Heat=" + fire.Heat);
            fire.Ignite();
            Add(report, "repeated_lava_does_not_restore_heat", Near(fire.Heat, 0.6f), "Heat=" + fire.Heat);
            fire.ApplyWater(-10f);
            fire.ApplyWater(float.NaN);
            Add(report, "invalid_water_does_not_change_heat", Near(fire.Heat, 0.6f), "Heat=" + fire.Heat);
            fire.ApplyWater(60f);
            Add(report, "100_litres_extinguishes_and_rescues", !fire.IsBurning && fire.IsRescued && Near(fire.Heat, 0f), "Heat=" + fire.Heat);
            fire.TickFire(4.9f);
            fire.Ignite();
            Add(report, "five_second_reignition_cooldown", !fire.IsBurning, "Burning=" + fire.IsBurning);
            fire.TickFire(0.2f);
            fire.Ignite();
            Add(report, "rescued_house_never_reignites_after_cooldown", !fire.IsBurning && fire.IsRescued, "Heat=" + fire.Heat);
            fire.ApplyWater(100f);
            fire.TickFire(6f);
            for (int i = 0; i < 10; i++)
            {
                fire.ApplyWater(1f);
                fire.TickFire(0.5f);
                fire.Ignite();
            }
            Add(report, "continuous_water_prevents_reignition", !fire.IsBurning, "Burning=" + fire.IsBurning);
            BurnableBuilding wetHouse = CreateBuilding(objects, "Unburnt wet protection house", origin - Vector3.right * 10f);
            wetHouse.ApplyWater(1f);
            wetHouse.Ignite();
            Add(report, "watering_cold_house_does_not_mark_rescued", !wetHouse.IsRescued && !wetHouse.IsBurning, "Rescued=" + wetHouse.IsRescued);
            wetHouse.TickFire(3f);
            wetHouse.Ignite();
            Add(report, "unrescued_dry_house_can_ignite", wetHouse.IsBurning && !wetHouse.IsRescued, "Heat=" + wetHouse.Heat);

            GameObject lavaObject = NewObject(objects, "Rotated lava contact test", origin + Vector3.right * 12f);
            lavaObject.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            BoxCollider lavaBox = lavaObject.AddComponent<BoxCollider>();
            lavaBox.size = new Vector3(4f, 0.6f, 1f);
            lavaBox.isTrigger = true;
            LavaIgnitionSource lava = lavaObject.AddComponent<LavaIgnitionSource>();
            lava.heatCollider = lavaBox;
            lava.enabled = false;
            lava.enabled = true;
            BurnableBuilding contactFire = CreateBuilding(objects, "Trigger contact house", lavaObject.transform.position + new Vector3(1.2f, 0f, 1.2f));
            contactFire.ignitionBounds.size = Vector3.one * 0.2f;
            Physics.SyncTransforms();
            lava.ScanNow();
            Add(report, "no_ignition_in_rotated_lava_empty_aabb", !contactFire.IsBurning,
                "Burning=" + contactFire.IsBurning + "; contact collider is trigger=" + contactFire.ignitionBounds.isTrigger);
            contactFire.transform.position = lavaObject.transform.position;
            Physics.SyncTransforms();
            lava.ScanNow();
            Add(report, "actual_lava_trigger_contact_ignites", contactFire.IsBurning, "Heat=" + contactFire.Heat);
            contactFire.ApplyWater(100f);
            contactFire.TickFire(6f);
            MeshRenderer lavaRenderer = lavaObject.AddComponent<MeshRenderer>();
            lava.surfaceRenderer = lavaRenderer;
            lavaRenderer.enabled = false;
            lava.ScanNow();
            Add(report, "hidden_lava_does_not_ignite", !contactFire.IsBurning, "Burning=" + contactFire.IsBurning);
            lavaRenderer.enabled = true;
            lava.heatEnabled = false;
            lava.ScanNow();
            Add(report, "disabled_heat_does_not_ignite", !contactFire.IsBurning, "Burning=" + contactFire.IsBurning);
            lava.heatEnabled = true;
            lava.ScanNow();
            Add(report, "reactivated_heat_cannot_reignite_rescued_house", !contactFire.IsBurning && contactFire.IsRescued, "Heat=" + contactFire.Heat);
            BurnableBuilding freshContact = CreateBuilding(objects, "Fresh house touching active lava", lavaObject.transform.position);
            freshContact.ignitionBounds.size = Vector3.one * .2f;
            Physics.SyncTransforms();
            lava.ScanNow();
            Add(report, "reactivated_heat_ignites_fresh_house_on_contact", freshContact.IsBurning, "Heat=" + freshContact.Heat);

            GameObject meshLavaObject = NewObject(objects, "Separated lava strips", origin + Vector3.right * 40f);
            lavaMesh = new Mesh { name = "Fire combat test disconnected lava" };
            lavaMesh.vertices = new[]
            {
                new Vector3(-2.2f, 0f, -2f), new Vector3(-2.2f, 0f, 2f),
                new Vector3(-1.8f, 0f, 2f), new Vector3(-1.8f, 0f, -2f),
                new Vector3(1.8f, 0f, -2f), new Vector3(1.8f, 0f, 2f),
                new Vector3(2.2f, 0f, 2f), new Vector3(2.2f, 0f, -2f)
            };
            lavaMesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            lavaMesh.RecalculateBounds();
            MeshFilter lavaFilter = meshLavaObject.AddComponent<MeshFilter>();
            lavaFilter.sharedMesh = lavaMesh;
            MeshCollider lavaMeshCollider = meshLavaObject.AddComponent<MeshCollider>();
            LavaIgnitionSource meshSource = meshLavaObject.AddComponent<LavaIgnitionSource>();
            meshSource.heatCollider = lavaMeshCollider;
            meshSource.surfaceMesh = lavaFilter;
            meshSource.RefreshSurfaceMesh();
            BurnableBuilding meshContactFire = CreateBuilding(objects, "House in empty lava bounds", meshLavaObject.transform.position);
            meshContactFire.ignitionBounds.size = Vector3.one * 0.2f;
            Physics.SyncTransforms();
            meshSource.ScanNow();
            Add(report, "no_fire_between_disconnected_lava_mesh_strips", !meshContactFire.IsBurning,
                "Burning=" + meshContactFire.IsBurning);
            meshContactFire.transform.position += Vector3.right * 2f;
            Physics.SyncTransforms();
            meshSource.ScanNow();
            Add(report, "actual_non_convex_lava_surface_ignites_trigger_box", meshContactFire.IsBurning &&
                lavaMeshCollider.sharedMesh == lavaMesh,
                "Heat=" + meshContactFire.Heat + "; mesh synchronized=" + (lavaMeshCollider.sharedMesh == lavaMesh));

            Vector3 cannonOrigin = origin + Vector3.forward * 30f;
            GameObject truck = NewObject(objects, "Cannon test rig", cannonOrigin);
            FireHoseSystem tank = truck.AddComponent<FireHoseSystem>();
            tank.enabled = false;
            FireTruckWaterCannon cannon = truck.AddComponent<FireTruckWaterCannon>();
            cannon.enabled = false;
            cannon.tank = tank;
            cannon.muzzle = truck.transform;
            cannon.ignoredCollisionRoot = truck.transform;
            cannon.maxRange = 30f;
            cannon.flowLitresPerSecond = 35f;
            BurnableBuilding target = CreateBuilding(objects, "Cannon fire target", cannonOrigin + Vector3.forward * 10f);
            target.gameObject.AddComponent<BoxCollider>(); // Solid water-ray target, independent of the trigger footprint.
            target.Ignite();
            GameObject wall = NewObject(objects, "Water blocking wall", cannonOrigin + Vector3.forward * 5f);
            BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(3f, 3f, 0.5f);
            Physics.SyncTransforms();
            tank.SetTankLitres(200f);
            cannon.TickSpray(0f);
            Add(report, "prediction_identifies_blocking_wall", cannon.HasPredictedImpact && cannon.PredictedTarget == null &&
                Mathf.Abs(cannon.AimPoint.z - (wall.transform.position.z - .25f)) < .1f,
                "Aim=" + cannon.AimPoint + "; predicted=" + cannon.PredictedTarget);
            cannon.SetSpraying(true);
            cannon.TickSpray(1f);
            Drain(cannon, .5f);
            Add(report, "wall_blocks_water_cooling", Near(target.Heat, 1f) && cannon.HitFire == null && Near(tank.TankLitres, 165f),
                "Heat=" + target.Heat + "; tank=" + tank.TankLitres);
            wallCollider.enabled = false;
            Physics.SyncTransforms();
            cannon.TickSpray(0f);
            Add(report, "prediction_follows_gravity_to_actual_target", cannon.PredictedTarget == target &&
                cannon.AimPoint.y < cannonOrigin.y - .2f, "Aim=" + cannon.AimPoint);
            cannon.SetSpraying(true);
            cannon.TickSpray(.02f);
            Add(report, "emission_pays_litres_without_instant_cooling", Near(target.Heat, 1f) &&
                Near(tank.TankLitres, 164.3f) && cannon.InFlightPacketCount == 1 && cannon.HitFire == null,
                "Heat=" + target.Heat + "; inFlight=" + cannon.InFlightPacketCount);
            cannon.SetSpraying(false);
            cannon.TickSpray(.1f);
            Add(report, "release_keeps_water_in_flight", !cannon.IsSpraying && cannon.InFlightPacketCount == 1 &&
                Near(target.Heat, 1f) && Near(tank.TankLitres, 164.3f), "inFlight=" + cannon.InFlightPacketCount);
            float oldScale = Time.timeScale;
            Vector3 pausedPosition = cannon.NewestPacketPosition;
            try
            {
                Time.timeScale = 0f;
                cannon.TickSpray(1f);
                Add(report, "pause_freezes_water_and_litres", cannon.InFlightPacketCount == 1 &&
                    cannon.NewestPacketPosition == pausedPosition && Near(target.Heat, 1f) && Near(tank.TankLitres, 164.3f),
                    "position=" + cannon.NewestPacketPosition);
            }
            finally { Time.timeScale = oldScale; }
            cannon.TickSpray(.3f);
            Add(report, "released_packet_cools_only_on_later_impact", Near(target.Heat, .993f) &&
                cannon.InFlightPacketCount == 0 && cannon.HitFire == target, "Heat=" + target.Heat);
            cannon.SetSpraying(true);
            cannon.TickSpray(1f);
            Drain(cannon, .5f);
            Add(report, "direct_hit_cools_by_consumed_litres", Near(target.Heat, 0.643f) && cannon.HitFire == target && Near(tank.TankLitres, 129.3f),
                "Heat=" + target.Heat + "; tank=" + tank.TankLitres);
            tank.SetTankLitres(5f);
            cannon.SetSpraying(true);
            cannon.TickSpray(1f);
            Drain(cannon, .5f);
            Add(report, "last_partial_tank_is_used_exactly", Near(target.Heat, 0.593f) && Near(tank.TankLitres, 0f) && !cannon.IsSpraying,
                "Heat=" + target.Heat + "; tank=" + tank.TankLitres + "; spraying=" + cannon.IsSpraying);
            cannon.TickSpray(1f);
            Add(report, "empty_tank_cannot_extinguish", Near(target.Heat, 0.593f), "Heat=" + target.Heat);
            tank.SetTankLitres(100f);
            cannon.SetSpraying(false);
            cannon.TickSpray(1f);
            Add(report, "released_input_stops_consumption", Near(tank.TankLitres, 100f) && Near(target.Heat, 0.593f),
                "Heat=" + target.Heat + "; tank=" + tank.TankLitres);
            cannon.SetSpraying(true);
            wallCollider.enabled = true;
            truck.transform.position = wall.transform.position;
            Physics.SyncTransforms();
            cannon.TickSpray(1f);
            Drain(cannon, .5f);
            Add(report, "muzzle_inside_wall_does_not_shoot_through", Near(target.Heat, 0.593f) && cannon.HitFire == null,
                "Heat=" + target.Heat + "; aim=" + cannon.AimPoint);
            truck.transform.position = cannonOrigin;
            wallCollider.enabled = false;
            wallMesh = new Mesh { name = "Fire combat test non-convex wall" };
            wallMesh.vertices = new[]
            {
                new Vector3(-2f, -2f, 0f), new Vector3(-2f, 2f, 0f),
                new Vector3(2f, 2f, 0f), new Vector3(2f, -2f, 0f)
            };
            wallMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            wallMesh.RecalculateBounds();
            MeshCollider meshWallCollider = wall.AddComponent<MeshCollider>();
            meshWallCollider.sharedMesh = wallMesh;
            meshWallCollider.convex = false;
            Physics.SyncTransforms();
            cannon.SetSpraying(true);
            cannon.TickSpray(1f);
            Drain(cannon, .5f);
            Add(report, "non_convex_mesh_wall_blocks_water", Near(target.Heat, 0.593f) && cannon.HitFire == null &&
                Mathf.Abs(cannon.AimPoint.z - wall.transform.position.z) < 0.01f,
                "Heat=" + target.Heat + "; aim=" + cannon.AimPoint);

            meshWallCollider.enabled = false;
            truck.transform.position = cannonOrigin + Vector3.right * 20f;
            Rigidbody movingBody = truck.AddComponent<Rigidbody>();
            movingBody.useGravity = false;
            movingBody.linearVelocity = Vector3.right * 10f;
            tank.SetTankLitres(100f);
            Physics.SyncTransforms();
            cannon.SetSpraying(true);
            cannon.TickSpray(.02f);
            Add(report, "water_inherits_vehicle_velocity", Near(cannon.LaunchVelocity.x, 10f) &&
                Near(cannon.NewestPacketPosition.x - truck.transform.position.x, .2f),
                "velocity=" + cannon.LaunchVelocity + "; packet=" + cannon.NewestPacketPosition);
            cannon.SetSpraying(false);
            cannon.TickSpray(8f);
            Add(report, "missed_water_expires_without_pool_leak", cannon.InFlightPacketCount == 0,
                "inFlight=" + cannon.InFlightPacketCount);
            tank.SetTankLitres(1000f);
            cannon.SetSpraying(true);
            int peakPackets = 0;
            for (int frame = 0; frame < 1440; frame++)
            {
                cannon.TickSpray(1f / 144f);
                peakPackets = Mathf.Max(peakPackets, cannon.InFlightPacketCount);
            }
            Add(report, "144hz_long_spray_preserves_flow_and_bounded_pool", cannon.IsSpraying && peakPackets <= 402 &&
                Mathf.Abs(tank.TankLitres - 650f) < .71f,
                "remaining=" + tank.TankLitres + "; peakPackets=" + peakPackets);
            Drain(cannon, 8.1f);
            Add(report, "all_high_frequency_packets_return_to_pool", cannon.InFlightPacketCount == 0,
                "inFlight=" + cannon.InFlightPacketCount);

            Vector3 arcOrigin = cannonOrigin + Vector3.right * 60f;
            GameObject arcRig = NewObject(objects, "Ballistic aim rig", arcOrigin);
            FireHoseSystem arcTank = arcRig.AddComponent<FireHoseSystem>();
            arcTank.enabled = false;
            var arcCannon = arcRig.AddComponent<FireTruckWaterCannon>(); arcCannon.enabled = false;
            Transform yaw = NewObject(objects, "Ballistic yaw", arcOrigin).transform; yaw.SetParent(arcRig.transform, true);
            Transform pitch = NewObject(objects, "Ballistic pitch", arcOrigin).transform; pitch.SetParent(yaw, true);
            Transform arcMuzzle = NewObject(objects, "Ballistic muzzle", arcOrigin).transform; arcMuzzle.SetParent(pitch, true);
            Camera camera = NewObject(objects, "Ballistic aim camera", arcOrigin + Vector3.up * 4f).AddComponent<Camera>();
            camera.enabled = false;
            BurnableBuilding arcTarget = CreateBuilding(objects, "Thirty metre target", arcOrigin + Vector3.forward * 30f);
            arcTarget.gameObject.AddComponent<BoxCollider>(); arcTarget.Ignite();
            camera.transform.LookAt(arcTarget.transform.position);
            arcCannon.tank = arcTank; arcCannon.yawPivot = yaw; arcCannon.pitchPivot = pitch;
            arcCannon.muzzle = arcMuzzle; arcCannon.aimCamera = camera; arcCannon.ignoredCollisionRoot = arcRig.transform;
            arcTank.SetTankLitres(100f);
            Physics.SyncTransforms(); arcCannon.TickSpray(0f);
            Add(report, "low_arc_compensates_gravity_for_camera_target", arcCannon.PredictedTarget == arcTarget &&
                arcCannon.HasReachableAim && arcCannon.LaunchVelocity.y > 2f && !arcCannon.IsAimObstructed,
                "velocity=" + arcCannon.LaunchVelocity + "; impact=" + arcCannon.AimPoint);
            arcCannon.SetSpraying(true); arcCannon.TickSpray(.02f);
            Add(report, "distant_house_waits_for_physical_water", Near(arcTarget.Heat, 1f), "Heat=" + arcTarget.Heat);
            Drain(arcCannon, 1.2f);
            Add(report, "predicted_low_arc_matches_later_impact", Near(arcTarget.Heat, .993f) && arcCannon.HitFire == arcTarget,
                "Heat=" + arcTarget.Heat);
            GameObject lowWall = NewObject(objects, "Camera can see over low wall", arcOrigin + Vector3.forward * 5f);
            lowWall.AddComponent<BoxCollider>().size = new Vector3(3f, 3f, .5f);
            Physics.SyncTransforms(); arcCannon.TickSpray(0f);
            Add(report, "muzzle_obstruction_overrides_camera_target", arcCannon.HasPredictedImpact &&
                arcCannon.IsAimObstructed && !arcCannon.HasReachableAim && !arcCannon.PredictedTarget,
                "impact=" + arcCannon.AimPoint + "; obstructed=" + arcCannon.IsAimObstructed);
            arcCannon.SetSpraying(true); arcCannon.TickSpray(.5f); Drain(arcCannon, 1.2f);
            Add(report, "low_arc_cannot_cool_through_cover", Near(arcTarget.Heat, .993f), "Heat=" + arcTarget.Heat);
        }
        catch (Exception exception)
        {
            report.error = exception.ToString();
        }
        finally
        {
            foreach (GameObject item in objects)
                if (item != null)
                    UnityEngine.Object.DestroyImmediate(item);
            if (wallMesh != null)
                UnityEngine.Object.DestroyImmediate(wallMesh);
            if (lavaMesh != null)
                UnityEngine.Object.DestroyImmediate(lavaMesh);
            Physics.SyncTransforms();
            report.allPassed = string.IsNullOrEmpty(report.error) && report.checks.Count > 0;
            foreach (Check check in report.checks)
                report.allPassed &= check.passed;
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/FireCombatChecks.json", JsonUtility.ToJson(report, true));
            if (report.allPassed)
                Debug.Log("Fire combat checks passed: " + report.checks.Count + ". Report: Logs/FireCombatChecks.json");
            else
                Debug.LogError("Fire combat checks failed. Inspect Logs/FireCombatChecks.json\n" + report.error);
        }
    }

    private static GameObject NewObject(List<GameObject> objects, string name, Vector3 position)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        item.transform.position = position;
        objects.Add(item);
        return item;
    }

    private static BurnableBuilding CreateBuilding(List<GameObject> objects, string name, Vector3 position)
    {
        GameObject item = NewObject(objects, name, position);
        BoxCollider bounds = item.AddComponent<BoxCollider>();
        bounds.isTrigger = true;
        BurnableBuilding building = item.AddComponent<BurnableBuilding>();
        building.ignitionBounds = bounds;
        return building;
    }

    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < 0.001f;

    private static void Drain(FireTruckWaterCannon cannon, float seconds)
    {
        cannon.SetSpraying(false);
        cannon.TickSpray(seconds);
    }

    private static void Add(Report report, string name, bool passed, string evidence)
    {
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    }
}
