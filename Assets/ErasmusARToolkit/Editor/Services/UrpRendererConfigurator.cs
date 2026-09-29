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

#if ERASMUS_HAS_URP
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
