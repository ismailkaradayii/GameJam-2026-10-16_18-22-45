using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Itfaiye.Editor
{
    // Explicit local review requests only. Preview scenes never change saved gameplay scenes.
    [InitializeOnLoad]
    public static class ItfaiyeWeaponFeelReview
    {
        public const string Folder = "Library/WeaponFeelReview";
        const string Pack = "Assets/Animation/Pistol_Handgun Locomotion Pack/";
        const string Prefab = "Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab";
        static double nextPoll;

        static ItfaiyeWeaponFeelReview() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            string request = Folder + "/request.txt";
            if (!File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if (command == "audit" && !EditorApplication.isPlaying) Audit();
                else if (command == "audit-fixed" && !EditorApplication.isPlaying) AuditFixed();
                else if (command == "run") ItfaiyeWeaponFeelChecks.Begin();
            }
            catch (Exception ex) { File.WriteAllText(Folder + "/error.txt", ex.ToString()); Debug.LogException(ex); }
        }

        [Serializable] public class Frame
        {
            public float time;
            public Vector3 leftFoot, rightFoot, leftKnee, rightKnee, leftToe, rightToe;
            public Quaternion leftRotation, rightRotation;
            public float[] legMuscles;
        }
        [Serializable] public class ClipData { public string name; public float length; public Vector3 speed; public List<Frame> frames = new List<Frame>(); }
        [Serializable] public class AuditData { public List<string> muscles = new List<string>(); public List<ClipData> clips = new List<ClipData>(); }

        [MenuItem("Tools/Itfaiye/Audit Weapon and Running Feet")]
        public static void Audit() => AuditClips(false);
        public static void AuditFixed() => AuditClips(true);
        static void AuditClips(bool stabilize)
        {
            Directory.CreateDirectory(Folder);
            var preview = EditorSceneManager.NewPreviewScene();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab).GetComponent<ItfaiyeFirstPersonController>();
            var body = Object.Instantiate(source.bodyAnimator.gameObject);
            SceneManager.MoveGameObjectToScene(body, preview);
            body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = body.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            ItfaiyeLegStabilizer legs = null;
            if (stabilize) { legs = body.AddComponent<ItfaiyeLegStabilizer>(); legs.Initialize(); }
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.enabled = renderer.name.EndsWith("lod0", StringComparison.OrdinalIgnoreCase);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var cameraObject = new GameObject("Foot Audit Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = preview;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.22f, .25f, .3f);
            camera.orthographic = true; camera.orthographicSize = 1.1f; camera.nearClipPlane = .01f;
            var lightObject = new GameObject("Foot Audit Light"); SceneManager.MoveGameObjectToScene(lightObject, preview);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var report = new AuditData();
            int[] muscleIds = Enumerable.Range(0, HumanTrait.MuscleCount).Where(i =>
                HumanTrait.MuscleName[i].Contains("Leg") || HumanTrait.MuscleName[i].Contains("Foot")).ToArray();
            report.muscles = muscleIds.Select(i => HumanTrait.MuscleName[i]).ToList();
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (string path in new[] { "Assets/Animation/run.fbx", "Assets/Animation/walk.fbx",
                    Pack + "pistol run.fbx", Pack + "pistol walk.fbx", Pack + "pistol run arc.fbx",
                    Pack + "pistol run arc (2).fbx", Pack + "pistol run backward.fbx", Pack + "pistol strafe.fbx" })
                {
                    var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
                    var data = new ClipData { name = clip.name, length = clip.length, speed = clip.averageSpeed };
                    using (var handler = new HumanPoseHandler(animator.avatar, body.transform))
                    for (int i = 0; i <= 32; i++)
                    {
                        float time = clip.length * i / 32f;
                        AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(body, clip, time); AnimationMode.EndSampling();
                        if (legs) legs.Stabilize(1f, 1f / 30f);
                        var pose = new HumanPose(); handler.GetHumanPose(ref pose);
                        if (i % 8 == 0 && (clip.name == "pistol run" || clip.name == "Run"))
                        {
                            Vector3 center = animator.GetBoneTransform(HumanBodyBones.Hips).position + Vector3.down * .45f;
                            camera.transform.position = center + new Vector3(1.6f, .1f, 3f); camera.transform.LookAt(center);
                            Capture(camera, Folder + "/" + (stabilize ? "fixed-" : "") + clip.name.Replace(' ', '-') + "-" + i + ".png");
                        }
                        data.frames.Add(new Frame { time = time,
                            leftFoot = Position(animator, HumanBodyBones.LeftFoot), rightFoot = Position(animator, HumanBodyBones.RightFoot),
                            leftKnee = Position(animator, HumanBodyBones.LeftLowerLeg), rightKnee = Position(animator, HumanBodyBones.RightLowerLeg),
                            leftToe = Position(animator, HumanBodyBones.LeftToes), rightToe = Position(animator, HumanBodyBones.RightToes),
                            leftRotation = animator.GetBoneTransform(HumanBodyBones.LeftFoot).localRotation,
                            rightRotation = animator.GetBoneTransform(HumanBodyBones.RightFoot).localRotation,
                            legMuscles = muscleIds.Select(m => pose.muscles[m]).ToArray() });
                    }
                    report.clips.Add(data);
                }
                File.WriteAllText(Folder + (stabilize ? "/feet-fixed.json" : "/feet-audit.json"), JsonUtility.ToJson(report, true));
            }
            finally { AnimationMode.StopAnimationMode(); EditorSceneManager.ClosePreviewScene(preview); }
        }
        static Vector3 Position(Animator animator, HumanBodyBones bone) => animator.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
        public static void Capture(Camera camera, string path)
        {
            var rt = new RenderTexture(960, 720, 24); var old = RenderTexture.active; var oldTarget = camera.targetTexture;
            var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
            try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = old; Object.DestroyImmediate(rt); Object.DestroyImmediate(image); }
        }
    }
}
