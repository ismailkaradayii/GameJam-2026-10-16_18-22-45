using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Itfaiye.Editor
{
    public static class ItfaiyePlayerSetup
    {
        const string ScenePath = "Assets/Scenes/itfaiye.unity";
        const string SourcePrefab = "Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_01m_01.prefab";
        const string ControllerPath = "Assets/Itfaiye/Animation/ItfaiyeLocomotion.controller";
        const string PrefabPath = "Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab";

        [MenuItem("Tools/Itfaiye/Set Up First Person Player")]
        public static void SetUp()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open the itfaiye scene in Edit Mode before setting up the player.");
            if (scene.GetRootGameObjects().Any(o => o.GetComponent<ItfaiyeFirstPersonController>() != null))
                throw new InvalidOperationException("This scene already contains an Itfaiye player. Edit the existing instance instead of duplicating it.");
            EnsureFolder("Assets/Itfaiye/Animation");
            EnsureFolder("Assets/Itfaiye/Prefabs");
            var idle = ConfigureClip("Assets/Animation/ıdle.fbx", "Idle");
            var walk = ConfigureClip("Assets/Animation/walk.fbx", "Walk");
            var run = ConfigureClip("Assets/Animation/run.fbx", "Run");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;
            var tree = new BlendTree { name = "Idle Walk Run", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.87f);
            tree.AddChild(run, 4.57f);
            var state = controller.layers[0].stateMachine.AddState("Locomotion");
            state.motion = tree;
            state.iKOnFeet = true;
            controller.layers[0].stateMachine.defaultState = state;

            var ground = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Collider>())
                .Where(c => !c.isTrigger && c.enabled).OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).FirstOrDefault();
            if (ground == null) throw new InvalidOperationException("The scene needs a walkable ground collider.");
            var spawn = new Vector3(ground.bounds.center.x, ground.bounds.max.y + .03f, ground.bounds.center.z);
            var root = new GameObject("Itfaiye First Person Player");
            Undo.RegisterCreatedObjectUndo(root, "Add first person player");
            root.transform.position = spawn;
            var motor = root.AddComponent<CharacterController>();
            motor.height = 1.86f;
            motor.radius = .28f;
            motor.center = new Vector3(0f, .93f, 0f);
            motor.stepOffset = .28f;
            motor.slopeLimit = 48f;
            motor.skinWidth = .025f;
            motor.minMoveDistance = 0f;
            var player = root.AddComponent<ItfaiyeFirstPersonController>();

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            body.name = "npc_csl_00_character_01m_01";
            body.transform.localPosition = new Vector3(0f, 0f, -.25f);
            body.transform.localRotation = Quaternion.identity;
            player.bodyAnimator = ConfigureAnimator(body, controller);
            ConfigureRenderers(body, false);

            var cameraObject = new GameObject("First Person Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.70f, .20f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = .025f;
            camera.farClipPlane = 1000f;
            camera.fieldOfView = 80f;
            cameraObject.AddComponent<AudioListener>();
            player.playerCamera = camera;
            var handsLayer = EnsureLayer("FirstPersonHands");
            camera.cullingMask &= ~(1 << handsLayer);
            var baseData = camera.GetUniversalAdditionalCameraData();
            baseData.renderPostProcessing = true;

            var hands = (GameObject)PrefabUtility.InstantiatePrefab(source, cameraObject.transform);
            hands.name = "First Person Hands";
            hands.transform.localPosition = new Vector3(0f, -1.60f, -.10f);
            hands.transform.localRotation = Quaternion.identity;
            player.handsAnimator = ConfigureAnimator(hands, controller);
            ConfigureRenderers(hands, true);
            foreach (var child in hands.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = handsLayer;
            hands.AddComponent<ItfaiyeFirstPersonHands>().view = camera.transform;

            var overlayObject = new GameObject("Hands Camera");
            overlayObject.transform.SetParent(cameraObject.transform, false);
            var overlay = overlayObject.AddComponent<Camera>();
            overlay.nearClipPlane = .02f;
            overlay.farClipPlane = 5f;
            overlay.fieldOfView = 80f;
            overlay.cullingMask = 1 << handsLayer;
            overlay.useOcclusionCulling = false;
            var overlayData = overlay.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
            overlayData.renderShadows = false;
            overlayData.renderPostProcessing = false;
            baseData.cameraStack.Add(overlay);
            player.handsCamera = overlay;

            foreach (var oldCamera in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>()).Where(c => !c.transform.IsChildOf(root.transform)))
            {
                Undo.RecordObject(oldCamera.gameObject, "Disable previous camera");
                oldCamera.gameObject.SetActive(false);
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
        }

        static Animator ConfigureAnimator(GameObject model, RuntimeAnimatorController controller)
        {
            var animator = model.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("The character must have a valid Humanoid avatar.");
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        [MenuItem("Tools/Itfaiye/Update First Person Framing")]
        public static void UpdateFraming()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before editing the prefab.");
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ApplyFraming(prefab.GetComponent<ItfaiyeFirstPersonController>());
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return;
            var player = scene.GetRootGameObjects().Select(o => o.GetComponent<ItfaiyeFirstPersonController>()).FirstOrDefault(p => p != null);
            if (player == null) return;
            ApplyFraming(player);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void ApplyFraming(ItfaiyeFirstPersonController player)
        {
            player.maximumPitch = 80f;
            player.bodyAnimator.transform.localPosition = new Vector3(0f, 0f, -.25f);
            player.playerCamera.transform.localPosition = new Vector3(0f, 1.70f, .20f);
            var hands = player.handsAnimator;
            hands.transform.localPosition = new Vector3(0f, -1.60f, -.10f);
            var pose = hands.GetComponent<ItfaiyeFirstPersonHands>();
            pose.presentationWeight = 1f;
            pose.leftRestPosition = new Vector3(-.28f, -.26f, .43f);
            pose.rightRestPosition = new Vector3(.28f, -.26f, .43f);
            EditorUtility.SetDirty(hands.transform);
            EditorUtility.SetDirty(pose);
            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(player.bodyAnimator.transform);
            EditorUtility.SetDirty(player.playerCamera.transform);
        }

        static void ConfigureRenderers(GameObject model, bool handsOnly)
        {
            foreach (var group in model.GetComponentsInChildren<LODGroup>(true)) group.enabled = false;
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool bestLod = renderer.name.EndsWith("lod0", StringComparison.OrdinalIgnoreCase);
                bool arms = renderer.name.Contains("_arms");
                bool lowerBody = renderer.name.Contains("_pants_") || renderer.name.Contains("_shoe_");
                renderer.enabled = bestLod && (!handsOnly || arms);
                renderer.shadowCastingMode = handsOnly ? ShadowCastingMode.Off
                    : lowerBody ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
                renderer.receiveShadows = !handsOnly;
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = new Bounds(Vector3.up, new Vector3(4f, 4f, 4f));
            }
        }

        static AnimationClip ConfigureClip(string path, string clipName)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Animation is missing: " + path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            if (clips.Length == 0) throw new InvalidOperationException("No animation take found: " + path);
            foreach (var clip in clips)
            {
                clip.name = clipName;
                clip.loopTime = true;
                clip.loopPose = true;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.heightFromFeet = true;
                clip.keepOriginalPositionY = false;
                // Extract forward travel; the CharacterController owns displacement.
                clip.lockRootPositionXZ = false;
                clip.keepOriginalPositionXZ = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        }

        static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = settings.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) continue;
                layers.GetArrayElementAtIndex(i).stringValue = name;
                settings.ApplyModifiedProperties();
                return i;
            }
            throw new InvalidOperationException("No free user layer for the first person hands.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
