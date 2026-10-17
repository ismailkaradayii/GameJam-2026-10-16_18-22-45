using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>Bounded world-space effects; fragments never become bullet blockers or damage sources.</summary>
    public sealed class LavaCombatEffects : MonoBehaviour
    {
        const int PoolSize = 18, MaximumFragments = 20;
        static LavaCombatEffects instance;
        Material flameMaterial, smokeMaterial, shardMaterial, flashMaterial, ringMaterial;
        AudioClip[] hits;
        AudioClip deathClip, landingClip, playerHitClip;
        Mesh chipMesh;
        readonly List<Burst> pool = new List<Burst>();

        sealed class Fragment
        {
            public Transform transform;
            public Mesh ownedMesh;
            public Vector3 velocity, spin, scale;
            public Renderer renderer;
            public MaterialPropertyBlock block = new MaterialPropertyBlock();
        }
        sealed class Burst
        {
            public GameObject root;
            public ParticleSystem flames, sparks, smoke, flash;
            public Light light;
            public LineRenderer ring;
            public AudioSource audio;
            public readonly List<Fragment> fragments = new List<Fragment>();
            public Vector3 center, ringNormal;
            public float ringDuration, ringRadius;
            public float started, lifetime, nextTrail;
            public bool death, active, priority, aftershock;
        }

        public static int ActiveFragments
        {
            get { int count = 0; if (instance) foreach (var b in instance.pool) count += b.fragments.Count; return count; }
        }
        public static int ShattersPlayed { get; private set; }
        public static int HitsPlayed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; ShattersPlayed = HitsPlayed = 0; }

        static LavaCombatEffects Get()
        {
            if (instance) return instance;
            instance = new GameObject("Lava combat effects (bounded pool)").AddComponent<LavaCombatEffects>();
            instance.flameMaterial = Resources.Load<Material>("LavaCombat/Flame");
            instance.smokeMaterial = Resources.Load<Material>("LavaCombat/Smoke");
            instance.shardMaterial = Resources.Load<Material>("LavaCombat/Shard");
            instance.flashMaterial = Resources.Load<Material>("LavaCombat/ImpactStar");
            instance.ringMaterial = Resources.Load<Material>("LavaCombat/ImpactRing");
            instance.hits = new[] { Resources.Load<AudioClip>("LavaCombat/SpiritHit1"), Resources.Load<AudioClip>("LavaCombat/SpiritHit2"), Resources.Load<AudioClip>("LavaCombat/SpiritHit3") };
            instance.deathClip = Resources.Load<AudioClip>("LavaCombat/SpiritBreak");
            instance.landingClip = Resources.Load<AudioClip>("LavaCombat/Land");
            instance.playerHitClip = Resources.Load<AudioClip>("LavaCombat/PlayerHit");
            instance.chipMesh = MakeChip();
            return instance;
        }

        public static void Hit(Vector3 point, Vector3 direction, bool bullet)
        {
            var fx = Get(); var b = fx.Rent(point, false);
            HitsPlayed++;
            b.priority = bullet;
            fx.EmitBurst(b, bullet ? 7 : 5, bullet ? 34 : 8, bullet ? 3 : 3, -direction.normalized, bullet ? 1.2f : .5f);
            if (bullet)
            {
                // A sharp, two-frame hot core precedes the longer ember spray.
                Emit(b.flash, point - direction.normalized * .06f, Vector3.zero, .85f, .075f, new Color(4f, 3.6f, 2.5f));
                for (int i = 0; i < 4; i++)
                    fx.AddFragment(b, fx.chipMesh, new[] { fx.shardMaterial }, point - direction.normalized * .07f,
                        Random.rotation, Vector3.one * Random.Range(.045f, .105f), false, -direction);
                b.ringNormal = direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
                b.ringDuration = .13f; b.ringRadius = .7f; b.ring.enabled = true;
                fx.Play(b, fx.hits[Random.Range(0, fx.hits.Length)], .9f);
            }
            b.light.color = bullet ? new Color(1f, .62f, .19f) : new Color(1f, .27f, .035f);
            b.light.intensity = bullet ? 4.8f : 1.2f;
        }

        public static void Landing(Vector3 point, bool attack)
        {
            var fx = Get(); var b = fx.Rent(point + Vector3.up * .12f, false);
            fx.EmitBurst(b, attack ? 7 : 3, attack ? 14 : 7, 2, Vector3.up, .35f);
            fx.Play(b, fx.landingClip, attack ? .38f : .18f);
            b.light.intensity = .65f;
        }

        public static void Emerge(Vector3 surface)
        {
            var fx = Get(); var b = fx.Rent(surface + Vector3.up * .15f, false);
            fx.EmitBurst(b, 32, 46, 9, Vector3.up * 1.5f, 1.15f);
            b.light.intensity = 4f; b.light.range = 6f;
            fx.Play(b, fx.deathClip, .45f);
        }

        public static void PlayerHit(Vector3 point)
        {
            var fx = Get(); var b = fx.Rent(point, false);
            fx.Play(b, fx.playerHitClip, .7f);
        }

        public static void Shatter(LavaCreatureActor actor, Vector3 direction)
        {
            var fx = Get();
            var solids = new List<Renderer>();
            foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>())
            {
                string part = renderer.name.ToLowerInvariant();
                if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && renderer.enabled &&
                    !part.Contains("flame") && !part.Contains("ember") && !part.Contains("eye")) solids.Add(renderer);
            }
            Bounds bounds = new Bounds(actor.transform.position + Vector3.up * .8f, Vector3.one);
            if (solids.Count > 0)
            {
                bounds = solids[0].bounds;
                foreach (var solid in solids) bounds.Encapsulate(solid.bounds);
            }
            var b = fx.Rent(bounds.center, true);
            ShattersPlayed++;
            // Prefer the large, recognisable pieces of the actual model over tiny facial details.
            solids.Sort((a, c) => c.bounds.size.sqrMagnitude.CompareTo(a.bounds.size.sqrMagnitude));
            foreach (Renderer renderer in solids)
            {
                if (b.fragments.Count >= MaximumFragments) break;
                Mesh mesh = null; bool owned = false;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    mesh = new Mesh { name = "Baked lava death pose" };
                    skin.BakeMesh(mesh); owned = true;
                }
                else if (renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                if (!mesh) continue;
                // A single skinned shell is fractured into spatial regions, not thrown out intact.
                if ((owned || mesh.isReadable) && mesh.vertexCount > 60 && renderer.bounds.size.sqrMagnitude > .4f)
                {
                    var sourceMaterials = renderer.sharedMaterials;
                    var fracturedMaterials = new Material[mesh.subMeshCount + 1];
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        fracturedMaterials[sub] = sourceMaterials.Length > 0 ? sourceMaterials[Mathf.Min(sub, sourceMaterials.Length - 1)] : fx.shardMaterial;
                    fracturedMaterials[fracturedMaterials.Length - 1] = fx.shardMaterial;
                    foreach (Mesh piece in SplitMesh(mesh))
                    {
                        if (b.fragments.Count >= MaximumFragments) { Destroy(piece); continue; }
                        fx.AddFragment(b, piece, fracturedMaterials, renderer.transform.position,
                            renderer.transform.rotation, renderer.transform.lossyScale, true, direction);
                    }
                    if (owned) Destroy(mesh);
                }
                else fx.AddFragment(b, mesh, renderer.sharedMaterials, renderer.transform.position,
                    renderer.transform.rotation, renderer.transform.lossyScale, owned, direction);
            }
            // Extra small glowing rock splinters keep the burst readable on compact models too.
            while (b.fragments.Count < MaximumFragments)
            {
                Vector3 position = bounds.center + Random.insideUnitSphere * Mathf.Min(.45f, bounds.extents.magnitude * .5f);
                fx.AddFragment(b, fx.chipMesh, new[] { fx.shardMaterial }, position, Random.rotation,
                    Vector3.one * Random.Range(.07f, .19f), false, direction);
            }
            fx.EmitBurst(b, 38, 95, 12, direction.normalized * .6f + Vector3.up * .4f, 1.75f);
            Emit(b.flash, bounds.center, Vector3.zero, 2.1f, .10f, new Color(4.2f, 3.3f, 1.7f));
            b.light.intensity = 8f; b.light.range = 8f;
            b.ringDuration = .28f; b.ringRadius = 3f;
            b.ring.enabled = true;
            fx.Play(b, fx.deathClip, .95f);
        }

        Burst Rent(Vector3 point, bool death)
        {
            Burst result = null;
            foreach (var b in pool) if (!b.active) { result = b; break; }
            if (result == null && pool.Count < PoolSize) { result = CreateBurst(); pool.Add(result); }
            if (result == null)
            {
                // Preserve long-lived death fragments when routine footfalls recycle the pool.
                foreach (var b in pool) if (!b.death && !b.priority && (result == null || b.started < result.started)) result = b;
                if (result == null) foreach (var b in pool) if (!b.death && (result == null || b.started < result.started)) result = b;
                if (result == null) { result = pool[0]; foreach (var b in pool) if (b.started < result.started) result = b; }
            }
            Clear(result);
            result.root.SetActive(true); result.root.transform.position = point;
            result.center = point; result.started = Time.time; result.lifetime = death ? 4.8f : 1.3f;
            result.ringNormal = Vector3.up; result.ringDuration = .32f; result.ringRadius = 2.5f;
            result.nextTrail = 0f; result.death = death; result.active = true;
            result.priority = death; result.aftershock = false;
            result.light.enabled = true; result.light.range = 3f; result.light.intensity = 0f;
            result.light.color = new Color(1f, .27f, .035f);
            result.flames.Play(); result.sparks.Play(); result.smoke.Play(); result.flash.Play();
            return result;
        }

        Burst CreateBurst()
        {
            var b = new Burst { root = new GameObject("Pooled lava impact") };
            b.root.transform.SetParent(transform);
            b.flames = Particles(b.root.transform, "Flame tongues", flameMaterial, 560, -.15f, false);
            b.sparks = Particles(b.root.transform, "Flying embers", flameMaterial, 180, .85f, true);
            b.smoke = Particles(b.root.transform, "Charred smoke", smokeMaterial, 40, -.035f, false);
            b.flash = Particles(b.root.transform, "Instant impact corona", flashMaterial ? flashMaterial : flameMaterial, 4, 0f, false);
            var lightObject = new GameObject("Burst light"); lightObject.transform.SetParent(b.root.transform, false);
            b.light = lightObject.AddComponent<Light>(); b.light.type = LightType.Point;
            b.light.color = new Color(1f, .27f, .035f); b.light.shadows = LightShadows.None;
            var ringObject = new GameObject("Expanding heat ring"); ringObject.transform.SetParent(b.root.transform, false);
            b.ring = ringObject.AddComponent<LineRenderer>(); b.ring.sharedMaterial = ringMaterial ? ringMaterial : flameMaterial;
            b.ring.useWorldSpace = true; b.ring.loop = true; b.ring.positionCount = 40;
            b.ring.shadowCastingMode = ShadowCastingMode.Off; b.ring.receiveShadows = false;
            b.audio = b.root.AddComponent<AudioSource>(); b.audio.playOnAwake = false; b.audio.spatialBlend = 1f;
            b.audio.minDistance = 3f; b.audio.maxDistance = 55f; b.audio.rolloffMode = AudioRolloffMode.Logarithmic;
            return b;
        }

        static ParticleSystem Particles(Transform parent, string name, Material material, int maximum, float gravity, bool streak)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.duration = 5f;
            main.maxParticles = maximum; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = gravity;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, .46f, .12f), .4f), new GradientColorKey(new Color(.6f, .12f, .02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.85f, .25f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, name.Contains("smoke") ? 2.5f : .15f));
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.renderMode = streak ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = 1.1f; renderer.velocityScale = .055f;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return ps;
        }

        void EmitBurst(Burst b, int flames, int sparks, int smoke, Vector3 bias, float strength)
        {
            for (int i = 0; i < flames; i++) Emit(b.flames, b.center, (Random.onUnitSphere + bias) * Random.Range(1f, 3.3f) * strength,
                Random.Range(.18f, .5f) * strength, Random.Range(.22f, .65f), new Color(2.1f, 1.1f, .26f));
            for (int i = 0; i < sparks; i++) Emit(b.sparks, b.center, (Random.onUnitSphere + bias + Vector3.up * .35f) * Random.Range(2.3f, 6f) * strength,
                Random.Range(.025f, .065f), Random.Range(.4f, 1.1f), new Color(3f, 1.7f, .4f));
            for (int i = 0; i < smoke; i++) Emit(b.smoke, b.center, (Random.insideUnitSphere + Vector3.up) * .6f * strength,
                Random.Range(.2f, .48f) * strength, Random.Range(.55f, 1.2f), new Color(.16f, .13f, .11f, .5f));
        }

        static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            ps.Emit(new ParticleSystem.EmitParams { position = position, velocity = velocity, startSize = size,
                startLifetime = life, startColor = color, rotation = Random.Range(-180f, 180f) }, 1);
        }

        void AddFragment(Burst b, Mesh mesh, Material[] materials, Vector3 position, Quaternion rotation, Vector3 scale, bool owned, Vector3 shotDirection)
        {
            // Recenter geometry so pieces rotate around themselves instead of the original skeleton origin.
            var pivot = new GameObject("Burning body fragment").transform;
            pivot.SetParent(b.root.transform, true);
            Vector3 center = position + rotation * Vector3.Scale(mesh.bounds.center, scale);
            pivot.SetPositionAndRotation(center, rotation); pivot.localScale = scale;
            var model = new GameObject("Fractured lava shell"); model.transform.SetParent(pivot, false);
            model.transform.localPosition = -mesh.bounds.center;
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = model.AddComponent<MeshRenderer>(); renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Vector3 outward = (center - b.center).normalized;
            if (outward.sqrMagnitude < .1f) outward = Random.onUnitSphere;
            b.fragments.Add(new Fragment { transform = pivot, ownedMesh = owned ? mesh : null, renderer = renderer,
                scale = scale, velocity = (outward + Random.insideUnitSphere * .45f + Vector3.up * .6f) * Random.Range(2.4f, 4.8f)
                    + shotDirection.normalized * 1.8f,
                spin = Random.onUnitSphere * Random.Range(170f, 520f) });
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, .05f);
            foreach (var b in pool)
            {
                if (!b.active) continue;
                float age = Time.time - b.started;
                if (age >= b.lifetime) { Clear(b); continue; }
                if (b.death && !b.aftershock && age > .16f)
                {
                    b.aftershock = true;
                    // Follow the initial crack with a lower ember bloom; leave the flying shell readable.
                    EmitBurst(b, 16, 18, 5, Vector3.up, .65f);
                    b.light.enabled = true; b.light.intensity = 1.7f;
                }
                b.light.intensity *= Mathf.Exp(-18f * dt);
                if (b.light.intensity < .025f) b.light.enabled = false;
                if (b.ring.enabled)
                {
                    float p = age / b.ringDuration;
                    if (p >= 1f) b.ring.enabled = false;
                    else
                    {
                        float radius = Mathf.Lerp(.06f, b.ringRadius, 1f - Mathf.Pow(1f - p, 2f));
                        b.ring.widthMultiplier = (b.death ? .12f : .045f) * (1f - p);
                        b.ring.startColor = b.ring.endColor = new Color(2f, 1.1f, .3f, 1f - p);
                        Vector3 axis = Vector3.Cross(b.ringNormal, Mathf.Abs(b.ringNormal.y) > .9f ? Vector3.right : Vector3.up).normalized;
                        Vector3 other = Vector3.Cross(b.ringNormal, axis);
                        for (int i = 0; i < 40; i++)
                        {
                            float angle = i * Mathf.PI * 2f / 40f;
                            b.ring.SetPosition(i, b.center + (axis * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * radius);
                        }
                    }
                }
                bool trail = b.death && age < 1.45f && age >= b.nextTrail;
                if (trail) b.nextTrail = age + .045f;
                foreach (var fragment in b.fragments)
                {
                    if (!fragment.transform) continue;
                    fragment.velocity += Vector3.down * (13f * dt);
                    Vector3 delta = fragment.velocity * dt;
                    if (Physics.Raycast(fragment.transform.position, delta.normalized, out var hit, delta.magnitude + .045f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                        !hit.collider.GetComponentInParent<LavaCreatureActor>() && !hit.collider.GetComponentInParent<CreatureCombatPlayer>())
                    {
                        fragment.transform.position = hit.point + hit.normal * .045f;
                        fragment.velocity = Vector3.Reflect(fragment.velocity, hit.normal) * .33f;
                        fragment.spin *= .65f;
                    }
                    else fragment.transform.position += delta;
                    fragment.transform.Rotate(fragment.spin * dt, Space.World);
                    float fade = Mathf.Clamp01((b.lifetime - age) / 1.3f);
                    fragment.transform.localScale = fragment.scale * fade;
                    fragment.block.SetColor("_EmissionColor", new Color(2.2f, .35f, .02f) * Mathf.Clamp01(1f - age / 3.5f));
                    fragment.renderer.SetPropertyBlock(fragment.block);
                    if (trail) Emit(b.flames, fragment.transform.position, Vector3.up * .6f - fragment.velocity * .08f,
                        Random.Range(.08f, .19f), .28f, new Color(2f, .8f, .16f));
                }
            }
        }

        void Play(Burst b, AudioClip clip, float volume)
        {
            if (!clip) return;
            b.audio.clip = clip; b.audio.volume = volume; b.audio.pitch = Random.Range(.93f, 1.07f); b.audio.Play();
        }

        static void Clear(Burst b)
        {
            foreach (var fragment in b.fragments)
            {
                if (fragment.transform) Destroy(fragment.transform.gameObject);
                if (fragment.ownedMesh) Destroy(fragment.ownedMesh);
            }
            b.fragments.Clear(); b.active = false;
            if (b.flames) b.flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (b.sparks) b.sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (b.smoke) b.smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (b.flash) b.flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (b.audio) b.audio.Stop();
            if (b.ring) b.ring.enabled = false;
            if (b.root) b.root.SetActive(false);
        }
        void OnDestroy() { foreach (var b in pool) Clear(b); if (chipMesh) Destroy(chipMesh); if (instance == this) instance = null; }

        static Mesh MakeChip()
        {
            Vector3[] points = { Vector3.up, Vector3.down * .7f, Vector3.left, Vector3.forward * .8f, Vector3.right * .75f, Vector3.back };
            int[] indices = { 0,3,2, 0,4,3, 0,5,4, 0,2,5, 1,2,3, 1,3,4, 1,4,5, 1,5,2 };
            var vertices = new Vector3[indices.Length]; var triangles = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++) { vertices[i] = points[indices[i]]; triangles[i] = i; }
            var mesh = new Mesh { name = "Angular volcanic splinter", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static List<Mesh> SplitMesh(Mesh source)
        {
            var result = new List<Mesh>();
            Vector3[] positions = source.vertices, normals = source.normals;
            Vector2[] uv = source.uv;
            Vector3 center = source.bounds.center;
            // Eight spatial buckets preserve the source silhouette and each submesh's material.
            for (int bucket = 0; bucket < 8; bucket++)
            {
                var vertices = new List<Vector3>(); var outputNormals = new List<Vector3>(); var outputUv = new List<Vector2>();
                var subsets = new List<int>[source.subMeshCount];
                var remap = new Dictionary<int, int>();
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    subsets[sub] = new List<int>(); int[] input = source.GetTriangles(sub);
                    for (int i = 0; i < input.Length; i += 3)
                    {
                        Vector3 mid = (positions[input[i]] + positions[input[i+1]] + positions[input[i+2]]) / 3f;
                        int region = (mid.x > center.x ? 1 : 0) | (mid.y > center.y ? 2 : 0) | (mid.z > center.z ? 4 : 0);
                        if (region != bucket) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int original = input[i+k];
                            if (!remap.TryGetValue(original, out int mapped))
                            {
                                mapped = vertices.Count; remap.Add(original, mapped); vertices.Add(positions[original]);
                                outputNormals.Add(normals.Length == positions.Length ? normals[original] : Vector3.up);
                                outputUv.Add(uv.Length == positions.Length ? uv[original] : Vector2.zero);
                            }
                            subsets[sub].Add(mapped);
                        }
                    }
                }
                if (vertices.Count == 0) continue;
                List<int> caps = CapFracture(vertices, outputNormals, outputUv, subsets);
                var piece = new Mesh { name = "Fractured lava region", indexFormat = IndexFormat.UInt32 };
                piece.SetVertices(vertices); piece.SetNormals(outputNormals); piece.SetUVs(0, outputUv); piece.subMeshCount = source.subMeshCount + 1;
                for (int sub = 0; sub < subsets.Length; sub++) piece.SetTriangles(subsets[sub], sub);
                piece.SetTriangles(caps, source.subMeshCount);
                piece.RecalculateBounds(); result.Add(piece);
            }
            return result;
        }

        sealed class Edge { public int a, b, count; }
        static List<int> CapFracture(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<int>[] subsets)
        {
            // Weld positions only for edge counting; retain the original normals/UV seams outside.
            var weld = new Dictionary<Vector3Int, int>(); var ids = new int[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3Int key = Vector3Int.RoundToInt(vertices[i] * 100000f);
                if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld.Add(key, id); }
                ids[i] = id;
            }
            var edges = new Dictionary<long, Edge>();
            foreach (var triangles in subsets)
                for (int i = 0; i < triangles.Count; i += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = triangles[i+k], b = triangles[i+(k+1)%3];
                        long key = ((long)Mathf.Min(ids[a], ids[b]) << 32) | (uint)Mathf.Max(ids[a], ids[b]);
                        if (edges.TryGetValue(key, out var edge)) edge.count++;
                        else edges.Add(key, new Edge { a = a, b = b, count = 1 });
                    }
            Vector3 center = Vector3.zero; int count = 0;
            foreach (var edge in edges.Values)
                if (edge.count == 1) { center += vertices[edge.a] + vertices[edge.b]; count += 2; }
            var result = new List<int>();
            if (count == 0) return result;
            center /= count;
            foreach (var edge in edges.Values)
            {
                if (edge.count != 1) continue;
                Vector3 a = vertices[edge.b], b = vertices[edge.a];
                Vector3 normal = Vector3.Cross(b - a, center - a).normalized;
                if (normal.sqrMagnitude < .1f) continue;
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(center);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up);
                result.Add(first); result.Add(first + 1); result.Add(first + 2);
            }
            return result;
        }
    }
}
