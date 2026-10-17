using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-70)]
    public sealed class EvacuationMission : MonoBehaviour
    {
        readonly List<Rect> placedMarkerRects = new List<Rect>();
        public FireRescuePlayer player;
        public VolcanoEruptionSequence eruption;
        public EvacuationResident[] residents;
        public Transform shelterEntrance;
        public Transform shelterApproach;
        public BoxCollider shelterSafeArea;
        public bool ShelterOpened { get; private set; }
        [Header("Evacuation ambience")]
        [SerializeField] private AudioClip crowdPanicLoop;
        [SerializeField, Range(0f, 1f)] private float crowdPanicVolume = .45f;
        [SerializeField, Min(.01f)] private float crowdFadeSpeed = 1.2f;
        [SerializeField] private AudioSource crowdSource;
        private bool crowdPaused;
        public bool startAutomatically = true;
        public float dispatchDelay = 3f;
        public float shoutRadius = 45f;
        [Tooltip("The village siren scenario lets the existing Q call reach every house; follow and shelter behavior is unchanged.")]
        public bool villageWideCall;
        public float shoutCooldown = 5f;
        public Key callKey = Key.Q;
        public event System.Action<EvacuationResident> ResidentRescued;
        public event System.Action<EvacuationResident> ResidentLost;
        public bool showHud = true;
        [Tooltip("When true, the Canvas RescueHUD draws the objective and subtitle while this component keeps world markers active.")]
        public bool canvasHudOwnsPanel;
        public bool IsActive { get; private set; }
        public bool IsComplete { get; private set; }
        public int RescuedCount { get; private set; }
        public int DeadCount { get { int count = 0; if (residents != null) foreach (var r in residents) if (r && r.IsDead) count++; return count; } }
        public int RemainingCount => Mathf.Max(0, TotalCount - RescuedCount - DeadCount);
        public bool IsResolved => TotalCount > 0 && RemainingCount == 0;
        public int TotalCount { get { int count = 0; if (residents != null) foreach (var r in residents) if (r) count++; return count; } }
        public int CalledCount { get { int n = 0; if (residents != null) foreach (var r in residents) if (r && r.IsCalled) n++; return n; } }
        public Transform PlayerTransform => player ? (player.playerBody ? player.playerBody.transform : player.transform) : null;
        public float CooldownRemaining => Mathf.Max(0, nextShout - Time.time);
        public string CurrentMessage => subtitle ?? string.Empty;
        public float MessageRemaining => Mathf.Max(0f, subtitleUntil - Time.time);
        public string DestinationHintForHud => DestinationHint();
        readonly HashSet<EvacuationResident> safeResidents = new HashSet<EvacuationResident>();
        readonly List<Vector3> trail = new List<Vector3>();
        float started, nextShout, subtitleUntil;
        string subtitle;
        GUIStyle titleStyle, bodyStyle, centerStyle, markerStyle;

        void Awake()
        {
            started = Time.time;
            if (residents == null || residents.Length == 0) residents = GetComponentsInChildren<EvacuationResident>(true);
            foreach (var resident in residents) if (resident) resident.mission = this;
            if (!player) player = FindFirstObjectByType<FireRescuePlayer>();
            if (!eruption) eruption = FindFirstObjectByType<VolcanoEruptionSequence>();
            if (eruption) eruption.evacuation = this;
            if (crowdPanicLoop)
            {
                Transform emitter = transform.Find("Crowd Panic Ambience");
                if (!emitter) { var child = new GameObject("Crowd Panic Ambience"); emitter = child.transform; emitter.SetParent(transform, false); }
                crowdSource = emitter.GetComponent<AudioSource>();
                if (!crowdSource) crowdSource = emitter.gameObject.AddComponent<AudioSource>();
                crowdSource.clip = crowdPanicLoop;
                crowdSource.playOnAwake = false;
                crowdSource.loop = true;
                crowdSource.spatialBlend = .8f;
                crowdSource.rolloffMode = AudioRolloffMode.Logarithmic;
                crowdSource.minDistance = 8f;
                crowdSource.maxDistance = 65f;
                crowdSource.dopplerLevel = 0f;
                crowdSource.volume = 0f;
                crowdPanicLoop.LoadAudioData();
            }
        }
        void Update()
        {
            UpdateCrowdPanicAudio();
            if (Time.timeScale <= 0f || (player && player.GameplaySuspended)) return;
            if (startAutomatically && !IsActive && !IsComplete && !IsResolved && Time.time - started >= dispatchDelay) BeginMission();
            var t = PlayerTransform;
            if (IsActive && t && shelterApproach && Mathf.Abs(t.position.y - shelterApproach.position.y) < 5f &&
                (EvacuationResident.FlatDistance(t.position, shelterApproach.position) < 14f || IsInsideShelter(t.position)))
                ShelterOpened = true;
            if (t && (trail.Count == 0 || Vector3.Distance(trail[trail.Count - 1], t.position) > .8f))
            {
                trail.Add(t.position);
                if (trail.Count > 160) trail.RemoveAt(0);
            }
            if (!IsActive || !Application.isFocused || Time.timeScale <= 0f || Cursor.lockState != CursorLockMode.Locked) return;
            if (Keyboard.current != null && callKey != Key.None && Keyboard.current[callKey].wasPressedThisFrame) Shout();
        }
        void UpdateCrowdPanicAudio()
        {
            if (!crowdSource || !crowdPanicLoop) return;
            if (Time.timeScale <= 0f)
            {
                if (!crowdPaused && crowdSource.isPlaying) crowdSource.Pause();
                crowdPaused = true;
                return;
            }
            if (crowdPaused) { crowdSource.UnPause(); crowdPaused = false; }
            int activeCount = 0;
            Vector3 center = Vector3.zero;
            if (IsActive && residents != null)
                foreach (var resident in residents)
                    if (resident && resident.isActiveAndEnabled && resident.IsCalled && !resident.IsSafe && !resident.IsDead)
                    { center += resident.transform.position; activeCount++; }
            bool shouldPlay = IsActive && activeCount > 0;
            if (shouldPlay)
            {
                crowdSource.transform.position = center / activeCount;
                if (!crowdSource.isPlaying) crowdSource.Play();
            }
            float target = shouldPlay ? crowdPanicVolume * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f)) : 0f;
            crowdSource.volume = Mathf.MoveTowards(crowdSource.volume, target, Mathf.Max(.01f, crowdFadeSpeed) * Time.deltaTime);
            if (!shouldPlay && crowdSource.volume <= .001f && crowdSource.isPlaying) crowdSource.Stop();
        }
        public void BeginMission()
        {
            if (IsActive || IsComplete || IsResolved) return;
            IsActive = true;
            Say("DISPATCH: The volcano is active! Lead the " + TotalCount + " villagers to shelter.", 7f);
        }
        public bool Shout()
        {
            if (!IsActive || IsComplete || !PlayerTransform || Time.timeScale <= 0f || (player && player.GameplaySuspended) || Time.time < nextShout) return false;
            if (player.IsDriving) { Say("Press F to leave the vehicle before calling out.", 3f); return false; }
            nextShout = Time.time + Mathf.Max(1f, shoutCooldown);
            int count = 0;
            foreach (var resident in residents)
                if (resident && resident.houseExit && (villageWideCall || EvacuationResident.FlatDistance(PlayerTransform.position, resident.houseExit.position) <= shoutRadius) && resident.CallOutside()) count++;
            Say(count > 0 ? "YOU: Everyone outside! Follow me!\n" + count + " villagers heard your call." :
                System.Array.Exists(residents, r => r && !r.IsDead && !r.IsCalled) ? "YOU: Everyone outside!\nMove closer to the other houses and call again." : "YOU: Follow me to the shelter!", 4.5f);
            return true;
        }
        void Say(string text, float seconds) { subtitle = text; subtitleUntil = Time.time + seconds; }
        public Vector3 FollowPoint(int index)
        {
            if (!PlayerTransform) return transform.position;
            // Each larger resident gets a distinct place along the walked route.
            float behind = 3f + index * 1.9f;
            Vector3 point = PlayerTransform.position;
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                float length = Vector3.Distance(point, trail[i]);
                if (length >= behind) return Vector3.Lerp(point, trail[i], behind / length);
                behind -= length; point = trail[i];
            }
            return point;
        }
        public bool CanEnterShelter(EvacuationResident resident)
        {
            if (!ShelterOpened || !shelterEntrance || !shelterApproach || !resident || resident.IsDead || resident.IsBurning ||
                EvacuationResident.FlatDistance(resident.transform.position, shelterApproach.position) >= 30f) return false;
            // Larger bodies file in one at a time, filling the deepest available places first.
            foreach (var other in residents)
            {
                if (!other || other == resident || other.IsSafe || other.IsDead || other.IsBurning || !other.IsCalled) continue;
                if (other.State == EvacuationResident.ResidentState.EnteringShelter && !IsInsideShelter(other.transform.position)) return false;
                if (other.formationIndex < resident.formationIndex &&
                    EvacuationResident.FlatDistance(other.transform.position, shelterApproach.position) < 27f) return false;
            }
            return true;
        }
        public bool IsInsideShelter(Vector3 feet)
        {
            if (!shelterSafeArea) return false;
            Vector3 local = shelterSafeArea.transform.InverseTransformPoint(feet + Vector3.up) - shelterSafeArea.center;
            Vector3 half = shelterSafeArea.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
        public void ResidentArrived(EvacuationResident resident)
        {
            if (!IsActive || !resident || !resident.IsSafe || !IsInsideShelter(resident.transform.position) ||
                !System.Array.Exists(residents, r => r == resident) || !safeResidents.Add(resident)) return;
            RescuedCount = safeResidents.Count;
            ResidentRescued?.Invoke(resident);
            if (RescuedCount == TotalCount && TotalCount > 0)
            {
                IsComplete = true; IsActive = false;
                Say("DISPATCH: All " + TotalCount + " villagers are safe!\nNow fill the truck's tank at the lake.", 8f);
            }
            else Say("Reached shelter: " + RescuedCount + " / " + TotalCount + (DeadCount > 0 ? " | Lost: " + DeadCount : ""), 3f);
            if (IsResolved) IsActive = false;
        }
        public void ResidentDied(EvacuationResident resident)
        {
            if (!resident || !resident.IsDead || residents == null || !System.Array.Exists(residents, r => r == resident)) return;
            Say("DISPATCH: A villager has died in the fire.\nSheltered: " + RescuedCount + " / " + TotalCount + "  |  Lost: " + DeadCount + "  |  Still outside: " + RemainingCount, 6f);
            ResidentLost?.Invoke(resident);
            if (IsResolved) IsActive = false;
        }
        void OnGUI()
        {
            if (!showHud || Time.timeScale <= 0f || VolcanoCinematic.IsPlayingAny || (!IsActive && !IsComplete)) return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
                centerStyle = new GUIStyle(bodyStyle) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
                markerStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                titleStyle.font = markerStyle.font = Ashfall.UI.AshfallTypography.Semibold;
                bodyStyle.font = centerStyle.font = Ashfall.UI.AshfallTypography.Regular;
                markerStyle.fontSize = 14; markerStyle.fontStyle = FontStyle.Normal;
                titleStyle.normal.textColor = bodyStyle.normal.textColor = centerStyle.normal.textColor = markerStyle.normal.textColor = Color.white;
            }
            var oldMatrix = GUI.matrix; var oldColor = GUI.color;
            float scale = Mathf.Clamp(Screen.height / 900f, .75f, 1.5f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float w = Screen.width / scale, h = Screen.height / scale;
            float x = w >= 720 ? w - 334 : 18, y = w >= 720 ? 138 : 330;
            if (!canvasHudOwnsPanel)
            {
                GUI.color = new Color(.035f, .07f, .065f, .92f); GUI.DrawTexture(new Rect(x,y,316,146),Texture2D.whiteTexture);
                GUI.color = IsComplete ? new Color(.3f,1f,.65f) : new Color(1f,.75f,.3f);
                GUI.Label(new Rect(x+12,y+8,292,24),IsComplete ? "EVACUATION COMPLETE" : "OBJECTIVE · EVACUATE",titleStyle);
                GUI.color=Color.white;
                GUI.Label(new Rect(x+12,y+38,292,44),"Sheltered: "+RescuedCount+" / "+TotalCount+"    Called: "+CalledCount+" / "+TotalCount+"\n"+
                    (IsComplete ? "Everyone is safe. Fill the water tank." : CalledCount<TotalCount ? "Go to the houses and call out." : "Lead the group to the shelter."),bodyStyle);
                GUI.color=new Color(.15f,.23f,.24f);GUI.DrawTexture(new Rect(x+12,y+87,292,6),Texture2D.whiteTexture);
                GUI.color=new Color(.2f,.85f,.6f);GUI.DrawTexture(new Rect(x+12,y+87,292*RescuedCount/Mathf.Max(1,TotalCount),6),Texture2D.whiteTexture);
                GUI.color=Color.white;
                string action = IsComplete ? "Watch for the volcano warning." : player && player.IsDriving ? "F: Exit and call villagers" : CooldownRemaining>0 ? "Call again in: "+Mathf.CeilToInt(CooldownRemaining)+" sec" : "Q: Call villagers";
                GUI.Label(new Rect(x+12,y+102,292,40),action+(IsComplete?"":"\n"+DestinationHint()),bodyStyle);
                if(Time.time<subtitleUntil)
                {
                    float panelWidth=Mathf.Min(760,w-36);
                    GUI.color=new Color(.02f,.03f,.04f,.9f);GUI.DrawTexture(new Rect((w-panelWidth)*.5f,h-170,panelWidth,78),Texture2D.whiteTexture);
                    GUI.color=Color.white;GUI.Label(new Rect((w-panelWidth)*.5f+12,h-166,panelWidth-24,70),subtitle,centerStyle);
                }
            }
            if(IsActive && player && player.viewCamera)
            {
                placedMarkerRects.Clear();
                if(shelterApproach) Marker(shelterApproach.position+Vector3.up*3,"SHELTER",new Color(.35f,1,.7f),scale,w,h);
                var seen=new HashSet<Transform>();
                foreach(var r in residents) if(r && !r.IsDead && !r.IsCalled && r.houseExit && seen.Add(r.houseExit))
                    Marker(r.houseExit.position+Vector3.up*4,"EVACUATE",new Color(1,.8f,.35f),scale,w,h);
            }
            GUI.matrix=oldMatrix;GUI.color=oldColor;
        }
        void OnDisable()
        {
            if (crowdSource) { crowdSource.volume = 0f; crowdSource.Stop(); }
            crowdPaused = false;
        }
        void Marker(Vector3 world,string label,Color color,float scale,float width,float height)
        {
            Vector3 screen=player.viewCamera.WorldToScreenPoint(world);
            if(screen.z<=0)return;
            float x=screen.x/scale,y=(Screen.height-screen.y)/scale;
            if(x<80||x>width-80||y<280||y>height-180)return;
            Rect plate = new Rect(x - 67, y - 22, 134, 44);
            bool placed = false;
            for (int attempt = 0; attempt < 9; attempt++)
            {
                int step = (attempt + 1) / 2;
                plate.y = y - 22 + (attempt == 0 ? 0 : (attempt % 2 == 1 ? step * 50 : -step * 50));
                if (plate.y < 260 || plate.yMax > height - 150) continue;
                bool overlaps = false;
                foreach (Rect other in placedMarkerRects) if (plate.Overlaps(other)) { overlaps = true; break; }
                if (!overlaps) { placed = true; break; }
            }
            if (!placed) return;
            placedMarkerRects.Add(plate);
            GUI.color = new Color(.025f, .028f, .032f, .92f); GUI.DrawTexture(plate, Texture2D.whiteTexture);
            GUI.color = new Color(.72f, .35f, .13f, .7f); GUI.DrawTexture(new Rect(plate.x, plate.y, 2, plate.height), Texture2D.whiteTexture);
            GUI.color = Ashfall.UI.AshfallTypography.Paper;
            GUI.Label(new Rect(plate.x + 10, plate.y + 3, plate.width - 20, plate.height - 6), Ashfall.UI.AshfallTypography.NaturalCase(label) + "\n" + Mathf.RoundToInt(Vector3.Distance(PlayerTransform.position, world)) + " m", markerStyle);
        }
        string DestinationHint()
        {
            if(!PlayerTransform)return "";
            Transform target=shelterApproach;float nearest=float.PositiveInfinity;bool house=false;
            foreach(var r in residents)if(r&&!r.IsDead&&!r.IsCalled&&r.houseExit)
            {
                float distance=EvacuationResident.FlatDistance(PlayerTransform.position,r.houseExit.position);
                if(distance<nearest){nearest=distance;target=r.houseExit;house=true;}
            }
            if(!target)return "";
            Vector3 delta=Vector3.ProjectOnPlane(target.position-PlayerTransform.position,Vector3.up);
            Vector3 forward=player.viewCamera?player.viewCamera.transform.forward:PlayerTransform.forward;
            float angle=Vector3.SignedAngle(Vector3.ProjectOnPlane(forward,Vector3.up),delta,Vector3.up);
            string direction=Mathf.Abs(angle)>135?"Behind":angle>25?"Right":angle< -25?"Left":"Ahead";
            return (house?"Nearest house: ":"Shelter: ")+Mathf.RoundToInt(delta.magnitude)+" m · "+direction;
        }
    }
}
