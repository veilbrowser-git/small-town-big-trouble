using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace SmallTown.EditorTools
{
    /// <summary>
    /// Recreates every piece of project configuration the game depends on:
    /// layers, collision matrix, URP assets, player settings and OpenXR for
    /// Quest. Safe to run repeatedly. Run from the menu or headless with
    /// -executeMethod SmallTown.EditorTools.ProjectSetup.ConfigureProject.
    /// </summary>
    public static class ProjectSetup
    {
        public const string Root = "Assets/SmallTown";
        public const string SettingsFolder = Root + "/Settings";
        const string PipelineAssetPath = SettingsFolder + "/SmallTownURP.asset";
        const string RendererAssetPath = SettingsFolder + "/SmallTownURP_Renderer.asset";
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string TmpEssentials = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        [MenuItem("Small Town/Setup/1. Configure Project Settings")]
        public static void ConfigureProject()
        {
            EnsureFolder(SettingsFolder);
            ConfigureLayers();
            ConfigureCollisionMatrix();
            ConfigureRenderPipeline();
            ConfigurePlayer();
            ConfigureXR();
            ImportTextMeshProEssentials();
            AssetDatabase.SaveAssets();
            Debug.Log("[SmallTown] Project settings configured.");
        }

        [MenuItem("Small Town/Setup/Run Everything (settings, prefabs, scenes)")]
        public static void RunEverything()
        {
            ConfigureProject();
            ContentBuilder.BuildAll();
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ Layers

        static void ConfigureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            foreach (var (index, name) in GameLayers.Names)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(index);
                if (slot.stringValue != name) slot.stringValue = name;
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureCollisionMatrix()
        {
            // The player's kinematic proxies shove loose items and trip triggers, and
            // nothing else. Held items never touch any part of a player.
            int[] player = { GameLayers.PlayerBody, GameLayers.PlayerHand, GameLayers.RemotePlayer };
            foreach (int a in player)
            {
                for (int b = 0; b < 32; b++)
                {
                    bool collide = b == GameLayers.Item || b == GameLayers.Interactable;
                    Physics.IgnoreLayerCollision(a, b, !collide);
                }
            }
            Physics.IgnoreLayerCollision(GameLayers.HeldItem, GameLayers.PlayerBody, true);
            Physics.IgnoreLayerCollision(GameLayers.HeldItem, GameLayers.PlayerHand, true);
            Physics.IgnoreLayerCollision(GameLayers.HeldItem, GameLayers.RemotePlayer, true);
            Physics.IgnoreLayerCollision(GameLayers.Interactable, GameLayers.World, true);
            Physics.IgnoreLayerCollision(GameLayers.Interactable, GameLayers.Interactable, true);
        }

        // ------------------------------------------------------------------ Rendering

        static void ConfigureRenderPipeline()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererAssetPath);
                try
                {
                    ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SmallTown] Renderer resources were not reloaded: " + e.Message);
                }
            }
            // Mobile VR: no post-processing stack.
            renderer.postProcessData = null;
            EditorUtility.SetDirty(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);
            }

            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.mainLightShadowmapResolution = 1024;
            pipeline.shadowDistance = 25f;
            pipeline.shadowCascadeCount = 1;
            pipeline.maxAdditionalLightsCount = 2;
            pipeline.useSRPBatcher = true;

            // These two have no public setters; write the serialized fields.
            var serialized = new SerializedObject(pipeline);
            SetIfPresent(serialized, "m_MainLightShadowsSupported", p => p.boolValue = true);
            SetIfPresent(serialized, "m_AdditionalLightsRenderingMode", p => p.intValue = (int)LightRenderingMode.PerVertex);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.vSyncCount = 0;
                QualitySettings.antiAliasing = 4;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void SetIfPresent(SerializedObject target, string property, Action<SerializedProperty> apply)
        {
            SerializedProperty found = target.FindProperty(property);
            if (found != null) apply(found);
            else Debug.LogWarning("[SmallTown] URP asset has no property " + property);
        }

        // ------------------------------------------------------------------ Player

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Small Town Games";
            PlayerSettings.productName = "Small Town Big Trouble";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.smalltowngames.bigtrouble");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            // Input System package only (1). The legacy Input Manager is not used.
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            SerializedProperty handler = settings.FindProperty("activeInputHandler");
            if (handler != null && handler.intValue != 1)
            {
                handler.intValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ------------------------------------------------------------------ XR

        static void ConfigureXR()
        {
            EnsureFolder("Assets/XR");
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
            }

            EnableOpenXR(perTarget, BuildTargetGroup.Android, true);
            // PC VR comes later. Only switch the desktop loader on where an OpenXR
            // runtime can exist, so the macOS editor stays quiet in play mode.
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                EnableOpenXR(perTarget, BuildTargetGroup.Standalone, false);
            }
            EditorUtility.SetDirty(perTarget);
        }

        static void EnableOpenXR(XRGeneralSettingsPerBuildTarget perTarget, BuildTargetGroup group, bool quest)
        {
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            XRManagerSettings manager = perTarget.ManagerSettingsForBuildTarget(group);
            if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoader, group))
            {
                if (!XRPackageMetadataStore.AssignLoader(manager, OpenXRLoader, group))
                {
                    Debug.LogError("[SmallTown] Could not assign the OpenXR loader for " + group);
                }
            }
            XRGeneralSettings general = perTarget.SettingsForBuildTarget(group);
            if (general != null) general.InitManagerOnStart = true;

            FeatureHelpers.RefreshFeatures(group);
            EnableFeature(group, "com.unity.openxr.feature.input.oculustouch");
            EnableFeature(group, "com.unity.openxr.feature.input.metaquestplus");
            if (quest) EnableFeature(group, "com.unity.openxr.feature.metaquest");

            OpenXRSettings openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openXR != null)
            {
                openXR.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                EditorUtility.SetDirty(openXR);
            }
        }

        static void EnableFeature(BuildTargetGroup group, string featureId)
        {
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
            if (feature == null)
            {
                Debug.LogWarning("[SmallTown] OpenXR feature not found: " + featureId);
                return;
            }
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        // ------------------------------------------------------------------ TextMeshPro

        static void ImportTextMeshProEssentials()
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) return;
            // The package lives in the package cache, so resolve its real location on disk.
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            string path = info != null
                ? Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage")
                : Path.GetFullPath(TmpEssentials);
            if (!File.Exists(path))
            {
                Debug.LogWarning("[SmallTown] TextMeshPro essentials package not found at " + path);
                return;
            }
            int written = UnityPackageExtractor.Extract(path, Directory.GetCurrentDirectory());
            AssetDatabase.Refresh();
            Debug.Log("[SmallTown] Imported TextMeshPro essentials (" + written + " files).");
        }
    }
}
