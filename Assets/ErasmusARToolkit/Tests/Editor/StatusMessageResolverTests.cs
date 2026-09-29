using NUnit.Framework;

namespace Erasmus.ARToolkit.Tests
{
    public class StatusMessageResolverTests
    {
        static StatusContext Context(ToolkitMode mode, bool fallback = false, bool tracking = true,
            int planes = 0, int placed = 0, bool hasPlaceables = true, bool camera = false)
        {
            return new StatusContext(mode, fallback, tracking, planes, placed, hasPlaceables, camera);
        }

        [Test]
        public void Desktop_WithCameraFeed_ExplainsCameraView()
        {
            Assert.AreEqual(StatusMessages.CameraPlace,
                StatusMessageResolver.Resolve(Context(ToolkitMode.Desktop, camera: true)));
            Assert.AreEqual(StatusMessages.DesktopManipulate,
                StatusMessageResolver.Resolve(Context(ToolkitMode.Desktop, placed: 1, camera: true)));
        }

        [Test]
        public void Fallback_WithCameraFeed_SaysCameraIsShown()
        {
            Assert.AreEqual(StatusMessages.FallbackCamera,
                StatusMessageResolver.Resolve(Context(ToolkitMode.Desktop, fallback: true, camera: true)));
        }

        [Test]
        public void Undetermined_ShowsChecking()
        {
            Assert.AreEqual(StatusMessages.Checking,
                StatusMessageResolver.Resolve(Context(ToolkitMode.Undetermined)));
        }

        [Test]
        public void AR_NotTracking_AsksToMovePhone()
        {
            Assert.AreEqual(StatusMessages.MovePhone,
                StatusMessageResolver.Resolve(Context(ToolkitMode.AR, tracking: false)));
        }

        [Test]
        public void AR_TrackingWithoutPlanes_AsksToScan()
        {
            Assert.AreEqual(StatusMessages.ScanSurfaces,
                StatusMessageResolver.Resolve(Context(ToolkitMode.AR, planes: 0)));
        }

        [Test]
        public void AR_WithPlanes_AsksToTap_ThenShowsManipulationHelp()
        {
            Assert.AreEqual(StatusMessages.TapToPlace,
                StatusMessageResolver.Resolve(Context(ToolkitMode.AR, planes: 2)));
            Assert.AreEqual(StatusMessages.TouchManipulate,
                StatusMessageResolver.Resolve(Context(ToolkitMode.AR, planes: 2, placed: 1)));
        }

        [Test]
        public void Desktop_ShowsDesktopHelp_OrFallbackWarning()
        {
            Assert.AreEqual(StatusMessages.DesktopPlace,
                StatusMessageResolver.Resolve(Context(ToolkitMode.Desktop)));
            Assert.AreEqual(StatusMessages.DesktopManipulate,
                StatusMessageResolver.Resolve(Context(ToolkitMode.Desktop, placed: 3)));
            Assert.AreEqual(StatusMessages.Fallback,
                StatusMessageResolver.Resolve(Context(ToolkitMode.Desktop, fallback: true)));
        }

        [Test]
        public void AR_Scanning_ExplainsTheRoomScan_OnceTracking()
        {
            Assert.AreEqual(StatusMessages.RoomScan, StatusMessageResolver.Resolve(
                new StatusContext(ToolkitMode.AR, false, true, 2, 1, true, false, true)));
            Assert.AreEqual(StatusMessages.TrackingLostWhileScanning, StatusMessageResolver.Resolve(
                new StatusContext(ToolkitMode.AR, false, false, 2, 1, true, false, true)));
            Assert.AreEqual(StatusMessages.MovePhone, StatusMessageResolver.Resolve(
                new StatusContext(ToolkitMode.AR, false, false, 2, 1, true, false, false)));
        }

        [Test]
        public void NoPlaceables_PointsToImageTargets()
        {
            Assert.AreEqual(StatusMessages.LookForImages,
                StatusMessageResolver.Resolve(Context(ToolkitMode.AR, planes: 2, hasPlaceables: false)));
        }
    }
}
