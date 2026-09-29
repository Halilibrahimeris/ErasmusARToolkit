using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Brings toolkit scenes made by older toolkit versions up to date: the theme, the AR rig settings
    /// and the components that newer versions add (they are also added when the scene starts, but then
    /// they cannot be seen or changed in the Inspector).
    /// </summary>
    public static class SceneUpgrader
    {
        public static bool NeedsUpgrade(ARToolkitManager manager)
        {
            if (manager == null)
            {
                return false;
            }

            if (manager.Theme == null)
            {
                return true;
            }

            var origin = manager.OriginObject;
            if (origin == null)
            {
                return false;
            }

            var xrOrigin = origin.GetComponent<XROrigin>();
            if (xrOrigin != null && xrOrigin.CameraYOffset != 0f)
            {
                return true;
            }

            var cameraManager = FindCameraManager(manager);
            if (cameraManager != null
                && cameraManager.requestedBackgroundRenderingMode != CameraBackgroundRenderingMode.BeforeOpaques)
            {
                return true;
            }

            return origin.GetComponent<ARAnchorManager>() == null
                   || origin.GetComponent<ARPointCloudManager>() == null
                   || (origin.GetComponent<ARPlaneManager>() != null
                       && (origin.GetComponent<ARPlaneSurfaces>() == null || origin.GetComponent<RoomScanner>() == null))
                   || (cameraManager != null && cameraManager.GetComponent<AROcclusionManager>() == null);
        }

        /// <summary>Returns true if anything was changed.</summary>
        public static bool Apply(ARToolkitManager manager, ToolkitTheme theme)
        {
            if (!NeedsUpgrade(manager))
            {
                return false;
            }

            if (manager.Theme == null && theme != null)
            {
                manager.SetTheme(theme);
                EditorUtility.SetDirty(manager);
            }

            var origin = manager.OriginObject;
            if (origin != null)
            {
                UpgradeRig(manager, origin);
            }

            return true;
        }

        /// <summary>Upgrades every toolkit scene that is open. Returns how many were changed (they are not saved).</summary>
        public static int UpgradeOpenScenes()
        {
            var theme = ToolkitAssets.GetOrCreateTheme();
            var changed = 0;
            foreach (var manager in SceneQueries.FindAll<ARToolkitManager>())
            {
                if (!manager.gameObject.scene.IsValid() || !Apply(manager, theme))
                {
                    continue;
                }

                changed++;
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                Debug.Log($"[Erasmus AR Toolkit] Upgraded the AR rig of scene '{manager.gameObject.scene.name}'. " +
                          "Save the scene to keep it.", manager);
            }

            return changed;
        }

        /// <summary>True when an open toolkit scene is from an older toolkit version.</summary>
        public static bool AnyOpenSceneNeedsUpgrade()
        {
            foreach (var manager in SceneQueries.FindAll<ARToolkitManager>())
            {
                if (manager.gameObject.scene.IsValid() && NeedsUpgrade(manager))
                {
                    return true;
                }
            }

            return false;
        }

        static void UpgradeRig(ARToolkitManager manager, GameObject origin)
        {
            var xrOrigin = origin.GetComponent<XROrigin>();
            if (xrOrigin != null && xrOrigin.CameraYOffset != 0f)
            {
                xrOrigin.CameraYOffset = 0f;
                EditorUtility.SetDirty(xrOrigin);
            }

            var cameraManager = FindCameraManager(manager);
            if (cameraManager != null)
            {
                if (cameraManager.requestedBackgroundRenderingMode != CameraBackgroundRenderingMode.BeforeOpaques)
                {
                    cameraManager.requestedBackgroundRenderingMode = CameraBackgroundRenderingMode.BeforeOpaques;
                    EditorUtility.SetDirty(cameraManager);
                }

                if (cameraManager.GetComponent<AROcclusionManager>() == null)
                {
                    var config = manager.Config;
                    RealWorldOcclusion.Configure(cameraManager.gameObject, config == null || config.RealWorldOcclusion);
                }
            }

            if (xrOrigin != null && origin.GetComponent<ARAnchorManager>() == null)
            {
                origin.AddComponent<ARAnchorManager>();
            }

            if (origin.GetComponent<ARPointCloudManager>() == null)
            {
                // Only a room scan on a phone without depth switches it on.
                origin.AddComponent<ARPointCloudManager>().enabled = false;
            }

            if (origin.GetComponent<ARPlaneManager>() != null)
            {
                if (origin.GetComponent<ARPlaneSurfaces>() == null)
                {
                    origin.AddComponent<ARPlaneSurfaces>();
                }

                if (origin.GetComponent<RoomScanner>() == null)
                {
                    origin.AddComponent<RoomScanner>();
                }
            }

            EditorUtility.SetDirty(origin);
        }

        static ARCameraManager FindCameraManager(ARToolkitManager manager)
        {
            var camera = manager.ARCamera;
            return camera != null ? camera.GetComponent<ARCameraManager>() : null;
        }
    }
}
