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
            Add(report, "100_litres_extinguishes", !fire.IsBurning && Near(fire.Heat, 0f), "Heat=" + fire.Heat);
            fire.TickFire(4.9f);
            fire.Ignite();
            Add(report, "five_second_reignition_cooldown", !fire.IsBurning, "Burning=" + fire.IsBurning);
            fire.TickFire(0.2f);
            fire.Ignite();
            Add(report, "dry_house_can_reignite_after_cooldown", fire.IsBurning, "Heat=" + fire.Heat);
            fire.ApplyWater(100f);
            fire.TickFire(6f);
            for (int i = 0; i < 10; i++)
            {
                fire.ApplyWater(1f);
                fire.TickFire(0.5f);
                fire.Ignite();
            }
            Add(report, "continuous_water_prevents_reignition", !fire.IsBurning, "Burning=" + fire.IsBurning);

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
            Add(report, "reactivated_heat_ignites_on_contact", contactFire.IsBurning, "Heat=" + contactFire.Heat);

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
            cannon.SetSpraying(true);
            cannon.TickSpray(1f);
            Add(report, "wall_blocks_water_cooling", Near(target.Heat, 1f) && cannon.HitFire == null && Near(tank.TankLitres, 165f),
                "Heat=" + target.Heat + "; tank=" + tank.TankLitres);
            wallCollider.enabled = false;
            Physics.SyncTransforms();
            cannon.TickSpray(1f);
            Add(report, "direct_hit_cools_by_consumed_litres", Near(target.Heat, 0.65f) && cannon.HitFire == target && Near(tank.TankLitres, 130f),
                "Heat=" + target.Heat + "; tank=" + tank.TankLitres);
            tank.SetTankLitres(5f);
            cannon.TickSpray(1f);
            Add(report, "last_partial_tank_is_used_exactly", Near(target.Heat, 0.6f) && Near(tank.TankLitres, 0f) && !cannon.IsSpraying,
                "Heat=" + target.Heat + "; tank=" + tank.TankLitres + "; spraying=" + cannon.IsSpraying);
            cannon.TickSpray(1f);
            Add(report, "empty_tank_cannot_extinguish", Near(target.Heat, 0.6f), "Heat=" + target.Heat);
            tank.SetTankLitres(100f);
            cannon.SetSpraying(false);
            cannon.TickSpray(1f);
            Add(report, "released_input_stops_consumption", Near(tank.TankLitres, 100f) && Near(target.Heat, 0.6f),
                "Heat=" + target.Heat + "; tank=" + tank.TankLitres);
            cannon.SetSpraying(true);
            wallCollider.enabled = true;
            truck.transform.position = wall.transform.position;
            Physics.SyncTransforms();
            cannon.TickSpray(1f);
            Add(report, "muzzle_inside_wall_does_not_shoot_through", Near(target.Heat, 0.6f) && cannon.HitFire == null,
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
            cannon.TickSpray(1f);
            Add(report, "non_convex_mesh_wall_blocks_water", Near(target.Heat, 0.6f) && cannon.HitFire == null &&
                Mathf.Abs(cannon.AimPoint.z - wall.transform.position.z) < 0.01f,
                "Heat=" + target.Heat + "; aim=" + cannon.AimPoint);
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

    private static void Add(Report report, string name, bool passed, string evidence)
    {
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    }
}
