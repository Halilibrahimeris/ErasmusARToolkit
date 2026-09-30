using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// The whole room scan at once, as on a phone: a user walks through a simulated classroom with a
    /// shiny floor, a desk, a chair and walls, and the scan component finds the floor, the surfaces, what
    /// stands in the way and where the room ends. And what happens when ARCore corrects its map while
    /// the scan runs.
    /// </summary>
    public class RoomScanEndToEndPlayModeTests
    {
        const float DeskHeight = 0.75f;
        const float SeatHeight = 0.45f;
        const float WalkSpeed = 0.8f;
        const float TurnSpeed = 35f;

        static readonly Rect Desk = Rect.MinMaxRect(0f, 0f, 1.6f, 0.7f);
        static readonly Rect Chair = Rect.MinMaxRect(1.9f, 0.1f, 2.3f, 0.5f);
        static readonly Rect Walls = Rect.MinMaxRect(-1.8f, -2.4f, 3.8f, 2.6f);

        // Camera position and the point it looks at: around the desk and the chair, then along the wall.
        static readonly Vector3[,] ThroughTheRoom =
        {
            { new Vector3(0.8f, 1.4f, -1.0f), new Vector3(0.8f, 0.5f, 0.35f) },
            { new Vector3(2.2f, 1.4f, -1.0f), new Vector3(2.0f, 0.3f, 0.35f) },
            { new Vector3(3.0f, 1.4f, 0.35f), new Vector3(1.8f, 0.3f, 0.35f) },
            { new Vector3(2.1f, 1.4f, 1.8f), new Vector3(2.1f, 0.2f, 0.3f) },
            { new Vector3(0.8f, 1.4f, 1.8f), new Vector3(0.8f, 0.5f, 0.35f) },
            { new Vector3(-1.0f, 1.4f, 0.35f), new Vector3(0.5f, 0.5f, 0.35f) },
            { new Vector3(0.4f, 1.4f, -0.6f), new Vector3(2.0f, 0.9f, -2.4f) },
            { new Vector3(1.0f, 1.4f, -0.4f), new Vector3(1.0f, 0.9f, -2.4f) },
            { new Vector3(1.6f, 1.4f, -0.6f), new Vector3(0.0f, 0.9f, -2.4f) },
            { new Vector3(1.2f, 1.5f, -0.8f), new Vector3(1.0f, 0.6f, -2.4f) },
        };

        readonly List<string> notifications = new List<string>();
        GameObject scannerObject;
        GameObject cameraObject;
        GameObject anchor;
        Material material;
        Material depthMaterial;
        Material boxMaterial;
        RoomSimulator room;
        RoomScanner scanner;

        [SetUp]
        public void SetUp()
        {
            notifications.Clear();
            cameraObject = new GameObject("Test AR Camera", typeof(Camera));
            cameraObject.transform.position = new Vector3(0.8f, 1.4f, -1f);
            cameraObject.transform.LookAt(new Vector3(0.8f, 0f, 0.2f));
            anchor = new GameObject("Test Anchor");
            material = new Material(Shader.Find("Erasmus AR Toolkit/AR Surface"));
            depthMaterial = new Material(Shader.Find("Erasmus AR Toolkit/Surface Depth"));
            boxMaterial = new Material(Shader.Find("Erasmus AR Toolkit/Obstacle"));
            scannerObject = new GameObject("Test Room Scanner");
            scanner = scannerObject.AddComponent<RoomScanner>();
            scanner.Notification += notifications.Add;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(scannerObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(anchor);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(depthMaterial);
            UnityEngine.Object.DestroyImmediate(boxMaterial);
            var leftover = GameObject.Find(MapFrame.RootName);
            if (leftover != null)
            {
                UnityEngine.Object.DestroyImmediate(leftover);
            }
        }

        void Use(RoomSimulator simulated)
        {
            room = simulated;
            var source = new SimulatedRoomSource(room, cameraObject.transform);
            scanner.ConfigureForTests(cameraObject.GetComponent<Camera>(), source, () => true, material, material, null,
                depthMaterial);
            scanner.SetObstacleMaterialsForTests(boxMaterial, boxMaterial);
            scanner.SetThermalStatusForTests(() => ThermalStatus.Unknown);
            scanner.Frame.UseTransformForTests(anchor.transform);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator SimulatedClassroom_EndToEnd()
        {
            var classroom = new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80, GlossyFloor = 0.15f })
            {
                Walls = Walls,
            };
            classroom.AddBox(new Vector3(Desk.xMin, 0f, Desk.yMin), new Vector3(Desk.xMax, DeskHeight, Desk.yMax));
            classroom.AddBox(new Vector3(Chair.xMin, 0f, Chair.yMin), new Vector3(Chair.xMax, SeatHeight, Chair.yMax));
            Use(classroom);

            scanner.StartScan();
            yield return WalkAlong(ThroughTheRoom, 2, () => scanner.Map.FloorState == FloorState.Trusted
                                                             && scanner.Obstacles.Count >= 2
                                                             && scanner.Walls.Count >= 1);
            scanner.FinishScan();
            yield return null;

            // The floor.
            var map = scanner.Map;
            Assert.IsFalse(scanner.IsScanning);
            Assert.IsTrue(scanner.HasModel);
            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            var floor = map.Floor.Level;
            Assert.IsNotNull(floor);
            Assert.AreEqual(0f, floor.DrawnHeight, 0.02f, "The floor of the room lies at height 0.");
            Assert.AreEqual(0f, scanner.Diagnostics.FloorHeight, 0.02f);
            Assert.Greater(floor.ShownArea, 6f, "Most of the floor the user walked along.");
            Assert.IsTrue(scanner.Progress.FloorFound);
            foreach (var level in map.Levels)
            {
                if (level.Height < floor.Height - FloorModel.SuppressBelow)
                {
                    Assert.IsTrue(!level.IsUsable || level.ShownArea <= 0f,
                        $"A surface of {level.ShownArea:0.00} m² is drawn at {level.Height:0.00}, below the shiny floor.");
                }
            }

            Assert.IsTrue(floor.IsShown(RoomSurfaceMap.CellOf(0.8f, -0.6f)), "The floor in front of the desk.");
            Assert.IsTrue(floor.IsShown(RoomSurfaceMap.CellOf(2.8f, -0.5f)));
            Assert.IsFalse(floor.IsShown(RoomSurfaceMap.CellOf(0.8f, 0.35f)), "No floor grid under the desk.");
            Assert.IsFalse(floor.IsShown(RoomSurfaceMap.CellOf(2.1f, 0.3f)), "No floor grid under the chair.");

            // The desk.
            SurfaceLevel desk = null;
            foreach (var level in map.Levels)
            {
                if (level.IsUsable && level.ShownArea > 0f && Mathf.Abs(level.Height - DeskHeight) < 0.05f)
                {
                    desk = level;
                }
            }

            Assert.IsNotNull(desk, "The desk top is a surface.");
            Assert.AreEqual(Desk.width * Desk.height, desk.ShownArea, 0.3f, "The desk is as large as the desk.");
            Assert.IsFalse(desk.IsShown(RoomSurfaceMap.CellOf(2.1f, 0.3f)), "The desk does not reach over the chair.");

            // What stands in the room, and where the room ends.
            Assert.IsTrue(BoxAt(new Vector3(0.8f, 0f, 0.35f)).HasValue, "The desk has a box.");
            var chair = BoxAt(new Vector3(2.1f, 0f, 0.3f));
            Assert.IsTrue(chair.HasValue, "The chair has a box.");
            Assert.AreEqual(SeatHeight, chair.Value.Size.y, 0.06f);
            Assert.LessOrEqual(chair.Value.Size.x, 0.65f);
            Assert.GreaterOrEqual(scanner.Progress.ObstacleCount, 2);
            Assert.GreaterOrEqual(scanner.Walls.Count, 1);
            Assert.AreEqual(scanner.Walls.Count, scanner.Progress.WallCount);
            WallSegment? wall = null;
            foreach (var segment in scanner.Walls)
            {
                if (segment.DistanceTo(new Vector2(1f, Walls.yMin)) < 0.12f)
                {
                    wall = segment;
                }
            }

            Assert.IsTrue(wall.HasValue, "The wall the user looked at is a wall.");
            Assert.GreaterOrEqual(wall.Value.Length, 1.5f);
            Assert.AreEqual(0f, RoomAxes.Difference(scanner.RoomYaw, 0f), 2f, "The grid lies along the walls.");

            // Where things can be placed.
            Assert.IsTrue(scanner.TryGetHit(Down(0.8f, -0.6f), out var onFloor));
            Assert.IsFalse(onFloor.Blocked);
            Assert.AreEqual(floor.Id, onFloor.LevelId);
            Assert.AreEqual(0f, onFloor.Pose.position.y, 0.02f);

            Assert.IsTrue(scanner.TryGetHit(Down(0.8f, 0.35f), out var onDesk));
            Assert.IsFalse(onDesk.Blocked, "Things are put on desks.");
            Assert.AreEqual(desk.Id, onDesk.LevelId);
            Assert.AreEqual(DeskHeight, onDesk.Pose.position.y, 0.03f);

            Assert.IsTrue(scanner.TryGetHit(Down(2.1f, 0.3f), out var onChair), "The user is told that a chair stands there.");
            Assert.IsTrue(onChair.Blocked);

            var outside = new Vector3(1f, 0f, Walls.yMin - 0.6f);
            var eye = new Vector3(1f, 1.4f, -0.8f);
            var throughTheWall = new Ray(eye, (outside - eye).normalized);
            Assert.IsTrue(!scanner.TryGetHit(throughTheWall, out var behind) || behind.Blocked,
                "Nothing is placed behind the wall.");

            // What is drawn.
            var root = GameObject.Find(MapFrame.RootName);
            Assert.IsNotNull(root);
            var drawn = new HashSet<string>();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.enabled && !renderer.forceRenderingOff)
                {
                    drawn.Add(renderer.name);
                }
            }

            CollectionAssert.IsSubsetOf(new[] { "Fill", "Outline", "Depth Mark", "Depth Write", "Obstacle Fill", "Wall Fill" },
                drawn);
            CollectionAssert.DoesNotContain(notifications, RoomScanText.ScanMoved);
            CollectionAssert.DoesNotContain(notifications, RoomScanText.DepthUnusable);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator DriftJump_WhileScanning_TheFloorComesBack()
        {
            Use(new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80 }));
            scanner.StartScan();
            yield return LookAround(10f, () => scanner.Map.FloorState == FloorState.Trusted);
            Assert.AreEqual(FloorState.Trusted, scanner.Map.FloorState);
            Assert.AreEqual(0f, FloorUnderTheCamera(), 0.015f);

            // ARCore corrects its map: everything it holds lies 3 cm higher now.
            anchor.transform.position = new Vector3(0f, 0.03f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(0.03f, FloorUnderTheCamera(), 0.015f, "The scan hangs from its anchor.");
            Assert.IsFalse(scanner.IsVerifying, "A few centimeters are put right by scanning on.");

            var jumpedAt = Time.unscaledTime;
            yield return LookAround(8f, () => Mathf.Abs(FloorUnderTheCamera()) <= 0.012f);
            var seconds = Time.unscaledTime - jumpedAt;

            // Half a second of waiting, a second of scanning, and the drawn height moves at 2 cm a second.
            Assert.AreEqual(0f, FloorUnderTheCamera(), 0.012f, "The floor is where depth sees it again.");
            Assert.Less(seconds, 4f);
            Assert.AreEqual(FloorState.Trusted, scanner.Map.FloorState);
            Assert.IsTrue(scanner.IsScanning);
            CollectionAssert.DoesNotContain(notifications, RoomScanText.ScanMoved);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Relocalisation_WhileScanning_IsCheckedAndPutRight()
        {
            Use(new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80 }));
            scanner.StartScan();
            yield return LookAround(10f, () => scanner.Map.FloorState == FloorState.Trusted);
            Assert.AreEqual(FloorState.Trusted, scanner.Map.FloorState);

            // Tracking was lost for a moment; ARCore finds itself 12 cm to the side and 4 cm lower.
            anchor.transform.position = new Vector3(0.12f, -0.04f, 0f);
            yield return null;
            yield return null;
            Assert.IsTrue(scanner.IsVerifying, "After a jump like that the scan is checked against the room.");
            var frames = scanner.Map.FramesIntegrated;

            yield return LookAround(8f, () => !scanner.IsVerifying);

            Assert.IsFalse(scanner.IsVerifying);
            Assert.AreEqual(frames, scanner.Map.FramesIntegrated, "Nothing is added to a scan that is being checked.");
            Assert.AreEqual(0.04f, scanner.Frame.HeightOffset, 0.012f);
            Assert.AreEqual(0f, FloorUnderTheCamera(), 0.015f);
            CollectionAssert.DoesNotContain(notifications, RoomScanText.ScanMoved);

            yield return LookAround(3f, () => scanner.Map.FramesIntegrated > frames + 3);
            Assert.IsTrue(scanner.IsScanning);
            Assert.Greater(scanner.Map.FramesIntegrated, frames, "The scan goes on.");
            Assert.AreEqual(0f, FloorUnderTheCamera(), 0.015f);
        }

        ObstacleVolume? BoxAt(Vector3 point)
        {
            foreach (var volume in scanner.Obstacles)
            {
                if (volume.ContainsXZ(point))
                {
                    return volume;
                }
            }

            return null;
        }

        static Ray Down(float x, float z)
        {
            return new Ray(new Vector3(x, 1.4f, z), Vector3.down);
        }

        /// <summary>World height of the scanned floor under the camera (NaN when nothing is there).</summary>
        float FloorUnderTheCamera()
        {
            var position = cameraObject.transform.position;
            return scanner.TryGetHit(Down(position.x, position.z + 1f), out var hit) && !hit.Blocked
                ? hit.Pose.position.y
                : float.NaN;
        }

        /// <summary>
        /// Walks from pose to pose at the pace of someone who scans a room, until all was seen
        /// <paramref name="rounds"/> times or the scan has what is asked for (after one round at least).
        /// </summary>
        IEnumerator WalkAlong(Vector3[,] poses, int rounds, Func<bool> done)
        {
            var moved = cameraObject.transform;
            for (var round = 0; round < rounds; round++)
            {
                for (var pose = 0; pose < poses.GetLength(0); pose++)
                {
                    var fromPosition = moved.position;
                    var fromRotation = moved.rotation;
                    var toPosition = poses[pose, 0];
                    var toRotation = Quaternion.LookRotation(poses[pose, 1] - toPosition);
                    var seconds = Mathf.Max(0.3f, Vector3.Distance(fromPosition, toPosition) / WalkSpeed,
                        Quaternion.Angle(fromRotation, toRotation) / TurnSpeed);
                    for (var passed = 0f; passed < seconds; passed += Mathf.Min(Time.unscaledDeltaTime, 0.05f))
                    {
                        var share = passed / seconds;
                        moved.SetPositionAndRotation(Vector3.Lerp(fromPosition, toPosition, share),
                            Quaternion.Slerp(fromRotation, toRotation, share));
                        yield return null;
                    }

                    moved.SetPositionAndRotation(toPosition, toRotation);
                    yield return null;
                }

                if (done())
                {
                    yield break;
                }
            }
        }

        /// <summary>A user who stands in one place and looks at the floor around it.</summary>
        IEnumerator LookAround(float seconds, Func<bool> done)
        {
            var moved = cameraObject.transform;
            var start = new Vector3(0.8f, 1.4f, -1f);
            var giveUpAt = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < giveUpAt)
            {
                moved.position = start + Vector3.right * (0.4f * Mathf.Sin(Time.time * 1.2f));
                moved.LookAt(new Vector3(0.8f, 0f, 0.2f));
                yield return null;
            }
        }
    }
}
