using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// Acceptance test of the room scan on a simulated classroom corner: a 1.6 m x 0.7 m desk (0.75 m high)
    /// and a chair next to it. ARCore-like input makes the mistakes seen on the phone: the desk plane is a
    /// convex hull that reaches 0.9 m past the desk, it is 3 cm too high, and the floor was detected twice
    /// 12 cm apart. Depth frames with noise from several camera positions must fix all three.
    /// </summary>
    public class RoomScanSimulationTests
    {
        const float DeskHeight = 0.75f;
        const float DeskArea = 1.6f * 0.7f;
        const int RaysPerSide = 40;
        const float HalfFieldOfViewDegrees = 25f;
        const int FramesPerPose = 3;

        static readonly Bounds Desk = FromMinMax(new Vector3(0f, 0f, 0f), new Vector3(1.6f, DeskHeight, 0.7f));
        static readonly Bounds Chair = FromMinMax(new Vector3(1.9f, 0f, 0.1f), new Vector3(2.3f, 0.45f, 0.5f));

        // Camera position and the point it looks at, like a student walking around the desk.
        static readonly Vector3[,] Poses =
        {
            { new Vector3(0.8f, 1.4f, -1.0f), new Vector3(0.8f, 0.5f, 0.35f) },
            { new Vector3(2.2f, 1.4f, -1.0f), new Vector3(2.0f, 0.3f, 0.35f) },
            { new Vector3(3.0f, 1.4f, 0.35f), new Vector3(1.8f, 0.3f, 0.35f) },
            { new Vector3(-1.0f, 1.4f, 0.35f), new Vector3(0.5f, 0.5f, 0.35f) },
            { new Vector3(0.8f, 1.4f, 1.8f), new Vector3(0.8f, 0.5f, 0.35f) },
            { new Vector3(2.1f, 1.4f, -0.3f), new Vector3(2.1f, 0f, 0.6f) },
            { new Vector3(1.8f, 1.5f, 0.35f), new Vector3(1.9f, 0f, 0.35f) },
            { new Vector3(2.4f, 1.5f, 0.35f), new Vector3(2.3f, 0f, 0.35f) },
            { new Vector3(1.3f, 1.5f, 0.35f), new Vector3(2.0f, 0f, 0.35f) },
        };

        RoomSurfaceMap map;

        [OneTimeSetUp]
        public void ScanTheRoom()
        {
            map = new RoomSurfaceMap();
            map.AddPrior(0f, Rectangle(-1.5f, -1.5f, 3.5f, 2.2f));
            map.AddPrior(0.12f, Rectangle(1f, -1f, 3f, 1.5f));
            map.AddPrior(0.78f, Rectangle(0f, 0f, 2.5f, 0.7f));

            var random = new System.Random(42);
            var frame = new ScanFrame();
            for (var pose = 0; pose < Poses.GetLength(0); pose++)
            {
                for (var repeat = 0; repeat < FramesPerPose; repeat++)
                {
                    Capture(Poses[pose, 0], Poses[pose, 1], random, frame);
                    map.Integrate(frame);
                }
            }

            RoomSurfaceMapTests.ShowAll(map);
        }

        [Test]
        public void DuplicateFloors_BecomeOneFloor()
        {
            var floors = 0;
            foreach (var level in map.Levels)
            {
                if (Mathf.Abs(level.Height) < 0.2f)
                {
                    floors++;
                    Assert.AreEqual(0f, level.Height, 0.01f);
                }
            }

            Assert.AreEqual(1, floors);
        }

        [Test]
        public void DeskHeight_IsCorrected()
        {
            Assert.AreEqual(DeskHeight, DeskLevel().Height, 0.01f);
        }

        [Test]
        public void DeskArea_MatchesTheRealDesk()
        {
            Assert.AreEqual(DeskArea, DeskLevel().ShownArea, DeskArea * 0.15f);
            Assert.IsTrue(DeskLevel().IsShown(RoomSurfaceMap.CellOf(0.8f, 0.35f)));
        }

        [Test]
        public void DeskPlane_NoLongerReachesOverTheChairAndFloor()
        {
            var level = DeskLevel();
            var shown = 0;
            var total = 0;
            for (var x = 1.825f; x < 2.45f; x += RoomSurfaceMap.CellSize)
            {
                for (var z = 0.125f; z < 0.6f; z += RoomSurfaceMap.CellSize)
                {
                    total++;
                    if (level.IsShown(RoomSurfaceMap.CellOf(x, z)))
                    {
                        shown++;
                    }
                }
            }

            Assert.AreEqual(0, shown, $"{shown} of {total} cells beside the desk are still drawn at desk height.");
        }

        [Test]
        public void ChairSeat_IsFoundByDepth()
        {
            var found = false;
            foreach (var level in map.Levels)
            {
                found |= Mathf.Abs(level.Height - 0.45f) < 0.02f && level.IsShown(RoomSurfaceMap.CellOf(2.1f, 0.3f));
            }

            Assert.IsTrue(found, "The chair seat should be discovered as its own surface.");
        }

        [Test]
        public void Raycast_OnTheDesk_ReturnsTheRealDeskHeight()
        {
            var ray = new Ray(new Vector3(0.8f, 1.5f, -0.3f), new Vector3(0f, -1f, 1f).normalized);

            Assert.IsTrue(map.Raycast(ray, 10f, out var pose));
            Assert.AreEqual(DeskHeight, pose.position.y, 0.01f);
        }

        SurfaceLevel DeskLevel()
        {
            SurfaceLevel best = null;
            foreach (var level in map.Levels)
            {
                if (best == null || Mathf.Abs(level.Height - DeskHeight) < Mathf.Abs(best.Height - DeskHeight))
                {
                    best = level;
                }
            }

            return best;
        }

        /// <summary>A depth frame: a grid of rays around the view direction, each with up to 1 cm of noise.</summary>
        static void Capture(Vector3 origin, Vector3 target, System.Random random, ScanFrame frame)
        {
            frame.Reset(origin, true);
            var rotation = Quaternion.LookRotation(target - origin);
            var extent = Mathf.Tan(HalfFieldOfViewDegrees * Mathf.Deg2Rad);
            for (var row = 0; row < RaysPerSide; row++)
            {
                for (var column = 0; column < RaysPerSide; column++)
                {
                    var local = new Vector3(
                        Mathf.Lerp(-extent, extent, column / (RaysPerSide - 1f)),
                        Mathf.Lerp(-extent, extent, row / (RaysPerSide - 1f)), 1f);
                    var direction = (rotation * local).normalized;
                    if (!Cast(origin, direction, out var distance, out var facesUp))
                    {
                        continue;
                    }

                    var noise = ((float)random.NextDouble() - 0.5f) * 0.02f;
                    frame.Add(origin + direction * (distance + noise), facesUp);
                }
            }
        }

        static bool Cast(Vector3 origin, Vector3 direction, out float distance, out bool facesUp)
        {
            distance = float.MaxValue;
            facesUp = false;
            if (direction.y < 0f)
            {
                distance = -origin.y / direction.y;
                facesUp = true;
            }

            foreach (var box in new[] { Desk, Chair })
            {
                if (box.IntersectRay(new Ray(origin, direction), out var hit) && hit > 0f && hit < distance)
                {
                    distance = hit;
                    facesUp = Mathf.Abs(origin.y + direction.y * hit - box.max.y) < 1e-3f;
                }
            }

            return distance < float.MaxValue;
        }

        static List<Vector2> Rectangle(float minX, float minZ, float maxX, float maxZ)
        {
            return new List<Vector2>
            {
                new Vector2(minX, minZ), new Vector2(maxX, minZ), new Vector2(maxX, maxZ), new Vector2(minX, maxZ),
            };
        }

        static Bounds FromMinMax(Vector3 min, Vector3 max)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }
    }
}
