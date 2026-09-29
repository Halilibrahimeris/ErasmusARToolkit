using NUnit.Framework;
using UnityEngine;
using static Erasmus.ARToolkit.Tests.RoomSurfaceMapTests;

namespace Erasmus.ARToolkit.Tests
{
    public class MapFrameTests
    {
        GameObject pose;
        MapFrame frame;

        [SetUp]
        public void SetUp()
        {
            pose = new GameObject("Test Anchor");
            frame = new MapFrame();
        }

        [TearDown]
        public void TearDown()
        {
            frame.Dispose();
            Object.DestroyImmediate(pose);
        }

        static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance, "x");
            Assert.AreEqual(expected.y, actual.y, tolerance, "y");
            Assert.AreEqual(expected.z, actual.z, tolerance, "z");
        }

        [Test]
        public void NotBegun_MapSpaceIsWorldSpace()
        {
            Assert.IsFalse(frame.IsBegun);
            Assert.IsFalse(frame.Refresh(out _, out _));
            AssertClose(new Vector3(1f, 2f, 3f), frame.ToMap(new Vector3(1f, 2f, 3f)));
            Assert.AreEqual(Matrix4x4.identity, frame.WorldToMap);
        }

        [Test]
        public void BegunWithoutAnArRig_MapSpaceIsWorldSpace()
        {
            Assert.IsFalse(frame.Begin(null, null, new Vector3(4f, 0f, 2f)));

            Assert.IsTrue(frame.IsBegun);
            Assert.IsFalse(frame.IsAnchored);
            Assert.IsTrue(frame.IsTracking);
            Assert.IsNotNull(frame.Root);
            Assert.AreEqual(MapFrame.RootName, frame.Root.name);
            AssertClose(new Vector3(1f, 2f, 3f), frame.ToMap(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void WorldToMap_AndBack_IsARoundTrip()
        {
            pose.transform.SetPositionAndRotation(new Vector3(2f, 0.4f, -1f), Quaternion.Euler(0f, 35f, 0f));
            frame.UseTransformForTests(pose.transform);
            frame.Refresh(out _, out _);

            var world = new Vector3(0.7f, 1.1f, 2.3f);
            var map = frame.ToMap(world);

            AssertClose(world, frame.ToWorld(map));
            Assert.AreEqual(1.1f - 0.4f, map.y, 1e-4f, "Heights are measured from the anchor.");
            AssertClose(Vector3.zero, frame.ToMap(pose.transform.position));
        }

        [Test]
        public void TiltedAnchor_GivesALevelMap()
        {
            pose.transform.SetPositionAndRotation(new Vector3(1f, 0f, 1f), Quaternion.Euler(8f, 40f, -5f));
            frame.UseTransformForTests(pose.transform);
            frame.Refresh(out _, out _);

            var up = frame.Root.up;
            AssertClose(Vector3.up, up);
            Assert.AreEqual(MapFrame.YawOf(pose.transform.rotation), frame.Root.eulerAngles.y, 0.01f);
            Assert.AreEqual(40f, frame.Root.eulerAngles.y, 1.5f);
        }

        [Test]
        public void YawOf_AnAnchorThatLooksStraightDown_UsesItsSideAxis()
        {
            var down = Quaternion.Euler(0f, 70f, 0f) * Quaternion.Euler(90f, 0f, 0f);

            Assert.AreEqual(70f, Mathf.Repeat(MapFrame.YawOf(down), 360f), 0.5f);
            Assert.AreEqual(0f, MapFrame.YawOf(Quaternion.identity), 1e-4f);
            Assert.AreEqual(-90f, MapFrame.YawOf(Quaternion.Euler(0f, -90f, 0f)), 1e-3f);
        }

        [Test]
        public void Refresh_TellsHowFarTheAnchorJumped()
        {
            frame.UseTransformForTests(pose.transform);
            Assert.IsTrue(frame.Refresh(out var moved, out var turned), "The first pose.");
            Assert.AreEqual(0f, moved);
            Assert.AreEqual(0f, turned);
            Assert.IsFalse(frame.Refresh(out _, out _), "Nothing changed.");

            pose.transform.position += new Vector3(0.03f, 0f, 0.04f);
            pose.transform.rotation = Quaternion.Euler(0f, 2f, 0f);

            Assert.IsTrue(frame.Refresh(out moved, out turned));
            Assert.AreEqual(0.05f, moved, 1e-4f);
            Assert.AreEqual(2f, turned, 1e-2f);
        }

        [Test]
        public void HeightOffset_RaisesTheMap()
        {
            pose.transform.position = new Vector3(0f, 0.2f, 0f);
            frame.UseTransformForTests(pose.transform);
            frame.Refresh(out _, out _);

            frame.AddHeightOffset(0.04f);

            Assert.AreEqual(0.04f, frame.HeightOffset, 1e-6f);
            Assert.AreEqual(0.24f, frame.ToWorldHeight(0f), 1e-4f);
            Assert.AreEqual(0.24f, frame.Root.position.y, 1e-4f);

            frame.Refresh(out _, out _);
            Assert.AreEqual(0.24f, frame.Root.position.y, 1e-4f, "The correction stays.");
        }

        [Test]
        public void Rays_AndPoses_AreConverted()
        {
            pose.transform.SetPositionAndRotation(new Vector3(1f, 0.5f, 0f), Quaternion.Euler(0f, 90f, 0f));
            frame.UseTransformForTests(pose.transform);
            frame.Refresh(out _, out _);

            var ray = frame.ToMap(new Ray(new Vector3(1f, 2f, 0f), Vector3.down));
            AssertClose(new Vector3(0f, 1.5f, 0f), ray.origin);
            AssertClose(Vector3.down, ray.direction);

            var world = frame.ToWorld(new Pose(new Vector3(0f, 0f, 1f), Quaternion.identity));
            AssertClose(new Vector3(2f, 0.5f, 0f), world.position);
            AssertClose(Vector3.up, world.up);
        }

        [Test]
        public void MapInAMovedFrame_IsHitWhereItLiesInTheWorld()
        {
            pose.transform.SetPositionAndRotation(new Vector3(3f, 0.1f, 1f), Quaternion.Euler(0f, 90f, 0f));
            frame.UseTransformForTests(pose.transform);
            frame.Refresh(out _, out _);
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };

            // A table of 1 m x 0.4 m whose long side points along the x axis of the map.
            map.AddPrior(0.7f, Polygon(new Rect(0f, 0f, 1f, 0.4f)));
            ShowAll(map);

            // In the world the map is turned by 90 degrees: its x axis points along -z.
            var above = new Ray(new Vector3(3.2f, 2f, 0.5f), Vector3.down);
            Assert.IsTrue(map.Raycast(frame.ToMap(above), 5f, out var hit));
            var world = frame.ToWorld(hit);
            AssertClose(new Vector3(3.2f, 0.8f, 0.5f), world.position, 1e-3f);

            var beside = new Ray(new Vector3(3.2f, 2f, 1.5f), Vector3.down);
            Assert.IsFalse(map.Raycast(frame.ToMap(beside), 5f, out _));
        }

        [Test]
        public void Dispose_RemovesTheRoot()
        {
            frame.UseTransformForTests(pose.transform);
            var root = frame.Root.gameObject;

            frame.Dispose();

            Assert.IsTrue(root == null);
            Assert.IsFalse(frame.IsBegun);
        }
    }

    public class SurfaceBindingsTests
    {
        GameObject root;
        RoomSurfaceMap map;
        SurfaceBindings bindings;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Test Root");
            map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            bindings = new SurfaceBindings();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Holder_FollowsTheHeightItsSurfaceIsDrawnAt()
        {
            map.AddPrior(0.78f, Polygon(new Rect(0f, 0f, 1.6f, 0.7f)));
            ShowAll(map);
            var level = map.Levels[0];
            var holder = bindings.Create(root.transform, new Pose(new Vector3(0.5f, level.DrawnHeight, 0.3f),
                Quaternion.identity), level.Id);
            Assert.AreEqual(0.78f, holder.transform.localPosition.y, 1e-4f);

            // Depth finds the table 3 cm lower than ARCore had it.
            Integrate(map, PointsOn(new Vector3(0.8f, 1.5f, -0.5f), 0.75f, new Rect(0f, 0f, 1.6f, 0.7f)), 3);
            for (var i = 0; i < 8; i++)
            {
                new RoomModelBuilder().Rebuild(map, 0.5f, true, null);
                map.UpdateDrawnHeights(0.5f);
                bindings.FollowHeights(map);
            }

            Assert.AreEqual(level.DrawnHeight, holder.transform.localPosition.y, 1e-4f);
            Assert.AreEqual(0.75f, holder.transform.localPosition.y, 0.011f);
            Assert.AreEqual(0.5f, holder.transform.localPosition.x, 1e-5f);
        }

        [Test]
        public void Holder_MovesOnToTheSurfaceItsOwnWentInto()
        {
            map.AddPrior(0f, Polygon(new Rect(0f, 0f, 2f, 2f)));
            map.AddPrior(0.12f, Polygon(new Rect(1f, 1f, 2f, 2f)));
            map.LevelMerged += bindings.OnLevelMerged;
            map.LevelRemoved += bindings.OnLevelRemoved;
            var duplicate = map.Levels[1];
            var holder = bindings.Create(root.transform, new Pose(new Vector3(2.5f, 0.12f, 2.5f), Quaternion.identity),
                duplicate.Id);

            Integrate(map, PointsOn(new Vector3(1.5f, 1.4f, -0.5f), 0f, new Rect(0f, 0f, 3f, 3f)), 2);
            ShowAll(map);
            bindings.FollowHeights(map);

            Assert.AreEqual(1, map.Levels.Count);
            Assert.IsTrue(holder.HasSurface);
            Assert.AreEqual(map.Levels[0].Id, holder.LevelId);
            Assert.AreEqual(0f, holder.transform.localPosition.y, 0.01f);
        }

        [Test]
        public void Holder_OfASurfaceThatIsGone_StaysWhereItIs()
        {
            map.AddPrior(0.75f, Polygon(new Rect(0f, 0f, 1f, 1f)));
            map.LevelRemoved += bindings.OnLevelRemoved;
            var holder = bindings.Create(root.transform, new Pose(new Vector3(0.5f, 0.75f, 0.5f), Quaternion.identity),
                map.Levels[0].Id);

            map.Clear();
            bindings.FollowHeights(map);

            Assert.IsFalse(holder.HasSurface);
            Assert.AreEqual(new Vector3(0.5f, 0.75f, 0.5f), holder.transform.localPosition);
            Assert.AreEqual(1, bindings.Count);
        }

        [Test]
        public void Move_PutsTheHolderOnAnotherSurface()
        {
            map.AddPrior(0f, Polygon(new Rect(0f, 0f, 2f, 2f)));
            map.AddPrior(0.75f, Polygon(new Rect(3f, 0f, 1f, 1f)));
            var holder = bindings.Create(root.transform, new Pose(new Vector3(1f, 0f, 1f), Quaternion.identity),
                map.Levels[0].Id);

            bindings.Move(holder, new Vector3(3.5f, 0.75f, 0.5f), map.Levels[1].Id);

            Assert.AreEqual(map.Levels[1].Id, holder.LevelId);
            Assert.AreEqual(new Vector3(3.5f, 0.75f, 0.5f), holder.transform.localPosition);
            Assert.AreEqual(1, bindings.Count);
        }

        [Test]
        public void DestroyedHolders_AreForgotten()
        {
            var holder = bindings.Create(root.transform, new Pose(Vector3.zero, Quaternion.identity), 3);
            Assert.AreEqual(1, bindings.Count);

            Object.DestroyImmediate(holder.gameObject);

            Assert.AreEqual(0, bindings.Count);
            Assert.DoesNotThrow(() => bindings.OnLevelMerged(3, 4));
            Assert.DoesNotThrow(() => bindings.FollowHeights(map));
        }
    }

    public class ScanVerifierTests
    {
        static readonly Rect Floor = new Rect(-2f, -2f, 4f, 4f);
        static readonly Rect Desk = new Rect(0f, 0f, 1.6f, 0.7f);
        static readonly Vector3 Camera = new Vector3(0f, 1.4f, -1f);

        /// <summary>A scanned room: a trusted floor and a desk.</summary>
        static RoomSurfaceMap ScannedRoom()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(Floor));
            map.AddPrior(0.75f, Polygon(Desk));
            for (var i = 0; i < FloorModel.TrustFrames + 2; i++)
            {
                map.Integrate(PointsOn(Camera, 0f, new Rect(-1f, -0.5f, 2f, 2f)));
                map.Integrate(PointsOn(new Vector3(0.8f, 1.5f, -0.6f), 0.75f, Desk));
            }

            ShowAll(map);
            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            return map;
        }

        static ScanVerdict Check(RoomSurfaceMap map, float floorHeight, Rect deskAt, out float shift)
        {
            var verifier = new ScanVerifier();
            verifier.Begin(map);
            for (var i = 0; i < 6; i++)
            {
                verifier.Add(PointsOn(Camera, floorHeight, new Rect(-1f, -0.5f, 2f, 2f)));
                verifier.Add(PointsOn(new Vector3(0.8f, 1.5f, -0.6f), 0.75f + floorHeight, deskAt));
            }

            return verifier.Finish(out shift);
        }

        [Test]
        public void RoomAsScanned_Matches()
        {
            Assert.AreEqual(ScanVerdict.Matches, Check(ScannedRoom(), 0f, Desk, out var shift));
            Assert.AreEqual(0f, shift, 0.005f);
        }

        [Test]
        public void FloorOneCentimeterOff_StillMatches()
        {
            Assert.AreEqual(ScanVerdict.Matches, Check(ScannedRoom(), 0.01f, Desk, out _));
        }

        [Test]
        public void RoomFourCentimetersLower_IsShifted_ByThatMuch()
        {
            Assert.AreEqual(ScanVerdict.Shifted, Check(ScannedRoom(), -0.04f, Desk, out var shift));
            Assert.AreEqual(-0.04f, shift, 0.005f);
        }

        [Test]
        public void RoomTenCentimetersHigher_HasMoved()
        {
            Assert.AreEqual(ScanVerdict.Moved, Check(ScannedRoom(), 0.1f, Desk, out _));
        }

        [Test]
        public void DeskSomewhereElse_HasMoved()
        {
            var deskNow = new Rect(Desk.x + 1.2f, Desk.y + 1f, Desk.width, Desk.height);

            Assert.AreEqual(ScanVerdict.Moved, Check(ScannedRoom(), 0f, deskNow, out _));
        }

        [Test]
        public void FloorThirtyCentimetersLower_HasMoved()
        {
            Assert.AreEqual(ScanVerdict.Moved, Check(ScannedRoom(), -0.3f, Desk, out _));
        }

        [Test]
        public void OnlyADeskInView_WhereTheModelHasIt_SaysNothing()
        {
            var verifier = new ScanVerifier();
            verifier.Begin(ScannedRoom());
            for (var i = 0; i < 6; i++)
            {
                verifier.Add(PointsOn(new Vector3(0.8f, 1.5f, -0.6f), 0.75f, Desk));
            }

            Assert.AreEqual(ScanVerdict.Unknown, verifier.Finish(out _));
            Assert.AreEqual(1f, verifier.Agreement, 0.05f);
        }

        [Test]
        public void NothingSeen_SaysNothing()
        {
            var verifier = new ScanVerifier();
            verifier.Begin(ScannedRoom());

            Assert.AreEqual(ScanVerdict.Unknown, verifier.Finish(out var shift));
            Assert.AreEqual(0f, shift);
            Assert.AreEqual(0, verifier.Frames);
        }

        [Test]
        public void Checking_DoesNotChangeTheModel()
        {
            var map = ScannedRoom();
            var frames = map.FramesIntegrated;
            var evidence = map.Levels[0].GetLogOdds(Cell(0.2f, 0.2f));

            Check(map, -0.04f, Desk, out _);

            Assert.AreEqual(frames, map.FramesIntegrated);
            Assert.AreEqual(evidence, map.Levels[0].GetLogOdds(Cell(0.2f, 0.2f)));
            Assert.AreEqual(0f, map.Floor.Height, 0.005f);
        }
    }
}
