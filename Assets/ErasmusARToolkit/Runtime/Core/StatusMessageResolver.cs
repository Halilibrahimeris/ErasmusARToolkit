namespace Erasmus.ARToolkit
{
    /// <summary>Snapshot of everything the status line depends on.</summary>
    public readonly struct StatusContext
    {
        public StatusContext(ToolkitMode mode, bool isFallback, bool isTracking, int planeCount,
            int placedCount, bool hasPlaceables, bool isCameraFeedActive = false, bool isScanning = false)
        {
            Mode = mode;
            IsFallback = isFallback;
            IsTracking = isTracking;
            PlaneCount = planeCount;
            PlacedCount = placedCount;
            HasPlaceables = hasPlaceables;
            IsCameraFeedActive = isCameraFeedActive;
            IsScanning = isScanning;
        }

        public ToolkitMode Mode { get; }
        public bool IsFallback { get; }
        public bool IsTracking { get; }
        public int PlaneCount { get; }
        public int PlacedCount { get; }
        public bool HasPlaceables { get; }

        /// <summary>A webcam / phone camera image is shown behind the scene in Desktop mode.</summary>
        public bool IsCameraFeedActive { get; }

        /// <summary>The room scan is running (AR mode).</summary>
        public bool IsScanning { get; }
    }

    public static class StatusMessages
    {
        public const string Checking = "Checking AR support...";
        public const string MovePhone = "Move your phone slowly to start tracking.";
        public const string ScanSurfaces = "Point the camera at the floor or a table and move slowly, or tap Scan Room.";
        public const string RoomScan = "Room scan: walk slowly and look at the floor and every table top. Tap Finish Scan when done.";
        public const string TapToPlace = "Tap a highlighted surface to place the selected object.";
        public const string TouchManipulate = "Drag to move, pinch to scale, twist to rotate. Tap an object to edit it.";
        public const string LookForImages = "Point the camera at one of the image targets.";
        public const string DesktopPlace = "Desktop mode: click the grid to place. Right-drag to orbit, scroll to zoom.";
        public const string CameraPlace = "Camera view: click to place objects on the virtual floor. Right-drag to adjust the view.";
        public const string DesktopManipulate = "Drag to move, Q/E to rotate, Shift+scroll to scale, Delete to remove.";
        public const string Fallback = "AR is not supported on this device. Running in Desktop mode.";
        public const string FallbackCamera = "AR is not supported on this device. Showing the camera; tap to place objects.";
    }

    /// <summary>Chooses the short hint shown at the top of the HUD.</summary>
    public static class StatusMessageResolver
    {
        public static string Resolve(StatusContext context)
        {
            switch (context.Mode)
            {
                case ToolkitMode.AR:
                    return ResolveAR(context);
                case ToolkitMode.Desktop:
                    return ResolveDesktop(context);
                default:
                    return StatusMessages.Checking;
            }
        }

        static string ResolveAR(StatusContext context)
        {
            if (!context.IsTracking)
            {
                return StatusMessages.MovePhone;
            }

            if (context.IsScanning)
            {
                return StatusMessages.RoomScan;
            }

            if (!context.HasPlaceables)
            {
                return StatusMessages.LookForImages;
            }

            if (context.PlacedCount > 0)
            {
                return StatusMessages.TouchManipulate;
            }

            return context.PlaneCount == 0 ? StatusMessages.ScanSurfaces : StatusMessages.TapToPlace;
        }

        static string ResolveDesktop(StatusContext context)
        {
            if (context.IsFallback)
            {
                return context.IsCameraFeedActive ? StatusMessages.FallbackCamera : StatusMessages.Fallback;
            }

            if (context.PlacedCount > 0)
            {
                return StatusMessages.DesktopManipulate;
            }

            return context.IsCameraFeedActive ? StatusMessages.CameraPlace : StatusMessages.DesktopPlace;
        }
    }
}
