using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>Shared, texture-free spirit meshes attached to the existing somersault rig.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(80)]
    public sealed class LavaSpiritVisual : MonoBehaviour
    {
        static Mesh bodyMesh, eyeMesh, tongueMesh;
        static Material bodyMaterial, rockMaterial, eyeMaterial, fireMaterial;
        static readonly Quaternion ModelBasis = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        static readonly int HitId = Shader.PropertyToID("_Hit");
        static readonly int HeatId = Shader.PropertyToID("_Heat");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int DamageId = Shader.PropertyToID("_Damage");
        static readonly int ImpactId = Shader.PropertyToID("_ImpactPoint");
        readonly List<Renderer> surfaces = new List<Renderer>();
        MaterialPropertyBlock block;
        LavaCreatureActor actor;
        LavaCreatureCombat combat;
        Transform rig;
        Light glow;
        ParticleSystem embers;
        Vector3 restPosition, restScale, localShot, impactPoint;
        float impactAt = -100f, landingAt = -100f, phase;
        float deathAt;
        Vector3 deathScale, deathPosition;
        Quaternion deathRotation;
        bool built, dying;

        public bool IsBuilt => built;
        public float ImpactFlash => Mathf.Clamp01(1f - Mathf.Max(0f, Time.time - impactAt - .055f) / .18f);
        public Transform VisualRoot => rig;

        public static LavaSpiritVisual Ensure(LavaCreatureActor owner)
        {
            if (!owner || !owner.jump) return null;
            var visual = owner.GetComponent<LavaSpiritVisual>() ?? owner.gameObject.AddComponent<LavaSpiritVisual>();
            visual.Build(owner);
            return visual;
        }

        void Build(LavaCreatureActor owner)
        {
            if (built) return;
            block = new MaterialPropertyBlock();
            actor = owner; combat = GetComponent<LavaCreatureCombat>();
            LoadSharedAssets();
            if (!bodyMaterial || !rockMaterial || !eyeMaterial || !fireMaterial) return;
            var source = GetComponentsInChildren<Renderer>(true);
            bool found = false;
            Bounds bounds = default;
            foreach (var renderer in source)
            {
                string part = renderer.name.ToLowerInvariant();
                if (renderer is ParticleSystemRenderer || part.Contains("flame") || part.Contains("ember")) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) return;
            var pivot = transform.Find("SomersaultVisual");
            if (!pivot) pivot = transform;
            float height = Mathf.Clamp(bounds.size.y, .4f, 5f);
            rig = new GameObject("Living flame spirit").transform;
            rig.SetParent(pivot, false);
            rig.localRotation = ModelBasis;
            rig.position = bounds.center - Vector3.up * (height * .5f);
            float scale = height / 1.8f / Mathf.Max(.001f, Mathf.Abs(pivot.lossyScale.x));
            rig.localScale = Vector3.one * scale;
            restPosition = rig.localPosition; restScale = rig.localScale;
            phase = Mathf.Repeat(GetInstanceID() * .618034f, 19f);

            // Keep the original animation hierarchy and hitboxes; replace only its rendering.
            foreach (var renderer in source) renderer.enabled = false;
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var light in GetComponentsInChildren<Light>(true)) light.enabled = false;

            MeshPart("Molten heart", bodyMesh, bodyMaterial, Vector3.zero, Vector3.one, Quaternion.identity);
            for (int side = -1; side <= 1; side += 2)
            {
                MeshPart("Eye socket", eyeMesh, rockMaterial, new Vector3(side * .19f, .73f, .338f),
                    new Vector3(side * 1.13f, 1.18f, 1f), Quaternion.identity);
                MeshPart("White-hot eye", eyeMesh, eyeMaterial, new Vector3(side * .19f, .73f, .35f),
                    new Vector3(side, 1f, 1f), Quaternion.identity);
            }
            MeshPart("Flame crown", tongueMesh, fireMaterial, new Vector3(-.13f, .77f, -.025f),
                new Vector3(.48f, 1.14f, .43f), Quaternion.Euler(0, 20, -12));
            MeshPart("Flame crown left", tongueMesh, fireMaterial, new Vector3(-.34f, .66f, -.06f),
                new Vector3(.3f, .91f, .29f), Quaternion.Euler(-8, -55, 20));
            MeshPart("Flame crown right", tongueMesh, fireMaterial, new Vector3(.31f, .65f, -.09f),
                new Vector3(.32f, .83f, .3f), Quaternion.Euler(12, 100, -24));
            MeshPart("Flame back crest", tongueMesh, fireMaterial, new Vector3(.08f, .72f, -.22f),
                new Vector3(.32f, .99f, .31f), Quaternion.Euler(-20, 180, 9));
            for (int side = -1; side <= 1; side += 2)
                MeshPart("Flame wisp hand", tongueMesh, fireMaterial, new Vector3(side * .4f, .3f, 0),
                    new Vector3(.23f, .71f, .24f), Quaternion.Euler(4, side * 60, side * -35));
            embers = BuildEmbers();
            glow = new GameObject("Spirit warm light").AddComponent<Light>();
            glow.transform.SetParent(rig, false); glow.transform.localPosition = Vector3.up * .65f;
            glow.type = LightType.Point; glow.color = new Color(1f, .28f, .035f);
            glow.range = height * 2.3f; glow.intensity = 1.7f; glow.shadows = LightShadows.None;
            built = true;
            Animate();
        }

        public void Impact(Vector3 point, Vector3 direction)
        {
            if (!built) return;
            impactAt = Time.time; impactPoint = point;
            localShot = rig.InverseTransformDirection(direction).normalized;
            if (localShot.sqrMagnitude < .01f) localShot = Vector3.back;
            Animate();
        }

        public void Land() => landingAt = Time.time;
        public void BeginDeath(Vector3 point, Vector3 direction)
        {
            if (!built) return;
            Impact(point, direction); dying = true; deathAt = Time.time;
            deathScale = rig.localScale; deathPosition = rig.localPosition; deathRotation = rig.localRotation;
            var emission = embers.emission; emission.enabled = false;
        }
        void LateUpdate()
        {
            if (!built || !actor) return;
            if (dying)
            {
                float t = Mathf.Clamp01((Time.time - deathAt - .035f) / .075f);
                // A brief hot hold, then compression, gives the explosion a visible anticipation.
                rig.localScale = deathScale * Mathf.Lerp(1f, .72f, t * t);
                rig.localPosition = deathPosition + ModelBasis * Vector3.up * (.13f * t * restScale.x);
                rig.localRotation = deathRotation;
                block.SetFloat(HitId, 1f); block.SetFloat(HeatId, 2f + t * 3f);
                foreach (var surface in surfaces) if (surface) surface.SetPropertyBlock(block);
                glow.intensity = 5f + t * 5f;
            }
            else if (!actor.IsDefeated) Animate();
        }

        void Animate()
        {
            float t = Time.time, age = Mathf.Max(0f, t - impactAt);
            float kick = Mathf.Sin(Mathf.Min(age, .55f) * 26f) * Mathf.Exp(-age * 8.5f);
            float flash = ImpactFlash;
            float landing = Mathf.Exp(-Mathf.Max(0f, t - landingAt) * 18f);
            float breathe = Mathf.Sin(t * 4.4f + phase) * .022f;
            float squash = flash * .28f + landing * .1f;
            rig.localPosition = restPosition + ModelBasis * (localShot * (.4f * kick) + Vector3.up * breathe) * restScale.x;
            rig.localRotation = ModelBasis * Quaternion.Euler(localShot.z * kick * 22f, 0, -localShot.x * kick * 25f);
            rig.localScale = Vector3.Scale(restScale, new Vector3(1f + squash, 1f - squash + breathe, 1f + squash * .35f));
            float charge = combat ? combat.AttackCharge * 1.8f : 0f;
            block.SetFloat(HitId, flash);
            block.SetFloat(HeatId, 1f + charge + Mathf.Sin(t * 7f + phase) * .075f);
            block.SetFloat(SeedId, phase);
            block.SetFloat(DamageId, actor ? 1f - actor.Health01 : 0f);
            block.SetVector(ImpactId, impactPoint);
            foreach (var surface in surfaces) if (surface) surface.SetPropertyBlock(block);
            glow.intensity = 1.5f + charge * 2f + flash * 4f + Mathf.Sin(t * 11f + phase) * .2f;
        }

        void MeshPart(string name, Mesh mesh, Material material, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            var go = new GameObject(name); go.transform.SetParent(rig, false);
            go.transform.SetLocalPositionAndRotation(position, rotation); go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = material == fireMaterial || material == eyeMaterial ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = false; surfaces.Add(renderer);
        }

        ParticleSystem BuildEmbers()
        {
            var go = new GameObject("Drifting spirit embers"); go.transform.SetParent(rig, false);
            go.transform.localPosition = new Vector3(0, .8f, -.04f);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, 1.3f); main.startSize = new ParticleSystem.MinMaxCurve(.018f, .045f);
            main.startColor = new Color(1.9f, .65f, .08f); main.maxParticles = 32;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Shape;
            main.gravityModifier = -.045f;
            var emission = ps.emission; emission.rateOverTime = 18f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 18f; shape.radius = .28f;
            shape.rotation = new Vector3(-90, 0, 0);
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, .2f, .015f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.85f, .1f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = Resources.Load<Material>("LavaCombat/Flame");
            renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = .045f; renderer.lengthScale = 1.4f;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            ps.Play(); return ps;
        }

        static void LoadSharedAssets()
        {
            if (!bodyMaterial) bodyMaterial = Resources.Load<Material>("LavaCombat/SpiritBody");
            if (!rockMaterial) rockMaterial = Resources.Load<Material>("LavaCombat/SpiritObsidian");
            if (!eyeMaterial) eyeMaterial = Resources.Load<Material>("LavaCombat/SpiritEye");
            if (!fireMaterial) fireMaterial = Resources.Load<Material>("LavaCombat/SpiritFire");
            if (!bodyMesh) bodyMesh = Lathe("Molten spirit heart", false);
            if (!tongueMesh) tongueMesh = Lathe("Flowing flame tongue", true);
            if (!eyeMesh) eyeMesh = Eye();
        }

        static Mesh Lathe(string name, bool flame)
        {
            const int rows = 20, columns = 28;
            var vertices = new Vector3[(rows + 1) * (columns + 1)];
            var uv = new Vector2[vertices.Length]; var triangles = new int[rows * columns * 6];
            for (int row = 0; row <= rows; row++)
            {
                float t = row / (float)rows;
                float profile = row == 0 || row == rows ? 0f : Mathf.Max(0f, Mathf.Sin(t * Mathf.PI));
                float radius = flame ? Mathf.Pow(profile, .72f) * Mathf.Lerp(.6f, .025f, t) :
                    .48f * Mathf.Pow(profile, .64f) * Mathf.Lerp(1.16f, .64f, t);
                for (int col = 0; col <= columns; col++)
                {
                    float angle = col / (float)columns * Mathf.PI * 2f;
                    float r = radius * (1f + .045f * Mathf.Sin(angle * 5f + t * 11f));
                    int i = row * (columns + 1) + col;
                    vertices[i] = new Vector3(Mathf.Cos(angle) * r + (flame ? .29f * t * t + .08f * Mathf.Sin(t * 6f) : 0),
                        flame ? t : .12f + t * 1.12f, Mathf.Sin(angle) * r * (flame ? 1f : .84f));
                    uv[i] = new Vector2(col / (float)columns, t);
                    if (row == rows || col == columns) continue;
                    int j = (row * columns + col) * 6, next = i + columns + 1;
                    triangles[j] = i; triangles[j + 1] = next; triangles[j + 2] = i + 1;
                    triangles[j + 3] = i + 1; triangles[j + 4] = next; triangles[j + 5] = next + 1;
                }
            }
            var mesh = new Mesh { name = name, vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static Mesh Eye()
        {
            var mesh = new Mesh { name = "Tapered spirit eye" };
            var vertices = new[] { new Vector3(-.125f, -.005f, 0), new Vector3(-.058f, -.075f, .008f),
                new Vector3(.065f, -.055f, 0), new Vector3(.14f, .08f, -.012f), new Vector3(-.065f, .035f, 0) };
            for (int i = 0; i < vertices.Length; i++)
            {
                float y = .73f + vertices[i].y, x = .19f + vertices[i].x;
                float t = (y - .12f) / 1.12f;
                float radius = .48f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), .64f) * Mathf.Lerp(1.16f, .64f, t);
                vertices[i].z = Mathf.Sqrt(Mathf.Max(.001f, radius * radius - x * x)) * .84f - .34f + .026f;
            }
            mesh.vertices = vertices;
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up, new Vector2(.5f, .5f) };
            mesh.triangles = new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
