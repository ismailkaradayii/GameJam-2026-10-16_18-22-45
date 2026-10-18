using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Rear intake hose and water tank. Input and player movement are handled separately.</summary>
    [DisallowMultipleComponent]
    public sealed class FireHoseSystem : MonoBehaviour
    {
        public Transform hoseSocket;
        public Transform nozzle;
        public LineRenderer hoseLine;
        [Min(1f)] public float tankCapacityLitres = 2000f;
        [Min(0f)] public float fillRateLitresPerSecond = 150f;
        [Min(1f)] public float maxHoseLength = 45f;
        [Min(0f)] public float startingTankLitres;
        [Range(12, 64)] public int hoseSegments = 36;
        [Tooltip("Fixed world-space hose diameter; independent of deployed length and truck transform scale.")]
        [Min(.02f)] public float hoseDiameter = .16f;
        public LayerMask hoseCollisionMask = ~0;
        [Tooltip("Usually the truck root; its colliders are excluded from hose ground sampling.")]
        public Transform ignoredCollisionRoot;
        [Min(0.01f)] public float hoseGroundClearance = 0.06f;
        [Min(0.1f)] public float groundProbeHeight = 8f;

        [SerializeField] private float tankLitres;
        [Header("Tank filling audio")]
        [SerializeField] private AudioClip tankFillClip;
        [SerializeField, Range(0f, 1f)] private float emptyTankFillVolume = 0.65f;
        [SerializeField, Range(0f, 1f)] private float fullTankFillVolume = 0.18f;
        [SerializeField, Range(0f, 1f)] private float emptyTankEcho = 0.35f;
        [SerializeField, Min(0.01f)] private float fillAudioFadeSpeed = 3f;
        private AudioSource tankFillSource;
        private AudioEchoFilter tankFillEcho;
        private FireTruckDrive truckDrive;
        private ThermalDamage thermal;
        public float PumpEfficiency { get { if (!thermal) thermal = GetComponent<ThermalDamage>(); return thermal ? thermal.PumpEfficiency : 1f; } }
        private bool fillAudioPaused;
        private Transform carryingHand;
        private Vector3 deployedPoint;
        private Quaternion deployedRotation;
        private Vector3[] hosePoints;
        private readonly RaycastHit[] groundHits = new RaycastHit[24];
        Terrain[] hoseTerrains;
        int groundFrame = -1;
        readonly Vector3[] intakeGroundOffsets = new Vector3[9];
        Vector3 lastDrawStart, lastDrawEnd, lastDrawForward;
        float lastDrawAt = -1;
        LakeHosePassage lastDrawPassage;

        public bool IsCarried { get; private set; }
        public bool IsDeployed { get; private set; }
        public bool PumpEnabled { get; private set; }
        public bool IsPumping { get; private set; }
        public float TankLitres => tankLitres;
        public float Tank01 => Mathf.Clamp01(tankLitres / Mathf.Max(1f, tankCapacityLitres));
        public string Status { get; private set; } = "Hose stowed on truck.";
        public WaterSourceVolume CurrentWaterSource { get; private set; }
        [System.NonSerialized] public bool fillingUnlocked = true;

        private void Awake()
        {
            tankLitres = Mathf.Clamp(startingTankLitres, 0f, Mathf.Max(1f, tankCapacityLitres));
            thermal = GetComponent<ThermalDamage>();
            if (ignoredCollisionRoot == null)
                ignoredCollisionRoot = transform;
            ConfigureHoseWidth();
            ConfigureFillAudio();
        }

        private void Update()
        {
            UpdateNozzlePose();
            TickPump(Time.deltaTime);
        }

        private void LateUpdate()
        {
            UpdateNozzlePose();
            DrawHose();
            UpdateFillAudio();
        }

        private void OnDisable()
        {
            PumpEnabled = false;
            IsPumping = false;
            StopFillAudio();
            if (hoseLine != null)
                hoseLine.enabled = false;
        }

        public bool CanReach(Vector3 point)
        {
            if(!hoseSocket)return false;
            var passage=LakeHosePassage.Find(point);
            return passage ? passage.Length(hoseSocket.position,point)<=maxHoseLength+.001f :
                (point-hoseSocket.position).sqrMagnitude<=maxHoseLength*maxHoseLength+.01f;
        }

        public bool TryPickup(Transform hand)
        {
            if (hand == null || nozzle == null || hoseSocket == null || !CanReach(hand.position))
                return false;
            carryingHand = hand;
            IsCarried = true;
            IsDeployed = true;
            PumpEnabled = false;
            UpdateNozzlePose();
            EvaluateState();
            return true;
        }

        public void Drop(Vector3 point)
        {
            if (nozzle == null || hoseSocket == null)
                return;
            deployedPoint = ClampToReach(point);
            Vector3 towardTruck = hoseSocket.position - deployedPoint;
            towardTruck.y = 0;
            deployedRotation = towardTruck.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(towardTruck.normalized, Vector3.up)
                : hoseSocket.rotation;
            PrepareIntakeGroundOffsets();
            deployedPoint = ResolveDeploymentPoint(deployedPoint);
            carryingHand = null;
            IsCarried = false;
            IsDeployed = true;
            UpdateNozzlePose();
            EvaluateState();
        }

        public void Retract()
        {
            carryingHand = null;
            IsCarried = false;
            IsDeployed = false;
            PumpEnabled = false;
            IsPumping = false;
            CurrentWaterSource = null;
            UpdateNozzlePose();
            if (hoseLine != null)
                hoseLine.enabled = false;
            EvaluateState();
        }

        public void TogglePump()
        {
            PumpEnabled = !PumpEnabled;
            EvaluateState();
        }

        public void SetTankLitres(float litres)
        {
            tankLitres = Mathf.Clamp(litres, 0f, Mathf.Max(1f, tankCapacityLitres));
            EvaluateState();
        }

        /// <summary>Removes water from the tank and returns the litres actually supplied.</summary>
        public float ConsumeWater(float litres)
        {
            if (litres <= 0f || float.IsNaN(litres) || float.IsInfinity(litres) ||
                tankLitres <= 0f || float.IsNaN(tankLitres) || float.IsInfinity(tankLitres))
                return 0f;
            float consumed = Mathf.Min(litres, tankLitres);
            tankLitres = Mathf.Max(0f, tankLitres - consumed);
            EvaluateState();
            return consumed;
        }

        /// <summary>Deterministic pump step, also usable by editor verification.</summary>
        public void TickPump(float deltaTime)
        {
            EvaluateState();
            if (!IsPumping || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;
            tankLitres = Mathf.Min(Mathf.Max(1f, tankCapacityLitres),
                tankLitres + Mathf.Max(0f, fillRateLitresPerSecond) * PumpEfficiency * deltaTime);
            EvaluateState();
        }

        public void EvaluateState()
        {
            if (!thermal) thermal = GetComponent<ThermalDamage>();
            IsPumping = false;
            CurrentWaterSource = null;
            if (thermal && thermal.IsDestroyed) { Status = "The fire truck's pump is destroyed."; return; }
            if (!fillingUnlocked) { Status = "First lead all seven villagers to shelter."; return; }
            if (hoseSocket == null || nozzle == null)
            {
                Status = "Hose connections are missing.";
                return;
            }
            if (!IsDeployed)
            {
                Status = "Hose stowed on truck.";
                return;
            }
            if (IsCarried)
            {
                Status = carryingHand != null && !CanReach(carryingHand.position)
                    ? "The hose is at its limit. Move closer to the truck."
                    : "Carry the intake to the lake and drop it in the water.";
                return;
            }
            if (!CanReach(nozzle.position))
            {
                Status = "Hose too taut. Move the truck closer to the water.";
                return;
            }
            if (!WaterSourceVolume.TryFindSource(nozzle.position, out WaterSourceVolume source))
            {
                Status = "The intake is not in the water.";
                return;
            }
            CurrentWaterSource = source;
            if (tankLitres >= Mathf.Max(1f, tankCapacityLitres))
            {
                PumpEnabled = false;
                Status = "Tank full. Pump stopped.";
                return;
            }
            if (!PumpEnabled)
            {
                Status = "Intake submerged. Start the pump.";
                return;
            }
            IsPumping = true;
            Status = "Pumping water from the lake.";
        }

        private void ConfigureFillAudio()
        {
            if (!tankFillClip) return;
            var emitter = new GameObject("Tank Fill Audio");
            emitter.transform.SetParent(transform, false);
            tankFillSource = emitter.AddComponent<AudioSource>();
            tankFillSource.playOnAwake = false;
            tankFillSource.clip = tankFillClip;
            tankFillSource.loop = true;
            tankFillSource.volume = 0f;
            tankFillSource.dopplerLevel = 0f;
            tankFillSource.minDistance = 12f;
            tankFillSource.maxDistance = 80f;
            tankFillEcho = emitter.AddComponent<AudioEchoFilter>();
            tankFillEcho.delay = 120f;
            tankFillEcho.decayRatio = .25f;
            tankFillEcho.dryMix = 1f;
            tankFillEcho.wetMix = 0f;
            truckDrive = GetComponent<FireTruckDrive>();
            tankFillClip.LoadAudioData();
        }

        private void UpdateFillAudio()
        {
            if (!tankFillSource || !tankFillClip) return;
            if (Time.timeScale <= 0f)
            {
                if (!fillAudioPaused && tankFillSource.isPlaying) tankFillSource.Pause();
                fillAudioPaused = true;
                return;
            }
            if (fillAudioPaused) { tankFillSource.UnPause(); fillAudioPaused = false; }

            bool filling = IsPumping && fillRateLitresPerSecond > 0f && PumpEfficiency > 0f;
            float fullness = Tank01;
            float sfx = Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f));
            float target = filling ? Mathf.Lerp(emptyTankFillVolume, fullTankFillVolume, fullness) * sfx : 0f;
            tankFillSource.spatialBlend = truckDrive && truckDrive.IsDriven ? 0f : .7f;
            tankFillEcho.wetMix = Mathf.MoveTowards(tankFillEcho.wetMix,
                filling ? emptyTankEcho * (1f - fullness) : 0f, Time.deltaTime * 2f);
            if (filling && !tankFillSource.isPlaying) tankFillSource.Play();
            tankFillSource.volume = Mathf.MoveTowards(tankFillSource.volume, target, fillAudioFadeSpeed * Time.deltaTime);
            if (!filling && tankFillSource.volume <= .001f && tankFillSource.isPlaying) tankFillSource.Stop();
        }

        private void StopFillAudio()
        {
            fillAudioPaused = false;
            if (!tankFillSource) return;
            tankFillSource.Stop();
            tankFillSource.volume = 0f;
        }

        private Vector3 ClampToReach(Vector3 point) => hoseSocket.position +
            Vector3.ClampMagnitude(point - hoseSocket.position, Mathf.Max(1f, maxHoseLength));

        private void UpdateNozzlePose()
        {
            if (nozzle == null || hoseSocket == null)
                return;
            if (IsCarried && carryingHand != null)
                nozzle.SetPositionAndRotation(ClampToReach(carryingHand.position), carryingHand.rotation);
            else if (IsDeployed)
                nozzle.SetPositionAndRotation(deployedPoint, deployedRotation);
            else
                nozzle.SetPositionAndRotation(hoseSocket.position, hoseSocket.rotation);
        }

        private void DrawHose()
        {
            if (hoseLine == null)
                return;
            hoseLine.enabled = IsDeployed && hoseSocket != null && nozzle != null;
            if (!hoseLine.enabled)
                return;

            Vector3 start = hoseSocket.position;
            Vector3 end = nozzle.position;
            var passage = !IsCarried ? LakeHosePassage.Find(end) : null;
            float distance = passage ? passage.Length(start,end) : Vector3.Distance(start,end);
            // Long segments bridge through the steep bank even when their endpoints clear the terrain.
            int count = Mathf.Clamp(Mathf.Max(hoseSegments, Mathf.CeilToInt(distance / .25f)), 12, 256) + 1;
            if (hosePoints != null && hosePoints.Length == count && Time.time - lastDrawAt < .2f &&
                (start - lastDrawStart).sqrMagnitude < .000001f && (end - lastDrawEnd).sqrMagnitude < .000001f &&
                (hoseSocket.forward - lastDrawForward).sqrMagnitude < .000001f && passage==lastDrawPassage) return;
            if (hosePoints == null || hosePoints.Length != count)
                hosePoints = new Vector3[count];
            Vector3 delta = end - start;
            Vector3 direction = delta.sqrMagnitude > .000001f ? delta.normalized : hoseSocket.forward;
            float slack = Mathf.Max(0f, maxHoseLength - distance);
            float lead = Mathf.Min(0.8f, Mathf.Min(distance * 0.2f, slack * 0.25f));
            Vector3 controlA = start + hoseSocket.forward * lead;
            Vector3 controlB = end - direction * lead;
            float sag = Mathf.Min(2.2f, Mathf.Min(distance * 0.15f, slack * 0.35f));

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float u = 1f - t;
                Vector3 point = u * u * u * start + 3f * u * u * t * controlA +
                    3f * u * t * t * controlB + t * t * t * end;
                if(passage)point=SamplePassage(start,end,passage,t,distance,lead);
                point.y -= Mathf.Sin(t * Mathf.PI) * sag;
                if (i > 0 && i < count - 1)
                    point.y = Mathf.Max(point.y, HoseSupportHeight(point, direction));
                hosePoints[i] = point;
            }

            // Even short chords can cut the top of a sharp terrain ridge. Lift their supporting
            // vertices, keeping the socket and intake attached, rather than only clamping vertices.
            for (int i = 0; i < count - 1; i++)
                for (int j = 1; j < 4; j++)
                {
                    float t = j / 4f;
                    Vector3 point = Vector3.Lerp(hosePoints[i], hosePoints[i + 1], t);
                    float lift = HoseSupportHeight(point, direction) - point.y;
                    if (lift <= 0) continue;
                    lift += .01f;
                    if (i == 0) hosePoints[i + 1].y += lift / t;
                    else if (i + 1 == count - 1) hosePoints[i].y += lift / (1f - t);
                    else { hosePoints[i].y += lift; hosePoints[i + 1].y += lift; }
                }

            hoseLine.useWorldSpace = true;
            hoseLine.positionCount = count;
            hoseLine.SetPositions(hosePoints);
            lastDrawStart = start; lastDrawEnd = end; lastDrawForward = hoseSocket.forward; lastDrawAt = Time.time;lastDrawPassage=passage;
        }

        Vector3 SamplePassage(Vector3 start,Vector3 end,LakeHosePassage passage,float t,float length,float lead)
        {
            Vector3 near=passage.dryGuide.position,far=passage.wetGuide.position;
            float first=Vector3.Distance(start,near),middle=Vector3.Distance(near,far),along=t*length;
            Vector3 through=(far-near).normalized;
            if(along<=first)
                return HoseCurve(start,start+hoseSocket.forward*lead,near-through*Mathf.Min(.8f,first*.2f),near,first>.001f?along/first:1);
            if(along<=first+middle)return Vector3.Lerp(near,far,(along-first)/Mathf.Max(.001f,middle));
            float last=Vector3.Distance(far,end),fraction=(along-first-middle)/Mathf.Max(.001f,last);
            return HoseCurve(far,far+through*Mathf.Min(.8f,last*.2f),end-(end-far).normalized*lead,end,fraction);
        }
        static Vector3 HoseCurve(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float t)
        {
            float u=1-t;return u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;
        }

        float HoseSupportHeight(Vector3 point, Vector3 direction)
        {
            float radius = Mathf.Max(.01f, hoseDiameter * .5f);
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * radius;
            float height = float.NegativeInfinity;
            for (int i = -1; i <= 1; i++)
                if (TryGetGroundHeight(point + side * i, out float groundY)) height = Mathf.Max(height, groundY);
            return height + radius + hoseGroundClearance;
        }

        void PrepareIntakeGroundOffsets()
        {
            // Measure the actual strainer, including the authored scene scale.
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * .02f);
            foreach (var mesh in nozzle.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mesh.sharedMesh) continue;
                Bounds local = mesh.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    bounds.Encapsulate(nozzle.InverseTransformPoint(mesh.transform.TransformPoint(corner)));
                }
            }
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                intakeGroundOffsets[i] = deployedRotation * Vector3.Scale(corner, nozzle.lossyScale);
            }
            intakeGroundOffsets[8] = Vector3.zero;
        }

        Vector3 SupportedIntakePoint(Vector3 point)
        {
            float minimumY = point.y;
            foreach (Vector3 offset in intakeGroundOffsets)
                if (TryGetGroundHeight(point + offset, out float groundY))
                    minimumY = Mathf.Max(minimumY, groundY + hoseGroundClearance - offset.y);
            point.y = minimumY;
            return point;
        }

        Vector3 ResolveDeploymentPoint(Vector3 requested)
        {
            Vector3 supported = SupportedIntakePoint(requested);
            if (!WaterSourceVolume.TryFindSource(requested, out var source)) return supported;
            if (source.ContainsIntake(supported) && CanReach(supported)) return supported;
            // Grounding a strainer on the shallow marker can lift it out of the water.
            // Find a nearby deeper placement in the same lake instead of burying the mesh.
            Vector3 waterCenter = source.volume ? source.volume.bounds.center : source.transform.position;
            Vector3 intoWater = Vector3.ProjectOnPlane(waterCenter - requested, Vector3.up).normalized;
            if (intoWater.sqrMagnitude < .01f) intoWater = Vector3.ProjectOnPlane(requested - hoseSocket.position, Vector3.up).normalized;
            for (int step = 1; step <= 12; step++)
                for (int fan = 0; fan < 8; fan++)
                {
                    float angle = fan == 0 ? 0 : (fan % 2 == 0 ? 1 : -1) * ((fan + 1) / 2) * 45f;
                    Vector3 candidate = SupportedIntakePoint(requested + Quaternion.AngleAxis(angle, Vector3.up) * intoWater * (step * .35f));
                    if (source.ContainsIntake(candidate) && CanReach(candidate)) return candidate;
                }
            return supported;
        }

        void ConfigureHoseWidth()
        {
            if (!hoseLine) return;
            // A .12 width curve multiplied by a .16 multiplier made the old hose only .0192m thick.
            hoseLine.widthCurve = AnimationCurve.Constant(0, 1, 1);
            hoseLine.widthMultiplier = Mathf.Max(.02f, hoseDiameter);
            hoseLine.textureMode = LineTextureMode.Tile;
        }

        private bool TryGetGroundHeight(Vector3 point, out float height)
        {
            if (groundFrame != Time.frameCount) { groundFrame = Time.frameCount; hoseTerrains = Terrain.activeTerrains; }
            height = float.NegativeInfinity;
            foreach (var terrain in hoseTerrains)
            {
                if (!terrain || !terrain.terrainData || (hoseCollisionMask.value & (1 << terrain.gameObject.layer)) == 0) continue;
                var collider = terrain.GetComponent<TerrainCollider>();
                if (!collider || !collider.enabled) continue;
                Vector3 relative = point - terrain.transform.position, size = terrain.terrainData.size;
                if (relative.x < 0 || relative.z < 0 || relative.x > size.x || relative.z > size.z) continue;
                int res = terrain.terrainData.holesResolution;
                if (res > 0 && terrain.terrainData.IsHole(Mathf.Clamp((int)(relative.x / size.x * res), 0, res - 1), Mathf.Clamp((int)(relative.z / size.z * res), 0, res - 1))) continue;
                height = Mathf.Max(height, terrain.SampleHeight(point) + terrain.transform.position.y);
            }
            // A sagging candidate may already be below the shore. Probe from above both endpoints and terrain.
            float ceiling = Mathf.Max(point.y, Mathf.Max(hoseSocket ? hoseSocket.position.y : point.y,
                nozzle ? nozzle.position.y : point.y));
            if (!float.IsNegativeInfinity(height)) ceiling = Mathf.Max(ceiling, height);
            Vector3 origin = new Vector3(point.x, ceiling + groundProbeHeight, point.z);
            int hits = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits,
                ceiling + groundProbeHeight - point.y + 60f, hoseCollisionMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] samples = groundHits;
            if (hits == groundHits.Length)
            {
                samples = Physics.RaycastAll(origin, Vector3.down, ceiling + groundProbeHeight - point.y + 60f, hoseCollisionMask, QueryTriggerInteraction.Ignore);
                hits = samples.Length;
            }
            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = samples[i];
                Transform hitTransform = hit.collider.transform;
                // The carried hose must not treat the player's capsule as a piece of terrain.
                if (hit.collider is CharacterController || hit.normal.y < .05f || WaterSourceVolume.IsSurfaceCollider(hit.collider) ||
                    (ignoredCollisionRoot != null && hitTransform.IsChildOf(ignoredCollisionRoot)) ||
                    (nozzle != null && hitTransform.IsChildOf(nozzle)))
                    continue;
                height = Mathf.Max(height, hit.point.y);
            }
            return !float.IsNegativeInfinity(height);
        }
    }
}
