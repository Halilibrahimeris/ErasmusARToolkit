using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>What checking the scanned model against the room showed.</summary>
    internal enum ScanVerdict
    {
        /// <summary>Too little was seen to tell.</summary>
        Unknown,

        /// <summary>The model lies where the room is.</summary>
        Matches,

        /// <summary>The model is a few centimeters too high or too low, and right otherwise.</summary>
        Shifted,

        /// <summary>The model is somewhere else: it has to be scanned again.</summary>
        Moved,
    }

    /// <summary>
    /// Checks a scanned model against what the camera sees now, without changing it. Used after ARCore
    /// lost tracking, after the app was paused and when the anchor of the scan jumped: the model may then
    /// lie beside the room. Frames are given in the space of the model.
    /// </summary>
    internal sealed class ScanVerifier
    {
        /// <summary>The floor may be this far (meters) off without anything being done...</summary>
        public const float Tolerance = 0.02f;

        /// <summary>...and up to this far off, the model is moved to it.</summary>
        public const float MaxShift = 0.06f;

        /// <summary>Share of the points on raised surfaces that have to land on their drawn cells.</summary>
        public const float MinAgreement = 0.6f;

        /// <summary>Fewer points on raised surfaces than this say nothing.</summary>
        public const int MinRaisedPoints = 100;

        /// <summary>Observations (distinct cells, summed over frames) of the floor that are needed.</summary>
        public const float MinFloorCells = 120f;

        const float FloorWindow = 0.15f;

        /// <summary>Surfaces the model does not know are looked for this far (meters) above and below its floor.</summary>
        const float SearchWindow = 0.6f;

        const float HeightBinSize = 0.02f;

        readonly HeightHistogram floorHeights = new HeightHistogram();
        readonly HeightHistogram unknownHeights = new HeightHistogram();
        readonly FrameCellSet frameCells = new FrameCellSet();
        RoomSurfaceMap map;
        int raisedPoints;
        int raisedAgreeing;

        public int Frames { get; private set; }

        /// <summary>Share (0..1) of the points on raised surfaces that landed on drawn cells.</summary>
        public float Agreement => raisedPoints > 0 ? (float)raisedAgreeing / raisedPoints : 1f;

        public void Begin(RoomSurfaceMap scannedMap)
        {
            map = scannedMap;
            floorHeights.Clear();
            unknownHeights.Clear();
            raisedPoints = 0;
            raisedAgreeing = 0;
            Frames = 0;
        }

        public void Add(ScanFrame frame)
        {
            if (map == null || frame == null || frame.Count == 0)
            {
                return;
            }

            Frames++;
            frameCells.NextFrame();
            var floor = map.Floor.Level;
            var origin = frame.Origin;
            for (var i = 0; i < frame.Count; i++)
            {
                if (frame.ConfidenceAt(i) < RoomSurfaceMap.HeightConfidence
                    || (frame.HasNormals && !frame.IsUpFacing(i)))
                {
                    continue;
                }

                var point = frame.PointAt(i);
                var distance = Vector3.Distance(origin, point);
                if (distance < RoomSurfaceMap.MinHeightRange || distance > RoomSurfaceMap.MaxHeightRange)
                {
                    continue;
                }

                var cell = RoomSurfaceMap.CellOf(point.x, point.z);
                var fromFloor = floor != null ? Mathf.Abs(point.y - floor.Height) : float.MaxValue;
                if (fromFloor <= FloorWindow)
                {
                    if (frameCells.Add(cell.x, cell.y, Mathf.FloorToInt(point.y / HeightBinSize)))
                    {
                        floorHeights.Add(point.y, DepthNoise.HeightWeight(distance), 1f);
                    }
                }
                else if (!NoteRaised(point, cell, DepthNoise.HitTolerance(distance), floor) && fromFloor <= SearchWindow
                         && frameCells.Add(cell.x, cell.y, Mathf.FloorToInt(point.y / HeightBinSize)))
                {
                    // A surface where the model has none.
                    unknownHeights.Add(point.y, DepthNoise.HeightWeight(distance), 1f);
                }
            }
        }

        public ScanVerdict Finish(out float floorShift)
        {
            floorShift = 0f;
            var floor = map != null ? map.Floor.Level : null;
            if (floor == null || !floorHeights.TryStrongestMode(MinFloorCells, out var measured))
            {
                // Without the floor in view, what is seen can only say that the model is wrong: a large
                // surface the model does not know, or raised surfaces that are not where they were.
                var strangeSurface = floor != null && unknownHeights.TryStrongestMode(MinFloorCells, out _);
                return strangeSurface || (raisedPoints >= MinRaisedPoints && Agreement < MinAgreement)
                    ? ScanVerdict.Moved
                    : ScanVerdict.Unknown;
            }

            floorShift = measured - floor.Height;
            var agrees = raisedPoints < MinRaisedPoints || Agreement >= MinAgreement;
            var off = Mathf.Abs(floorShift);
            if (!agrees || off > MaxShift)
            {
                return ScanVerdict.Moved;
            }

            return off <= Tolerance ? ScanVerdict.Matches : ScanVerdict.Shifted;
        }

        /// <summary>True when the point lies at the height of a raised surface of the model.</summary>
        bool NoteRaised(Vector3 point, Vector2Int cell, float tolerance, SurfaceLevel floor)
        {
            SurfaceLevel nearest = null;
            var nearestDistance = tolerance;
            foreach (var level in map.Levels)
            {
                var distance = Mathf.Abs(level.Height - point.y);
                if (level != floor && level.IsUsable && level.ShownArea > 0f && distance <= nearestDistance)
                {
                    nearest = level;
                    nearestDistance = distance;
                }
            }

            if (nearest == null)
            {
                return false;
            }

            raisedPoints++;
            if (nearest.IsShown(cell))
            {
                raisedAgreeing++;
            }

            return true;
        }
    }
}
