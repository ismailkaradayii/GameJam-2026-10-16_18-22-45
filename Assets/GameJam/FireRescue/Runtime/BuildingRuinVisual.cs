using UnityEngine;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent]
    public sealed class BuildingRuinVisual : MonoBehaviour
    {
        Material ash;
        Mesh ashMesh;
        public void Collapse(BurnableBuilding building)
        {
            Bounds footprint = building.ignitionBounds ? building.ignitionBounds.bounds : new Bounds(transform.position + Vector3.up * 5, Vector3.one * 15);
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>()) renderer.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            ash = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            ash.name = "Charred rubble (runtime)"; ash.color = new Color(.12f, .10f, .085f);
            ash.SetFloat("_Smoothness", .07f);
            var rubble = new GameObject("Burned house · ash and charred beams"); rubble.transform.SetParent(transform, false);
            var random = new System.Random(Mathf.RoundToInt(footprint.center.x * 100));
            float ground = footprint.min.y + .15f;
            var terrain = Terrain.activeTerrain;
            if (terrain) ground = Mathf.Max(ground, terrain.SampleHeight(footprint.center) + terrain.transform.position.y + .12f);
            CreateAshBed(rubble.transform, footprint, ground, random);
            for (int i = 0; i < 28; i++)
            {
                float x = (float)random.NextDouble() - .5f, z = (float)random.NextDouble() - .5f;
                float height = .25f + (float)random.NextDouble() * .6f;
                Vector3 position = new Vector3(footprint.center.x + x * footprint.size.x * .72f, ground + height * .5f, footprint.center.z + z * footprint.size.z * .72f);
                CreatePiece(rubble.transform, position, new Vector3(.3f + (float)random.NextDouble() * .65f, height, 2f + (float)random.NextDouble() * 5),
                    Quaternion.Euler((float)random.NextDouble() * 12, (float)random.NextDouble() * 180, 5), true);
            }
            if (building.smoke)
            {
                var dust = Instantiate(building.smoke, rubble.transform); dust.name = "Collapse ash cloud";
                dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                dust.transform.SetPositionAndRotation(new Vector3(footprint.center.x, ground + .35f, footprint.center.z), Quaternion.identity);
                Vector3 parentScale = rubble.transform.lossyScale;
                dust.transform.localScale = new Vector3(1/Mathf.Max(.001f,Mathf.Abs(parentScale.x)),1/Mathf.Max(.001f,Mathf.Abs(parentScale.y)),1/Mathf.Max(.001f,Mathf.Abs(parentScale.z)));
                var main = dust.main; main.loop = false; main.duration = 6; main.startLifetime = new ParticleSystem.MinMaxCurve(4, 8);
                main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
                main.startSize3D = false; main.startSize = new ParticleSystem.MinMaxCurve(1.2f,2f);
                var shape = dust.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(footprint.size.x * .75f,.2f,footprint.size.z * .75f);
                var velocity = dust.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = .25f; velocity.y = .6f; velocity.z = .1f;
                main.startColor = new Color(.18f, .16f, .14f, .75f);
                var emission = dust.emission; emission.rateOverTime = 25;
                dust.Play(true); Destroy(dust.gameObject, 15);
            }
        }
        void CreateAshBed(Transform parent, Bounds footprint, float ground, System.Random random)
        {
            const int segments = 32;
            var vertices = new Vector3[segments + 1]; var triangles = new int[segments * 3];
            var go = new GameObject("Scattered ash"); go.transform.SetParent(parent, false);
            vertices[0] = go.transform.InverseTransformPoint(new Vector3(footprint.center.x, ground + .18f, footprint.center.z));
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments, radius = .83f + (float)random.NextDouble() * .17f;
                var point = new Vector3(footprint.center.x + Mathf.Cos(angle) * footprint.size.x * .44f * radius,
                    ground, footprint.center.z + Mathf.Sin(angle) * footprint.size.z * .44f * radius);
                if (Terrain.activeTerrain) point.y = Mathf.Max(ground, Terrain.activeTerrain.SampleHeight(point) + Terrain.activeTerrain.transform.position.y + .08f);
                vertices[i + 1] = go.transform.InverseTransformPoint(point);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % segments + 1; triangles[i * 3 + 2] = i + 1;
            }
            ashMesh = new Mesh { name = "Uneven ash footprint" }; ashMesh.vertices = vertices; ashMesh.triangles = triangles;
            ashMesh.RecalculateNormals(); ashMesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = ashMesh; go.AddComponent<MeshRenderer>().sharedMaterial = ash;
        }
        void CreatePiece(Transform parent, Vector3 position, Vector3 scale, Quaternion rotation, bool solid)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = solid ? "Charred timber" : "Ash bed"; piece.transform.SetParent(parent, true);
            piece.transform.SetPositionAndRotation(position, rotation); piece.transform.localScale = scale;
            // Parent houses are scaled; debris dimensions are in world metres.
            Vector3 s = parent.lossyScale; piece.transform.localScale = new Vector3(scale.x / s.x, scale.y / s.y, scale.z / s.z);
            piece.GetComponent<Renderer>().sharedMaterial = ash;
            if (!solid) Destroy(piece.GetComponent<Collider>());
        }
        void OnDestroy() { if (ash) Destroy(ash); if (ashMesh) Destroy(ashMesh); }
    }
}
