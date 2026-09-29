using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// Runs the room scan component with a fake depth source that sees a 2 m x 2 m floor, so the whole
    /// loop (integrate, filter, mesh, placement raycast) is checked without an AR device.
    /// </summary>
    public class RoomScannerPlayModeTests
    {
        const float TimeoutSeconds = 5f;
        const string ViewRootName = MapFrame.RootName;

        GameObject scannerObject;
        GameObject cameraObject;
        Material material;
        Material depthMaterial;
        FloorSource source;
        RoomScanner scanner;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Test AR Camera", typeof(Camera));
            cameraObject.transform.position = new Vector3(0f, 1.4f, -1f);
            cameraObject.transform.LookAt(new Vector3(0f, 0f, 0.8f));
            material = new Material(Shader.Find("Erasmus AR Toolkit/AR Surface"));
            depthMaterial = new Material(Shader.Find("Erasmus AR Toolkit/Surface Depth"));
            source = new FloorSource(cameraObject.transform.position);

            scannerObject = new GameObject("Test Room Scanner");
            scanner = scannerObject.AddComponent<RoomScanner>();
            scanner.ConfigureForTests(cameraObject.GetComponent<Camera>(), source, () => true, material, material, null,
                depthMaterial);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(scannerObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(depthMaterial);
            var leftover = GameObject.Find(ViewRootName);
            if (leftover != null)
            {
                Object.DestroyImmediate(leftover);
            }
        }

        [UnityTest]
        public IEnumerator Scan_OfAFloor_BuildsASurface_ThatPlacementHits()
        {
            scanner.StartScan();
            Assert.IsTrue(scanner.IsScanning);
            Assert.IsTrue(source.Began);

            yield return WaitForModel();

            Assert.IsTrue(scanner.HasModel, "The scan should have drawn the floor.");
            Assert.AreEqual(1, scanner.Progress.SurfaceCount);
            Assert.Greater(scanner.Progress.SurfaceArea, 3f);

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Assert.IsTrue(scanner.TryGetPose(center, out var pose));
            Assert.AreEqual(0f, pose.position.y, 0.01f);

            var root = GameObject.Find(ViewRootName);
            Assert.IsNotNull(root, "Scanned surfaces should be drawn.");
            Assert.Greater(root.GetComponentsInChildren<MeshRenderer>().Length, 0);

            scanner.FinishScan();
            Assert.IsFalse(scanner.IsScanning);
            Assert.IsTrue(source.Ended);
            Assert.IsTrue(scanner.HasModel, "Finishing keeps the scanned model.");
        }

        [UnityTest]
        public IEnumerator SetVisible_HidesTheGrid_ButKeepsTheModel()
        {
            scanner.StartScan();
            yield return WaitForModel();

            scanner.SetVisible(false);

            foreach (var renderer in GameObject.Find(ViewRootName).GetComponentsInChildren<MeshRenderer>())
            {
                // Occluders and depth corrections are invisible anyway and keep working for the real world.
                if (renderer.name != "Occluder" && !renderer.name.StartsWith("Depth"))
                {
                    Assert.IsTrue(renderer.forceRenderingOff, renderer.name + " should be hidden.");
                }
            }

            Assert.IsTrue(scanner.HasModel);
        }

        [UnityTest]
        public IEnumerator ScannedFloor_GetsADepthCorrection_ThatKeepsWorkingWhenPlanesAreHidden()
        {
            scanner.StartScan();
            yield return WaitForModel();
            scanner.SetVisible(false);

            MeshRenderer fill = null, mark = null, unmark = null, write = null;
            foreach (var renderer in GameObject.Find(ViewRootName).GetComponentsInChildren<MeshRenderer>())
            {
                switch (renderer.name)
                {
                    case "Fill": fill = renderer; break;
                    case "Depth Mark": mark = renderer; break;
                    case "Depth Unmark": unmark = renderer; break;
                    case "Depth Write": write = renderer; break;
                }
            }

            Assert.IsNotNull(mark, "The scanned floor should get a depth mark pass.");
            Assert.IsNotNull(unmark, "The scanned floor should get a depth unmark pass.");
            Assert.IsNotNull(write, "The scanned floor should get a depth write pass.");
            Assert.AreEqual(RenderOrder.SurfaceDepth, mark.sharedMaterial.renderQueue);
            Assert.AreEqual(RenderOrder.SurfaceDepth + 1, unmark.sharedMaterial.renderQueue);
            Assert.AreEqual(RenderOrder.SurfaceDepth + 2, write.sharedMaterial.renderQueue);
            Assert.IsTrue(mark.enabled && !mark.forceRenderingOff);
            Assert.IsTrue(unmark.enabled && !unmark.forceRenderingOff);
            Assert.IsTrue(write.enabled && !write.forceRenderingOff);
            Assert.IsTrue(fill.forceRenderingOff, "The grid is hidden.");

            // The correction covers everything the grid covers, and a little more.
            var steady = mark.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreSame(steady, write.GetComponent<MeshFilter>().sharedMesh);
            var drawn = fill.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.LessOrEqual(steady.bounds.min.x, drawn.min.x + 1e-4f);
            Assert.LessOrEqual(steady.bounds.min.z, drawn.min.z + 1e-4f);
            Assert.GreaterOrEqual(steady.bounds.max.x, drawn.max.x - 1e-4f);
            Assert.GreaterOrEqual(steady.bounds.max.z, drawn.max.z - 1e-4f);
            Assert.AreEqual(scanner.Map.Levels[0].DrawnHeight, mark.transform.position.y, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ResetScan_ForgetsTheModel()
        {
            scanner.StartScan();
            yield return WaitForModel();
            scanner.FinishScan();

            scanner.ResetScan();

            Assert.IsFalse(scanner.HasModel);
            Assert.IsFalse(scanner.TryGetPose(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out _));
        }

        IEnumerator WaitForModel()
        {
            var giveUpAt = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!scanner.HasModel && Time.realtimeSinceStartup < giveUpAt)
            {
                yield return null;
            }
        }

        /// <summary>A depth camera that always sees the same floor with upward-facing points.</summary>
        sealed class FloorSource : IScanFrameSource
        {
            readonly Vector3 origin;

            public FloorSource(Vector3 cameraPosition)
            {
                origin = cameraPosition;
            }

            public bool Began { get; private set; }
            public bool Ended { get; private set; }
            public bool IsPrecise => true;
            public bool IsAvailable => true;

            public void Begin() => Began = true;

            public void End() => Ended = true;

            public bool TryReadFrame(ScanFrame frame)
            {
                frame.Reset(origin, true);
                for (var z = 0.02f; z < 2f; z += 0.04f)
                {
                    for (var x = -0.98f; x < 1f; x += 0.04f)
                    {
                        frame.Add(new Vector3(x, 0f, z), true);
                    }
                }

                return true;
            }
        }
    }
}
