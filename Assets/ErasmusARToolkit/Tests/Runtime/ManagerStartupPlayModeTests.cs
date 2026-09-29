using System.Collections;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// Starts the toolkit on a rig as an older toolkit version made it (camera height offset, no anchor
    /// manager, no plane surfaces, no room scanner): the manager has to bring it up to date by itself.
    /// </summary>
    public class ManagerStartupPlayModeTests
    {
        const float OldCameraOffset = 1.1176f;
        const float StartupTimeoutSeconds = 3f;

        GameObject root;
        GameObject session;
        GameObject origin;
        GameObject desktopRig;
        Camera arCamera;
        ARToolkitConfig config;
        ARToolkitManager manager;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<ARToolkitConfig>();

            // Inactive while it is put together, like a scene that is still loading.
            root = new GameObject("AR Toolkit (test)");
            root.SetActive(false);

            session = NewChild("AR Session", root.transform);
            origin = NewChild("XR Origin", root.transform);
            var offset = NewChild("Camera Offset", origin.transform);
            var cameraObject = NewChild("AR Camera", offset.transform);
            arCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<ARCameraManager>();

            var xrOrigin = origin.AddComponent<XROrigin>();
            xrOrigin.CameraFloorOffsetObject = offset;
            xrOrigin.Camera = arCamera;
            xrOrigin.CameraYOffset = OldCameraOffset;
            var planeManager = origin.AddComponent<ARPlaneManager>();
            origin.AddComponent<ARRaycastManager>();
            var surface = origin.AddComponent<ARPlacementSurface>();

            desktopRig = NewChild("Desktop Rig", root.transform);
            var desktopCamera = NewChild("Desktop Camera", desktopRig.transform).AddComponent<Camera>();

            manager = root.AddComponent<ARToolkitManager>();
            manager.Initialize(config, null, root.transform, session, origin, arCamera, surface, planeManager,
                desktopRig, desktopCamera, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(root);
            Object.Destroy(config);
        }

        [UnityTest]
        public IEnumerator Awake_OnAnOlderRig_BringsItUpToDate_AndStartsInDesktopMode()
        {
            root.SetActive(true);

            Assert.AreEqual(0f, origin.GetComponent<XROrigin>().CameraYOffset,
                "Phone AR has no floor-level origin: the camera offset only shifts world heights.");
            Assert.AreEqual(CameraBackgroundRenderingMode.BeforeOpaques,
                arCamera.GetComponent<ARCameraManager>().requestedBackgroundRenderingMode);
            Assert.IsNotNull(origin.GetComponent<ARAnchorManager>());
            Assert.IsNotNull(origin.GetComponent<ARPlaneSurfaces>());
            Assert.IsNotNull(origin.GetComponent<RoomScanner>());
            Assert.IsNotNull(arCamera.GetComponent<AROcclusionManager>());
            Assert.AreSame(origin.GetComponent<RoomScanner>(), manager.RoomScanner);
            Assert.IsFalse(session.activeSelf, "The AR rig stays off until AR is chosen.");
            Assert.IsFalse(origin.activeSelf);

            var timeout = Time.realtimeSinceStartup + StartupTimeoutSeconds;
            while (manager.Mode == ToolkitMode.Undetermined && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.AreEqual(ToolkitMode.Desktop, manager.Mode);
            Assert.IsTrue(desktopRig.activeSelf);
            Assert.IsFalse(origin.activeSelf, "Desktop mode never starts the AR rig.");

            // Past the first status refresh, which decides about the screen.
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsFalse(manager.KeepsScreenOn, "Only AR mode keeps the screen on.");
        }

        static GameObject NewChild(string name, Transform parent)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }
    }
}
