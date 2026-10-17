#if UNITY_EDITOR
using Ashfall.MainMenu;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using Itfaiye;

namespace GameJam.FireRescue.Editor
{
    /// <summary>One-time editor wiring for the existing menu, truck prefab, and fire scenes.</summary>
    public static class AudioIntegrationSetup
    {
        private const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
        private const string VolcanoScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
        private const string FireRescueScenePath = "Assets/Scenes/İtfaiye aracı.unity";
        private const string TruckPrefabPath = "Assets/GameJam/FireRescue/Prefabs/Fire Truck.prefab";
        private const string ClickClipPath = "Assets/Art/sesdosya/click.mp3";
        private const string EngineClipPath = "Assets/Art/sesdosya/cardrive.wav";
        private const string FireClipPath = "Assets/Art/sesdosya/fire.wav";
        private const string WindClipPath = "Assets/Art/sesdosya/wind.wav";
        private const string FootstepClipPath = "Assets/Art/sesdosya/walksound.mp3";
        private const string LavaBubblingClipPath = "Assets/Art/sesdosya/Loops/LavaFlowLoop.wav";
        private const string RunningClipPath = "Assets/Art/sesdosya/Loops/BreathingRunningLoop.wav";
        private const string SpiritClipPath = "Assets/Art/sesdosya/alevruhu.wav";
        private const string PlayerPainClipPath = "Assets/Art/sesdosya/acı cekmek.mp3";
        private const string CrowdPanicClipPath = "Assets/Art/sesdosya/crowdpanic.mp3";
        private const string TargetImpactClipPath = "Assets/Art/sesdosya/çarpmases.mp3";
        private const string WaterSprayClipPath = "Assets/Art/sesdosya/Fire Hose Spray Water.mp3";
        private const string PlayerPrefabPath = "Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab";
        private const string CompatibleVideoPath = "Assets/Art/BackGround/arkaplan_UnityBaseline.mp4";
        private const string BackgroundRenderTexturePath = "Assets/Art/BackGround/RT_ASHFALL_Background.renderTexture";

        [MenuItem("ASHFALL/Integrate Audio Systems")]
        public static void IntegrateAudioSystems()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play Mode before audio authoring.");
            AudioClip clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClickClipPath);
            AudioClip engineClip = AssetDatabase.LoadAssetAtPath<AudioClip>(EngineClipPath);
            AudioClip fireClip = AssetDatabase.LoadAssetAtPath<AudioClip>(FireClipPath);
            AudioClip windClip = AssetDatabase.LoadAssetAtPath<AudioClip>(WindClipPath);
            AudioClip explosionClip = FindExistingExplosionClip();
            AudioClip footstepClip = AssetDatabase.LoadAssetAtPath<AudioClip>(FootstepClipPath);
            AudioClip bubblingClip = AssetDatabase.LoadAssetAtPath<AudioClip>(LavaBubblingClipPath);
            AudioClip painClip = AssetDatabase.LoadAssetAtPath<AudioClip>(PlayerPainClipPath);
            AudioClip crowdClip = AssetDatabase.LoadAssetAtPath<AudioClip>(CrowdPanicClipPath);
            AudioClip impactClip = AssetDatabase.LoadAssetAtPath<AudioClip>(TargetImpactClipPath);
            AudioClip sprayClip = AssetDatabase.LoadAssetAtPath<AudioClip>(WaterSprayClipPath);
            if (clickClip == null || engineClip == null || fireClip == null)
            {
                Debug.LogError("ASHFALL audio integration stopped. Missing clip(s): " +
                    (clickClip == null ? ClickClipPath + " " : string.Empty) +
                    (engineClip == null ? EngineClipPath + " " : string.Empty) +
                    (fireClip == null ? FireClipPath : string.Empty));
                return;
            }

            ConfigureTruckPrefab(engineClip, sprayClip);
            ConfigurePlayerPrefab(painClip, impactClip);

            Scene originalScene = SceneManager.GetActiveScene();
            if (originalScene.IsValid() && originalScene.isDirty)
                EditorSceneManager.SaveScene(originalScene);

            OpenAndInstallScene(MainMenuPath, scene => ConfigureMenu(scene, clickClip));
            OpenAndInstallScene(VolcanoScenePath, scene =>
            {
                ConfigureFireAmbience(scene, fireClip);
                ConfigureGameplayAudio(scene, windClip, footstepClip, explosionClip, bubblingClip, painClip, crowdClip, impactClip, sprayClip);
            });
            OpenAndInstallScene(FireRescueScenePath, scene =>
            {
                ConfigureFireAmbience(scene, fireClip);
                ConfigureGameplayAudio(scene, windClip, footstepClip, null, bubblingClip, painClip, crowdClip, impactClip, sprayClip);
            });

            EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ASHFALL audio integration complete: menu UI sounds, speed-based truck loop, and shared fire ambience are wired.");
            if (windClip == null)
                Debug.LogWarning("ASHFALL: Wind audio was not assigned because " + WindClipPath + " is missing.");
            if (footstepClip == null)
                Debug.LogWarning("ASHFALL: Footsteps are ready for a clip, but no walksound file was found at " + FootstepClipPath + ". Add it and rerun ASHFALL/Integrate Audio Systems.");
            if (explosionClip == null)
                Debug.LogWarning("ASHFALL: No existing AudioClip named 'explode' was found in Assets. The post-explosion wind timeline is wired, but the explosion sound must be added and assigned on WindAudioAmbience > Wind Audio Pulse.");
            WarnMissing(LavaBubblingClipPath, bubblingClip);
            WarnMissing(PlayerPainClipPath, painClip);
            WarnMissing(CrowdPanicClipPath, crowdClip);
            WarnMissing(TargetImpactClipPath, impactClip);
            WarnMissing(WaterSprayClipPath, sprayClip);
        }

        [MenuItem("ASHFALL/Integrate Explosion Wind Timeline")]
        public static void IntegrateExplosionWindTimeline()
        {
            AudioClip windClip = AssetDatabase.LoadAssetAtPath<AudioClip>(WindClipPath);
            AudioClip explosionClip = FindExistingExplosionClip();
            Scene originalScene = SceneManager.GetActiveScene();
            if (originalScene.IsValid() && originalScene.isDirty)
                EditorSceneManager.SaveScene(originalScene);

            OpenAndInstallScene(VolcanoScenePath, scene => ConfigureWindAudio(scene, windClip, explosionClip));
            if (originalScene.IsValid() && !string.IsNullOrEmpty(originalScene.path))
                EditorSceneManager.OpenScene(originalScene.path, OpenSceneMode.Single);

            AssetDatabase.SaveAssets();
            if (explosionClip == null)
                Debug.LogWarning("ASHFALL: No existing AudioClip named 'explode' was found in Assets; the timeline remains wired with a missing clip warning.");
            else
                Debug.Log("ASHFALL: Explosion and wind timeline assigned from " + AssetDatabase.GetAssetPath(explosionClip));
        }

        private static AudioClip FindExistingExplosionClip()
        {
            foreach (string guid in AssetDatabase.FindAssets("explode t:AudioClip", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path).Equals("explode", System.StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return null;
        }

        private static void ConfigureTruckPrefab(AudioClip engineClip, AudioClip sprayClip)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(TruckPrefabPath);
            try
            {
                if (root.GetComponent<FireTruckDrive>() == null)
                    throw new System.InvalidOperationException("The Fire Truck prefab has no FireTruckDrive component.");

                AudioSource source = root.GetComponent<AudioSource>();
                if (source == null) source = root.AddComponent<AudioSource>();
                source.clip = engineClip;
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 4f;
                source.maxDistance = 60f;
                source.volume = 0f;

                FireTruckEngineAudio engine = root.GetComponent<FireTruckEngineAudio>();
                if (engine == null) engine = root.AddComponent<FireTruckEngineAudio>();
                SerializedObject serialized = new SerializedObject(engine);
                serialized.FindProperty("engineSource").objectReferenceValue = source;
                serialized.FindProperty("engineLoop").objectReferenceValue = engineClip;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                FireTruckWaterCannon cannon = root.GetComponentInChildren<FireTruckWaterCannon>(true);
                if (cannon && sprayClip)
                {
                    FireTruckSprayAudio spray = root.GetComponent<FireTruckSprayAudio>();
                    if (!spray) spray = root.AddComponent<FireTruckSprayAudio>();
                    Transform emitter = cannon.muzzle ? cannon.muzzle.Find("Water Spray Audio") : root.transform.Find("Water Spray Audio");
                    if (!emitter)
                    {
                        GameObject emitterObject = new GameObject("Water Spray Audio");
                        emitter = emitterObject.transform;
                        emitter.SetParent(cannon.muzzle ? cannon.muzzle : root.transform, false);
                    }
                    AudioSource spraySource = emitter.GetComponent<AudioSource>();
                    if (!spraySource) spraySource = emitter.gameObject.AddComponent<AudioSource>();
                    Configure3DSource(spraySource, true, 0f, 3f, 50f);
                    SerializedObject sprayData = new SerializedObject(spray);
                    sprayData.FindProperty("cannon").objectReferenceValue = cannon;
                    sprayData.FindProperty("spraySource").objectReferenceValue = spraySource;
                    sprayData.FindProperty("sprayLoop").objectReferenceValue = sprayClip;
                    sprayData.FindProperty("volume").floatValue = .65f;
                    sprayData.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(spray);
                }
                PrefabUtility.SaveAsPrefabAsset(root, TruckPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigurePlayerPrefab(AudioClip painClip, AudioClip impactClip)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                LavaPlayerContact contact = root.GetComponent<LavaPlayerContact>();
                if (contact)
                {
                    Transform audioEmitter = root.transform.Find("Player Pain Audio");
                    if (!audioEmitter)
                    {
                        GameObject child = new GameObject("Player Pain Audio");
                        audioEmitter = child.transform;
                        audioEmitter.SetParent(root.transform, false);
                    }
                    AudioSource painSource = audioEmitter.GetComponent<AudioSource>();
                    if (!painSource) painSource = audioEmitter.gameObject.AddComponent<AudioSource>();
                    Configure3DSource(painSource, false, 0f, 1f, 15f);
                    SerializedObject contactData = new SerializedObject(contact);
                    contactData.FindProperty("painSource").objectReferenceValue = painSource;
                    contactData.FindProperty("painClip").objectReferenceValue = painClip;
                    contactData.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(contact);
                }
                ItfaiyePistolFeedback pistol = root.GetComponentInChildren<ItfaiyePistolFeedback>(true);
                if (pistol)
                {
                    pistol.targetImpactClip = impactClip;
                    EditorUtility.SetDirty(pistol);
                }
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureMenu(Scene scene, AudioClip clickClip)
        {
            AshfallMenuController controller = Object.FindFirstObjectByType<AshfallMenuController>(FindObjectsInactive.Include);
            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (controller == null || canvas == null)
            {
                Debug.LogError("ASHFALL audio integration could not find the existing menu controller and Canvas.");
                return;
            }

            VideoPlayer videoPlayer = GameObject.Find("ASHFALLVideoPlayer")?.GetComponent<VideoPlayer>();
            VideoClip compatibleVideo = AssetDatabase.LoadAssetAtPath<VideoClip>(CompatibleVideoPath);
            RenderTexture backgroundTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(BackgroundRenderTexturePath);
            if (videoPlayer != null && compatibleVideo != null && backgroundTexture != null)
            {
                if (!videoPlayer.clip) videoPlayer.clip = compatibleVideo;
                videoPlayer.renderMode = VideoRenderMode.RenderTexture;
                videoPlayer.targetTexture = backgroundTexture;
                videoPlayer.playOnAwake = false;
                videoPlayer.isLooping = false;
                videoPlayer.waitForFirstFrame = true;
                videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                videoPlayer.controlledAudioTrackCount = 0;
                EditorUtility.SetDirty(videoPlayer);
            }
            else
                Debug.LogWarning("ASHFALL: Compatible menu MP4 was not assigned because the video player, converted clip, or RenderTexture is missing.");

            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
            AshfallMenuAudioFeedback feedback = controller.GetComponent<AshfallMenuAudioFeedback>();
            if (feedback == null) feedback = controller.gameObject.AddComponent<AshfallMenuAudioFeedback>();
            SerializedObject serialized = new SerializedObject(feedback);
            SerializedProperty buttonArray = serialized.FindProperty("targetButtons");
            buttonArray.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
                buttonArray.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            serialized.FindProperty("hoverClip").objectReferenceValue = clickClip;
            serialized.FindProperty("clickClip").objectReferenceValue = clickClip;
            serialized.FindProperty("volume").floatValue = 0.8f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(controller.gameObject);
            EditorUtility.SetDirty(feedback);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureFireAmbience(Scene scene, AudioClip fireClip)
        {
            BurnableBuilding[] buildings = Object.FindObjectsByType<BurnableBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (buildings.Length == 0)
            {
                Debug.LogWarning($"ASHFALL: No BurnableBuilding components were found in {scene.path}; fire ambience was not added.");
                return;
            }

            FireAudioAmbience ambience = Object.FindFirstObjectByType<FireAudioAmbience>(FindObjectsInactive.Include);
            if (ambience == null)
            {
                GameObject ambienceObject = new GameObject("FireAudioAmbience");
                SceneManager.MoveGameObjectToScene(ambienceObject, scene);
                AudioSource source = ambienceObject.AddComponent<AudioSource>();
                ambience = ambienceObject.AddComponent<FireAudioAmbience>();
                SerializedObject serialized = new SerializedObject(ambience);
                serialized.FindProperty("ambienceSource").objectReferenceValue = source;
                serialized.FindProperty("fireLoop").objectReferenceValue = fireClip;
                serialized.FindProperty("maximumVolume").floatValue = 0.65f;
                serialized.FindProperty("fadeSpeed").floatValue = 1f;
                serialized.FindProperty("spatialBlend").floatValue = 0f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                SerializedObject serialized = new SerializedObject(ambience);
                serialized.FindProperty("fireLoop").objectReferenceValue = fireClip;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(ambience.gameObject);
            EditorUtility.SetDirty(ambience);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureGameplayAudio(Scene scene, AudioClip windClip, AudioClip footstepClip, AudioClip explosionClip,
            AudioClip bubblingClip, AudioClip painClip, AudioClip crowdClip, AudioClip impactClip, AudioClip sprayClip)
        {
            ConfigureWindAudio(scene, windClip, explosionClip);
            AudioClip runningClip = AssetDatabase.LoadAssetAtPath<AudioClip>(RunningClipPath);
            AudioClip spiritClip = AssetDatabase.LoadAssetAtPath<AudioClip>(SpiritClipPath);
            WarnMissing(RunningClipPath, runningClip);
            WarnMissing(SpiritClipPath, spiritClip);
            foreach (LavaCreatureActor actor in Object.FindObjectsByType<LavaCreatureActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var audio = actor.GetComponent<LavaSpiritAudio>();
                if (!audio) audio = actor.gameObject.AddComponent<LavaSpiritAudio>();
                audio.clip = spiritClip;
                actor.bulletImpactClip = impactClip;
                EditorUtility.SetDirty(actor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
                EditorUtility.SetDirty(audio);
                PrefabUtility.RecordPrefabInstancePropertyModifications(audio);
            }

            foreach (ProgressiveLavaFlow flow in Object.FindObjectsByType<ProgressiveLavaFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!bubblingClip) continue;
                SerializedObject flowData = new SerializedObject(flow);
                var previous = flowData.FindProperty("flowAudioSource").objectReferenceValue as AudioSource;
                Transform emitter = flow.transform.Find("Lava Flow Audio");
                if (!emitter) { var child = new GameObject("Lava Flow Audio"); emitter = child.transform; emitter.SetParent(flow.transform, false); }
                AudioSource source = emitter.GetComponent<AudioSource>();
                if (!source) source = emitter.gameObject.AddComponent<AudioSource>();
                Configure3DSource(source, true, 0f, 8f, 70f);
                flowData.FindProperty("flowAudioSource").objectReferenceValue = source;
                flowData.FindProperty("bubblingClip").objectReferenceValue = bubblingClip;
                flowData.FindProperty("bubblingVolume").floatValue = .5f;
                flowData.ApplyModifiedPropertiesWithoutUndo();
                if (previous && previous != source && previous.gameObject == flow.gameObject) Object.DestroyImmediate(previous);
                EditorUtility.SetDirty(flow);
                EditorUtility.SetDirty(source);
                PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
            }

            foreach (LavaPlayerContact contact in Object.FindObjectsByType<LavaPlayerContact>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform emitter = contact.transform.Find("Player Pain Audio");
                if (!emitter) { var child = new GameObject("Player Pain Audio"); emitter = child.transform; emitter.SetParent(contact.transform, false); }
                AudioSource source = emitter.GetComponent<AudioSource>();
                if (!source) source = emitter.gameObject.AddComponent<AudioSource>();
                Configure3DSource(source, false, 0f, 1f, 15f);
                SerializedObject contactData = new SerializedObject(contact);
                contactData.FindProperty("painSource").objectReferenceValue = source;
                contactData.FindProperty("painClip").objectReferenceValue = painClip;
                contactData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(contact);
                EditorUtility.SetDirty(source);
                PrefabUtility.RecordPrefabInstancePropertyModifications(contact);
            }

            foreach (EvacuationMission mission in Object.FindObjectsByType<EvacuationMission>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!crowdClip) continue;
                Transform emitter = mission.transform.Find("Crowd Panic Ambience");
                if (!emitter) { var child = new GameObject("Crowd Panic Ambience"); emitter = child.transform; emitter.SetParent(mission.transform, false); }
                AudioSource source = emitter.GetComponent<AudioSource>();
                if (!source) source = emitter.gameObject.AddComponent<AudioSource>();
                Configure3DSource(source, true, 0f, 8f, 65f);
                SerializedObject missionData = new SerializedObject(mission);
                missionData.FindProperty("crowdPanicLoop").objectReferenceValue = crowdClip;
                missionData.FindProperty("crowdSource").objectReferenceValue = source;
                missionData.FindProperty("crowdPanicVolume").floatValue = .45f;
                missionData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mission);
                EditorUtility.SetDirty(source);
                PrefabUtility.RecordPrefabInstancePropertyModifications(mission);
            }

            foreach (ItfaiyePistolFeedback pistol in Object.FindObjectsByType<ItfaiyePistolFeedback>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (impactClip) pistol.targetImpactClip = impactClip;
                EditorUtility.SetDirty(pistol);
                PrefabUtility.RecordPrefabInstancePropertyModifications(pistol);
            }

            foreach (FireTruckWaterCannon cannon in Object.FindObjectsByType<FireTruckWaterCannon>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!sprayClip) continue;
                FireTruckSprayAudio spray = cannon.GetComponent<FireTruckSprayAudio>();
                if (!spray) spray = cannon.gameObject.AddComponent<FireTruckSprayAudio>();
                Transform emitter = cannon.muzzle ? cannon.muzzle.Find("Water Spray Audio") : cannon.transform.Find("Water Spray Audio");
                if (!emitter) { var child = new GameObject("Water Spray Audio"); emitter = child.transform; emitter.SetParent(cannon.muzzle ? cannon.muzzle : cannon.transform, false); }
                AudioSource source = emitter.GetComponent<AudioSource>();
                if (!source) source = emitter.gameObject.AddComponent<AudioSource>();
                Configure3DSource(source, true, 0f, 3f, 50f);
                SerializedObject sprayData = new SerializedObject(spray);
                sprayData.FindProperty("cannon").objectReferenceValue = cannon;
                sprayData.FindProperty("spraySource").objectReferenceValue = source;
                sprayData.FindProperty("sprayLoop").objectReferenceValue = sprayClip;
                sprayData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(spray);
                EditorUtility.SetDirty(source);
                PrefabUtility.RecordPrefabInstancePropertyModifications(spray);
            }

            FireRescuePlayer[] players = Object.FindObjectsByType<FireRescuePlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (FireRescuePlayer player in players)
            {
                AudioSource footstepSource = player.GetComponent<AudioSource>();
                if (footstepSource == null) footstepSource = player.gameObject.AddComponent<AudioSource>();
                FireRescueMovementAudio movementAudio = player.GetComponent<FireRescueMovementAudio>();
                if (movementAudio == null) movementAudio = player.gameObject.AddComponent<FireRescueMovementAudio>();
                SerializedObject movementData = new SerializedObject(movementAudio);
                movementData.FindProperty("player").objectReferenceValue = player;
                movementData.FindProperty("footstepSource").objectReferenceValue = footstepSource;
                movementData.FindProperty("footstepClip").objectReferenceValue = footstepClip;
                movementData.FindProperty("walkingClip").objectReferenceValue = footstepClip;
                movementData.FindProperty("runningClip").objectReferenceValue = runningClip;
                movementData.FindProperty("minimumHorizontalSpeed").floatValue = 0.35f;
                movementData.FindProperty("runningSpeedThreshold").floatValue = 0.65f;
                movementData.FindProperty("loopClipDurationThreshold").floatValue = 1.2f;
                movementData.FindProperty("walkingVolume").floatValue = 0.55f;
                movementData.FindProperty("runningVolume").floatValue = 0.72f;
                movementData.FindProperty("walkingStepInterval").floatValue = 0.48f;
                movementData.FindProperty("runningStepInterval").floatValue = 0.32f;
                movementData.ApplyModifiedPropertiesWithoutUndo();
                movementAudio.enabled = true;
                footstepSource.enabled = true;
                footstepSource.playOnAwake = false;
                footstepSource.clip = null;
                EditorUtility.SetDirty(footstepSource);
                EditorUtility.SetDirty(player.gameObject);
                EditorUtility.SetDirty(movementAudio);
                PrefabUtility.RecordPrefabInstancePropertyModifications(footstepSource);
                PrefabUtility.RecordPrefabInstancePropertyModifications(movementAudio);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void Configure3DSource(AudioSource source, bool loop, float volume, float minDistance, float maxDistance)
        {
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = volume;
            source.spatialBlend = loop ? 1f : 0f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
        }

        private static void WarnMissing(string path, AudioClip clip)
        {
            if (!clip) Debug.LogWarning("ASHFALL audio integration: missing clip at " + path);
        }

        private static void ConfigureWindAudio(Scene scene, AudioClip windClip, AudioClip explosionClip)
        {
            if (windClip == null)
            {
                Debug.LogWarning("ASHFALL: Wind clip is missing at " + WindClipPath + ". WindAudioAmbience was not configured.");
                return;
            }

            GameObject windObject = GameObject.Find("WindAudioAmbience");
            if (windObject == null)
            {
                windObject = new GameObject("WindAudioAmbience");
                SceneManager.MoveGameObjectToScene(windObject, scene);
            }

            AudioSource windSource = windObject.GetComponent<AudioSource>();
            if (windSource == null) windSource = windObject.AddComponent<AudioSource>();
            windSource.playOnAwake = false;
            windSource.loop = false;
            windSource.volume = 0f;
            windSource.spatialBlend = 0f;
            windSource.dopplerLevel = 0f;
            WindAudioPulse wind = windObject.GetComponent<WindAudioPulse>();
            if (wind == null) wind = windObject.AddComponent<WindAudioPulse>();
            SerializedObject windData = new SerializedObject(wind);
            windData.FindProperty("source").objectReferenceValue = windSource;
            windData.FindProperty("explosionSound").objectReferenceValue = explosionClip;
            windData.FindProperty("windLoop").objectReferenceValue = windClip;
            // This loop is a post-impact ambience everywhere; a scene without the volcano sequencer stays silent.
            windData.FindProperty("waitForExplosionSequence").boolValue = true;
            windData.FindProperty("explosionVolume").floatValue = 0.85f;
            windData.FindProperty("minimumVolume").floatValue = 0.08f;
            windData.FindProperty("maximumVolume").floatValue = 0.42f;
            windData.FindProperty("explosionFadeDuration").floatValue = 0.8f;
            windData.FindProperty("windFadeDuration").floatValue = 2f;
            windData.FindProperty("postExplosionSeconds").floatValue = 4f;
            windData.FindProperty("shortestSwellingInterval").floatValue = 8f;
            windData.FindProperty("longestSwellingInterval").floatValue = 15f;
            windData.FindProperty("swellDuration").floatValue = 4f;
            windData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(windObject);
            EditorUtility.SetDirty(windSource);
            EditorUtility.SetDirty(wind);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void OpenAndInstallScene(string path, System.Action<Scene> install)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            install(scene);
        }
    }
}
#endif
