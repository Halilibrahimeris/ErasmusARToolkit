using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// The shared parts of the project modules in the demo scene (Desktop mode): HUD buttons, placement,
    /// the camera image switch and info cards. Requires the demo scene in Build Settings.
    /// </summary>
    public class ModulePlayModeTests
    {
        const string DemoSceneName = "ARToolkitDemo";
        const float StartupTimeoutSeconds = 3f;

        ARToolkitManager manager;
        ARToolkitHUD hud;
        GameObject module;

        [UnitySetUp]
        public IEnumerator LoadDemoScene()
        {
            if (!Application.CanStreamedLevelBeLoaded(DemoSceneName))
            {
                Assert.Ignore("Demo scene is not in Build Settings. Run 'Generate Demo Scene' first.");
            }

            yield return SceneManager.LoadSceneAsync(DemoSceneName, LoadSceneMode.Single);
            manager = Object.FindAnyObjectByType<ARToolkitManager>();
            hud = Object.FindAnyObjectByType<ARToolkitHUD>();
            var timeout = Time.realtimeSinceStartup + StartupTimeoutSeconds;
            while (manager.Mode == ToolkitMode.Undetermined && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (module != null)
            {
                Object.Destroy(module);
            }

            if (manager != null && manager.CameraImage != null)
            {
                manager.CameraImage.Show();
            }
        }

        static GameObject NewModule(string name, out GameObject content)
        {
            var root = new GameObject(name);
            content = GameObject.CreatePrimitive(PrimitiveType.Cube);
            content.transform.SetParent(root.transform, false);
            content.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            return root;
        }

        [UnityTest]
        public IEnumerator TapToPlace_AddsAHudButton_WaitsForATap_ThenStandsOnTheGround()
        {
            module = NewModule("Portal Test", out var content);
            module.transform.position = new Vector3(3f, 2f, 3f);
            var placement = module.AddComponent<ModulePlacement>();
            placement.PlacementMode = PlacementMode.TapToPlace;
            Assert.IsFalse(content.activeSelf, "The content is hidden until it is placed.");
            yield return null;

            Assert.IsTrue(placement.IsModeReady);
            Assert.IsTrue(placement.IsAwaitingTap);
            Assert.IsTrue(manager.Interaction.IsTapCaptured, "The next tap goes to the module.");
            var button = hud.Modules.Items.OfType<Button>().FirstOrDefault();
            Assert.IsNotNull(button, "The module has a button in the HUD strip.");
            Assert.AreEqual(PlacementText.Cancel, button.text);
            Assert.IsTrue(hud.HasModuleParts);

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            ((ITapHandler)placement).OnTap(center);

            Assert.IsTrue(placement.IsPlaced);
            Assert.IsFalse(placement.IsAwaitingTap);
            Assert.IsFalse(manager.Interaction.IsTapCaptured);
            Assert.IsTrue(content.activeSelf);
            Assert.AreEqual(0f, module.transform.position.y, 0.001f, "It stands on the virtual ground.");
            var toCamera = manager.ActiveCamera.transform.position - module.transform.position;
            toCamera.y = 0f;
            Assert.Greater(Vector3.Dot(module.transform.forward, toCamera.normalized), 0.99f, "Its front faces the user.");
            Assert.AreEqual("Move Portal Test", button.text);
        }

        [UnityTest]
        public IEnumerator AtAppStart_InDesktopMode_StaysWhereItIsInTheScene()
        {
            module = NewModule("Design Test", out var content);
            module.transform.position = new Vector3(1f, 0f, 2f);
            module.AddComponent<ModulePlacement>();
            yield return null;
            yield return null;

            var placement = module.GetComponent<ModulePlacement>();
            Assert.IsTrue(placement.IsPlaced);
            Assert.IsTrue(content.activeSelf);
            Assert.AreEqual(new Vector3(1f, 0f, 2f), module.transform.position);
            Assert.AreEqual(FloorSource.VirtualGround, placement.FloorSource);
            Assert.IsFalse(hud.Modules.Items.Any(), "On the PC there is nothing to move: no button.");
        }

        [UnityTest]
        public IEnumerator Module_SwitchedOff_TakesItsButtonAway()
        {
            module = NewModule("Story Test", out _);
            var placement = module.AddComponent<ModulePlacement>();
            placement.PlacementMode = PlacementMode.TapToPlace;
            yield return null;
            Assert.AreEqual(1, hud.Modules.Items.Count);

            module.SetActive(false);
            Assert.AreEqual(0, hud.Modules.Items.Count);
            Assert.IsFalse(manager.Interaction.IsTapCaptured);
        }

        [UnityTest]
        public IEnumerator CameraImage_Hide_ShowsTheSky_Show_BringsTheCameraBack()
        {
            var camera = manager.ActiveCamera;
            var color = camera.backgroundColor;
            var sky = new Color(0.4f, 0.6f, 0.9f);
            manager.CameraImage.Hide(sky);
            yield return null;

            Assert.IsTrue(manager.CameraImage.IsHidden);
            Assert.AreEqual(sky, camera.backgroundColor);
            Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags);
            Assert.IsTrue(manager.CameraFeed.IsImageHidden);

            manager.CameraImage.Show();
            Assert.IsFalse(manager.CameraImage.IsHidden);
            Assert.AreEqual(color, camera.backgroundColor);
            Assert.IsFalse(manager.CameraFeed.IsImageHidden);
        }

        [UnityTest]
        public IEnumerator Design_WalksFromTheStartCamera_AndSwitchesViews()
        {
            module = new GameObject("Design Test");
            module.SetActive(false);
            var placement = module.AddComponent<ModulePlacement>();
            var design = module.AddComponent<DesignSpace>();
            var start = new GameObject("Start Camera").AddComponent<DesignStartPoint>();
            start.transform.SetParent(module.transform, false);
            start.transform.localPosition = new Vector3(0.5f, 1.5f, -1f);
            start.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.transform.SetParent(module.transform, false);
            var sky = new Color(0.3f, 0.5f, 0.8f);
            placement.Configure(PlacementMode.AtAppStart, start.transform, true);
            design.Configure(new Vector3(4f, 3f, 4f), sky, start, shell);
            module.SetActive(true);
            yield return null;
            yield return null;

            var walker = manager.ActiveCamera.GetComponent<DesktopCameraController>();
            Assert.IsTrue(design.IsModeReady);
            Assert.IsTrue(walker.IsWalking, "On the PC the camera walks through the design.");
            Assert.Less(Vector3.Distance(start.transform.position, manager.ActiveCamera.transform.position), 0.01f);
            Assert.AreEqual(DesignView.AR, design.View);
            Assert.IsFalse(shell.activeSelf, "The room shell belongs to the Virtual view.");
            Assert.IsTrue(hud.Modules.Items.OfType<Button>().Any(b => b.text == DesignText.ViewButton(DesignView.AR)));

            design.SetView(DesignView.Virtual);
            Assert.IsTrue(shell.activeSelf);
            Assert.IsTrue(manager.CameraImage.IsHidden);
            Assert.AreEqual(sky, manager.ActiveCamera.backgroundColor);

            design.SetPeeking(true);
            Assert.IsFalse(manager.CameraImage.IsHidden, "Holding View shows the room.");
            design.SetPeeking(false);
            Assert.IsTrue(manager.CameraImage.IsHidden);

            design.SetView(DesignView.AR);
            Assert.IsFalse(manager.CameraImage.IsHidden);
            Assert.IsFalse(shell.activeSelf);

            module.SetActive(false);
            Assert.IsFalse(walker.IsWalking, "Switching the design off gives the orbit camera back.");
        }

        [UnityTest]
        public IEnumerator Poster_SoundPlaysWhileTheImageIsSeen_AndGoesOnAfterwards()
        {
            var sound = AudioClip.Create("Narration", 44100 * 4, 1, 44100, false);
            var item = new ImageTargetItem("Sound Poster", null, 0.3f, null, new InfoLabelData("Poster", "Listen."),
                new PosterMedia(null, sound));
            module = new GameObject("Simulated Image");
            var content = ToolkitObjectFactory.SpawnImageContent(item, module.transform, new Vector2(0.3f, 0.4f), manager.Theme);
            yield return null;

            var surface = content.GetComponentInChildren<VideoSurface>();
            Assert.IsNotNull(surface, "Poster media gets a player.");
            Assert.IsTrue(surface.WantsPlaying);
            Assert.IsTrue(surface.IsPlaying);

            content.gameObject.SetActive(false);
            Assert.IsFalse(surface.IsPlaying, "The sound stops when the image is lost.");
            Assert.IsTrue(surface.WantsPlaying);

            content.gameObject.SetActive(true);
            yield return null;
            Assert.IsTrue(surface.IsPlaying, "It goes on when the image is seen again.");

            manager.Interaction.Select(content);
            var root = hud.GetComponent<UIDocument>().rootVisualElement;
            var media = root.Q<Button>("edit-media");
            Assert.IsFalse(media.ClassListContains(HudView.HiddenClass), "The panel offers Pause.");
            Assert.AreEqual("Pause", media.text);
            surface.TogglePlay();
            Assert.IsFalse(surface.IsPlaying);
            manager.Interaction.ClearSelection();
            Object.Destroy(sound);
        }

        [UnityTest]
        public IEnumerator VirtualWall_TakesPictures_TheGroundStaysTheGround()
        {
            var surface = Object.FindAnyObjectByType<DesktopPlacementSurface>();
            var wall = VirtualWall.Create(surface.transform, manager.Theme, 0f);
            module = wall.gameObject;
            surface.SetWall(wall);
            yield return null;

            try
            {
                var camera = manager.ActiveCamera;
                var onWall = (Vector2)camera.WorldToScreenPoint(new Vector3(0.3f, 1.2f, 2f));
                Assert.IsTrue(surface.TryGetWallPose(onWall, out var pose));
                Assert.AreEqual(2f, pose.position.z, 0.01f);
                Assert.AreEqual(1.2f, pose.position.y, 0.01f);
                Assert.Less((pose.rotation * Vector3.forward).z, -0.99f, "The picture faces the room.");

                var onGround = (Vector2)camera.WorldToScreenPoint(Vector3.zero);
                Assert.IsTrue(surface.TryGetPose(onGround, out var ground));
                Assert.AreEqual(0f, ground.position.y, 0.001f);
            }
            finally
            {
                surface.SetWall(null);
            }
        }

        [UnityTest]
        public IEnumerator Route_FoundStops_ShowProgress_AndAnArrow()
        {
            module = new GameObject("Route Test");
            var tracker = module.AddComponent<RouteTracker>();
            yield return null;

            var first = new ImageTargetItem("Entrance", null, 0.2f, null, new InfoLabelData("Entrance", ""), null,
                new RouteStop(1, "Library", RouteArrow.Right));
            var second = new ImageTargetItem("Library", null, 0.2f, null, new InfoLabelData("Library", ""), null,
                new RouteStop(2, "", RouteArrow.None));
            tracker.UseStops(new[] { first, second });

            var anchor = new GameObject("Stop Anchor");
            anchor.transform.SetParent(module.transform, false);
            var content = ToolkitObjectFactory.SpawnImageContent(first, anchor.transform, new Vector2(0.2f, 0.2f), manager.Theme);
            ImageTargetEvents.RaiseFound(first, content);

            Assert.AreEqual(1, tracker.Progress.FoundCount);
            StringAssert.Contains("1 / 2", hud.Modules.InfoText);
            StringAssert.Contains("Next: Library (right)", hud.Modules.InfoText);
            var view = content.GetComponentInChildren<RouteStopView>();
            Assert.IsNotNull(view, "The stop shows the way on.");
            Assert.IsNotNull(view.GetComponentInChildren<MeshRenderer>(), "An arrow.");

            ImageTargetEvents.RaiseFound(second, null);
            Assert.IsTrue(tracker.Progress.IsComplete);
            StringAssert.Contains("complete", hud.Modules.InfoText);
        }

        [UnityTest]
        public IEnumerator SimulatedImage_InView_IsFound()
        {
            var simulator = Object.FindAnyObjectByType<DesktopImageTargetSimulator>();
            if (!simulator.ShowsFloorImages)
            {
                Assert.Ignore("A webcam is on: images are recognised in its picture instead of lying on the ground.");
            }

            var timeout = Time.realtimeSinceStartup + 2f;
            while (!simulator.IsSeen("SampleMarker") && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.IsTrue(simulator.IsSeen("SampleMarker"), "The sample marker lies in view of the Desktop camera.");
        }

        [UnityTest]
        public IEnumerator Vision_ButtonCyclesFilters_NormalDrawsNothing()
        {
            module = new GameObject("Vision Test");
            var simulator = module.AddComponent<VisionSimulator>();
            simulator.SetFilters(new[]
            {
                new VisionFilterEntry(VisionFilterKind.Protanopia),
                new VisionFilterEntry(VisionFilterKind.TunnelVision, true, "Only the middle."),
            });
            yield return null;

            Assert.IsNotNull(simulator.View, "The filter quad sits in front of the camera.");
            Assert.IsFalse(simulator.View.IsDrawing);
            Assert.IsTrue(hud.Modules.Items.OfType<Button>().Any(b => b.text == "Vision: Normal"));

            simulator.NextFilter();
            yield return null;
            Assert.AreEqual(VisionFilterKind.Protanopia, simulator.Current);
            Assert.IsTrue(simulator.View.IsDrawing);
            StringAssert.Contains("Protanopia", hud.Modules.InfoText);

            simulator.NextFilter();
            Assert.AreEqual("Only the middle.", hud.Modules.InfoText);
            simulator.NextFilter();
            Assert.AreEqual(VisionFilterKind.Normal, simulator.Current);
            Assert.IsFalse(simulator.View.IsDrawing);
            Assert.AreEqual(string.Empty, hud.Modules.InfoText);
        }

        [UnityTest]
        public IEnumerator Vision_EyeHeight_LiftsTheDesign_InTheVirtualViewOnly()
        {
            module = new GameObject("Design And Vision");
            var designObject = new GameObject("Design");
            designObject.transform.SetParent(module.transform, false);
            designObject.SetActive(false);
            var placement = designObject.AddComponent<ModulePlacement>();
            var design = designObject.AddComponent<DesignSpace>();
            placement.Configure(PlacementMode.AtAppStart, null, false);
            design.Configure(new Vector3(4f, 3f, 4f), Color.gray, null, null);
            designObject.SetActive(true);
            yield return null;

            var simulator = module.AddComponent<VisionSimulator>();
            yield return null;

            simulator.SetEye(EyeHeight.Wheelchair);
            Assert.AreEqual(0f, designObject.transform.position.y, 0.001f, "The AR view keeps the design in the room.");

            design.SetView(DesignView.Virtual);
            Assert.AreEqual(0.3f, designObject.transform.position.y, 0.001f, "From 1.2 m the world looks 0.3 m higher.");

            simulator.SetEye(EyeHeight.Child);
            Assert.AreEqual(0.5f, designObject.transform.position.y, 0.001f);

            design.SetView(DesignView.AR);
            Assert.AreEqual(0f, designObject.transform.position.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator RoomFill_RisesAfterStart_PausesAndFollowsTheSlider()
        {
            module = new GameObject("Fill Test");
            module.SetActive(false);
            var placement = module.AddComponent<ModulePlacement>();
            var fill = module.AddComponent<RoomFill>();
            placement.Configure(PlacementMode.AtAppStart, null, true);
            fill.Configure(FillKind.Water, 1f, 2f, new Color(0.2f, 0.5f, 0.9f, 0.5f), null, new Vector2(3f, 3f), "Water", "Full!");
            module.SetActive(true);
            yield return null;
            yield return null;

            Assert.IsTrue(placement.IsPlaced);
            Assert.IsNotNull(fill.Area);
            Assert.IsTrue(hud.Modules.Items.OfType<Slider>().Any(), "A slider sets the level by hand.");
            StringAssert.Contains("Water · 0 cm", hud.Modules.InfoText);

            fill.Play();
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(fill.Level, 0.05f);
            var surface = module.transform.Find("Water Surface");
            Assert.IsNotNull(surface);
            Assert.AreEqual(fill.Level, surface.localPosition.y, 0.001f);

            fill.Pause();
            var paused = fill.Level;
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(paused, fill.Level, 0.0001f);

            fill.SetLevel(0.8f);
            StringAssert.Contains("80 cm", hud.Modules.InfoText);
        }

        [UnityTest]
        public IEnumerator RoomFill_Objects_PileUpInLayers()
        {
            module = new GameObject("Pile Test");
            module.SetActive(false);
            var placement = module.AddComponent<ModulePlacement>();
            var fill = module.AddComponent<RoomFill>();
            placement.Configure(PlacementMode.AtAppStart, null, true);
            fill.Configure(FillKind.Objects, 0.5f, 0f, Color.cyan, null, new Vector2(1f, 1f), "Bottles", "");
            module.SetActive(true);
            yield return null;
            yield return null;

            fill.SetLevel(0.1f);
            var few = fill.ObjectCount;
            fill.SetLevel(0.4f);
            Assert.Greater(few, 0);
            Assert.Greater(fill.ObjectCount, few);
        }

        [UnityTest]
        public IEnumerator Portal_WalkingThroughTheDoor_GoesInside_AndBackOut()
        {
            module = new GameObject("Portal Test");
            module.SetActive(false);
            module.transform.position = new Vector3(0f, 0f, 4f);
            var placement = module.AddComponent<ModulePlacement>();
            var portal = module.AddComponent<PortalController>();
            placement.Configure(PlacementMode.AtAppStart, null, true);
            portal.Configure(1.2f, 2.2f, new Vector3(6f, 3f, 6f), Color.cyan, null);
            module.SetActive(true);
            yield return null;
            yield return null;

            var camera = manager.ActiveCamera.transform;
            Assert.IsTrue(placement.IsPlaced);
            Assert.AreEqual(PortalState.Outside, portal.State);
            Assert.IsTrue(portal.Shell.activeSelf, "Outside, the mask hides the world except through the door.");
            Assert.IsFalse(portal.DoorOccluder.activeSelf);
            Assert.IsTrue(camera.GetComponent<DesktopCameraController>().IsWalking, "On the PC the camera walks.");
            Assert.Less(Vector3.Distance(camera.position, module.transform.TransformPoint(new Vector3(0f, 1.5f, 3f))), 0.01f,
                "The camera starts a few steps in front of the door.");

            camera.position = module.transform.TransformPoint(new Vector3(0f, 1.5f, -1f));
            yield return null;
            yield return null;
            Assert.AreEqual(PortalState.Inside, portal.State);
            Assert.IsFalse(portal.Shell.activeSelf);
            Assert.IsTrue(portal.DoorOccluder.activeSelf, "Inside, the real room shows only through the door.");
            StringAssert.Contains(PortalText.Line(PortalState.Inside), hud.Modules.InfoText);

            camera.position = module.transform.TransformPoint(new Vector3(0f, 1.5f, 2f));
            yield return null;
            yield return null;
            Assert.AreEqual(PortalState.Outside, portal.State);
            Assert.IsTrue(portal.Shell.activeSelf);
        }

        [UnityTest]
        public IEnumerator Story_CharactersTalkWalkAndWaitForATap()
        {
            module = new GameObject("Story Test");
            module.SetActive(false);
            module.transform.position = new Vector3(0f, 0f, 3f);
            var placement = module.AddComponent<ModulePlacement>();
            placement.Configure(PlacementMode.AtAppStart, null, true);
            var stage = module.AddComponent<StoryStage>();
            var ayse = new GameObject("Ayse").AddComponent<StoryCharacter>();
            ayse.transform.SetParent(module.transform, false);
            ayse.Configure("Ayse", 2f);
            var door = new GameObject("Door").AddComponent<StoryPoint>();
            door.transform.SetParent(module.transform, false);
            door.transform.localPosition = new Vector3(1f, 0f, 0f);
            stage.SetSteps(new[]
            {
                new StoryStep(ayse, StoryAction.Say, "Hello!", seconds: 1f),
                new StoryStep(ayse, StoryAction.WalkTo, point: door),
                new StoryStep(ayse, StoryAction.TurnToUser),
                new StoryStep(ayse, StoryAction.Say, "Tap to go on.", StoryAdvance.Tap, seconds: 0.1f),
            });
            module.SetActive(true);
            yield return null;
            yield return null;

            Assert.IsNotNull(stage.Runner, "The story starts once it is placed.");
            Assert.AreEqual(0, stage.Runner.Index);
            Assert.AreEqual("Ayse: Hello!", hud.Modules.SubtitleText);
            Assert.IsTrue(hud.Modules.Items.OfType<Button>().Any(b => b.text == StoryText.Next));

            var timeout = Time.realtimeSinceStartup + 6f;
            while (stage.Runner.Index < 3 && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.AreEqual(3, stage.Runner.Index, "Say, walk and turn follow each other.");
            var flat = ayse.transform.position - door.transform.position;
            flat.y = 0f;
            Assert.Less(flat.magnitude, 0.3f, "Ayse walked to the door.");

            timeout = Time.realtimeSinceStartup + 2f;
            while (stage.Runner.Phase != StoryPhase.WaitingForTap && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.AreEqual(StoryPhase.WaitingForTap, stage.Runner.Phase);
            Assert.IsTrue(manager.Interaction.IsTapCaptured, "A tap on the scene goes on.");
            stage.Next();
            yield return null;
            Assert.AreEqual(StoryPhase.Finished, stage.Runner.Phase);
            Assert.IsTrue(string.IsNullOrEmpty(hud.Modules.SubtitleText), "The line is gone.");
        }

        [UnityTest]
        public IEnumerator InfoCard_OpensAReadOnlyPanel()
        {
            module = GameObject.CreatePrimitive(PrimitiveType.Cube);
            module.name = "Chair";
            module.transform.position = new Vector3(0f, 0.5f, 1f);
            var card = module.AddComponent<InfoCard>();
            card.SetInfo("Chair", "A place to sit.");
            yield return null;

            Assert.IsNotNull(card.ToolkitObject);
            manager.Interaction.Select(card.ToolkitObject);
            var root = hud.GetComponent<UIDocument>().rootVisualElement;
            Assert.IsFalse(root.Q("edit-panel").ClassListContains(HudView.HiddenClass));
            Assert.AreEqual("Chair", root.Q<Label>("edit-title").text);
            Assert.IsTrue(root.Q("edit-scale").ClassListContains(HudView.HiddenClass), "Nothing to scale.");
            Assert.IsTrue(root.Q("edit-delete").ClassListContains(HudView.HiddenClass), "Nothing to delete.");
            Assert.IsTrue(card.ToolkitObject.IsLabelVisible, "The card opens with the panel.");

            manager.Interaction.ClearSelection();
            Assert.IsFalse(card.ToolkitObject.IsLabelVisible);
        }
    }
}
