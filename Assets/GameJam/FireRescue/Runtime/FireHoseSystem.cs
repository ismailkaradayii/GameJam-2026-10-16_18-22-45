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
        private Transform carryingHand;
        private Vector3 deployedPoint;
        private Quaternion deployedRotation;
        private Vector3[] hosePoints;
        private readonly RaycastHit[] groundHits = new RaycastHit[24];

        public bool IsCarried { get; private set; }
        public bool IsDeployed { get; private set; }
        public bool PumpEnabled { get; private set; }
        public bool IsPumping { get; private set; }
        public float TankLitres => tankLitres;
        public float Tank01 => Mathf.Clamp01(tankLitres / Mathf.Max(1f, tankCapacityLitres));
        public string Status { get; private set; } = "Hortum araçta.";
        public WaterSourceVolume CurrentWaterSource { get; private set; }
        [System.NonSerialized] public bool fillingUnlocked = true;

        private void Awake()
        {
            tankLitres = Mathf.Clamp(startingTankLitres, 0f, Mathf.Max(1f, tankCapacityLitres));
            if (ignoredCollisionRoot == null)
                ignoredCollisionRoot = transform;
            ConfigureHoseWidth();
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
        }

        private void OnDisable()
        {
            PumpEnabled = false;
            IsPumping = false;
            if (hoseLine != null)
                hoseLine.enabled = false;
        }

        public bool CanReach(Vector3 point) => hoseSocket != null &&
            (point - hoseSocket.position).sqrMagnitude <= maxHoseLength * maxHoseLength + 0.01f;

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
            deployedRotation = towardTruck.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(towardTruck.normalized, Vector3.up)
                : hoseSocket.rotation;
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
                tankLitres + Mathf.Max(0f, fillRateLitresPerSecond) * deltaTime);
            EvaluateState();
        }

        public void EvaluateState()
        {
            IsPumping = false;
            CurrentWaterSource = null;
            if (!fillingUnlocked) { Status = "Önce evlerdeki yedi kişiyi sığınağa götür."; return; }
            if (hoseSocket == null || nozzle == null)
            {
                Status = "Hortum bağlantıları eksik.";
                return;
            }
            if (!IsDeployed)
            {
                Status = "Hortum araçta.";
                return;
            }
            if (IsCarried)
            {
                Status = carryingHand != null && !CanReach(carryingHand.position)
                    ? "Hortumun uzunluk sınırına ulaştın. Araca yaklaş."
                    : "Emiş ucunu göle götürüp suya bırak.";
                return;
            }
            if (!CanReach(nozzle.position))
            {
                Status = "Hortum çok gergin. Aracı suya yaklaştır.";
                return;
            }
            if (!WaterSourceVolume.TryFindSource(nozzle.position, out WaterSourceVolume source))
            {
                Status = "Emiş ucu suda değil. Hortumu göle bırak.";
                return;
            }
            CurrentWaterSource = source;
            if (tankLitres >= Mathf.Max(1f, tankCapacityLitres))
            {
                PumpEnabled = false;
                Status = "Su deposu dolu. Pompa durdu.";
                return;
            }
            if (!PumpEnabled)
            {
                Status = "Emiş ucu suda. Pompayı açabilirsin.";
                return;
            }
            IsPumping = true;
            Status = "Gölden su çekiliyor. Depo dolduruluyor.";
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

            int count = Mathf.Clamp(hoseSegments, 12, 64) + 1;
            if (hosePoints == null || hosePoints.Length != count)
                hosePoints = new Vector3[count];
            Vector3 start = hoseSocket.position;
            Vector3 end = nozzle.position;
            Vector3 delta = end - start;
            float distance = delta.magnitude;
            Vector3 direction = distance > 0.001f ? delta / distance : hoseSocket.forward;
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
                point.y -= Mathf.Sin(t * Mathf.PI) * sag;
                if (i > 0 && i < count - 1 && TryGetGroundHeight(point, out float groundY))
                    point.y = Mathf.Max(point.y, groundY + Mathf.Max(hoseGroundClearance, hoseDiameter * .5f));
                hosePoints[i] = point;
            }

            hoseLine.useWorldSpace = true;
            hoseLine.positionCount = count;
            hoseLine.SetPositions(hosePoints);
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
            int hits = Physics.RaycastNonAlloc(point + Vector3.up * groundProbeHeight, Vector3.down,
                groundHits, groundProbeHeight + 60f, hoseCollisionMask, QueryTriggerInteraction.Ignore);
            height = float.NegativeInfinity;
            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = groundHits[i];
                Transform hitTransform = hit.collider.transform;
                // The carried hose must not treat the player's capsule as a piece of terrain.
                if (hit.collider is CharacterController || hit.normal.y < 0.25f ||
                    (ignoredCollisionRoot != null && hitTransform.IsChildOf(ignoredCollisionRoot)) ||
                    (nozzle != null && hitTransform.IsChildOf(nozzle)))
                    continue;
                height = Mathf.Max(height, hit.point.y);
            }
            return !float.IsNegativeInfinity(height);
        }
    }
}
