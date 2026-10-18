using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Itfaiye.Editor
{
    // Samples preview copies only; the open game and its Play Mode checks stay untouched.
    [InitializeOnLoad]
    public static class ItfaiyeJumpFootChecks
    {
        const string Folder = "Logs/JumpFoot";
        static double next;
        [Serializable] public class Pose
        {
            public string state;
            public int samples;
            public float leftBefore = 1, leftAfter = 1, rightAfter = 1, maxAnkleShift;
            public bool supported;
        }
        [Serializable] public class Report
        {
            public string status;
            public List<Pose> poses = new List<Pose>();
            public List<string> errors = new List<string>();
        }
        static ItfaiyeJumpFootChecks() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .5;
            const string request = "Logs/JumpFoot.command";
            if (!File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim(); File.Delete(request);
            if (command == "check") Run();
        }

        [MenuItem("GameJam/Final Loop/Check player jump feet")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Directory.CreateDirectory(Folder);
            var report = new Report(); var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab");
                var source = prefab.GetComponent<ItfaiyeFirstPersonController>();
                var model = Object.Instantiate(source.bodyAnimator.gameObject);
                SceneManager.MoveGameObjectToScene(model, preview);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); model.transform.localScale = Vector3.one;
                var animator = model.GetComponent<Animator>(); animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var legs = model.GetComponent<ItfaiyeLegStabilizer>();
                if (!legs) legs = model.AddComponent<ItfaiyeLegStabilizer>();
                legs.Initialize(); animator.Rebind();
                animator.SetBool("Equipped", true); animator.SetBool("Grounded", false);
                var supports = typeof(ItfaiyeLegStabilizer).GetMethod("SupportsPose", BindingFlags.Static | BindingFlags.NonPublic);
                foreach (string state in new[] { "Standing Jump", "Moving Jump" })
                {
                    var pose = new Pose { state = state, supported = true };
                    for (int i = 0; i <= 96; i++)
                    {
                        animator.Play(state, 0, i / 96f); animator.Update(0);
                        var current = animator.GetCurrentAnimatorStateInfo(0);
                        pose.supported &= current.IsName(state) && (bool)supports.Invoke(null, new object[] { current });
                        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                        Vector3 beforeLeft = leftFoot.position, beforeRight = rightFoot.position;
                        pose.leftBefore = Mathf.Min(pose.leftBefore, Forward(animator, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes));
                        legs.Stabilize(1f, 1f / 120f);
                        pose.leftAfter = Mathf.Min(pose.leftAfter, Forward(animator, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes));
                        pose.rightAfter = Mathf.Min(pose.rightAfter, Forward(animator, HumanBodyBones.RightFoot, HumanBodyBones.RightToes));
                        pose.maxAnkleShift = Mathf.Max(pose.maxAnkleShift, Vector3.Distance(beforeLeft, leftFoot.position), Vector3.Distance(beforeRight, rightFoot.position));
                        pose.samples++;
                    }
                    report.poses.Add(pose);
                }
                foreach (string jump in new[] { "Standing Jump", "Moving Jump" })
                {
                    animator.Play("Pistol Locomotion", 0, 0); animator.Update(0);
                    animator.CrossFadeInFixedTime(jump, .08f); animator.Update(.02f);
                    if (!animator.IsInTransition(0) ||
                        !(bool)supports.Invoke(null, new object[] { animator.GetCurrentAnimatorStateInfo(0) }) ||
                        !(bool)supports.Invoke(null, new object[] { animator.GetNextAnimatorStateInfo(0) }))
                        report.errors.Add(jump + " take-off crossfade loses leg correction.");
                    animator.Play(jump, 0, .6f); animator.Update(0);
                    animator.CrossFadeInFixedTime("Pistol Locomotion", .15f); animator.Update(.02f);
                    if (!animator.IsInTransition(0) ||
                        !(bool)supports.Invoke(null, new object[] { animator.GetCurrentAnimatorStateInfo(0) }) ||
                        !(bool)supports.Invoke(null, new object[] { animator.GetNextAnimatorStateInfo(0) }))
                        report.errors.Add(jump + " landing crossfade loses leg correction.");
                }
                animator.Play("Kneel Down", 0, .4f); animator.Update(0);
                if ((bool)supports.Invoke(null, new object[] { animator.GetCurrentAnimatorStateInfo(0) }))
                    report.errors.Add("Kneeling posture must keep its authored leg pose.");
                foreach (var pose in report.poses)
                    if (!pose.supported || pose.leftAfter < .25f || pose.rightAfter < .25f || pose.maxAnkleShift > .02f)
                        report.errors.Add(pose.state + " has a reversed boot or changes the authored ankle trajectory.");
            }
            catch (Exception ex) { report.errors.Add(ex.ToString()); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            report.status = report.errors.Count == 0 && report.poses.Count == 2 ? "SUCCESS" : "FAILED";
            File.WriteAllText(Folder + "/checks.json", JsonUtility.ToJson(report, true));
        }

        static float Forward(Animator animator, HumanBodyBones foot, HumanBodyBones toe) =>
            animator.transform.InverseTransformDirection(animator.GetBoneTransform(toe).position - animator.GetBoneTransform(foot).position).normalized.z;
    }
}
