using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.FireRescue
{
    /// <summary>Bounded surface waves, crowns and splashes shared by bullets, swimmers and the monitor.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(WaterSourceVolume))]
    public sealed class WaterSurfaceInteraction : MonoBehaviour
    {
        public const int RippleCapacity = 32;
        const int Sectors = 48, Bands = 4;
        struct Ripple { public Vector3 point, direction; public float born, strength, speed, life, stretch; public bool crown; }
        readonly Ripple[] ripples = new Ripple[RippleCapacity];
        readonly List<Vector3> vertices = new List<Vector3>(RippleCapacity * Sectors * Bands);
        readonly List<Vector3> normals = new List<Vector3>(RippleCapacity * Sectors * Bands);
        readonly List<Vector2> uv = new List<Vector2>(RippleCapacity * Sectors * Bands);
        readonly List<Color> colors = new List<Color>(RippleCapacity * Sectors * Bands);
        readonly List<int> indices = new List<int>(RippleCapacity * Sectors * (Bands - 1) * 6);
        int next;
        float nextCannon, nextAudio;
        WaterSourceVolume source;
        Mesh mesh;
        Transform root;
        ParticleSystem drops, spray;
        AudioSource audioSource;
        AudioClip bulletSound, splashSound, strokeSound;
        public int BulletImpacts { get; private set; }
        public int SwimImpulses { get; private set; }
        public int HandSplashes { get; private set; }
        public int CannonImpacts { get; private set; }
        public int ActiveRipples { get; private set; }
        public ParticleSystem Droplets => drops;

        void Awake() { source = GetComponent<WaterSourceVolume>(); }

        void Ensure()
        {
            if (mesh) return;
            if (!source) source = GetComponent<WaterSourceVolume>();
            var go = new GameObject("Surface waves · pooled visuals"); go.transform.SetParent(transform, false); root = go.transform;
            mesh = new Mesh { name = "Expanding water waves and crowns" }; mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = Resources.Load<Material>("WaterRealism/Ripple");
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            drops = WaterVisualUtility.CreateParticles("Splash droplets", root, "Droplet", 320, true, 1f);
            spray = WaterVisualUtility.CreateParticles("Splash spray", root, "Mist", 96, false, .15f);
            audioSource = go.AddComponent<AudioSource>(); audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1; audioSource.minDistance = 3; audioSource.maxDistance = 45;
            audioSource.rolloffMode = AudioRolloffMode.Logarithmic; audioSource.dopplerLevel = 0;
            bulletSound = Resources.Load<AudioClip>("WaterRealism/BulletSplash");
            splashSound = Resources.Load<AudioClip>("WaterRealism/BodySplash");
            strokeSound = Resources.Load<AudioClip>("WaterRealism/SwimStroke");
        }

        public void Bullet(Vector3 point, Vector3 direction)
        {
            if (!Valid(point)) return;
            Ensure(); BulletImpacts++;
            AddWave(point, direction, .55f, 1.75f, 2.1f, 1f, true);
            AddWave(point, direction, .20f, 1.23f, 2.6f, 1f, false);
            Splash(point, direction, .75f, 25); Sound(bulletSound, point, .6f);
        }

        public void Enter(Vector3 point, float fallSpeed)
        {
            if (!Valid(point)) return;
            Ensure();
            float strength = Mathf.Clamp(.5f + Mathf.Abs(fallSpeed) * .07f, .6f, 1.65f);
            AddWave(point, Vector3.forward, strength, 2.1f, 3.3f, 1, true);
            AddWave(point, Vector3.forward, strength * .45f, 1.5f, 3.6f, 1, false);
            Splash(point, Vector3.down, strength, 44); Sound(splashSound, point, .8f);
        }

        public void Swim(Vector3 point, Vector3 velocity, bool stroke)
        {
            if (!Valid(point)) return;
            Ensure(); SwimImpulses++;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            AddWave(point, velocity, stroke ? .22f : .12f, .85f + speed * .12f, 2.1f, 1.4f, false);
            if (stroke) HandStroke(point, velocity, speed / 3.6f);
        }

        public void HandStroke(Vector3 point, Vector3 pull, float effort)
        {
            if (!Valid(point)) return;
            Ensure(); HandSplashes++; SwimImpulses++;
            float strength = Mathf.Lerp(.3f, .5f, Mathf.Clamp01(effort));
            // The same crown and droplet emitter as a bullet splash, with a softer, wider paddle impulse.
            AddWave(point, pull, strength, 1.2f, 1.7f, 1.25f, true);
            AddWave(point, pull, strength * .45f, .8f, 2.2f, 1.4f, false);
            Splash(point, pull, strength, 18);
            Sound(strokeSound, point, .4f);
        }

        public void Cannon(Vector3 point, Vector3 velocity, float litres)
        {
            if (!Valid(point) || Time.time < nextCannon) return;
            nextCannon = Time.time + .12f; Ensure(); CannonImpacts++;
            AddWave(point, velocity, .4f, 1.6f, 2f, 1.12f, true);
            Splash(point, velocity, .55f, 16);
        }

        bool Valid(Vector3 point)
        {
            if (!source) source = GetComponent<WaterSourceVolume>();
            return isActiveAndEnabled && source && source.ContainsSurface(point);
        }

        void AddWave(Vector3 point, Vector3 direction, float strength, float speed, float life, float stretch, bool crown)
        {
            point.y = source.SurfaceY;
            direction.y = 0; if (direction.sqrMagnitude < .001f) direction = Vector3.forward;
            ripples[next] = new Ripple { point = point, direction = direction.normalized, born = Time.time,
                strength = strength, speed = speed, life = life, stretch = stretch, crown = crown };
            next = (next + 1) % RippleCapacity;
        }

        void Splash(Vector3 point, Vector3 incoming, float strength, int count)
        {
            point.y = source.SurfaceY + .035f;
            var lateral = Vector3.ProjectOnPlane(incoming.normalized, Vector3.up) * .6f;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.399963f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 velocity = radial * Random.Range(.7f, 2.6f) * strength + Vector3.up * Random.Range(1.5f, 5.5f) * Mathf.Sqrt(strength) + lateral;
                // A few tall central droplets form the short impact column; the rest form its crown.
                if (i < 5) velocity = Vector3.up * Random.Range(3.5f, 5.2f) * Mathf.Sqrt(strength) + radial * .25f;
                float lifetime = Mathf.Clamp(2f * velocity.y / Mathf.Abs(Physics.gravity.y), .16f, 1.1f);
                WaterVisualUtility.Emit(drops, point + radial * .055f, velocity, Random.Range(.018f, .045f) * Mathf.Sqrt(strength), lifetime, .75f);
            }
            for (int i = 0; i < 3; i++) WaterVisualUtility.Emit(spray, point, Vector3.up * .6f + Random.insideUnitSphere * .35f,
                .22f * Mathf.Sqrt(strength), .45f, .2f);
        }

        void Sound(AudioClip clip, Vector3 point, float volume)
        {
            if (!clip || Time.time < nextAudio) return;
            nextAudio = Time.time + .12f;
            audioSource.transform.position = point;
            audioSource.pitch = Random.Range(.94f, 1.06f); audioSource.PlayOneShot(clip, volume);
        }

        void LateUpdate()
        {
            if (!mesh || !source) return;
            vertices.Clear(); normals.Clear(); colors.Clear(); uv.Clear(); indices.Clear(); ActiveRipples = 0;
            for (int i = 0; i < RippleCapacity; i++)
            {
                var wave = ripples[i]; float age = Time.time - wave.born;
                if (wave.life <= 0 || age >= wave.life) continue;
                ActiveRipples++;
                BuildRing(wave, age, false);
                if (wave.crown && age < .32f) BuildRing(wave, age, true);
            }
            mesh.Clear(false); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
        }

        void BuildRing(Ripple wave, float age, bool crown)
        {
            int start = vertices.Count;
            float radius = crown ? .07f + age * .85f : .08f + age * wave.speed;
            float width = crown ? .065f : .11f + age * .055f;
            float fade = crown ? 1f - age / .32f : Mathf.Pow(1f - age / wave.life, 2f);
            Vector3 right = Vector3.Cross(Vector3.up, wave.direction);
            for (int band = 0; band < Bands; band++)
            {
                float cross = band / (float)(Bands - 1);
                float r = Mathf.Max(.015f, radius + (cross - .5f) * width * 2);
                for (int s = 0; s < Sectors; s++)
                {
                    float angle = s * Mathf.PI * 2 / Sectors;
                    Vector3 radial = right * Mathf.Cos(angle) + wave.direction * (Mathf.Sin(angle) * wave.stretch);
                    Vector3 p = wave.point + radial * r; p.y = source.SurfaceY + .014f;
                    float height = crown ? Mathf.Sin(age / .32f * Mathf.PI) * .42f * wave.strength * cross * (1 + .22f * Mathf.Sin(angle * 11f))
                        : Mathf.Sin(cross * Mathf.PI) * .045f * wave.strength * fade;
                    p.y += height;
                    bool inside = source.ContainsSurface(p);
                    vertices.Add(root.InverseTransformPoint(p));
                    Vector3 normal = crown ? (radial + Vector3.up * .25f).normalized : (Vector3.up - radial * Mathf.Cos(cross * Mathf.PI) * .45f * wave.strength * fade).normalized;
                    normals.Add(root.InverseTransformDirection(normal)); uv.Add(new Vector2(s / (float)Sectors, cross));
                    colors.Add(new Color(.82f, .94f, 1f, inside ? fade * Mathf.Sin(cross * Mathf.PI) * Mathf.Min(.75f, .3f + wave.strength) : 0));
                }
            }
            for (int band = 0; band < Bands - 1; band++) for (int s = 0; s < Sectors; s++)
            {
                int a = start + band * Sectors + s, b = start + band * Sectors + (s + 1) % Sectors;
                indices.Add(a); indices.Add(a + Sectors); indices.Add(b);
                indices.Add(b); indices.Add(a + Sectors); indices.Add(b + Sectors);
            }
        }

        void OnDisable()
        {
            System.Array.Clear(ripples, 0, ripples.Length); ActiveRipples = 0;
            if (mesh) mesh.Clear(); if (drops) drops.Clear(); if (spray) spray.Clear(); if (audioSource) audioSource.Stop();
        }
        void OnDestroy() { if (mesh) Destroy(mesh); }

        public static bool Raycast(Ray ray, float distance, out WaterSourceVolume water, out Vector3 point)
        {
            water = null; point = default;
            if (Mathf.Abs(ray.direction.y) < .00001f) return false;
            float nearest = distance;
            var sources = WaterSourceVolume.ActiveSources;
            for (int i = 0; i < sources.Count; i++)
            {
                var candidate = sources[i];
                if (!candidate) continue;
                float t = (candidate.SurfaceY - ray.origin.y) / ray.direction.y;
                if (t < .001f || t > nearest) continue;
                Vector3 contact = ray.GetPoint(t);
                if (!candidate.ContainsSurface(contact)) continue;
                nearest = t; point = contact; water = candidate;
            }
            return water != null;
        }
    }
}
