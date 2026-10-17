using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Itfaiye.Editor
{
    public static class PistolAssetAudit
    {
        public const string Pack = "Assets/Animation/Pistol_Handgun Locomotion Pack";
        [MenuItem("Tools/Itfaiye/Audit Pistol Assets %#F8")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory("Library/ItfaiyePistol");
            var report = new StringBuilder();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_01m_01.prefab");
            var model = UnityEngine.Object.Instantiate(source);
            model.hideFlags = HideFlags.HideAndDontSave;
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = model.GetComponent<Animator>();
            animator.applyRootMotion = false;
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (var path in Directory.GetFiles(Pack, "pistol*.fbx").OrderBy(p => p))
                {
                    var clip = AssetDatabase.LoadAllAssetsAtPath(path.Replace('\\', '/')).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
                    report.AppendLine($"{Path.GetFileName(path)} | {clip.length:F3}s | velocity {clip.averageSpeed:F3} | turn {clip.averageAngularSpeed:F3} | human={clip.humanMotion}");
                    foreach (float t in new[] { 0f, .5f, .95f })
                    {
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(model, clip, clip.length * t);
                        AnimationMode.EndSampling();
                        report.Append($"  t={t} root={model.transform.position:F3} ");
                        foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal })
                        {
                            var b = animator.GetBoneTransform(bone);
                            if (b != null) report.Append($"{bone}={model.transform.InverseTransformPoint(b.position):F3} rot={b.rotation.eulerAngles:F1}; ");
                        }
                        report.AppendLine();
                    }
                }
            }
            finally { AnimationMode.StopAnimationMode(); UnityEngine.Object.DestroyImmediate(model); }
            var gun = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/A3500X/Models/A3500X black.fbx"));
            try
            {
                report.AppendLine("GUN TRANSFORMS");
                foreach (var t in gun.GetComponentsInChildren<Transform>()) report.AppendLine($"{t.name}: localPos={t.localPosition:F4} rot={t.localEulerAngles:F2} scale={t.localScale:F4}");
                foreach (var r in gun.GetComponentsInChildren<Renderer>()) report.AppendLine($"{r.name}: bounds={r.bounds} materials={string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name : "null"))}");
                foreach (var m in gun.GetComponentsInChildren<MeshFilter>()) report.AppendLine($"{m.name}: meshBounds={m.sharedMesh.bounds}, vertices={m.sharedMesh.vertexCount}");
            }
            finally { UnityEngine.Object.DestroyImmediate(gun); }
            File.WriteAllText("Library/ItfaiyePistol/audit.txt", report.ToString());
            Debug.Log("Pistol asset audit saved: Library/ItfaiyePistol/audit.txt");
        }
    }
}
