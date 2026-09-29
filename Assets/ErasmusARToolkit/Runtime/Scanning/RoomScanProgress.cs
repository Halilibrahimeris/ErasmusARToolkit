using System.Globalization;

namespace Erasmus.ARToolkit
{
    /// <summary>What the room scan has found so far.</summary>
    public readonly struct RoomScanProgress
    {
        public RoomScanProgress(bool isScanning, int surfaceCount, float surfaceArea, float checkedArea, bool isPrecise,
            bool floorFound = true)
        {
            IsScanning = isScanning;
            SurfaceCount = surfaceCount;
            SurfaceArea = surfaceArea;
            CheckedArea = checkedArea;
            IsPrecise = isPrecise;
            FloorFound = floorFound;
        }

        public bool IsScanning { get; }

        /// <summary>Surfaces (heights) with something drawn on them.</summary>
        public int SurfaceCount { get; }

        /// <summary>Drawn surface area in square meters.</summary>
        public float SurfaceArea { get; }

        /// <summary>Floor area the camera has checked, in square meters.</summary>
        public float CheckedArea { get; }

        /// <summary>False when the phone has no depth sensing and tracking points are used instead.</summary>
        public bool IsPrecise { get; }

        /// <summary>The scan knows which surface the floor is.</summary>
        public bool FloorFound { get; }
    }

    /// <summary>Texts the HUD shows for the room scan.</summary>
    public static class RoomScanText
    {
        public const string ScanButton = "Scan Room";
        public const string FinishButton = "Finish Scan";
        public const string NoSurfaces = "No surfaces found. Tap Scan Room and move more slowly.";
        public const string NoScannedSurface = "No scanned surface there. Tap Scan Room to add more.";
        public const string ScanReset = "Scan cleared. Look around again.";
        public const string LessPrecise =
            "This phone has no depth sensing, so the scan uses tracking points and is less exact.";
        public const string DepthUnusable =
            "The depth image of this phone cannot be used, so the scan uses tracking points and is less exact.";
        public const string DepthRecalibrated = "The depth image was read the wrong way. The scan starts again.";
        public const string ShinyFloor =
            "Shiny or plain floor: the phone measures it poorly. Move slowly and turn on more light.";
        public const string FindingFloor = "point the phone at the floor near your feet";
        public const string SetFloorButton = "Set Floor";
        public const string FloorSet = "This surface is the floor now.";
        public const string ScanMoved =
            "The scan no longer matches the room. Tap Scan Room, then Reset, and scan again.";
        public const string TooFar =
            "This is too far from where the scan started. Nothing is added here.";

        public static string ButtonText(bool isScanning) => isScanning ? FinishButton : ScanButton;

        public static string Progress(RoomScanProgress progress)
        {
            var text = $"Scanning · {Surfaces(progress.SurfaceCount)} · {Area(progress.CheckedArea)} m² checked";
            if (!progress.FloorFound)
            {
                text += " · " + FindingFloor;
            }

            return progress.IsPrecise ? text : text + " (tracking points)";
        }

        public static string Finished(RoomScanProgress progress)
        {
            return $"Scan finished: {Surfaces(progress.SurfaceCount)}, {Area(progress.SurfaceArea)} m².";
        }

        static string Area(float squareMeters) => squareMeters.ToString("0.0", CultureInfo.InvariantCulture);

        static string Surfaces(int count) => count == 1 ? "1 surface" : count + " surfaces";
    }
}
