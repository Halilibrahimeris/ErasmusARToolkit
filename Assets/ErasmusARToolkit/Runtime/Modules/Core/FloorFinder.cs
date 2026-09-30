using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>Where a floor height came from.</summary>
    public enum FloorSource
    {
        /// <summary>The floor of the room scan.</summary>
        Scan,

        /// <summary>The lowest floor-sized plane ARCore found below the phone.</summary>
        Plane,

        /// <summary>The virtual ground of Desktop mode.</summary>
        VirtualGround,

        /// <summary>Nothing found yet: the phone's height above the floor was assumed.</summary>
        Fallback,
    }

    public readonly struct FloorEstimate
    {
        public FloorEstimate(float height, FloorSource source)
        {
            Height = height;
            Source = source;
        }

        /// <summary>World height (y) of the floor.</summary>
        public float Height { get; }

        public FloorSource Source { get; }

        /// <summary>False while the height is only assumed.</summary>
        public bool IsMeasured => Source != FloorSource.Fallback;
    }

    /// <summary>
    /// The height of the floor under the user: the scanned floor, else the lowest ARCore floor below the
    /// phone, else the Desktop virtual ground, else the phone's height minus how high a phone is held.
    /// </summary>
    public static class FloorFinder
    {
        /// <summary>A plane further below the phone than this is a reflection or a wrong guess.</summary>
        public const float MaxDepthBelowCamera = 3f;

        /// <summary>Planes smaller than this (square meters) do not count as the floor.</summary>
        public const float MinPlaneArea = 0.5f;

        public static FloorEstimate Find(ARToolkitManager manager, float phoneHeight = 1.5f)
        {
            var camera = manager != null ? manager.ActiveCamera : null;
            var cameraY = camera != null ? camera.transform.position.y : 0f;
            var scanFloor = float.NaN;
            var planeFloor = float.NaN;
            float? ground = null;
            if (manager != null && manager.Mode == ToolkitMode.AR)
            {
                if (manager.RoomScanner != null)
                {
                    scanFloor = manager.RoomScanner.FloorHeight;
                }

                if (manager.PlaneManager != null)
                {
                    planeFloor = LowestFloorPlane(PlaneSamples(manager), cameraY);
                }
            }
            else if (manager != null && manager.Mode == ToolkitMode.Desktop && manager.DesktopGround != null)
            {
                ground = manager.DesktopGround.Height;
            }

            return Resolve(scanFloor, planeFloor, ground, cameraY, phoneHeight);
        }

        /// <param name="scanFloor">The scanned floor, NaN if none.</param>
        /// <param name="planeFloor">The lowest ARCore floor, NaN if none.</param>
        /// <param name="virtualGround">The Desktop ground, null outside Desktop mode.</param>
        internal static FloorEstimate Resolve(float scanFloor, float planeFloor, float? virtualGround, float cameraY,
            float phoneHeight)
        {
            if (!float.IsNaN(scanFloor))
            {
                return new FloorEstimate(scanFloor, FloorSource.Scan);
            }

            if (!float.IsNaN(planeFloor))
            {
                return new FloorEstimate(planeFloor, FloorSource.Plane);
            }

            if (virtualGround.HasValue)
            {
                return new FloorEstimate(virtualGround.Value, FloorSource.VirtualGround);
            }

            return new FloorEstimate(cameraY - Mathf.Max(0f, phoneHeight), FloorSource.Fallback);
        }

        internal readonly struct PlaneSample
        {
            public PlaneSample(float y, bool isHorizontalUp, bool isTracking, bool isSubsumed, float area)
            {
                Y = y;
                IsHorizontalUp = isHorizontalUp;
                IsTracking = isTracking;
                IsSubsumed = isSubsumed;
                Area = area;
            }

            public float Y { get; }
            public bool IsHorizontalUp { get; }
            public bool IsTracking { get; }
            public bool IsSubsumed { get; }
            public float Area { get; }
        }

        /// <summary>The lowest tracked, floor-sized, upward plane below the camera, or NaN.</summary>
        internal static float LowestFloorPlane(IEnumerable<PlaneSample> planes, float cameraY)
        {
            var lowest = float.NaN;
            foreach (var plane in planes)
            {
                if (!plane.IsHorizontalUp || !plane.IsTracking || plane.IsSubsumed || plane.Area < MinPlaneArea
                    || plane.Y >= cameraY || cameraY - plane.Y > MaxDepthBelowCamera)
                {
                    continue;
                }

                if (float.IsNaN(lowest) || plane.Y < lowest)
                {
                    lowest = plane.Y;
                }
            }

            return lowest;
        }

        static IEnumerable<PlaneSample> PlaneSamples(ARToolkitManager manager)
        {
            foreach (var plane in manager.PlaneManager.trackables)
            {
                yield return new PlaneSample(plane.transform.position.y,
                    plane.alignment == PlaneAlignment.HorizontalUp,
                    plane.trackingState == TrackingState.Tracking,
                    plane.subsumedBy != null,
                    plane.size.x * plane.size.y);
            }
        }
    }
}
