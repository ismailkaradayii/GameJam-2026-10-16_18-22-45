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
            var fire = building.GetComponent<BuildingFireVisual>();
            if (fire) fire.ClearForRuin();
            foreach (var particles in GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var light in GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var overlay in building.windowGlowOverlays)
                if (overlay) { overlay.enabled = false; overlay.gameObject.SetActive(false); }
            Bounds footprint = building.ignitionBounds ? building.ignitionBounds.bounds : new Bounds(transform.position + Vector3.up * 5, Vector3.one * 15);
            // LOD groups can re-enable their window/emissive renderers on the next camera
            // cull, leaving yellow panes floating above the ruin. Retire the groups first.
            foreach (var lod in GetComponentsInChildren<LODGroup>(true)) lod.enabled = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
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
            CreateCollapseDust(building, rubble.transform, footprint, ground);
        }
        void CreateCollapseDust(BurnableBuilding building, Transform parent, Bounds footprint, float ground)
        {
            var go = new GameObject("Collapse ash cloud"); go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(footprint.center.x, ground + .35f, footprint.center.z);
            var dust = go.AddComponent<ParticleSystem>(); dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main; main.loop = false; main.playOnAwake = false; main.duration = .25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.5f); main.startSpeed = new ParticleSystem.MinMaxCurve(.35f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.4f, 3.2f); main.startColor = new Color(.22f, .21f, .2f, .5f);
            main.maxParticles = 64;
            var emission = dust.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 42) });
            var shape = dust.shape; shape.shapeType = ParticleSystemShapeType.Box;
            Vector3 scale = go.transform.lossyScale;
            shape.scale = new Vector3(footprint.size.x * .7f / Mathf.Max(.01f, Mathf.Abs(scale.x)), .2f,
                footprint.size.z * .7f / Mathf.Max(.01f, Mathf.Abs(scale.z)));
            var color = dust.colorOverLifetime; color.enabled = true;
            color.color = new Gradient { colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.6f, .1f), new GradientAlphaKey(0, 1) } };
            var size = dust.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .7f, 1, 1.8f));
            var renderer = dust.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = building.smoke ? building.smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial : Resources.Load<Material>("LavaCombat/Smoke");
            renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream> {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dust.Play(false); Destroy(go, 3);
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
