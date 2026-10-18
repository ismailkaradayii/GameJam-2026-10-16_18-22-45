using System;
using GameJam.FireRescue;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

public static class VolcanoHazardChecks
{
    public static void Run(Action<string,bool,string> check)
    {
        var root = new GameObject("Mission hazard verification fixture");
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        Vector3 savedPosition = player.transform.position;
        bool bodyEnabled = player.playerBody.enabled;
        var leader = player.GetComponent<NavMeshAgent>(); if (leader) leader.enabled = false;
        Mesh mesh = null;
        try
        {
            Vector3 origin = new Vector3(18000, 1000, 18000);
            var lava = new GameObject("Test lava surface"); lava.transform.SetParent(root.transform); lava.transform.position = origin;
            mesh = new Mesh(); mesh.vertices = new[] { new Vector3(-25,0,-25),new Vector3(-25,0,25),new Vector3(25,0,-25),new Vector3(25,0,25) };
            mesh.triangles = new[] {0,1,2,2,1,3}; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            lava.AddComponent<MeshFilter>().sharedMesh = mesh; lava.AddComponent<MeshRenderer>();
            var collider = lava.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
            var source = lava.AddComponent<LavaIgnitionSource>(); source.heatCollider = collider;
            var cooling = lava.AddComponent<LavaCoolingSurface>(); cooling.steamMaterial = Resources.Load<Material>("LavaCombat/Smoke");
            var houseObject = new GameObject("House at lava contact"); houseObject.transform.SetParent(root.transform); houseObject.transform.position = origin;
            var box = houseObject.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(3,3,3);
            var house = houseObject.AddComponent<BurnableBuilding>(); house.ignitionBounds = box; house.secondsToCollapse = 90;
            Physics.SyncTransforms(); source.ScanNow();
            check("visible_hot_lava_ignites_house",house.IsBurning,"Actual mesh penetrates the fitted ignition volume.");
            house.TickFire(45); check("house_does_not_collapse_early",!house.IsBurnedOut&&Mathf.Abs(house.SecondsUntilCollapse-45)<.01f,"45 seconds of a 90-second fire.");
            house.ApplyWater(house.litresToExtinguish); house.TickFire(200);
            check("extinguished_house_stops_burning_down",!house.IsBurnedOut&&!house.IsBurning,"Water stops cumulative fire damage.");

            player.playerBody.enabled = false; player.transform.position = origin - Vector3.up * .65f; player.playerBody.enabled = true; Physics.SyncTransforms();
            var contact = player.GetComponent<LavaPlayerContact>(); contact.RefreshCollisionPassThrough();
            var health = player.GetComponent<CreatureCombatPlayer>(); float before = health.Health;
            contact.TickContact(1);
            check("extinguished_foundation_protects_player",!contact.InHotLava&&health.Health==before&&!source.IsHotAt(origin),"Standing on the house's water-cooled foundation causes no contact damage.");
            Vector3 hotPoint = origin + Vector3.right * 10;
            player.playerBody.enabled = false; player.transform.position = hotPoint - Vector3.up * .65f; player.playerBody.enabled = true; Physics.SyncTransforms();
            before = health.Health; contact.TickContact(1);
            check("molten_lava_damages_player",contact.InHotLava&&Mathf.Abs(health.Health-(before-18))<.02f,"One second of immersion removes 18 health.");
            check("lava_is_passable_but_still_raycastable",Physics.GetIgnoreCollision(player.playerBody,collider)&&collider.Raycast(new Ray(hotPoint+Vector3.up*10,Vector3.down),out _,20),"Character passes through; water still hits the same visible surface.");

            var rig = new GameObject("Actual water cannon fixture"); rig.transform.SetParent(root.transform); rig.transform.position = hotPoint + new Vector3(0,12,-10);
            rig.transform.rotation = Quaternion.LookRotation(hotPoint-rig.transform.position);
            var tank = rig.AddComponent<FireHoseSystem>(); tank.enabled = false; tank.SetTankLitres(1000);
            var cannon = rig.AddComponent<FireTruckWaterCannon>(); cannon.enabled = false; cannon.tank = tank; cannon.muzzle = rig.transform; cannon.ignoredCollisionRoot = rig.transform;
            // The player's collision test is finished; keep its body out of this water-only fixture.
            player.playerBody.enabled = false; Physics.SyncTransforms();
            bool hitCooling = false;
            cannon.SetSpraying(true);
            for (int i=0;i<200;i++) { cannon.TickSpray(.02f); hitCooling |= cannon.HitLava==cooling; }
            cannon.SetSpraying(false);
            for (int i=0;i<100;i++) { cannon.TickSpray(.02f); hitCooling |= cannon.HitLava==cooling; }
            player.playerBody.enabled = bodyEnabled; Physics.SyncTransforms();
            check("water_cannon_cools_lava_and_consumes_tank",hitCooling&&!cooling.IsHotAt(hotPoint)&&Mathf.Abs(tank.TankLitres-860)<.02f,"Four seconds emit 140 litres; physical water finishes travelling after release.");
            check("cooling_is_local",cooling.IsHotAt(origin-Vector3.right*20)&&!cooling.IsHotAt(hotPoint),"Distant molten lava on the same mesh remains hot.");
            before=health.Health; contact.TickContact(1);
            check("cooled_lava_stops_contact_damage",!contact.InHotLava&&health.Health==before,"Cooled patch no longer burns the player.");
            source.ScanNow(); check("cooled_lava_stops_house_ignition",!house.IsBurning&&cooling.CoolingAt(origin)>=1&&!source.IsHotAt(origin),"The rescued house keeps its cold foundation after the nearby cannon test.");
            var doomedObject = new GameObject("Unrescued house collapse fixture"); doomedObject.transform.SetParent(root.transform);
            var doomed = doomedObject.AddComponent<BurnableBuilding>(); doomed.secondsToCollapse = 90;
            doomed.Ignite(); doomed.TickFire(100);
            check("unattended_house_becomes_ash",doomed.IsBurnedOut&&!doomed.IsBurning&&doomed.transform.Find("Burned house · ash and charred beams"),"An unrescued building collapses; the previously rescued house stays safe.");
        }
        finally
        {
            player.playerBody.enabled=false; player.transform.position=savedPosition; player.playerBody.enabled=bodyEnabled;
            Object.DestroyImmediate(root); if(mesh)Object.DestroyImmediate(mesh);
        }
    }
}
