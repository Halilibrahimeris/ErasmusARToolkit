using System.Collections.Generic;
using UnityEngine;
#if ERASMUS_HAS_URP
using System.Linq;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
#endif

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// AR Foundation draws the phone camera image through URP renderer features. Unity's
    /// validation only checks the renderer of the current platform, so this adds the
    /// features to every URP renderer used by any quality level (Android uses "Mobile").
    /// The features do nothing unless an AR camera is active, so PC renderers are unaffected.
    /// </summary>
    public static class UrpRendererConfigurator
    {
        public static bool IsConfigured
        {
            get
            {
#if ERASMUS_HAS_URP
                return FindRenderers().All(HasRequiredFeatures);
#else
                return true;
#endif
            }
        }

        /// <summary>Adds the missing features and returns how many were added.</summary>
        public static int AddMissingFeatures()
        {
#if ERASMUS_HAS_URP
            var added = 0;
            foreach (var renderer in FindRenderers())
            {
                added += AddFeature<ARBackgroundRendererFeature>(renderer) ? 1 : 0;
                added += AddFeature<ARCommandBufferSupportRendererFeature>(renderer) ? 1 : 0;
            }

            if (added > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[Erasmus AR Toolkit] Added {added} AR renderer feature(s) to URP renderers.");
            }

            return added;
#else
            return 0;
#endif
        }

        /// <summary>
        /// The renderers of the Android quality levels keep what the toolkit's surface shaders rely on:
        /// forward rendering (deferred uses the stencil buffer itself), no depth priming (the camera image
        /// writes the real-world depth after a depth prepass would have run, so primed objects disappear)
        /// and a depth buffer that has a stencil part.
        /// </summary>
        public static bool IsStencilSafe
        {
            get
            {
#if ERASMUS_HAS_URP
                return FindAndroidRenderers().All(IsStencilSafeRenderer);
#else
                return true;
#endif
            }
        }

        /// <summary>Changes the renderers that need it and returns how many were changed.</summary>
        public static int MakeStencilSafe()
        {
#if ERASMUS_HAS_URP
            var changed = 0;
            foreach (var renderer in FindAndroidRenderers())
            {
                if (IsStencilSafeRenderer(renderer))
                {
                    continue;
                }

                if (!IsForward(renderer.renderingMode))
                {
                    renderer.renderingMode = RenderingMode.Forward;
                }

                renderer.depthPrimingMode = DepthPrimingMode.Disabled;
                if (!HasStencil(renderer.depthAttachmentFormat))
                {
                    renderer.depthAttachmentFormat = DepthFormat.Default;
                }

                EditorUtility.SetDirty(renderer);
                changed++;
                Debug.Log($"[Erasmus AR Toolkit] URP renderer '{renderer.name}' now uses forward rendering without " +
                          "depth priming and a depth buffer with stencil, as AR occlusion needs.", renderer);
            }

            if (changed > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return changed;
#else
            return 0;
#endif
        }

        /// <summary>
        /// The camera image is not HDR, so HDR rendering only costs the phone an extra full-screen copy.
        /// Frame pacing spreads the frames evenly (no stutter at 30 frames per second).
        /// </summary>
        public static bool IsPhoneRenderingTuned
        {
            get
            {
#if ERASMUS_HAS_URP
                return PlayerSettings.Android.optimizedFramePacing && FindAndroidAssets().All(asset => !asset.supportsHDR);
#else
                return true;
#endif
            }
        }

        public static void TunePhoneRendering()
        {
#if ERASMUS_HAS_URP
            PlayerSettings.Android.optimizedFramePacing = true;
            foreach (var asset in FindAndroidAssets())
            {
                if (asset.supportsHDR)
                {
                    asset.supportsHDR = false;
                    EditorUtility.SetDirty(asset);
                    Debug.Log($"[Erasmus AR Toolkit] HDR is now off in the URP asset '{asset.name}' (used on Android).", asset);
                }
            }

            AssetDatabase.SaveAssets();
#endif
        }

#if ERASMUS_HAS_URP
        const string AndroidPlatform = "Android";

        static bool IsForward(RenderingMode mode)
        {
            return mode == RenderingMode.Forward || mode == RenderingMode.ForwardPlus;
        }

        static bool HasStencil(DepthFormat format)
        {
            return format == DepthFormat.Default || format == DepthFormat.Depth_16_Stencil_8
                   || format == DepthFormat.Depth_24_Stencil_8 || format == DepthFormat.Depth_32_Stencil_8;
        }

        static bool IsStencilSafeRenderer(UniversalRendererData renderer)
        {
            return IsForward(renderer.renderingMode) && renderer.depthPrimingMode == DepthPrimingMode.Disabled
                   && HasStencil(renderer.depthAttachmentFormat);
        }

        /// <summary>The URP assets of the quality levels that are switched on for Android.</summary>
        static IEnumerable<UniversalRenderPipelineAsset> FindAndroidAssets()
        {
            var assets = new HashSet<UniversalRenderPipelineAsset>();
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                if (!QualitySettings.IsPlatformIncluded(AndroidPlatform, i))
                {
                    continue;
                }

                // A quality level without its own asset uses the one of the graphics settings.
                var levelAsset = QualitySettings.GetRenderPipelineAssetAt(i);
                if (levelAsset == null)
                {
                    levelAsset = GraphicsSettings.defaultRenderPipeline;
                }

                if (levelAsset is UniversalRenderPipelineAsset asset)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }

        static IEnumerable<UniversalRendererData> FindAndroidRenderers()
        {
            var renderers = new HashSet<UniversalRendererData>();
            foreach (var asset in FindAndroidAssets())
            {
                foreach (var renderer in asset.rendererDataList)
                {
                    if (renderer is UniversalRendererData universal)
                    {
                        renderers.Add(universal);
                    }
                }
            }

            return renderers;
        }

        static bool HasRequiredFeatures(ScriptableRendererData renderer)
        {
            return renderer.rendererFeatures.Any(f => f is ARBackgroundRendererFeature)
                   && renderer.rendererFeatures.Any(f => f is ARCommandBufferSupportRendererFeature);
        }

        static IEnumerable<ScriptableRendererData> FindRenderers()
        {
            var assets = new HashSet<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset defaultAsset)
            {
                assets.Add(defaultAsset);
            }

            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset levelAsset)
                {
                    assets.Add(levelAsset);
                }
            }

            var renderers = new HashSet<ScriptableRendererData>();
            foreach (var asset in assets)
            {
                foreach (var renderer in asset.rendererDataList)
                {
                    if (renderer != null)
                    {
                        renderers.Add(renderer);
                    }
                }
            }

            return renderers;
        }

        /// <summary>Same steps as URP's "Add Renderer Feature" button.</summary>
        static bool AddFeature<T>(ScriptableRendererData renderer) where T : ScriptableRendererFeature
        {
            if (renderer.rendererFeatures.Any(feature => feature is T))
            {
                return false;
            }

            var feature = ScriptableObject.CreateInstance<T>();
            feature.name = typeof(T).Name;
            Undo.RegisterCreatedObjectUndo(feature, "Add AR Renderer Feature");
            AssetDatabase.AddObjectToAsset(feature, renderer);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var serialized = new SerializedObject(renderer);
            var features = serialized.FindProperty("m_RendererFeatures");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            var map = serialized.FindProperty("m_RendererFeatureMap");
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(renderer);
            return true;
        }
#endif
    }
}
