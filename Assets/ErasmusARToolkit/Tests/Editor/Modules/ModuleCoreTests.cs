using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Tests
{
    public class ModulePlacementMathTests
    {
        const float Delta = 0.0001f;

        [Test]
        public void YawOnly_KeepsHeading_DropsPitchAndRoll()
        {
            var tilted = Quaternion.Euler(-35f, 70f, 12f);
            var yaw = ModulePlacementMath.YawOnly(tilted);
            Assert.AreEqual(70f, yaw.eulerAngles.y, 0.01f);
            Assert.AreEqual(0f, Vector3.Angle(yaw * Vector3.up, Vector3.up), 0.01f);
        }

        [Test]
        public void YawOnly_LookingStraightDown_UsesTheTopOfTheView()
        {
            var down = Quaternion.Euler(90f, 30f, 0f);
            var yaw = ModulePlacementMath.YawOnly(down);
            Assert.AreEqual(30f, yaw.eulerAngles.y, 0.01f);
        }

        [Test]
        public void AtAppStart_WithoutStartPoint_PutsTheRootUnderTheCamera_OnTheFloor()
        {
            var pose = ModulePlacementMath.AtAppStart(new Vector3(1f, 1.4f, 2f), Quaternion.Euler(10f, 90f, 0f), -0.1f,
                new Pose(Vector3.zero, Quaternion.identity));
            Assert.AreEqual(1f, pose.position.x, Delta);
            Assert.AreEqual(-0.1f, pose.position.y, Delta);
            Assert.AreEqual(2f, pose.position.z, Delta);
            Assert.AreEqual(90f, pose.rotation.eulerAngles.y, 0.01f);
        }

        [Test]
        public void AtAppStart_StartPoint_LandsWhereThePhoneIs_LookingTheSameWay()
        {
            // The start point stands 3 m into the design and looks to its right (+X).
            var startLocal = new Pose(new Vector3(0f, 1.5f, 3f), Quaternion.Euler(0f, 90f, 0f));
            var camera = new Vector3(-2f, 1.3f, 5f);
            var cameraRotation = Quaternion.Euler(0f, 180f, 0f);
            var root = ModulePlacementMath.AtAppStart(camera, cameraRotation, 0f, startLocal);

            var start = ModulePlacementMath.Follow(root, startLocal);
            Assert.AreEqual(camera.x, start.position.x, Delta);
            Assert.AreEqual(camera.z, start.position.z, Delta);
            Assert.AreEqual(0f, root.position.y, Delta, "The root stands on the floor.");
            Assert.AreEqual(180f, start.rotation.eulerAngles.y, 0.01f, "The start point looks where the phone looks.");
        }

        [Test]
        public void TapToPlace_FaceUser_TurnsTheFrontToTheCamera()
        {
            var pose = ModulePlacementMath.TapToPlace(new Vector3(0f, 0f, 2f), new Vector3(0f, 1.5f, 0f), true);
            var front = pose.rotation * Vector3.forward;
            Assert.AreEqual(-1f, front.z, Delta);
            Assert.AreEqual(0f, front.y, Delta);
            Assert.AreEqual(new Vector3(0f, 0f, 2f), pose.position);
        }

        [Test]
        public void TapToPlace_NotFacingUser_KeepsTheSceneRotation()
        {
            var pose = ModulePlacementMath.TapToPlace(new Vector3(1f, 0f, 2f), Vector3.zero, false);
            Assert.AreEqual(Quaternion.identity, pose.rotation);
        }

        [Test]
        public void LocalOffset_ThenFollow_GivesTheWorldPoseBack()
        {
            var holder = new Pose(new Vector3(1f, 0.2f, -3f), Quaternion.Euler(0f, 37f, 0f));
            var world = new Pose(new Vector3(2f, 0f, -1f), Quaternion.Euler(0f, 120f, 0f));
            var offset = ModulePlacementMath.LocalOffset(holder, world);
            var back = ModulePlacementMath.Follow(holder, offset);
            Assert.AreEqual(0f, Vector3.Distance(world.position, back.position), Delta);
            Assert.AreEqual(0f, Quaternion.Angle(world.rotation, back.rotation), 0.01f);
        }

        [Test]
        public void Follow_MovesWithTheHolder()
        {
            var holder = new Pose(Vector3.zero, Quaternion.identity);
            var offset = ModulePlacementMath.LocalOffset(holder, new Pose(new Vector3(0f, 0f, 1f), Quaternion.identity));
            var moved = new Pose(new Vector3(0f, 0.05f, 0f), Quaternion.Euler(0f, 90f, 0f));
            var pose = ModulePlacementMath.Follow(moved, offset);
            Assert.AreEqual(1f, pose.position.x, Delta);
            Assert.AreEqual(0.05f, pose.position.y, Delta);
            Assert.AreEqual(0f, pose.position.z, Delta);
        }

        [Test]
        public void Relative_IsInMeters_WhateverTheRootScale()
        {
            var root = new GameObject("Root").transform;
            var child = new GameObject("Child").transform;
            try
            {
                root.localScale = Vector3.one * 2f;
                child.SetParent(root, false);
                child.localPosition = new Vector3(0f, 0f, 1f);
                var relative = ModulePlacementMath.Relative(root, child);
                Assert.AreEqual(2f, relative.position.z, Delta);
                Assert.AreEqual(Vector3.zero, ModulePlacementMath.Relative(root, null).position);
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [Test]
        public void PlacementText_MoveButton_SaysWhatMoves()
        {
            Assert.AreEqual("Design: Move Here", PlacementText.MoveButton("Design", PlacementMode.AtAppStart));
            Assert.AreEqual("Move Portal", PlacementText.MoveButton("Portal", PlacementMode.TapToPlace));
        }
    }

    public class FloorFinderTests
    {
        static FloorFinder.PlaneSample Floor(float y, float area = 4f, bool up = true, bool tracking = true,
            bool subsumed = false)
        {
            return new FloorFinder.PlaneSample(y, up, tracking, subsumed, area);
        }

        [Test]
        public void Resolve_ScanFloor_BeatsEverythingElse()
        {
            var floor = FloorFinder.Resolve(-1.2f, -1.4f, 0f, 0f, 1.5f);
            Assert.AreEqual(-1.2f, floor.Height);
            Assert.AreEqual(FloorSource.Scan, floor.Source);
            Assert.IsTrue(floor.IsMeasured);
        }

        [Test]
        public void Resolve_NoScan_UsesThePlane()
        {
            var floor = FloorFinder.Resolve(float.NaN, -1.4f, null, 0f, 1.5f);
            Assert.AreEqual(FloorSource.Plane, floor.Source);
            Assert.AreEqual(-1.4f, floor.Height);
        }

        [Test]
        public void Resolve_Desktop_UsesTheVirtualGround()
        {
            var floor = FloorFinder.Resolve(float.NaN, float.NaN, 0.25f, 1.6f, 1.5f);
            Assert.AreEqual(FloorSource.VirtualGround, floor.Source);
            Assert.AreEqual(0.25f, floor.Height);
        }

        [Test]
        public void Resolve_NothingKnown_AssumesThePhoneHeight()
        {
            var floor = FloorFinder.Resolve(float.NaN, float.NaN, null, 1.6f, 1.5f);
            Assert.AreEqual(0.1f, floor.Height, 0.0001f);
            Assert.AreEqual(FloorSource.Fallback, floor.Source);
            Assert.IsFalse(floor.IsMeasured);
        }

        [Test]
        public void LowestFloorPlane_TakesTheLowestPlaneBelowTheCamera()
        {
            var planes = new List<FloorFinder.PlaneSample> { Floor(-0.7f), Floor(-1.45f), Floor(-1.3f) };
            Assert.AreEqual(-1.45f, FloorFinder.LowestFloorPlane(planes, 0f), 0.0001f);
        }

        [Test]
        public void LowestFloorPlane_SkipsPlanesThatAreNotTheFloor()
        {
            var planes = new List<FloorFinder.PlaneSample>
            {
                Floor(0.5f),                        // above the camera
                Floor(-1f, up: false),               // a ceiling
                Floor(-1.1f, tracking: false),
                Floor(-1.2f, subsumed: true),
                Floor(-1.3f, area: 0.1f),           // a small box
                Floor(-4f),                          // a reflection far below
                Floor(-0.8f),
            };
            Assert.AreEqual(-0.8f, FloorFinder.LowestFloorPlane(planes, 0f), 0.0001f);
        }

        [Test]
        public void LowestFloorPlane_NoCandidates_IsNaN()
        {
            Assert.IsTrue(float.IsNaN(FloorFinder.LowestFloorPlane(new List<FloorFinder.PlaneSample>(), 0f)));
        }
    }

    public class TapRoutingTests
    {
        sealed class Handler : ITapHandler
        {
            public bool Wants = true;
            public int Taps;
            public bool WantsTap => Wants;
            public void OnTap(Vector2 screenPosition) => Taps++;
        }

        [Test]
        public void TopWanting_Empty_IsNull()
        {
            Assert.IsNull(new TapHandlerStack().TopWanting());
        }

        [Test]
        public void TopWanting_TheLastAddedHandlerWins()
        {
            var stack = new TapHandlerStack();
            var first = new Handler();
            var second = new Handler();
            stack.Push(first);
            stack.Push(second);
            Assert.AreSame(second, stack.TopWanting());
        }

        [Test]
        public void TopWanting_SkipsHandlersThatDoNotWantTaps()
        {
            var stack = new TapHandlerStack();
            var first = new Handler();
            var second = new Handler { Wants = false };
            stack.Push(first);
            stack.Push(second);
            Assert.AreSame(first, stack.TopWanting());
            first.Wants = false;
            Assert.IsNull(stack.TopWanting());
        }

        [Test]
        public void Push_AgainMovesTheHandlerToTheTop()
        {
            var stack = new TapHandlerStack();
            var first = new Handler();
            var second = new Handler();
            stack.Push(first);
            stack.Push(second);
            stack.Push(first);
            Assert.AreSame(first, stack.TopWanting());
            Assert.AreEqual(2, stack.Count);
        }

        [Test]
        public void Remove_UncoversTheNextHandler()
        {
            var stack = new TapHandlerStack();
            var first = new Handler();
            var second = new Handler();
            stack.Push(first);
            stack.Push(second);
            stack.Remove(second);
            Assert.AreSame(first, stack.TopWanting());
        }

        [Test]
        public void TopWanting_DropsDestroyedComponents()
        {
            var stack = new TapHandlerStack();
            var go = new GameObject("Handler");
            stack.Push(go.AddComponent<ModulePlacement>());
            Object.DestroyImmediate(go);
            Assert.IsNull(stack.TopWanting());
            Assert.AreEqual(0, stack.Count);
        }
    }

    public class InfoCardTests
    {
        [Test]
        public void Title_PrefersTheCardTitle_ThenTheObjectName()
        {
            Assert.AreEqual("Chair", InfoCardText.Title("  Chair ", "Cube (3)"));
            Assert.AreEqual("Cube (3)", InfoCardText.Title("  ", "Cube (3)"));
        }

        [Test]
        public void SetInfo_FillsTheCard()
        {
            var go = new GameObject("Painting");
            try
            {
                var card = go.AddComponent<InfoCard>();
                Assert.IsFalse(card.Info.HasContent);
                card.SetInfo("Mona", "Oil on wood.", true);
                Assert.IsTrue(card.Info.HasContent);
                Assert.AreEqual("Mona", card.Info.Title);
                Assert.IsTrue(card.Info.ShowOnStart);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AttachInfoCard_MakesAReadOnlyObject_WithACollider()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            try
            {
                var toolkitObject = ToolkitObjectFactory.AttachInfoCard(go, "Cube", new InfoLabelData(), null);
                Assert.IsFalse(toolkitObject.IsManipulatable, "Nothing to scale, turn or move.");
                Assert.IsFalse(toolkitObject.IsPlaced, "It cannot be deleted.");
                Assert.IsNotNull(go.GetComponent<Collider>(), "It can be tapped.");
                Assert.IsFalse(toolkitObject.HasLabel, "An empty card draws no label.");
                Assert.IsTrue(toolkitObject.ShowsLabelOnSelect);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AttachInfoCard_SelectingOpensTheCard_DeselectingClosesIt()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var toolkitObject = ToolkitObjectFactory.AttachInfoCard(go, "Cube", new InfoLabelData("Cube", "A box."), null);
                Assert.IsTrue(toolkitObject.HasLabel);
                Assert.IsFalse(toolkitObject.IsLabelVisible);
                toolkitObject.SetSelected(true);
                Assert.IsTrue(toolkitObject.IsLabelVisible);
                toolkitObject.SetSelected(false);
                Assert.IsFalse(toolkitObject.IsLabelVisible);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    public class ModuleHudTests
    {
        static VisualElement Layout()
        {
            var root = new VisualElement();
            root.Add(new Label { name = ModuleHud.InfoLineName });
            root.Add(new Label { name = ModuleHud.SubtitleName });
            root.Add(new VisualElement { name = ModuleHud.StripName });
            return root;
        }

        [Test]
        public void AddButton_BeforeBind_IsAttachedOnBind()
        {
            var hud = new ModuleHud();
            var button = hud.AddButton("Portal", () => { });
            var root = Layout();
            hud.Bind(root);
            Assert.AreSame(root.Q(ModuleHud.StripName), button.parent);
            Assert.IsTrue(button.ClassListContains(ModuleHud.ButtonClass));
            Assert.IsFalse(root.Q(ModuleHud.StripName).ClassListContains(HudView.HiddenClass));
        }

        [Test]
        public void AddButton_WithoutModuleParts_StillReturnsAButton_AndWarnsOnce()
        {
            var hud = new ModuleHud();
            hud.Bind(new VisualElement());
            LogAssert.Expect(LogType.Warning, new Regex("no module parts"));
            Assert.IsNotNull(hud.AddButton("One", () => { }));
            Assert.IsNotNull(hud.AddButton("Two", () => { }));
            Assert.IsFalse(hud.HasParts);
        }

        [Test]
        public void Remove_TheLastButton_HidesTheStrip()
        {
            var hud = new ModuleHud();
            var root = Layout();
            hud.Bind(root);
            var button = hud.AddButton("Story", () => { });
            hud.Remove(button);
            Assert.IsNull(button.parent);
            Assert.IsTrue(root.Q(ModuleHud.StripName).ClassListContains(HudView.HiddenClass));
        }

        [Test]
        public void InfoLine_OneLinePerOwner_EmptyRemovesIt()
        {
            var hud = new ModuleHud();
            var root = Layout();
            hud.Bind(root);
            var route = new object();
            var vision = new object();
            hud.SetInfoLine(route, "Route 1 / 3");
            hud.SetInfoLine(vision, "Vision: Normal");
            hud.SetInfoLine(route, "Route 2 / 3");
            Assert.AreEqual("Vision: Normal\nRoute 2 / 3", hud.InfoText);
            hud.SetInfoLine(vision, "");
            hud.SetInfoLine(route, null);
            Assert.AreEqual(string.Empty, hud.InfoText);
            Assert.IsTrue(root.Q(ModuleHud.InfoLineName).ClassListContains(HudView.HiddenClass));
        }

        [Test]
        public void Subtitle_ShowsTheLatest()
        {
            var hud = new ModuleHud();
            var root = Layout();
            var ayse = new object();
            var mert = new object();
            hud.SetSubtitle(ayse, "Ayse: Hello!");
            hud.Bind(root);
            hud.SetSubtitle(mert, "Mert: Hi.");
            Assert.AreEqual("Mert: Hi.", hud.SubtitleText);
            hud.SetSubtitle(mert, null);
            Assert.AreEqual("Ayse: Hello!", hud.SubtitleText);
        }
    }
}
