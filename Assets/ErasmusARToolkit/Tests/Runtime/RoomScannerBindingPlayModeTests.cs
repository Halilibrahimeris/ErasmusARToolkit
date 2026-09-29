using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// The scanned room as something that lies in the world: it follows its anchor, objects placed on its
    /// surfaces hang from it, and it can be checked against the room.
    /// </summary>
    public class RoomScannerBindingPlayModeTests
    {
        const float TimeoutSeconds = 6f;

        readonly List<string> notifications = new List<string>();
        GameObject scannerObject;
        GameObject cameraObject;
        GameObject anchor;
        GameObject placedRoot;
        Material material;
        RoomSimulator room;
        SimulatedRoomSource source;
        RoomScanner scanner;

        static Vector2 ScreenCenter => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        [SetUp]
        public void SetUp()
        {
            notifications.Clear();
            cameraObject = new GameObject("Test AR Camera", typeof(Camera));
            cameraObject.transform.position = new Vector3(0f, 1.4f, -1f);
            cameraObject.transform.LookAt(new Vector3(0f, 0f, 0.8f));
            anchor = new GameObject("Test Anchor");
            placedRoot = new GameObject("Placed Before The Scan");
            material = new Material(Shader.Find("Erasmus AR Toolkit/AR Surface"));
            room = new RoomSimulator(new SimulationOptions { Columns = 100, Rows = 100 });
            source = new SimulatedRoomSource(room, cameraObject.transform);

            scannerObject = new GameObject("Test Room Scanner");
            scanner = scannerObject.AddComponent<RoomScanner>();
            scanner.Notification += notifications.Add;
            scanner.ConfigureForTests(cameraObject.GetComponent<Camera>(), source, () => true, material, material, null);
            scanner.Frame.UseTransformForTests(anchor.transform);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(scannerObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(anchor);
            UnityEngine.Object.DestroyImmediate(placedRoot);
            UnityEngine.Object.DestroyImmediate(material);
            var leftover = GameObject.Find(MapFrame.RootName);
            if (leftover != null)
            {
                UnityEngine.Object.DestroyImmediate(leftover);
            }
        }

        [UnityTest]
        public IEnumerator RootFollowsTheMapFrame()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            scanner.FinishScan();
            var root = GameObject.Find(MapFrame.RootName);
            Assert.IsNotNull(root);
            Assert.IsTrue(scanner.TryGetPose(ScreenCenter, out var before));
            Assert.AreEqual(0f, before.position.y, 0.01f);

            // ARCore corrects its map: what was at height 0 is at 1.5 cm now, a little to the side.
            anchor.transform.position = new Vector3(0.01f, 0.015f, 0f);
            anchor.transform.rotation = Quaternion.Euler(0f, 0.4f, 0f);
            yield return null;
            yield return null;

            Assert.AreEqual(anchor.transform.position, root.transform.position);
            Assert.AreEqual(0.4f, root.transform.eulerAngles.y, 1e-3f);
            Assert.IsTrue(scanner.TryGetPose(ScreenCenter, out var after));
            Assert.AreEqual(0.015f, after.position.y, 0.01f, "The scanned floor moved with the anchor.");
            Assert.IsFalse(scanner.IsVerifying, "A small correction needs no check.");
        }

        [UnityTest]
        public IEnumerator TryGetHit_TellsWhichSurfaceWasHit()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);

            Assert.IsTrue(scanner.TryGetHit(ScreenCenter, out var hit));

            Assert.AreEqual(scanner.Map.Levels[0].Id, hit.LevelId);
            Assert.IsFalse(hit.Blocked);
            Assert.AreEqual(0f, hit.Pose.position.y, 0.01f);
            Assert.AreEqual(Vector3.up, hit.Pose.up);
        }

        [UnityTest]
        public IEnumerator PlacedObject_FollowsItsLevelHeight()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            Assert.IsTrue(scanner.TryGetHit(ScreenCenter, out var hit));
            var placed = Place(hit);
            Assert.IsTrue(placed.HasSurface);
            Assert.AreEqual(0f, placed.transform.position.y, 0.01f);

            // The scan finds out that the surface lies 4 cm higher.
            source.IsAvailable = false;
            var level = scanner.Map.FindLevel(hit.LevelId);
            level.SetHeight(level.Height + 0.04f);
            yield return WaitUntil(() => placed.transform.position.y > 0.034f);

            Assert.AreEqual(level.DrawnHeight, placed.SurfaceHolder.transform.localPosition.y, 1e-4f);
            Assert.AreEqual(0.04f, placed.transform.position.y, 0.011f);
            Assert.AreEqual(0f, placed.HeightAboveSurface, 1e-4f);
        }

        [UnityTest]
        public IEnumerator PlacedObject_StaysBoundWhenItsLevelMerges()
        {
            // ARCore had a second plane for the floor, 10 cm too high; the object was placed on it.
            scanner.Map.EnsureLevel(0f);
            var duplicate = scanner.Map.Levels[scanner.Map.EnsureLevel(0.1f)];
            var holder = scanner.CreateContentHolder(new Pose(new Vector3(0.2f, 0.1f, 0.6f), Quaternion.identity),
                duplicate.Id);
            Assert.AreEqual(duplicate.Id, holder.LevelId);

            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel && scanner.Map.Levels.Count == 1);
            yield return new WaitForSecondsRealtime(0.6f);

            Assert.AreEqual(1, scanner.Map.Levels.Count);
            Assert.IsTrue(holder.HasSurface);
            Assert.AreEqual(scanner.Map.Levels[0].Id, holder.LevelId);
            Assert.AreEqual(scanner.Map.Levels[0].DrawnHeight, holder.transform.localPosition.y, 1e-4f);
        }

        [UnityTest]
        public IEnumerator DeletedObject_LeavesNoHolder()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            Assert.IsTrue(scanner.TryGetHit(ScreenCenter, out var hit));
            var placed = Place(hit);
            var holder = placed.SurfaceHolder.gameObject;
            Assert.AreEqual(1, scanner.HolderCount);

            UnityEngine.Object.Destroy(placed.gameObject);
            yield return null;
            yield return null;

            Assert.IsTrue(holder == null, "The holder goes with its object.");
            Assert.AreEqual(0, scanner.HolderCount);
        }

        [UnityTest]
        public IEnumerator ResetScan_KeepsPlacedObjects()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            Assert.IsTrue(scanner.TryGetHit(ScreenCenter, out var hit));
            var placed = Place(hit);
            var position = placed.transform.position;
            scanner.FinishScan();

            scanner.ResetScan();
            yield return null;

            Assert.IsTrue(placed != null);
            Assert.IsFalse(placed.HasSurface, "Its surface is gone.");
            Assert.AreEqual(position, placed.transform.position);
            Assert.AreEqual(1, scanner.HolderCount);

            // Scanning the room again puts it back on the floor.
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            Assert.IsTrue(scanner.Adopt(placed));
            Assert.IsTrue(placed.HasSurface);
            Assert.AreEqual(1, scanner.HolderCount);
        }

        [UnityTest]
        public IEnumerator Adopt_PutsAnObjectPlacedBeforeTheScan_OnTheScannedSurface()
        {
            // Placed on a plane of ARCore that was 3 cm too high.
            var placed = NewObject(placedRoot.transform, new Vector3(0.3f, 0.03f, 0.7f));
            var floating = NewObject(placedRoot.transform, new Vector3(-0.3f, 0.5f, 0.7f));
            Assert.IsFalse(scanner.Adopt(placed), "There is no scan yet.");

            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            scanner.FinishScan();

            Assert.IsTrue(scanner.Adopt(placed));
            Assert.IsFalse(scanner.Adopt(floating), "Half a meter above the floor: it stands on something else.");

            Assert.IsTrue(placed.HasSurface);
            Assert.AreEqual(0f, placed.transform.position.y, 0.01f);
            Assert.AreEqual(0.3f, placed.transform.position.x, 1e-3f);
            Assert.AreEqual(0.7f, placed.transform.position.z, 1e-3f);
            Assert.AreSame(placed.SurfaceHolder.transform, placed.transform.parent);
            Assert.IsFalse(floating.HasSurface);
            Assert.AreSame(placedRoot.transform, floating.transform.parent);
        }

        [UnityTest]
        public IEnumerator ObjectOnAScannedSurface_CannotBeLoweredIntoIt()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            Assert.IsTrue(scanner.TryGetHit(ScreenCenter, out var hit));
            var placed = Place(hit);

            placed.Manipulator.Raise(0.06f);
            Assert.AreEqual(0.06f, placed.HeightAboveSurface, 1e-4f);

            placed.Manipulator.Raise(-0.2f);
            Assert.AreEqual(0f, placed.HeightAboveSurface, 1e-4f);
            Assert.AreEqual(hit.Pose.position.y, placed.transform.position.y, 1e-3f);
        }

        [UnityTest]
        public IEnumerator Reverify_ModelAFewCentimetersOff_MovesIt()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel && scanner.Map.FloorState != FloorState.Unknown);
            scanner.FinishScan();
            Assert.IsNotNull(scanner.Map.Floor.Level, "The check needs a floor.");

            // The model hangs 4 cm too high now.
            anchor.transform.position = new Vector3(0f, 0.04f, 0f);
            yield return null;
            scanner.Reverify();
            Assert.IsTrue(scanner.IsVerifying);
            yield return WaitUntil(() => !scanner.IsVerifying);

            Assert.IsFalse(scanner.IsVerifying);
            Assert.AreEqual(-0.04f, scanner.Frame.HeightOffset, 0.01f);
            Assert.IsTrue(scanner.TryGetPose(ScreenCenter, out var pose));
            Assert.AreEqual(0f, pose.position.y, 0.015f);
            CollectionAssert.DoesNotContain(notifications, RoomScanText.ScanMoved);
            Assert.IsTrue(source.Ended, "The source that was started for the check is stopped again.");
        }

        [UnityTest]
        public IEnumerator Reverify_ModelSomewhereElse_AsksToScanAgain()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel && scanner.Map.FloorState != FloorState.Unknown);
            scanner.FinishScan();

            anchor.transform.position = new Vector3(0f, 0.3f, 0f);
            yield return null;
            yield return WaitUntil(() => !scanner.IsVerifying && notifications.Contains(RoomScanText.ScanMoved));

            CollectionAssert.Contains(notifications, RoomScanText.ScanMoved);
            Assert.AreEqual(0f, scanner.Frame.HeightOffset, 1e-6f, "A model that is far off is not moved by guesswork.");
        }

        [UnityTest]
        public IEnumerator TrustedFloor_GetsTheWholeFloorSteadyMesh()
        {
            var depthMaterial = new Material(Shader.Find("Erasmus AR Toolkit/Surface Depth"));
            scanner.ConfigureForTests(cameraObject.GetComponent<Camera>(), source, () => true, material, material, null,
                depthMaterial);
            scanner.StartScan();

            // A phone that is held still counts less: the user walks along the floor.
            var start = cameraObject.transform.position;
            var giveUpAt = Time.realtimeSinceStartup + 10f;
            while (scanner.Map.FloorState != FloorState.Trusted && Time.realtimeSinceStartup < giveUpAt)
            {
                cameraObject.transform.position = start + Vector3.right * (0.4f * Mathf.Sin(Time.time * 1.2f));
                yield return null;
            }

            Assert.AreEqual(FloorState.Trusted, scanner.Map.FloorState);
            yield return new WaitForSecondsRealtime(0.6f);

            var floor = GameObject.Find("Surface " + scanner.Map.Floor.Level.Id);
            Assert.IsNotNull(floor);
            var mark = floor.transform.Find("Depth Mark");
            Assert.IsNotNull(mark, "The floor has a depth correction.");
            var mesh = mark.GetComponent<MeshFilter>().sharedMesh;
            StringAssert.Contains("Floor Window", mesh.name);
            Assert.AreEqual(SurfaceMeshBuilder.FloorWindowSize, mesh.bounds.size.x, 1e-3f);
            Assert.AreEqual(SurfaceMeshBuilder.FloorWindowSize, mesh.bounds.size.z, 1e-3f);
            Assert.IsTrue(mark.GetComponent<MeshRenderer>().enabled);
            Assert.AreSame(mesh, floor.transform.Find("Depth Write").GetComponent<MeshFilter>().sharedMesh);
            Assert.AreEqual(1, scanner.Diagnostics.DepthSlotsUsed);

            // Far from where the window was made, it is made anew around the camera.
            cameraObject.transform.position = start + new Vector3(5f, 0f, 0f);
            yield return null;
            yield return null;
            var moved = mark.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(5f, moved.center.x, 1f);

            UnityEngine.Object.DestroyImmediate(depthMaterial);
        }

        [UnityTest]
        public IEnumerator NinthLevel_GetsNoSlot_FloorKeepsIts()
        {
            var depthMaterial = new Material(Shader.Find("Erasmus AR Toolkit/Surface Depth"));
            var points = new SimulatedRoomSource(room, cameraObject.transform, precise: false);
            scanner.ConfigureForTests(cameraObject.GetComponent<Camera>(), points, () => true, material, material, null,
                depthMaterial);

            // Planes of ARCore: the floor, and behind the camera nine shelves, each larger than the one before.
            var map = scanner.Map;
            map.AddPrior(0f, Rectangle(-3f, -8f, 3f, 3f));
            for (var i = 0; i < 9; i++)
            {
                map.AddPrior(0.3f + 0.15f * i, Rectangle(-2f, -7f, -2f + 0.4f + 0.1f * i, -6.5f));
            }

            Assert.AreEqual(10, map.Levels.Count);
            var floorId = map.Levels[0].Id;
            var smallestId = map.Levels[1].Id;
            var largestId = map.Levels[9].Id;

            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel && scanner.Map.Floor.Level != null);
            yield return new WaitForSecondsRealtime(0.6f);

            Assert.AreEqual(10, map.Levels.Count);
            Assert.AreEqual(floorId, map.Floor.Level.Id);
            Assert.AreEqual(RenderOrder.SurfaceDepthSlots, scanner.Diagnostics.DepthSlotsUsed);
            Assert.IsTrue(HasDepthCorrection(floorId), "The floor comes first.");
            Assert.IsTrue(HasDepthCorrection(largestId));
            Assert.IsFalse(HasDepthCorrection(smallestId), "There are eight corrections for ten surfaces.");

            UnityEngine.Object.DestroyImmediate(depthMaterial);
        }

        static bool HasDepthCorrection(int levelId)
        {
            var surface = GameObject.Find("Surface " + levelId);
            var mark = surface != null ? surface.transform.Find("Depth Mark") : null;
            return mark != null && mark.GetComponent<MeshRenderer>().enabled;
        }

        static List<Vector2> Rectangle(float minX, float minZ, float maxX, float maxZ)
        {
            return new List<Vector2>
            {
                new Vector2(minX, minZ), new Vector2(maxX, minZ), new Vector2(maxX, maxZ), new Vector2(minX, maxZ),
            };
        }

        ToolkitObject Place(SurfaceHit hit)
        {
            var holder = scanner.CreateContentHolder(hit.Pose, hit.LevelId);
            var placed = NewObject(holder.transform, hit.Pose.position);
            placed.SetAnchorHolder(holder.gameObject);
            return placed;
        }

        static ToolkitObject NewObject(Transform parent, Vector3 position)
        {
            var instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
            instance.transform.localScale = Vector3.one * 0.1f;
            var manipulator = instance.AddComponent<ObjectManipulator>();
            manipulator.Initialize(instance.transform.localScale, new Vector2(0.2f, 5f));
            var toolkitObject = instance.AddComponent<ToolkitObject>();
            toolkitObject.Initialize("Test Object", new InfoLabelData(), manipulator, null, null, true);
            return toolkitObject;
        }

        static IEnumerator WaitUntil(Func<bool> condition)
        {
            var giveUpAt = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < giveUpAt)
            {
                yield return null;
            }
        }
    }
}
