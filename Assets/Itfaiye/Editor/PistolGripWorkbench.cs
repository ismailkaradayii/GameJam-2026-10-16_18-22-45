using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Itfaiye.Editor
{
    public static class PistolGripWorkbench
    {
        [MenuItem("Tools/Itfaiye/Silah Poz Ayarlari")]
        public static void SelectPoseSettings()
        {
            var player = UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>();
            if (player == null) throw new InvalidOperationException("Open the itfaiye scene first.");
            Selection.activeGameObject = player.handsAnimator.gameObject;
            EditorGUIUtility.PingObject(Selection.activeGameObject);
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
        }
        [Serializable] public class Joint { public string name; public string parent; public Vector3 position; public Quaternion rotation; public Vector3 localPosition; public Quaternion localRotation; }
        [Serializable] public class MuscleSample { public string name; public float value; public Joint[] joints; }
        [Serializable] public class Anatomy { public Joint[] joints; public MuscleSample[] samples; }

        [MenuItem("Tools/Itfaiye/Inspect Grip Anatomy %#F4")]
        public static void Inspect()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_01m_01.prefab");
            var model = UnityEngine.Object.Instantiate(source);
            model.hideFlags = HideFlags.HideAndDontSave;
            var animator = model.GetComponent<Animator>();
            var clip = AssetDatabase.LoadAllAssetsAtPath(PistolAssetAudit.Pack + "/pistol idle.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(model, clip, 0f); AnimationMode.EndSampling();
                using (var handler = new HumanPoseHandler(animator.avatar, model.transform))
                {
                    var pose = new HumanPose(); handler.GetHumanPose(ref pose);
                    var muscles = Enumerable.Range(0, HumanTrait.MuscleCount).Where(i => HumanTrait.MuscleName[i].Contains("Stretched") || HumanTrait.MuscleName[i].Contains("Spread")).ToArray();
                    foreach (int i in muscles) pose.muscles[i] = 0f;
                    handler.SetHumanPose(ref pose);
                    var anatomy = new Anatomy { joints = Read(animator) };
                    var samples = new List<MuscleSample>();
                    foreach (int i in muscles)
                    {
                        foreach (float v in new[] { -1f, -.5f, .5f, 1f })
                        {
                            pose.muscles[i] = v; handler.SetHumanPose(ref pose);
                            samples.Add(new MuscleSample { name = HumanTrait.MuscleName[i], value = v, joints = Read(animator) });
                        }
                        pose.muscles[i] = 0f;
                    }
                    anatomy.samples = samples.ToArray();
                    Directory.CreateDirectory("Library/ItfaiyePistol");
                    File.WriteAllText("Library/ItfaiyePistol/anatomy.json", JsonUtility.ToJson(anatomy, true));
                }
            }
            finally { AnimationMode.StopAnimationMode(); UnityEngine.Object.DestroyImmediate(model); }
            Debug.Log("Grip anatomy exported.");
        }

        static Joint[] Read(Animator animator)
        {
            var result = new List<Joint>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = (HumanBodyBones)i;
                if (i < (int)HumanBodyBones.LeftThumbProximal && bone != HumanBodyBones.LeftHand && bone != HumanBodyBones.RightHand && bone != HumanBodyBones.LeftLowerArm && bone != HumanBodyBones.RightLowerArm && bone != HumanBodyBones.LeftUpperArm && bone != HumanBodyBones.RightUpperArm) continue;
                var t = animator.GetBoneTransform(bone); if (t == null) continue;
                result.Add(new Joint { name = bone.ToString(), parent = t.parent.name, position = t.position, rotation = t.rotation, localPosition = t.localPosition, localRotation = t.localRotation });
                if (bone.ToString().EndsWith("Distal")) foreach (Transform child in t)
                    result.Add(new Joint { name = bone + "Tip", parent = t.name, position = child.position, rotation = child.rotation, localPosition = child.localPosition, localRotation = child.localRotation });
            }
            return result.ToArray();
        }

        [MenuItem("Tools/Itfaiye/Capture Current Grip %#F3")]
        public static void Capture()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Start Play Mode first.");
            var player = UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>();
            player.pistol.Equip();
            double ready = EditorApplication.timeSinceStartup + 1;
            void Wait()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Wait; return; }
                if (EditorApplication.timeSinceStartup < ready) return;
                EditorApplication.update -= Wait;
                CaptureViews(player);
            }
            EditorApplication.update += Wait;
        }

        public static void CaptureViews(ItfaiyeFirstPersonController player, string folder = "Library/ItfaiyePistol/Grip")
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder + "/posed.json", JsonUtility.ToJson(new Anatomy { joints = Read(player.handsAnimator) }, true));
            var weapon = player.pistol.weapon.transform;
            var cameraObject = new GameObject("Grip Inspection Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .21f, .25f);
            camera.cullingMask = 1 << LayerMask.NameToLayer("FirstPersonHands");
            camera.nearClipPlane = .01f; camera.farClipPlane = 3f;
            camera.orthographic = true; camera.orthographicSize = .20f;
            var rt = new RenderTexture(900, 900, 24);
            var texture = new Texture2D(900, 900, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            var lightObject = new GameObject("Grip Inspection Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point; light.range = 3f; light.intensity = .15f; light.shadows = LightShadows.None;
            try
            {
                var target = weapon.position + weapon.rotation * new Vector3(0f, -.015f, -.065f);
                var offsets = new[] { new Vector3(.5f, .12f, -.32f), new Vector3(-.5f, .12f, -.20f), new Vector3(.5f, .03f, .05f), new Vector3(-.5f, .03f, .05f), new Vector3(0f, .50f, -.01f) };
                for (int i = 0; i < offsets.Length; i++)
                {
                    camera.transform.position = target + weapon.rotation * offsets[i];
                    camera.transform.LookAt(target, weapon.up);
                    light.transform.position = camera.transform.position + weapon.up * .2f;
                    camera.targetTexture = rt; camera.Render();
                    RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); texture.Apply();
                    File.WriteAllBytes(folder + "/view-" + i + ".png", texture.EncodeToPNG());
                }
                camera.orthographic = false;
                camera.transform.SetPositionAndRotation(player.playerCamera.transform.position, player.playerCamera.transform.rotation);
                camera.fieldOfView = player.playerCamera.fieldOfView;
                camera.aspect = 16f / 9f;
                camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); texture.Apply();
                File.WriteAllBytes(folder + "/first-person.png", texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old; camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(lightObject);
                UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt);
            }
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(folder + "/game.png"));
            Debug.Log("Grip views captured.");
        }
    }
}
