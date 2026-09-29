using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// The room scan component on an XR Origin as the toolkit builds it, with simulated sources: what
    /// happens to the ARCore planes while the scan starts, when the depth image turns out to be unusable,
    /// and when the user holds the phone still.
    /// </summary>
    public class RoomScannerSourcesPlayModeTests
    {
        const float TimeoutSeconds = 5f;
        const string ViewRootName = "Room Surfaces (scanned)";

        readonly List<string> notifications = new List<string>();
        GameObject rig;
        Camera arCamera;
        Material material;
        RoomScanner scanner;
        ARPlaneSurfaces planes;
        GatedSource depth;
        SimulatedRoomSource points;

        [SetUp]
        public void SetUp()
        {
            notifications.Clear();
            rig = new GameObject("Test XR Origin");
            rig.SetActive(false);
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(rig.transform, false);
            var cameraObject = new GameObject("Test AR Camera", typeof(Camera));
            cameraObject.transform.SetParent(offset.transform, false);
            cameraObject.transform.position = new Vector3(0f, 1.4f, -1f);
            cameraObject.transform.LookAt(new Vector3(0f, 0f, 0.8f));
            arCamera = cameraObject.GetComponent<Camera>();

            var origin = rig.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = offset;
            origin.Camera = arCamera;
            origin.CameraYOffset = 0f;
            rig.AddComponent<ARPlaneManager>();
            planes = rig.AddComponent<ARPlaneSurfaces>();
            scanner = rig.AddComponent<RoomScanner>();
            scanner.Notification += notifications.Add;

            material = new Material(Shader.Find("Erasmus AR Toolkit/AR Surface"));
            var room = new RoomSimulator(new SimulationOptions { Columns = 100, Rows = 100 });
            depth = new GatedSource(new SimulatedRoomSource(room, cameraObject.transform));
            points = new SimulatedRoomSource(room, cameraObject.transform, precise: false);
            scanner.ConfigureForTests(arCamera, depth, points, () => true, material, material, null);
            rig.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(rig);
            UnityEngine.Object.DestroyImmediate(material);
            var leftover = GameObject.Find(ViewRootName);
            if (leftover != null)
            {
                UnityEngine.Object.DestroyImmediate(leftover);
            }
        }

        [UnityTest]
        public IEnumerator ArcorePlanes_StayVisibleUntilTheFirstDepthFrame()
        {
            depth.Delivers = false;
            scanner.StartScan();
            yield return WaitUntil(() => depth.Reads >= 3);

            Assert.IsTrue(scanner.IsScanning);
            Assert.IsFalse(planes.IsSuppressed, "Until depth delivers, the ARCore planes are all there is to see.");

            depth.Delivers = true;
            yield return WaitUntil(() => scanner.Map.FramesIntegrated > 0);
            yield return null;

            Assert.IsTrue(planes.IsSuppressed);
        }

        [UnityTest]
        public IEnumerator ResetScan_WhileScanning_ShowsArcorePlanesAgain()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            Assert.IsTrue(planes.IsSuppressed);

            depth.Delivers = false;
            yield return null;
            scanner.ResetScan();

            Assert.IsTrue(scanner.IsScanning);
            Assert.IsFalse(scanner.HasModel);
            Assert.IsFalse(planes.IsSuppressed);
            CollectionAssert.Contains(notifications, RoomScanText.ScanReset);

            depth.Delivers = true;
            yield return WaitUntil(() => scanner.HasModel);
            Assert.IsTrue(planes.IsSuppressed);
        }

        [UnityTest]
        public IEnumerator FinishedScan_KeepsTheArcorePlanesHidden_UntilItIsReset()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            scanner.FinishScan();
            Assert.IsTrue(planes.IsSuppressed);

            scanner.ResetScan();

            Assert.IsFalse(planes.IsSuppressed);
        }

        [UnityTest]
        public IEnumerator MappingNeverVerified_FallsBackToTrackingPoints()
        {
            depth.Delivers = false;
            depth.Mapping = MappingState.Checking;
            UseTheFloorArcoreFound();
            scanner.StartScan();
            yield return WaitUntil(() => depth.Reads >= 3);
            Assert.IsFalse(scanner.HasModel);

            depth.Mapping = MappingState.Unverifiable;
            depth.IsAvailable = false;
            yield return WaitUntil(() => scanner.HasModel);

            Assert.IsTrue(scanner.HasModel, "The scan should go on with tracking points.");
            Assert.IsTrue(depth.Ended);
            Assert.IsTrue(points.Began);
            Assert.IsFalse(scanner.Progress.IsPrecise);
            CollectionAssert.Contains(notifications, RoomScanText.DepthUnusable);
            CollectionAssert.DoesNotContain(notifications, RoomScanText.LessPrecise);
            Assert.IsFalse(scanner.Map.PriorsNeedConfirmation);
        }

        [UnityTest]
        public IEnumerator PhoneWithoutDepth_SaysThatTheScanIsLessExact()
        {
            depth.IsAvailable = false;
            UseTheFloorArcoreFound();
            scanner.StartScan();
            Assert.IsTrue(planes.IsSuppressed, "Without depth the scan draws the planes itself, from the start.");
            yield return WaitUntil(() => scanner.HasModel);

            Assert.IsTrue(scanner.HasModel);
            Assert.IsFalse(scanner.Progress.IsPrecise);
            Assert.IsTrue(planes.IsSuppressed);
            CollectionAssert.Contains(notifications, RoomScanText.LessPrecise);
        }

        [UnityTest]
        public IEnumerator MappingChanged_StartsTheScanOver()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);
            var before = scanner.Map.FramesIntegrated;
            Assert.Greater(before, 1);

            depth.Delivers = false;
            depth.RaiseMappingChanged();
            yield return null;
            yield return null;

            Assert.AreEqual(0, scanner.Map.FramesIntegrated, "What was scanned with the wrong mapping is dropped.");
            Assert.IsFalse(scanner.HasModel);
            Assert.IsTrue(scanner.IsScanning);
            CollectionAssert.Contains(notifications, RoomScanText.DepthRecalibrated);
        }

        [UnityTest]
        public IEnumerator UserStandsStill_EvidenceGrowsAtHalfRate()
        {
            var cell = RoomSurfaceMap.CellOf(0f, 0.8f);
            scanner.StartScan();
            var startedAt = Time.unscaledTime;
            yield return WaitUntil(() => scanner.Map.FramesIntegrated >= 6);
            var firstFramesTook = Time.unscaledTime - startedAt;
            var afterFirstFrames = Time.unscaledTime;
            var evidenceBefore = scanner.Map.Levels[0].GetLogOdds(cell);
            yield return new WaitForSecondsRealtime(1.6f);
            var seconds = Time.unscaledTime - afterFirstFrames;

            var frames = scanner.Map.FramesIntegrated - 6;
            Assert.Less(firstFramesTook, 2f, "The first frames always count, eight times a second.");
            Assert.GreaterOrEqual(frames, 1, "A view that stays the same is still looked at, in case the room changes.");
            Assert.LessOrEqual(frames, Mathf.CeilToInt(seconds / 0.5f) + 1,
                "A phone that is held still shows the same view: it counts twice a second, not eight times.");
            Assert.Greater(evidenceBefore, 0f);
            Assert.AreEqual(evidenceBefore + frames * 0.5f * SurfaceEvidence.HitWeight,
                scanner.Map.Levels[0].GetLogOdds(cell), 1e-3f);
        }

        [UnityTest]
        public IEnumerator SetFloorAt_MakesTheSurfaceUnderThePointerTheFloor()
        {
            scanner.StartScan();
            yield return WaitUntil(() => scanner.HasModel);

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Assert.IsTrue(scanner.SetFloorAt(center));

            Assert.AreEqual(FloorState.Trusted, scanner.Map.FloorState);
            Assert.AreEqual(FloorState.Trusted, scanner.Diagnostics.Floor);
            Assert.AreEqual(0f, scanner.Diagnostics.FloorHeight, 0.01f);
            Assert.IsTrue(scanner.Progress.FloorFound);
            CollectionAssert.Contains(notifications, RoomScanText.FloorSet);
        }

        [UnityTest]
        public IEnumerator SetFloorAt_WithoutASurfaceThere_SaysSo()
        {
            scanner.StartScan();
            yield return null;

            Assert.IsFalse(scanner.SetFloorAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)));

            CollectionAssert.Contains(notifications, RoomScanText.NoScannedSurface);
        }

        /// <summary>
        /// Tracking points have no normals, so they find no surfaces by themselves: on a phone the
        /// planes ARCore detected tell the scan where to look.
        /// </summary>
        void UseTheFloorArcoreFound()
        {
            scanner.Map.EnsureLevel(0f);
        }

        static IEnumerator WaitUntil(Func<bool> condition)
        {
            var giveUpAt = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < giveUpAt)
            {
                yield return null;
            }
        }

        /// <summary>A depth source that can be told to deliver nothing, and says what a real one says about itself.</summary>
        sealed class GatedSource : IScanFrameSource, IScanSourceDiagnostics
        {
            readonly SimulatedRoomSource inner;

            public GatedSource(SimulatedRoomSource inner)
            {
                this.inner = inner;
            }

            public event Action MappingChanged;

            public bool Delivers { get; set; } = true;
            public int Reads { get; private set; }
            public bool Ended => inner.Ended;
            public bool IsPrecise => true;
            public bool IsAvailable { get; set; } = true;
            public MappingState Mapping { get; set; } = MappingState.Verified;
            public float DepthLagMs => 0f;
            public int DepthWidth => 160;
            public int DepthHeight => 120;
            public float ConfidentRatio => 1f;
            public float CaptureMs => 0f;

            public float MappingError(int candidate) => float.NaN;

            public void RaiseMappingChanged() => MappingChanged?.Invoke();

            public void Begin() => inner.Begin();

            public void End() => inner.End();

            public bool TryReadFrame(ScanFrame frame)
            {
                Reads++;
                return Delivers && inner.TryReadFrame(frame);
            }
        }
    }
}
