using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Creates a ready-to-run toolkit scene: AR rig, Desktop rig, light, HUD and EventSystem.</summary>
    public static class SceneBuilder
    {
        const string RootName = "AR Toolkit";

        /// <summary>Creates, saves and adds a new scene to the build list. Returns its path, or null if cancelled.</summary>
        public static string CreateNewScene(ARToolkitConfig config, string scenePath = ToolkitPaths.DefaultScenePath,
            bool overwrite = false)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return null;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Build(config);

            ToolkitAssets.EnsureFolder(System.IO.Path.GetDirectoryName(scenePath).Replace('\\', '/'));
            var path = overwrite ? scenePath : AssetDatabase.GenerateUniqueAssetPath(scenePath);
            EditorSceneManager.SaveScene(scene, path);
            AddSceneToBuildSettings(path);
            Debug.Log("[Erasmus AR Toolkit] Created AR scene: " + path);
            return path;
        }

        /// <summary>Adds the toolkit to the open scene. Returns the existing manager if there already is one.</summary>
        public static ARToolkitManager AddToCurrentScene(ARToolkitConfig config)
        {
            var existing = SceneQueries.FindFirst<ARToolkitManager>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                return existing;
            }

            DisableOtherMainCameras();
            var manager = Build(config);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return manager;
        }

        public static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.path != path && System.IO.File.Exists(s.path))
                .ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static ARToolkitManager Build(ARToolkitConfig config)
        {
            var theme = ToolkitAssets.GetOrCreateTheme();
            var planePrefab = ToolkitAssets.GetOrCreatePlanePrefab(theme);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create AR Toolkit");
            var placedRoot = CreateChild("Placed Objects", root.transform);

            var session = CreateChild("AR Session", root.transform, typeof(ARSession), typeof(ARInputManager));
            var origin = CreateXROrigin(root.transform, out var arCamera);
            RealWorldOcclusion.Configure(arCamera.gameObject, config == null || config.RealWorldOcclusion);
            var planeManager = origin.AddComponent<ARPlaneManager>();
            planeManager.planePrefab = planePrefab;
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            origin.AddComponent<ARPlaneSurfaces>();
            origin.AddComponent<ARRaycastManager>();
            origin.AddComponent<ARAnchorManager>();

            // Room scan mode. Tracking points are only needed by a scan on phones without depth.
            origin.AddComponent<ARPointCloudManager>().enabled = false;
            origin.AddComponent<RoomScanner>();
            var arSurface = origin.AddComponent<ARPlacementSurface>();
            var imageManager = origin.AddComponent<ARTrackedImageManager>();
            AssignImageLibrary(imageManager, config);
            var spawner = origin.AddComponent<ImageTargetSpawner>();

            var desktopRig = CreateChild("Desktop Rig", root.transform);
            var desktopCamera = CreateDesktopCamera(desktopRig.transform);
            var ground = CreateChild("Virtual Ground", desktopRig.transform).AddComponent<VirtualGround>();
            ground.Initialize(theme);
            var desktopSurface = desktopRig.AddComponent<DesktopPlacementSurface>();
            desktopSurface.Initialize(desktopCamera, ground);
            var simulator = desktopRig.AddComponent<DesktopImageTargetSimulator>();
            var webcam = desktopRig.AddComponent<WebcamBackground>();

            CreateLight(root.transform);

            var manager = root.AddComponent<ARToolkitManager>();
            var interaction = root.AddComponent<InteractionController>();
            manager.Initialize(config, theme, placedRoot.transform, session, origin, arCamera, arSurface,
                planeManager, desktopRig, desktopCamera, desktopSurface);
            manager.SetDesktopParts(ground, webcam);
            spawner.Initialize(manager);
            simulator.Initialize(manager);
            webcam.Initialize(desktopCamera, manager);

            var hudObject = CreateChild("HUD", root.transform, typeof(UIDocument), typeof(ScreenshotService));
            var document = hudObject.GetComponent<UIDocument>();
            document.panelSettings = ToolkitAssets.GetOrCreatePanelSettings();
            document.visualTreeAsset = ToolkitAssets.FindAsset<VisualTreeAsset>(ToolkitAssets.HudLayoutName);
            if (document.visualTreeAsset == null)
            {
                Debug.LogError("[Erasmus AR Toolkit] ARToolkitHUD.uxml was not found. Re-import the toolkit package.");
            }

            var hud = hudObject.AddComponent<ARToolkitHUD>();
            hud.Initialize(manager, interaction);
            EnsureEventSystem();

            // AR objects stay off in the saved scene so AR Foundation never starts on PC.
            // ARToolkitManager switches them on at runtime when AR is available.
            session.SetActive(false);
            origin.SetActive(false);

            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                EditorUtility.SetDirty(component);
            }

            Selection.activeGameObject = root;
            return manager;
        }

        static GameObject CreateXROrigin(Transform parent, out Camera arCamera)
        {
            var origin = CreateChild("XR Origin (Mobile AR)", parent, typeof(XROrigin));
            var offset = CreateChild("Camera Offset", origin.transform);
            var cameraGo = CreateChild("AR Camera", offset.transform, typeof(Camera), typeof(AudioListener),
                typeof(ARCameraManager), typeof(ARCameraBackground), typeof(TrackedPoseDriver));

            arCamera = cameraGo.GetComponent<Camera>();
            arCamera.tag = "MainCamera";
            arCamera.clearFlags = CameraClearFlags.SolidColor;
            arCamera.backgroundColor = Color.black;
            arCamera.nearClipPlane = 0.1f;
            arCamera.farClipPlane = 20f;

            // The toolkit's surface marks and depth corrections are drawn after the camera image and are
            // depth-tested against the real-world depth it writes.
            cameraGo.GetComponent<ARCameraManager>().requestedBackgroundRenderingMode =
                CameraBackgroundRenderingMode.BeforeOpaques;

            // Same bindings as AR Foundation's "XR Origin (Mobile AR)" menu item.
            var poseDriver = cameraGo.GetComponent<TrackedPoseDriver>();
            var position = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            position.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotation = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotation.AddBinding("<HandheldARInputDevice>/deviceRotation");
            poseDriver.positionInput = new InputActionProperty(position);
            poseDriver.rotationInput = new InputActionProperty(rotation);

            var xrOrigin = origin.GetComponent<XROrigin>();
            xrOrigin.CameraFloorOffsetObject = offset;
            xrOrigin.Camera = arCamera;

            // Phone AR has no floor-level tracking origin: an offset would only shift every world height.
            xrOrigin.CameraYOffset = 0f;
            return origin;
        }

        static Camera CreateDesktopCamera(Transform parent)
        {
            var cameraGo = CreateChild("Desktop Camera", parent, typeof(Camera), typeof(AudioListener));
            var camera = cameraGo.GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.11f, 0.16f);
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 100f;
            cameraGo.AddComponent<DesktopCameraController>();
            return camera;
        }

        static void CreateLight(Transform parent)
        {
            if (SceneQueries.FindAll<Light>().Any(l => l.type == LightType.Directional && l.gameObject.scene.IsValid()))
            {
                return;
            }

            var light = CreateChild("Directional Light", parent, typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        static void AssignImageLibrary(ARTrackedImageManager imageManager, ARToolkitConfig config)
        {
            if (config == null || config.ImageLibrary == null)
            {
                return;
            }

            var serialized = new SerializedObject(imageManager);
            var property = serialized.FindProperty("m_SerializedLibrary");
            if (property != null)
            {
                property.objectReferenceValue = config.ImageLibrary;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void EnsureEventSystem()
        {
            if (SceneQueries.FindFirst<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
        }

        static void DisableOtherMainCameras()
        {
            foreach (var camera in SceneQueries.FindAll<Camera>())
            {
                if (camera.CompareTag("MainCamera") && camera.gameObject.activeSelf)
                {
                    Undo.RecordObject(camera.gameObject, "Disable Main Camera");
                    camera.gameObject.SetActive(false);
                    Debug.Log($"[Erasmus AR Toolkit] Disabled '{camera.name}' so the toolkit cameras are used.", camera);
                }
            }
        }

        static GameObject CreateChild(string name, Transform parent, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
