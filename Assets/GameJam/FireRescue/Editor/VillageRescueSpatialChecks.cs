using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class VillageRescueSpatialChecks
{
    [Serializable] sealed class Blocker { public string collider; public Vector3 position; public float penetration; }
    [Serializable] sealed class Report
    {
        public bool allPassed;
        public int truckPoses, checkedSolids, houses, residents;
        public List<Blocker> blockers = new List<Blocker>();
        public List<string> errors = new List<string>();
    }
    [MenuItem("GameJam/Final Loop/Check village streets")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode for street clearance.");
        var report = new Report();
        try
        {
            var navigation = Object.FindFirstObjectByType<ObjectiveNavigation>();
            var truck = Object.FindFirstObjectByType<FireTruckDrive>();
            var body = truck.GetComponent<BoxCollider>();
            var houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None);
            var dressing = GameObject.Find("Town Dressing").transform;
            report.houses = houses.Length; report.residents = Object.FindObjectsByType<EvacuationResident>(FindObjectsSortMode.None).Length;
            var solids = houses.SelectMany(h => h.GetComponentsInChildren<Collider>(true)).Concat(dressing.GetComponentsInChildren<Collider>(true))
                .Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c is not WheelCollider).Distinct().ToArray();
            report.checkedSolids = solids.Length; Physics.SyncTransforms();
            var road = navigation.lakeRoad.Where(t => t).Select(t => t.position).ToArray();
            float aboveGround = truck.transform.position.y - VillageRescuePolishSetup.Ground(truck.transform.position);
            for (int segment = 1; segment < road.Length; segment++)
            {
                Vector3 a = road[segment - 1], b = road[segment];
                int steps = Mathf.CeilToInt(EvacuationResident.FlatDistance(a,b) / 2f);
                Quaternion rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(b-a,Vector3.up));
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 p = Vector3.Lerp(a,b,i/(float)Mathf.Max(1,steps)); p.y = VillageRescuePolishSetup.Ground(p)+aboveGround; report.truckPoses++;
                    foreach (var solid in solids)
                    {
                        if (!Physics.ComputePenetration(body,p,rotation,solid,solid.transform.position,solid.transform.rotation,out _,out float depth) || depth < .05f) continue;
                        string name = Hierarchy(solid.transform);
                        if (report.blockers.Any(x => x.collider == name)) continue;
                        report.blockers.Add(new Blocker { collider = name, position = p, penetration = depth });
                    }
                }
            }
        }
        catch (Exception e) { report.errors.Add(e.ToString()); }
        report.allPassed = report.errors.Count == 0 && report.blockers.Count == 0 && report.houses == 7 && report.residents == 7;
        File.WriteAllText(VillageRescuePolishSetup.Output + "/After/street-clearance.json", JsonUtility.ToJson(report,true));
        Debug.Log("Village streets: " + (report.allPassed ? "PASS" : "FAIL") + ", poses=" + report.truckPoses + ", blockers=" + report.blockers.Count);
    }
    static string Hierarchy(Transform t) => t.parent ? Hierarchy(t.parent)+"/"+t.name : t.name;
}
