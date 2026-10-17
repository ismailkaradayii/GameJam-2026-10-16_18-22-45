using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Itfaiye.Editor
{
    public static class ItfaiyePistolSetup
    {
        public const string ControllerPath = "Assets/Itfaiye/Animation/ItfaiyePistol.controller";
        const string PlayerPath = "Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab";
        const string MaterialsPath = "Assets/Itfaiye/Materials";
        static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();

        [MenuItem("Tools/Itfaiye/Set Up Pistol %#F7")]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before setting up the pistol.");
            if (SceneManager.GetActiveScene().path != "Assets/Scenes/itfaiye.unity") throw new InvalidOperationException("Open itfaiye first.");
            Clips.Clear();
            foreach (var path in Directory.GetFiles(PistolAssetAudit.Pack, "pistol*.fbx"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                Clips[name] = ImportClip(path.Replace('\\', '/'), name);
            }
            if (Clips.Count != 20) throw new InvalidOperationException("Expected exactly 20 pistol clips.");
            var controller = BuildController();
            if (!AssetDatabase.IsValidFolder(MaterialsPath)) AssetDatabase.CreateFolder("Assets/Itfaiye", "Materials");
            var black = MakeMaterial("A3500X black");
            var magazineMaterial = MakeMaterial("magazine");
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var player = root.GetComponent<ItfaiyeFirstPersonController>();
                var pistol = root.GetComponent<ItfaiyePistol>();
                if (pistol == null) pistol = root.AddComponent<ItfaiyePistol>();
                if (pistol.weapon != null) UnityEngine.Object.DestroyImmediate(pistol.weapon);
                pistol.player = player;
                player.pistol = pistol;
                player.bodyAnimator.runtimeAnimatorController = controller;
                player.handsAnimator.runtimeAnimatorController = controller;
                var hands = player.handsAnimator.GetComponent<ItfaiyeFirstPersonHands>();
                hands.pistol = pistol;
                var bone = player.handsAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                var gun = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/A3500X/Models/A3500X black.fbx"));
                gun.name = "A3500X Pistol";
                gun.transform.SetParent(bone, false);
                gun.transform.localScale = Vector3.one * .055f;
                int layer = LayerMask.NameToLayer("FirstPersonHands");
                foreach (var child in gun.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
                var magazine = new GameObject("Magazine Assembly").transform;
                magazine.SetParent(gun.transform, false);
                magazine.gameObject.layer = layer;
                foreach (var r in gun.GetComponentsInChildren<Renderer>(true))
                {
                    bool isMagazine = r.sharedMaterials.Any(m => m != null && m.name.Contains("magazine"));
                    r.sharedMaterials = r.sharedMaterials.Select(m => isMagazine ? magazineMaterial : black).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    if (isMagazine) r.transform.SetParent(magazine, true);
                }
                foreach (var col in gun.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(col);
                pistol.weapon = gun;
                ConfigureGrip(player);
                pistol.magazine = magazine;
                pistol.slide = gun.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "slid_low");
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(gun.transform, false);
                muzzle.localPosition = new Vector3(0f, 1.24f, 2.2f);
                muzzle.gameObject.layer = layer;
                pistol.muzzle = muzzle;
                var flash = muzzle.gameObject.AddComponent<Light>();
                flash.type = LightType.Point;
                flash.color = new Color(1f, .64f, .25f);
                flash.intensity = 2.5f;
                flash.range = 1.1f;
                flash.shadows = LightShadows.None;
                flash.enabled = false;
                pistol.muzzleLight = flash;
                ConfigureMechanics(player);
                gun.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("Library/ItfaiyePistol");
            File.WriteAllText("Library/ItfaiyePistol/setup.txt", "SUCCESS: 20 Humanoid clips, directional locomotion, two jumps, three kneeling states, A3500X grip rig, magazine/slide reload, muzzle particles, URP materials.\n" + string.Join("\n", Clips.Select(p => p.Key + " -> " + p.Value.name)));
            Debug.Log("Pistol ready. Play, click Game, then press 1. WASD / Shift / C / Space / Mouse / R / 0.");
        }

        [MenuItem("Tools/Itfaiye/Update Pistol Grip %#F5")]
        public static void UpdateGrip()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var player = root.GetComponent<ItfaiyeFirstPersonController>();
                ConfigureGrip(player);
                ConfigureMechanics(player);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            foreach (var entry in controller.layers[0].stateMachine.states)
                if (entry.state.name.EndsWith("Jump"))
                    foreach (var transition in entry.state.transitions) transition.hasExitTime = false;
            AssetDatabase.SaveAssets();
            var scene = SceneManager.GetActiveScene();
            var scenePlayer = UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>();
            if (scenePlayer != null && scenePlayer.gameObject.scene == scene)
            {
                var hands = scenePlayer.handsAnimator.GetComponent<ItfaiyeFirstPersonHands>();
                Undo.RecordObject(hands, "Adjust pistol hip position");
                hands.pistolHipPosition = ItfaiyeFirstPersonHands.DefaultPistolHipPosition;
                hands.pistolHipEuler = Vector3.zero;
                PrefabUtility.RecordPrefabInstancePropertyModifications(hands);
                EditorUtility.SetDirty(hands);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void ConfigureGrip(ItfaiyeFirstPersonController player)
        {
            var hands = player.handsAnimator.GetComponent<ItfaiyeFirstPersonHands>();
            hands.pistolHipPosition = ItfaiyeFirstPersonHands.DefaultPistolHipPosition;
            hands.pistolHipEuler = Vector3.zero;
            hands.pistolAimDistance = .50f;
            var gun = player.pistol.weapon.transform;
            gun.SetParent(player.playerCamera.transform, false);
            gun.localPosition = hands.pistolHipPosition;
            gun.localRotation = Quaternion.identity;
            gun.localScale = Vector3.one * .055f;
        }

        static void ConfigureMechanics(ItfaiyeFirstPersonController player)
        {
            var pistol = player.pistol;
            var gun = pistol.weapon.transform;
            int layer = LayerMask.NameToLayer("FirstPersonHands");
            Transform Group(string name, params string[] parts)
            {
                var group = gun.Find(name);
                if (group == null) { group = new GameObject(name).transform; group.SetParent(gun, false); }
                group.gameObject.layer = layer;
                foreach (string partName in parts)
                {
                    var part = gun.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == partName);
                    if (part != null && part.parent != group) part.SetParent(group, true);
                }
                return group;
            }
            pistol.slide = Group("Slide Assembly", "slid_low", "front scoop_low", "behind scoop_low", "led_low", "nail_low", "bottun 1_low", "needle_low", "hh_low");
            pistol.trigger = Group("Trigger Assembly", "trigger_low");
            void SetPivot(Transform root, Vector3 point)
            {
                var children = root.Cast<Transform>().ToArray();
                foreach (var child in children) child.SetParent(gun, true);
                root.localPosition = point;
                root.localRotation = Quaternion.identity;
                foreach (var child in children) child.SetParent(root, true);
            }
            SetPivot(pistol.magazine, new Vector3(0f, -1.40f, -1.49f));
            SetPivot(pistol.trigger, new Vector3(0f, .80f, -.21f));
            pistol.reloadDuration = 3.2f;
            pistol.muzzle.localPosition = new Vector3(0f, 1.24f, 2.19f);
            pistol.muzzle.localRotation = Quaternion.identity;
            pistol.muzzleLight.range = .65f;
            pistol.muzzleLight.intensity = .4f;
            pistol.muzzleFlash = MakeParticles(pistol.muzzle, "Muzzle Flash", false, layer, gun.localScale.x);
            pistol.muzzleSmoke = MakeParticles(pistol.muzzle, "Muzzle Smoke", true, layer, gun.localScale.x);
        }

        static ParticleSystem MakeParticles(Transform muzzle, string name, bool smoke, int layer, float scale)
        {
            var child = muzzle.Find(name);
            if (child == null) { child = new GameObject(name).transform; child.SetParent(muzzle, false); }
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one / scale;
            child.gameObject.layer = layer;
            var system = child.GetComponent<ParticleSystem>();
            if (system == null) system = child.gameObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false; main.loop = false; main.duration = smoke ? .55f : .08f;
            main.simulationSpace = smoke ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(smoke ? .25f : .045f, smoke ? .42f : .065f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(smoke ? .12f : .15f, smoke ? .25f : .45f);
            main.startSize = new ParticleSystem.MinMaxCurve(smoke ? .025f : .10f, smoke ? .045f : .14f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = smoke ? new Color(.52f, .56f, .61f, .24f) : new Color(1f, .38f, .07f, 1f);
            main.maxParticles = 32;
            var emission = system.emission; emission.enabled = false;
            var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 9f; shape.radius = .002f;
            var color = system.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.6f, .35f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var size = system.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, smoke ? .5f : 1f, 1f, smoke ? 2.5f : .4f));
            var velocity = system.velocityOverLifetime; velocity.enabled = smoke; velocity.space = ParticleSystemSimulationSpace.World; velocity.y = .06f;
            string path = MaterialsPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Itfaiye/Pistol Particle")); AssetDatabase.CreateAsset(material, path); }
            material.SetFloat("_Glow", smoke ? 0f : 1f);
            material.SetFloat("_DstBlend", (float)(smoke ? BlendMode.OneMinusSrcAlpha : BlendMode.One));
            EditorUtility.SetDirty(material);
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        static AnimationClip ImportClip(string path, string name)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            bool jump = name.Contains("jump");
            bool transition = name.Contains(" to ");
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = name;
                clip.loopTime = !jump && !transition;
                clip.loopPose = clip.loopTime;
                // Extract authored travel AND turning. The motor alone owns displacement/yaw.
                clip.lockRootRotation = false;
                clip.keepOriginalOrientation = true;
                clip.lockRootPositionXZ = false;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootHeightY = !jump;
                clip.keepOriginalPositionY = true;
                clip.heightFromFeet = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        }

        static AnimatorController BuildController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            else
            {
                controller.layers = Array.Empty<AnimatorControllerLayer>();
                controller.parameters = Array.Empty<AnimatorControllerParameter>();
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                    if (asset != controller) UnityEngine.Object.DestroyImmediate(asset, true);
                controller.AddLayer("Base Layer");
            }
            foreach (string name in new[] { "Speed", "MoveX", "MoveY" }) controller.AddParameter(name, AnimatorControllerParameterType.Float);
            foreach (string name in new[] { "Equipped", "Kneeling", "Grounded", "MovingJump" }) controller.AddParameter(name, AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            var layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;
            var sm = controller.layers[0].stateMachine;
            var unarmedTree = new BlendTree { name = "Unarmed Idle Walk Run", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(unarmedTree, controller);
            unarmedTree.AddChild(LoadClip("Assets/Animation/ıdle.fbx"), 0f);
            unarmedTree.AddChild(LoadClip("Assets/Animation/walk.fbx"), 1.87f);
            unarmedTree.AddChild(LoadClip("Assets/Animation/run.fbx"), 4.57f);
            var unarmed = sm.AddState("Unarmed", new Vector3(50, 100));
            unarmed.motion = unarmedTree;
            sm.defaultState = unarmed;
            var tree = new BlendTree { name = "Pistol 8 Direction Walk Run", blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveY", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller);
            var children = new List<ChildMotion> { new ChildMotion { motion = Clips["pistol idle"], position = Vector2.zero, timeScale = 1f } };
            Add(children, "pistol walk", 0, 1, 1.87f);
            Add(children, "pistol walk backward", 0, -1, 1.87f);
            Add(children, "pistol strafe", -1, 0, 1.87f);
            Add(children, "pistol strafe (2)", 1, 0, 1.87f);
            Add(children, "pistol walk arc (2)", -1, 1, 1.87f);
            Add(children, "pistol walk arc", 1, 1, 1.87f);
            Add(children, "pistol walk backward arc", -1, -1, 1.87f);
            Add(children, "pistol walk backward arc (2)", 1, -1, 1.87f);
            Add(children, "pistol run", 0, 1, 4.57f);
            Add(children, "pistol run backward", 0, -1, 4.57f);
            Add(children, "pistol strafe", -1, 0, 4.57f);
            Add(children, "pistol strafe (2)", 1, 0, 4.57f);
            Add(children, "pistol run arc (2)", -1, 1, 4.57f);
            Add(children, "pistol run arc", 1, 1, 4.57f);
            Add(children, "pistol run backward arc", -1, -1, 4.57f);
            Add(children, "pistol run backward arc (2)", 1, -1, 4.57f);
            tree.children = children.ToArray();
            var moving = sm.AddState("Pistol Locomotion", new Vector3(340, 100));
            moving.motion = tree;
            var equip = Transition(unarmed, moving, .22f); equip.AddCondition(AnimatorConditionMode.If, 0, "Equipped");
            var holster = sm.AddAnyStateTransition(unarmed); Configure(holster, .22f); holster.canTransitionToSelf = false;
            holster.AddCondition(AnimatorConditionMode.IfNot, 0, "Equipped");
            var down = State(sm, "Kneel Down", "pistol stand to kneel", 340, 270);
            var kneel = State(sm, "Kneeling Idle", "pistol kneeling idle", 610, 270);
            var up = State(sm, "Stand Up", "pistol kneel to stand", 610, 100);
            Transition(moving, down, .12f).AddCondition(AnimatorConditionMode.If, 0, "Kneeling");
            var settled = Transition(down, kneel, .16f, .88f); settled.AddCondition(AnimatorConditionMode.If, 0, "Kneeling");
            var cancel = Transition(down, up, .16f, .25f); cancel.AddCondition(AnimatorConditionMode.IfNot, 0, "Kneeling");
            Transition(kneel, up, .16f).AddCondition(AnimatorConditionMode.IfNot, 0, "Kneeling");
            Transition(up, moving, .16f, .9f);
            var idleJump = State(sm, "Standing Jump", "pistol jump", 340, -90);
            var movingJump = State(sm, "Moving Jump", "pistol jump (2)", 610, -90);
            idleJump.speed = 2.7f;
            movingJump.speed = 1.1f;
            foreach (var state in new[] { idleJump, movingJump })
            {
                var start = sm.AddAnyStateTransition(state); Configure(start, .08f); start.canTransitionToSelf = false;
                start.AddCondition(AnimatorConditionMode.If, 0, "Equipped");
                start.AddCondition(AnimatorConditionMode.If, 0, "Jump");
                start.AddCondition(state == movingJump ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "MovingJump");
                var land = Transition(state, moving, .15f); land.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static void Add(List<ChildMotion> children, string name, float x, float y, float speed)
        {
            var clip = Clips[name];
            children.Add(new ChildMotion { motion = clip, position = new Vector2(x, y).normalized * speed,
                timeScale = Mathf.Clamp(speed / Mathf.Max(.5f, new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude), .5f, 2f) });
        }
        static AnimationClip LoadClip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        static AnimatorState State(AnimatorStateMachine machine, string name, string clip, float x, float y)
        { var state = machine.AddState(name, new Vector3(x, y)); state.motion = Clips[clip]; return state; }
        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration, float exit = -1f)
        { var t = from.AddTransition(to); Configure(t, duration); t.hasExitTime = exit >= 0f; t.exitTime = exit; return t; }
        static void Configure(AnimatorStateTransition transition, float duration)
        { transition.duration = duration; transition.hasFixedDuration = true; transition.hasExitTime = false; transition.interruptionSource = TransitionInterruptionSource.None; }

        static Material MakeMaterial(string name)
        {
            string path = MaterialsPath + "/" + name + " URP.mat";
            var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/A3500X/Materials/" + name + ".mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetTexture("_BaseMap", original.GetTexture("_MainTex"));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", original.GetTexture("_BumpMap"));
            material.SetTexture("_MetallicGlossMap", original.GetTexture("_MetallicGlossMap"));
            material.SetTexture("_OcclusionMap", original.GetTexture("_OcclusionMap"));
            material.SetFloat("_Metallic", .8f);
            material.SetFloat("_Smoothness", .38f);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
