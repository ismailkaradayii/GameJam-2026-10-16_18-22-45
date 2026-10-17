using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Itfaiye.Editor
{
    // Explicit Play Mode review in the real, baked map. No scenes or assets are saved.
    [InitializeOnLoad]
    public static class ItfaiyeWeaponVisibilityChecks
    {
        const string Folder = "Logs/WeaponVisibilityReview";
        const string Pending = "Itfaiye.WeaponVisibility.Pending";
        const string Prefab = "Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab";
        const int Width = 640, Height = 480;

        [Serializable] public class Sample
        {
            public string name;
            public Vector3 position;
            public float yaw;
            public int legacyPixels, fixedPixels;
            public bool passed;
        }
        [Serializable] public class Report
        {
            public string status = "running", scene, error;
            public bool prefabOverlayIgnoresWorldOcclusion, runtimeOverlayIgnoresWorldOcclusion;
            public int reproducedDisappearances;
            public List<Sample> samples = new List<Sample>();
            public List<string> errors = new List<string>();
        }

        static Report report;
        static ItfaiyeFirstPersonController player;
        static ItfaiyeWeaponVisibilityDriver driver;
        static readonly List<Sample> route = new List<Sample>();
        static int sampleIndex = -1, lastFrame;
        static float placedAt, captureDelta;
        static double started;
        static bool background;

        static ItfaiyeWeaponVisibilityChecks()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
                if (state == PlayModeStateChange.ExitingPlayMode && sampleIndex >= 0) Finish("Review interrupted.");
            };
        }

        [MenuItem("GameJam/Final Loop/Review Weapon Visibility")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Start this review in Edit Mode.");
            if (!Object.FindFirstObjectByType<FireRescuePlayer>())
                throw new InvalidOperationException("Open the gameplay map before running this review.");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        static void Start()
        {
            Directory.CreateDirectory(Folder);
            report = new Report { scene = SceneManager.GetActiveScene().path };
            started = EditorApplication.timeSinceStartup;
            captureDelta = Time.captureDeltaTime;
            background = Application.runInBackground;
            Application.runInBackground = true;
            Time.captureDeltaTime = 1f / 60f;
            Application.logMessageReceived += Log;
            try
            {
                var source = Object.FindFirstObjectByType<FireRescuePlayer>();
                var truck = source.drive;
                var truckBounds = truck.GetComponentsInChildren<Collider>()
                    .Where(c => !(c is WheelCollider) && !c.isTrigger).First().bounds;
                var originalPosition = source.firstPerson.transform.position;
                var scale = source.firstPerson.transform.lossyScale;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                report.prefabOverlayIgnoresWorldOcclusion = !asset.GetComponent<ItfaiyeFirstPersonController>().handsCamera.useOcclusionCulling;

                // Freeze existing gameplay while retaining the map, truck meshes and baked visibility data.
                foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (behaviour.GetType().Assembly == typeof(FireRescuePlayer).Assembly)
                    { behaviour.StopAllCoroutines(); behaviour.enabled = false; }
                foreach (var body in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) body.isKinematic = true;
                foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
                foreach (var audio in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) audio.enabled = false;
                source.firstPerson.gameObject.SetActive(false);

                var container = new GameObject("Temporary weapon visibility review");
                container.SetActive(false);
                var instance = Object.Instantiate(asset, container.transform);
                instance.transform.localScale = scale;
                player = instance.GetComponent<ItfaiyeFirstPersonController>();
                player.externalControl = true;
                player.recoverFromFall = false;
                player.pistol.externalControl = true;
                player.pistol.showHud = false;
                player.playerCamera.useOcclusionCulling = false;
                player.playerCamera.farClipPlane = 3000f;
                player.playerCamera.aspect = player.handsCamera.aspect = 4f / 3f;
                player.handsCamera.farClipPlane = 8f;
                // Exercise old serialized scene overrides as well as the corrected prefab.
                player.handsCamera.useOcclusionCulling = true;
                container.SetActive(true);
                report.runtimeOverlayIgnoresWorldOcclusion = !player.handsCamera.useOcclusionCulling;
                player.playerCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                driver = container.AddComponent<ItfaiyeWeaponVisibilityDriver>();
                driver.player = player;
                player.pistol.Equip();

                route.Clear();
                Vector3 center = truckBounds.center;
                float side = truckBounds.extents.x, front = truckBounds.extents.z;
                foreach (float gap in new[] { 14f, 7f, 2f })
                {
                    Add("truck-left-" + gap, center + Vector3.left * (side + gap), center);
                    Add("truck-right-" + gap, center + Vector3.right * (side + gap), center);
                    Add("truck-front-" + gap, center + Vector3.forward * (front + gap), center);
                    Add("truck-rear-" + gap, center + Vector3.back * (front + gap), center);
                }
                Add("village-spawn", originalPosition, originalPosition + Vector3.forward * 20f);
                foreach (float distance in new[] { 60f, 120f, 200f })
                    foreach (float offset in new[] { -55f, 55f })
                    {
                        var point = center + new Vector3(offset, 0, distance);
                        Add("rocky-route-" + distance + "-" + offset, point, point + Vector3.forward * 25f);
                    }
                sampleIndex = 0;
                Place();
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }

        static void Add(string name, Vector3 point, Vector3 lookAt)
        {
            var hits = Physics.RaycastAll(new Vector3(point.x, 2000f, point.z), Vector3.down, 4000f,
                Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("FirstPersonHands")), QueryTriggerInteraction.Ignore);
            var ground = hits.Where(h => !h.collider.transform.IsChildOf(player.transform)).OrderBy(h => h.distance).FirstOrDefault();
            if (!ground.collider) return;
            point.y = ground.point.y + .08f;
            Vector3 direction = lookAt - point;
            route.Add(new Sample { name = name, position = point, yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg });
        }

        static void Place()
        {
            var sample = route[sampleIndex];
            var motor = player.GetComponent<CharacterController>();
            motor.enabled = false;
            player.transform.SetPositionAndRotation(sample.position, Quaternion.Euler(0, sample.yaw, 0));
            motor.enabled = true;
            placedAt = Time.time;
        }

        static void Update()
        {
            if (sampleIndex < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 180) throw new TimeoutException("Visibility review timed out.");
                if (Time.time - placedAt < .4f || !player.pistol.Ready) return;
                var sample = route[sampleIndex];
                var camera = player.handsCamera;
                try
                {
                    // Compare pixels from the complete gameplay camera stack, including baked map culling.
                    var renderers = player.pistol.weapon.GetComponentsInChildren<Renderer>(true);
                    camera.useOcclusionCulling = true;
                    sample.legacyPixels = WeaponPixels(player.playerCamera, renderers);
                    camera.useOcclusionCulling = false;
                    sample.fixedPixels = WeaponPixels(player.playerCamera, renderers);
                }
                finally
                {
                    camera.useOcclusionCulling = false;
                }
                sample.passed = sample.fixedPixels > 200 && player.pistol.Ready;
                if (sample.legacyPixels < sample.fixedPixels * .5f) report.reproducedDisappearances++;
                report.samples.Add(sample);
                if (sampleIndex == 8 || sampleIndex == 10 || sampleIndex == 15 || sampleIndex == 18)
                    Capture(player.playerCamera, Folder + "/" + sample.name + ".png", 960, 720);
                Save();
                if (++sampleIndex >= route.Count) Finish(null);
                else Place();
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }

        static int WeaponPixels(Camera camera, Renderer[] renderers)
        {
            var enabled = renderers.Select(r => r.enabled).ToArray();
            var withWeapon = Capture(camera, null, Width, Height);
            Color32[] withoutWeapon;
            try
            {
                foreach (var renderer in renderers) renderer.enabled = false;
                withoutWeapon = Capture(camera, null, Width, Height);
            }
            finally { for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = enabled[i]; }
            int pixels = 0;
            for (int i = 0; i < withWeapon.Length; i++)
                if (Mathf.Abs(withWeapon[i].r - withoutWeapon[i].r) + Mathf.Abs(withWeapon[i].g - withoutWeapon[i].g)
                    + Mathf.Abs(withWeapon[i].b - withoutWeapon[i].b) > 40) pixels++;
            return pixels;
        }

        static Color32[] Capture(Camera camera, string path, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24);
            var previous = RenderTexture.active;
            var target = camera.targetTexture;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                if (path != null) File.WriteAllBytes(path, texture.EncodeToPNG());
                return texture.GetPixels32();
            }
            finally
            {
                camera.targetTexture = target;
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(rt);
            }
        }

        static void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }

        static void Save() => File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true));

        static void Finish(string error)
        {
            sampleIndex = -1;
            if (report == null) return;
            report.error = error;
            report.status = error == null && report.errors.Count == 0 && report.prefabOverlayIgnoresWorldOcclusion
                && report.runtimeOverlayIgnoresWorldOcclusion && report.samples.Count >= 12 && report.samples.All(s => s.passed) ? "SUCCESS" : "FAILED";
            Save();
            Application.logMessageReceived -= Log;
            Application.runInBackground = background;
            Time.captureDeltaTime = captureDelta;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }

    [DefaultExecutionOrder(-95)]
    public sealed class ItfaiyeWeaponVisibilityDriver : MonoBehaviour
    {
        public ItfaiyeFirstPersonController player;
        void Update()
        {
            player.SimulateExternalMovement(Vector2.zero, false, false, false, true, 5f, Vector2.zero);
            player.pistol.SetExternalInput(true, false, false, false, false, false);
        }
        void LateUpdate() { player.ApplyExternalView(); }
    }
}
