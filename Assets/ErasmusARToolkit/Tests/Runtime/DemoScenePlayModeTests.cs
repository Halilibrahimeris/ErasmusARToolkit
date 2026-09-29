using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// Loads the demo scene and checks the Desktop mode end to end.
    /// Requires Samples/Scenes/ARToolkitDemo.unity in Build Settings
    /// (Tools > Erasmus AR Toolkit > Developer > Generate Demo Scene adds it).
    /// </summary>
    public class DemoScenePlayModeTests
    {
        const string DemoSceneName = "ARToolkitDemo";
        const float StartupTimeoutSeconds = 3f;

        ARToolkitManager manager;

        [UnitySetUp]
        public IEnumerator LoadDemoScene()
        {
            if (!Application.CanStreamedLevelBeLoaded(DemoSceneName))
            {
                Assert.Ignore("Demo scene is not in Build Settings. Run 'Generate Demo Scene' first.");
            }

            yield return SceneManager.LoadSceneAsync(DemoSceneName, LoadSceneMode.Single);
            manager = Object.FindAnyObjectByType<ARToolkitManager>();
            Assert.IsNotNull(manager, "Demo scene has no ARToolkitManager.");

            var timeout = Time.realtimeSinceStartup + StartupTimeoutSeconds;
            while (manager.Mode == ToolkitMode.Undetermined && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            // Let Start() of the HUD, ground and simulator run.
            yield return null;
            yield return null;
        }

        [Test]
        public void StartsInDesktopMode_WithDesktopCameraActive()
        {
            Assert.AreEqual(ToolkitMode.Desktop, manager.Mode);
            Assert.IsNotNull(manager.ActiveCamera);
            Assert.IsTrue(manager.ActiveCamera.isActiveAndEnabled);
            // With a webcam connected the camera-view hint is shown instead.
            CollectionAssert.Contains(new[] { StatusMessages.DesktopPlace, StatusMessages.CameraPlace }, manager.Status);
        }

        [Test]
        public void DesktopMode_OffersCameraButton_AndGridToggle()
        {
            var root = Object.FindAnyObjectByType<ARToolkitHUD>().GetComponent<UIDocument>().rootVisualElement;
            Assert.IsNotNull(manager.CameraFeed, "The Desktop rig should have a camera feed.");
            Assert.IsFalse(root.Q<Button>("camera-button").ClassListContains("hidden"),
                "Camera button should be visible in Desktop mode when the device camera is enabled.");
            StringAssert.EndsWith("Grid", root.Q<Button>("planes-button").text);
        }

        [Test]
        public void Hud_IsUIToolkit_WithOneButtonPerPlaceable()
        {
            var hud = Object.FindAnyObjectByType<ARToolkitHUD>();
            Assert.IsNotNull(hud);
            var root = hud.GetComponent<UIDocument>().rootVisualElement;
            var buttons = root.Q<ScrollView>("placeables").Query<Button>(className: "placeable-button").ToList();
            Assert.AreEqual(manager.Config.Placeables.Count, buttons.Count);
            Assert.IsTrue(buttons[0].ClassListContains("selected"), "First placeable should start selected.");
            Assert.AreEqual(manager.Status, root.Q<Label>("status").text);
        }

        [UnityTest]
        public IEnumerator PointerGuard_DetectsHudButtons_ButNotTheOpenView()
        {
            yield return null;
            var root = Object.FindAnyObjectByType<ARToolkitHUD>().GetComponent<UIDocument>().rootVisualElement;
            var button = root.Q<Button>("clear-button");
            var panelSize = root.panel.visualTree.layout.size;
            var center = button.worldBound.center;
            var screenPosition = new Vector2(
                center.x / panelSize.x * Screen.width,
                Screen.height - center.y / panelSize.y * Screen.height);

            Assert.IsTrue(UIPointerGuard.IsOverUI(screenPosition), "The Clear All button should block placement.");
            Assert.IsFalse(UIPointerGuard.IsOverUI(new Vector2(Screen.width * 0.4f, Screen.height * 0.5f)),
                "The middle of the view should allow placement.");
        }

        [UnityTest]
        public IEnumerator SelectingAnObject_OpensEditPanel_ThatEditsIt()
        {
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Assert.IsTrue(manager.ActiveSurface.TryGetPose(center, out var pose));
            var placed = ToolkitObjectFactory.SpawnPlaceable(manager.Config.Placeables[0], pose,
                manager.PlacedObjectsRoot, manager.Config.ScaleLimits, manager.Theme);
            manager.Registry.Add(placed);
            var interaction = manager.GetComponent<InteractionController>();
            var root = Object.FindAnyObjectByType<ARToolkitHUD>().GetComponent<UIDocument>().rootVisualElement;
            var panel = root.Q("edit-panel");

            interaction.Select(placed);
            yield return null;
            Assert.IsFalse(panel.ClassListContains("hidden"), "Edit panel should open for the selected object.");
            Assert.IsFalse(root.Q<Button>("edit-delete").ClassListContains("hidden"), "Placed objects can be deleted.");

            root.Q<Slider>("edit-scale").value = 2f;
            yield return null;
            Assert.AreEqual(2f, placed.Manipulator.ScaleMultiplier, 0.001f);
            StringAssert.Contains("Scale 2.00x", root.Q<Label>("edit-values").text);

            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine("Temp", "ErasmusEditPanel.png"), shot.EncodeToPNG());
            Object.Destroy(shot);

            interaction.ClearSelection();
            yield return null;
            Assert.IsTrue(panel.ClassListContains("hidden"));
            manager.ClearPlacedObjects();
        }

        [UnityTest]
        public IEnumerator Reticle_ShowsWhereObjectsCanBePlaced()
        {
            yield return null;
            var reticle = Object.FindAnyObjectByType<PlacementReticle>();
            Assert.IsNotNull(reticle);
            Assert.IsTrue(reticle.IsShown, "The screen center looks at the floor, so the reticle should show.");
        }

        [Test]
        public void ImageContentWithoutPrefab_ShowsItsLabelAndOutline()
        {
            var anchor = new GameObject("Test Image Anchor").transform;
            try
            {
                var item = new ImageTargetItem("LabelOnly", null, 0.2f, null, new InfoLabelData("Title", "Body"));
                var content = ToolkitObjectFactory.SpawnImageContent(item, anchor, new Vector2(0.2f, 0.1f), manager.Theme);
                Assert.IsNotNull(content.Label);
                Assert.IsTrue(content.Label.IsVisible, "Without a prefab the label is the content, so it must show.");
                Assert.IsNotNull(anchor.GetComponentInChildren<LineRenderer>(), "Detected images get an outline.");
            }
            finally
            {
                Object.Destroy(anchor.gameObject);
            }
        }

        [Test]
        public void InfoLabels_UseTextMeshPro()
        {
            var labels = Object.FindObjectsByType<InfoLabel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsNotEmpty(labels, "The simulated sample marker has a label.");
            foreach (var label in labels)
            {
                var text = label.GetComponentInChildren<TMPro.TextMeshPro>(true);
                Assert.IsNotNull(text);
                Assert.IsFalse(string.IsNullOrEmpty(text.text));
            }
        }

        [Test]
        public void DesktopSurface_PlacesObjectAtScreenCenter_WithLabelAndCollider()
        {
            var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Assert.IsTrue(manager.ActiveSurface.TryGetPose(screenCenter, out var pose), "Center of screen should hit the ground.");
            Assert.AreEqual(0f, pose.position.y, 0.001f);

            var item = manager.Config.Placeables[0];
            var placed = ToolkitObjectFactory.SpawnPlaceable(item, pose, manager.PlacedObjectsRoot,
                manager.Config.ScaleLimits, manager.Theme);
            manager.Registry.Add(placed);

            Assert.AreEqual(1, manager.Registry.Count);
            Assert.IsTrue(placed.IsManipulatable);
            Assert.IsNotNull(placed.GetComponentInChildren<Collider>());
            Assert.AreEqual(item.Info.HasContent, placed.Label != null);

            placed.Manipulator.SetScaleMultiplier(100f);
            Assert.AreEqual(manager.Config.ScaleLimits.y, placed.Manipulator.ScaleMultiplier, 0.0001f);

            manager.ClearPlacedObjects();
            Assert.AreEqual(0, manager.Registry.Count);
        }

        [Test]
        public void ImageTargets_AreSimulatedOnTheGround()
        {
            var simulator = Object.FindAnyObjectByType<DesktopImageTargetSimulator>();
            Assert.IsNotNull(simulator);

            // With a webcam and recognition (Windows) the floor copies are hidden and the real image is
            // recognised in the webcam picture; otherwise they lie visibly on the floor.
            var includeHidden = simulator.IsRecognisingWebcamImages ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            var simulated = Object.FindObjectsByType<ToolkitObject>(includeHidden, FindObjectsSortMode.None)
                .Where(o => !o.IsPlaced)
                .ToArray();
            var expected = manager.Config.ImageTargets.Count(t => t != null && t.Texture != null);
            Assert.AreEqual(expected, simulated.Length);
            if (simulator.IsRecognisingWebcamImages)
            {
                Assert.IsTrue(simulated.All(o => !o.gameObject.activeInHierarchy), "Floor copies hide while the webcam recognises images.");
            }
            foreach (var content in simulated)
            {
                Assert.IsTrue(content.IsManipulatable, "Image content can be edited (scale, rotate, raise).");
                Assert.IsFalse(content.Manipulator.CanMove, "Image content stays on its image.");
            }
        }

        [UnityTest]
        public IEnumerator Screenshot_WithHud_IsCaptured()
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Assert.Greater(texture.width, 0);
                Directory.CreateDirectory("Temp");
                File.WriteAllBytes(Path.Combine("Temp", "ErasmusDemoScreenshot.png"), texture.EncodeToPNG());
            }
            finally
            {
                Object.Destroy(texture);
            }
        }
    }
}
