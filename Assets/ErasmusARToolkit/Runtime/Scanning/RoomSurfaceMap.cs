using System;
using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The room model built by the room scan: every horizontal surface height (<see cref="SurfaceLevel"/>)
    /// with what is known about each 5 cm cell at that height. Like the space setup of a VR headset it
    /// fuses many camera frames instead of trusting one:
    /// <list type="bullet">
    /// <item>A depth point on an upward-facing surface near a level's height is a hit for its cell.</item>
    /// <item>A camera ray that passes a level's height and ends clearly beyond it is a miss for the cell
    /// where it crossed: the camera saw through that spot, so nothing lies there at that height. This
    /// removes the parts of ARCore planes that reach past a table edge (ARCore planes are convex hulls).</item>
    /// <item>Planes ARCore detected give their cells a head start (<see cref="AddPrior"/>).</item>
    /// <item>The floor is found first (<see cref="FloorModel"/>). Nothing can be seen through a floor, so
    /// a depth point below a trusted floor is a reflection or a bad measurement: its ray is cut off at the
    /// floor. It carves nothing, makes no level, and where the floor is known it counts as a weak sign of
    /// the floor. Without this rule a shiny floor gets holes and a second, lower "floor".</item>
    /// </list>
    /// How close a point must be depends on its distance (<see cref="DepthNoise"/>), and how much it counts
    /// on how sure ARCore is of it. Levels closer than <see cref="LevelMergeDistance"/> are one surface. A
    /// dense group of upward-facing depth points at a new height adds a level.
    /// </summary>
    public sealed class RoomSurfaceMap
    {
        public const float CellSize = 0.05f;

        /// <summary>Surfaces closer than this in height (meters) are one surface.</summary>
        public const float LevelMergeDistance = 0.08f;

        public const float MinRange = 0.3f;
        public const float MaxRange = 4.5f;

        /// <summary>Points farther than this (meters) confirm nothing: their error is as large as the tolerance.</summary>
        public const float MaxHitRange = 3.5f;

        public const float MinHitRange = 0.4f;

        /// <summary>Only points in this range (meters) are exact enough to measure a height.</summary>
        public const float MinHeightRange = 0.5f;

        public const float MaxHeightRange = 3f;

        /// <summary>ARCore confidence (0..255) a depth point needs to confirm a cell (what Google recommends).</summary>
        public const byte MinConfidence = 128;

        /// <summary>Confidence a depth point needs to count toward a height.</summary>
        public const byte HeightConfidence = 160;

        /// <summary>Confidence a depth point needs to carve: removing is worse than not adding.</summary>
        public const byte CarveConfidence = 180;

        /// <summary>Flatter rays are skipped: where they cross a height depends too much on depth noise.</summary>
        public const float MinRayVertical = 0.15f;

        /// <summary>A detected plane this close (meters) to the floor is a plane of the floor.</summary>
        public const float FloorPlaneDistance = 0.15f;

        /// <summary>More levels than this are not kept: the weakest go.</summary>
        public const int MaxLevels = 12;

        [Obsolete("Levels are created and merged with LevelMergeDistance.")]
        public const float RefinedMergeDistance = LevelMergeDistance;

        [Obsolete("The tolerance depends on the distance: see DepthNoise.HitTolerance.")]
        public const float HitTolerance = DepthNoise.MinHitTolerance;

        [Obsolete("The tolerance depends on the distance: see DepthNoise.HitTolerance.")]
        public const float HitToleranceSlope = 0.01f;

        [Obsolete("The margin depends on the distance: see DepthNoise.MissMargin.")]
        public const float MissMargin = DepthNoise.MinMissMargin;

        [Obsolete("The margin depends on the distance: see DepthNoise.MissMargin.")]
        public const float MissMarginSlope = 0.02f;

        const float DiscoveryBinSize = 0.02f;
        const float DiscoveryDecay = 0.7f;
        const float DiscoveryFrameCap = 60f;
        const float DiscoveryThreshold = 105f;
        const float DiscoveryForget = 0.5f;

        /// <summary>New surfaces are only looked for this far below the camera (not the ceiling).</summary>
        const float DiscoveryBelowCamera = 0.2f;

        /// <summary>...and, once the floor is trusted, this far above it.</summary>
        const float DiscoveryAboveFloor = 0.05f;

        const float HeightBinSize = 0.02f;
        const float HeightDecay = 0.97f;
        const float SparseHeightWeight = 0.5f;

        /// <summary>Observations (distinct cells, summed over frames) that make a height peak.</summary>
        const float FusionCells = 30f;

        /// <summary>
        /// A level that depth has found follows its own peak only. When it is out of view, the peak of a
        /// neighbor (the floor under a low step) must not take it along.
        /// </summary>
        const float OwnPeakRadius = 0.05f;

        /// <summary>Depth and a detected plane agree when depth is this far below the plane at most...</summary>
        const float AgreeBelow = 0.06f;

        /// <summary>...or this far above it.</summary>
        const float AgreeAbove = 0.08f;

        const float FloorClampDown = 0.02f;
        const float FloorClampUp = 0.03f;
        const float FloorClampUpSharp = 0.08f;
        const float RaisedClamp = 0.05f;

        /// <summary>Frames of a sharp, strong depth peak above the floor plane before depth overrules the plane.</summary>
        const int OverruleFrames = 40;

        const float OverruleCells = 300f;
        const float OverruleRadius = 0.025f;

        /// <summary>Levels below the floor are not hit by placement rays (meters below the drawn floor).</summary>
        const float FloorGuard = 0.02f;

        const float DefaultFrameSeconds = 0.125f;
        const float MinFrameWeight = 0.05f;
        const float PruneConfirmedArea = 0.1f;
        const int PruneAgeFrames = 80;

        const byte Usable = 1;
        const byte Clipped = 2;
        const byte CanHit = 4;
        const byte CanHeight = 8;
        const byte CanCarve = 16;
        const byte Reflection = 32;

        readonly List<SurfaceLevel> levels = new List<SurfaceLevel>();
        readonly Dictionary<long, Vote> votes = new Dictionary<long, Vote>();
        readonly HashSet<long> checkedCells = new HashSet<long>();
        readonly Dictionary<int, float> discovery = new Dictionary<int, float>();
        readonly Dictionary<int, float> discoveryThisFrame = new Dictionary<int, float>();
        readonly List<int> discoveryKeys = new List<int>();
        readonly List<float> crossings = new List<float>();
        readonly HeightHistogram heights = new HeightHistogram();
        readonly FrameCellSet frameCells = new FrameCellSet();
        readonly FloorModel floor = new FloorModel { PlanesNeedDepth = true };
        Vector3[] pointPositions = Array.Empty<Vector3>();
        float[] pointDistances = Array.Empty<float>();
        byte[] pointFlags = Array.Empty<byte>();
        Vector2Int[] pointCells = Array.Empty<Vector2Int>();
        bool priorsNeedConfirmation = true;
        bool inPriorBatch;
        int nextLevelId;
        double clock;

        public IReadOnlyList<SurfaceLevel> Levels => levels;

        /// <summary>Floor area (square meters) the camera has already checked at any height.</summary>
        public float CheckedArea => checkedCells.Count * CellSize * CellSize;

        public int FramesIntegrated { get; private set; }

        /// <summary>
        /// True (phones with depth): cells of a raised surface that lie in a detected plane show after a depth
        /// hit. False (phones without depth, which see too little to confirm): they show at once.
        /// </summary>
        public bool PriorsNeedConfirmation
        {
            get => priorsNeedConfirmation;
            set
            {
                if (priorsNeedConfirmation != value)
                {
                    priorsNeedConfirmation = value;
                    floor.PlanesNeedDepth = value;
                    UpdatePriorWeights();
                }
            }
        }

        /// <summary>How sure the scan is about the floor.</summary>
        public FloorState FloorState => floor.State;

        /// <summary>Depth points of the last frame that were seen through the floor.</summary>
        public int Reflections { get; private set; }

        /// <summary>Depth points of the last frame that lay too far below the camera to be real.</summary>
        public int Implausible { get; private set; }

        internal FloorModel Floor => floor;

        /// <summary>Two levels were one surface: the first id is gone, its cells are in the second.</summary>
        internal event Action<int, int> LevelMerged;

        internal event Action<int> LevelRemoved;

        public static Vector2Int CellOf(float x, float z)
        {
            return new Vector2Int(Mathf.FloorToInt(x / CellSize), Mathf.FloorToInt(z / CellSize));
        }

        public void Clear()
        {
            for (var i = 0; i < levels.Count; i++)
            {
                LevelRemoved?.Invoke(levels[i].Id);
            }

            levels.Clear();
            votes.Clear();
            checkedCells.Clear();
            discovery.Clear();
            heights.Clear();
            floor.Reset();
            inPriorBatch = false;
            clock = 0.0;
            FramesIntegrated = 0;
            Reflections = 0;
            Implausible = 0;
        }

        /// <summary>Index of the level at (about) this height, made if there is none.</summary>
        public int EnsureLevel(float height)
        {
            var nearest = NearestLevel(height, LevelMergeDistance);
            if (nearest >= 0)
            {
                return nearest;
            }

            var level = new SurfaceLevel(nextLevelId++, height, FramesIntegrated);
            level.SetPriorWeight(PriorWeightOf(level));
            if (inPriorBatch)
            {
                level.BeginPriors();
            }

            levels.Add(level);
            return levels.Count - 1;
        }

        public void ClearPriors()
        {
            foreach (var level in levels)
            {
                level.ClearPriors();
            }
        }

        /// <summary>
        /// Starts a new reading of the planes ARCore detected: call <see cref="AddPrior"/> for every plane
        /// and then <see cref="EndPriors"/>. Planes that did not change redraw nothing.
        /// </summary>
        internal void BeginPriors()
        {
            inPriorBatch = true;
            foreach (var level in levels)
            {
                level.BeginPriors();
            }
        }

        internal void EndPriors()
        {
            inPriorBatch = false;
            foreach (var level in levels)
            {
                level.EndPriors();
            }
        }

        /// <summary>
        /// Marks the cells inside a detected plane (x/z polygon, any simple shape) as probably a
        /// surface. Filled row by row (even-odd rule), so large planes stay cheap.
        /// </summary>
        public void AddPrior(float height, IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return;
            }

            // ARCore often adds planes a few centimeters above or below the floor: they are the floor.
            var level = floor.Level != null && Mathf.Abs(height - floor.Level.Height) <= FloorPlaneDistance
                ? floor.Level
                : levels[EnsureLevel(height)];
            var minZ = polygon[0].y;
            var maxZ = polygon[0].y;
            for (var i = 1; i < polygon.Count; i++)
            {
                minZ = Mathf.Min(minZ, polygon[i].y);
                maxZ = Mathf.Max(maxZ, polygon[i].y);
            }

            var cells = 0;
            var lastRow = Mathf.FloorToInt(maxZ / CellSize);
            for (var row = Mathf.FloorToInt(minZ / CellSize); row <= lastRow; row++)
            {
                var z = (row + 0.5f) * CellSize;
                crossings.Clear();
                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                {
                    var a = polygon[i];
                    var b = polygon[j];
                    if ((a.y > z) != (b.y > z))
                    {
                        crossings.Add(a.x + (z - a.y) * (b.x - a.x) / (b.y - a.y));
                    }
                }

                crossings.Sort();
                for (var k = 0; k + 1 < crossings.Count; k += 2)
                {
                    // Cells whose centers lie between the two crossings.
                    var first = Mathf.CeilToInt(crossings[k] / CellSize - 0.5f);
                    var last = Mathf.FloorToInt(crossings[k + 1] / CellSize - 0.5f);
                    if (first <= last)
                    {
                        cells += level.SetPriorRun(row, first, last);
                    }
                }
            }

            level.AddPlaneSample(height, cells * CellSize * CellSize);
        }

        /// <summary>Adds one camera frame of evidence. Each cell counts at most once per frame.</summary>
        public void Integrate(ScanFrame frame)
        {
            if (frame == null || frame.Count == 0)
            {
                return;
            }

            FramesIntegrated++;
            clock = frame.Time > clock ? frame.Time : clock + DefaultFrameSeconds;
            var weight = Mathf.Clamp(frame.Weight, MinFrameWeight, 1f);
            floor.NoteCamera(frame.Origin.y, clock);
            frameCells.NextFrame();

            // The floor is picked before the heights are refined: depth corrects the height of a floor
            // within narrow limits only, from the first frame on.
            UpdateFloor();
            Classify(frame, weight);
            RefineHeights(frame, weight);
            DiscoverLevels();
            MergeCloseLevels();
            PruneLevels();
            CollectVotes(frame, weight);
            ApplyVotes(frame.HasNormals ? SurfaceEvidence.HitWeight : SurfaceEvidence.SparseHitWeight, weight);
        }

        /// <summary>
        /// Height of the floor as it is drawn. Until a floor is picked: the lowest drawn surface of at least
        /// <paramref name="minArea"/> square meters, else the lowest drawn surface, else null.
        /// </summary>
        public float? FloorHeight(float minArea)
        {
            if (floor.Level != null)
            {
                return floor.DrawnHeight;
            }

            float? large = null;
            float? lowest = null;
            foreach (var level in levels)
            {
                if (level.ShownArea <= 0f || !level.IsUsable)
                {
                    continue;
                }

                if (!lowest.HasValue || level.DrawnHeight < lowest.Value)
                {
                    lowest = level.DrawnHeight;
                }

                if (level.ShownArea >= minArea && (!large.HasValue || level.DrawnHeight < large.Value))
                {
                    large = level.DrawnHeight;
                }
            }

            return large ?? lowest;
        }

        /// <summary>Nearest shown surface along the ray. The pose faces up (y) like a detected plane.</summary>
        public bool Raycast(Ray ray, float maxDistance, out Pose pose)
        {
            return Raycast(ray, maxDistance, out pose, out _);
        }

        /// <param name="levelId">Id of the level that was hit (see <see cref="SurfaceLevel.Id"/>).</param>
        internal bool Raycast(Ray ray, float maxDistance, out Pose pose, out int levelId)
        {
            pose = default;
            levelId = -1;
            var bestDistance = maxDistance;
            var found = false;
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
            {
                return false;
            }

            var lowest = floor.Level != null ? floor.DrawnHeight - FloorGuard : float.NegativeInfinity;
            foreach (var level in levels)
            {
                var height = level.DrawnHeight;
                if (!level.IsUsable || height < lowest)
                {
                    continue;
                }

                var distance = (height - ray.origin.y) / ray.direction.y;
                if (distance <= 0f || distance >= bestDistance)
                {
                    continue;
                }

                var point = ray.GetPoint(distance);
                if (level.IsShown(CellOf(point.x, point.z)))
                {
                    bestDistance = distance;
                    pose = new Pose(new Vector3(point.x, height, point.z), Quaternion.identity);
                    levelId = level.Id;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>The level with this id, or null.</summary>
        internal SurfaceLevel FindLevel(int id)
        {
            for (var i = 0; i < levels.Count; i++)
            {
                if (levels[i].Id == id)
                {
                    return levels[i];
                }
            }

            return null;
        }

        /// <summary>The user says which level the floor is (see <see cref="FloorModel.ForceTrust"/>).</summary>
        internal bool SetFloor(int levelId)
        {
            var level = FindLevel(levelId);
            if (level == null)
            {
                return false;
            }

            floor.ForceTrust(level);
            UpdatePriorWeights();
            return true;
        }

        /// <summary>
        /// Moves the drawn heights toward the measured ones (see <see cref="HeightLock"/>). True when a
        /// level has to be redrawn for it.
        /// </summary>
        internal bool UpdateDrawnHeights(float seconds)
        {
            var changed = false;
            for (var i = 0; i < levels.Count; i++)
            {
                changed |= levels[i].UpdateDrawnHeight(seconds);
            }

            return changed;
        }

        /// <summary>
        /// Looks at every point once: is it usable, what may it do, and was it seen through the floor. The
        /// passes that follow read the answers.
        /// </summary>
        void Classify(ScanFrame frame, float weight)
        {
            var count = frame.Count;
            if (pointFlags.Length < count)
            {
                pointPositions = new Vector3[count];
                pointDistances = new float[count];
                pointFlags = new byte[count];
                pointCells = new Vector2Int[count];
            }

            Reflections = 0;
            Implausible = 0;
            var origin = frame.Origin;
            var floorLevel = floor.IsTrusted && origin.y > floor.Height ? floor.Level : null;
            var floorHeight = floorLevel != null ? floorLevel.Height : 0f;
            for (var i = 0; i < count; i++)
            {
                pointFlags[i] = 0;
                var point = frame.PointAt(i);
                var ray = point - origin;
                var distance = ray.magnitude;
                if (distance < MinRange || distance > MaxRange)
                {
                    continue;
                }

                if (floor.HasCameraHeight && floor.CameraHeight - point.y > FloorModel.MaxCameraAbove)
                {
                    // Deeper than any floor: what a shiny floor mirrors, often the underside of a desk.
                    Implausible++;
                    continue;
                }

                var flags = Usable;
                var confidence = frame.ConfidenceAt(i);
                var facesUp = !frame.HasNormals || frame.IsUpFacing(i);
                var steep = Mathf.Abs(ray.y) >= MinRayVertical * distance;
                if (floorLevel != null && point.y < floorHeight - DepthNoise.Band(distance))
                {
                    // Nothing is seen through a floor: the ray ends where it meets the floor.
                    var crossing = origin + ray * ((origin.y - floorHeight) / (origin.y - point.y));
                    var cell = CellOf(crossing.x, crossing.z);
                    flags |= Clipped;
                    if (floorLevel.GetLogOdds(cell) >= SurfaceEvidence.ConfirmedLogOdds)
                    {
                        flags |= Reflection;
                        Reflections++;
                    }
                    else if (!floorLevel.HasPrior(cell) && frame.HasNormals && facesUp
                             && confidence >= HeightConfidence
                             && frameCells.Add(cell.x, cell.y, HeightBinOf(point.y)))
                    {
                        // Beside the known floor: it may be a lower floor of its own.
                        floor.NoteBelow(point.y, weight);
                    }

                    point = crossing;
                    distance = Vector3.Distance(origin, crossing);
                    pointCells[i] = cell;
                }
                else
                {
                    pointCells[i] = CellOf(point.x, point.z);
                    if (confidence >= MinConfidence && facesUp && distance >= MinHitRange && distance <= MaxHitRange)
                    {
                        flags |= CanHit;
                    }

                    if (confidence >= HeightConfidence && facesUp && distance >= MinHeightRange
                        && distance <= MaxHeightRange)
                    {
                        flags |= CanHeight;
                    }
                }

                if (confidence >= CarveConfidence && frame.AllowCarving && steep)
                {
                    flags |= CanCarve;
                }

                pointPositions[i] = point;
                pointDistances[i] = distance;
                pointFlags[i] = flags;
            }
        }

        /// <summary>
        /// Collects the heights of the frame (every cell and height once) and lets every level take its
        /// height from the peak nearest to it.
        /// </summary>
        void RefineHeights(ScanFrame frame, float weight)
        {
            heights.Decay(Mathf.Pow(HeightDecay, weight));
            discoveryThisFrame.Clear();
            var origin = frame.Origin;
            var sourceWeight = frame.HasNormals ? 1f : SparseHeightWeight;
            var trustedFloor = floor.IsTrusted ? floor.Height + DiscoveryAboveFloor : float.NegativeInfinity;
            for (var i = 0; i < frame.Count; i++)
            {
                if ((pointFlags[i] & CanHeight) == 0)
                {
                    continue;
                }

                var point = pointPositions[i];
                var cell = pointCells[i];
                if (!frameCells.Add(cell.x, cell.y, HeightBinOf(point.y)))
                {
                    continue;
                }

                heights.Add(point.y, DepthNoise.HeightWeight(pointDistances[i]) * weight * sourceWeight, weight);
                if (frame.HasNormals && point.y < origin.y - DiscoveryBelowCamera && point.y >= trustedFloor
                    && NearestLevel(point.y, LevelMergeDistance) < 0)
                {
                    var bin = Mathf.FloorToInt(point.y / DiscoveryBinSize);
                    discoveryThisFrame.TryGetValue(bin, out var count);
                    discoveryThisFrame[bin] = count + weight;
                }
            }

            foreach (var level in levels)
            {
                Fuse(level);
            }
        }

        /// <summary>
        /// The height of a level from depth and from the plane ARCore detected. Depth follows the real
        /// surface but is measured too low on shiny floors; the plane is steady but can be a few
        /// centimeters off. Where they agree, depth corrects the plane within limits. Where they do not,
        /// the plane decides for the floor, and depth for every other level.
        /// </summary>
        void Fuse(SurfaceLevel level)
        {
            var plane = level.PlaneHeight;

            // A level that only a plane made looks for its surface (the plane may have drifted), and so
            // does the floor next to its plane. Every other level stays with the peak it has.
            var radius = level.HasDepthHeight && !(level.IsFloor && plane.HasValue)
                ? OwnPeakRadius
                : SurfaceLevel.HeightWindow;
            if (!heights.TryNearestMode(level.Height, radius, FusionCells, out var depth, out var sharp))
            {
                if (!level.HasDepthHeight && plane.HasValue)
                {
                    level.SetHeight(plane.Value);
                }

                // Out of view: the last height stays.
                return;
            }

            level.ConfirmHeight();
            if (!plane.HasValue)
            {
                level.SetHeight(depth);
                return;
            }

            var difference = depth - plane.Value;
            if (difference >= -AgreeBelow && difference <= AgreeAbove)
            {
                level.PlaneDisagreement = 0;
                var down = level.IsFloor ? FloorClampDown : RaisedClamp;
                var up = level.IsFloor ? (sharp ? FloorClampUpSharp : FloorClampUp) : RaisedClamp;
                level.SetHeight(plane.Value + Mathf.Clamp(difference, -down, up));
                return;
            }

            if (!level.IsFloor)
            {
                // A plane that drifted: the level moves onto the real surface (and merges with its level).
                level.SetHeight(depth);
                return;
            }

            level.PlaneDisagreement++;
            var depthOverrules = difference > AgreeAbove && sharp && level.PlaneDisagreement >= OverruleFrames
                                 && heights.CountAround(depth, OverruleRadius) >= OverruleCells;
            level.SetHeight(depthOverrules ? depth : plane.Value);
        }

        /// <summary>
        /// Upward-facing points at a height with no level build up evidence that fades each frame. Only a
        /// surface seen over several frames makes a new level; one noisy frame does not.
        /// </summary>
        void DiscoverLevels()
        {
            discoveryKeys.Clear();
            discoveryKeys.AddRange(discovery.Keys);
            foreach (var bin in discoveryKeys)
            {
                discovery[bin] *= DiscoveryDecay;
            }

            foreach (var pair in discoveryThisFrame)
            {
                discovery.TryGetValue(pair.Key, out var value);
                discovery[pair.Key] = value + Mathf.Min(pair.Value, DiscoveryFrameCap);
            }

            discoveryKeys.Clear();
            discoveryKeys.AddRange(discovery.Keys);
            foreach (var bin in discoveryKeys)
            {
                if (!discovery.TryGetValue(bin, out var value))
                {
                    continue;
                }

                if (value < DiscoveryForget)
                {
                    discovery.Remove(bin);
                    continue;
                }

                discovery.TryGetValue(bin - 1, out var below);
                discovery.TryGetValue(bin + 1, out var above);
                var total = below + value + above;
                if (total < DiscoveryThreshold || value < below || value < above)
                {
                    continue;
                }

                var center = (bin + 0.5f + (above - below) / total) * DiscoveryBinSize;
                EnsureLevel(center);
                discovery.Remove(bin - 1);
                discovery.Remove(bin);
                discovery.Remove(bin + 1);
            }
        }

        void MergeCloseLevels()
        {
            for (var i = 0; i < levels.Count; i++)
            {
                for (var j = i + 1; j < levels.Count; j++)
                {
                    if (Mathf.Abs(levels[i].Height - levels[j].Height) >= LevelMergeDistance)
                    {
                        continue;
                    }

                    var keep = Survives(levels[i], levels[j]) ? i : j;
                    var drop = keep == i ? j : i;
                    var survivor = levels[keep];
                    var absorbed = levels[drop];
                    survivor.Absorb(absorbed);
                    floor.OnMerged(absorbed, survivor);
                    levels.RemoveAt(drop);
                    LevelMerged?.Invoke(absorbed.Id, survivor.Id);
                    i = -1;
                    break;
                }
            }
        }

        /// <summary>The floor survives a merge; else the level that depth confirmed more of; else the older one.</summary>
        static bool Survives(SurfaceLevel a, SurfaceLevel b)
        {
            if (a.IsFloor != b.IsFloor)
            {
                return a.IsFloor;
            }

            if (!Mathf.Approximately(a.ConfirmedArea, b.ConfirmedArea))
            {
                return a.ConfirmedArea > b.ConfirmedArea;
            }

            return a.Id < b.Id;
        }

        /// <summary>
        /// Levels that came from a few noisy frames and never became a surface go, and so do the weakest
        /// ones when there are too many.
        /// </summary>
        void PruneLevels()
        {
            for (var i = levels.Count - 1; i >= 0; i--)
            {
                var level = levels[i];
                if (!level.IsFloor && level != floor.Former && level.ShownArea <= 0f
                    && level.ConfirmedArea < PruneConfirmedArea && level.PlaneArea <= 0f
                    && FramesIntegrated - level.CreatedFrame >= PruneAgeFrames)
                {
                    RemoveLevelAt(i);
                }
            }

            while (levels.Count > MaxLevels)
            {
                var weakest = -1;
                for (var i = 0; i < levels.Count; i++)
                {
                    if (levels[i].IsFloor || levels[i] == floor.Former)
                    {
                        continue;
                    }

                    if (weakest < 0 || Strength(levels[i]) < Strength(levels[weakest]))
                    {
                        weakest = i;
                    }
                }

                if (weakest < 0)
                {
                    return;
                }

                RemoveLevelAt(weakest);
            }
        }

        static float Strength(SurfaceLevel level)
        {
            return level.ConfirmedArea + 0.5f * level.PlaneArea + level.ShownArea;
        }

        void RemoveLevelAt(int index)
        {
            var level = levels[index];
            levels.RemoveAt(index);
            floor.OnRemoved(level);
            LevelRemoved?.Invoke(level.Id);
        }

        void UpdateFloor()
        {
            var below = floor.Update(levels);
            if (below.HasValue)
            {
                // Depth found a surface below the floor, beside its known cells: it gets its level.
                EnsureLevel(below.Value);
            }

            UpdatePriorWeights();
        }

        void UpdatePriorWeights()
        {
            for (var i = 0; i < levels.Count; i++)
            {
                levels[i].SetPriorWeight(PriorWeightOf(levels[i]));
            }
        }

        /// <summary>
        /// The floor shows where ARCore detected it: nothing reaches past the edge of a floor, and on a
        /// shiny floor depth is too weak to confirm it. A raised surface needs depth to confirm its cells.
        /// </summary>
        float PriorWeightOf(SurfaceLevel level)
        {
            return priorsNeedConfirmation && !level.IsFloor
                ? SurfaceEvidence.PrecisePriorWeight
                : SurfaceEvidence.PriorWeight;
        }

        void CollectVotes(ScanFrame frame, float weight)
        {
            votes.Clear();
            var origin = frame.Origin;
            var floorIndex = floor.IsTrusted ? levels.IndexOf(floor.Level) : -1;
            var reference = floor.Reference;
            for (var i = 0; i < frame.Count; i++)
            {
                var flags = pointFlags[i];
                if ((flags & Usable) == 0)
                {
                    continue;
                }

                var point = pointPositions[i];
                var distance = pointDistances[i];
                if ((flags & Clipped) != 0)
                {
                    if ((flags & Reflection) != 0 && floorIndex >= 0)
                    {
                        AddVote(floorIndex, pointCells[i], VoteKind.Weak);
                    }
                }
                else if ((flags & CanHit) != 0)
                {
                    var hit = LevelHitBy(point.y, DepthNoise.HitTolerance(distance));
                    if (hit >= 0)
                    {
                        AddVote(hit, pointCells[i], VoteKind.Hit);
                        NoteSeenThrough(levels[hit], reference, origin, point, weight);
                    }
                }

                if ((flags & CanCarve) != 0)
                {
                    Carve(origin, point, distance, floorIndex);
                }
            }
        }

        /// <summary>A miss for every level the ray crosses and ends clearly beyond.</summary>
        void Carve(Vector3 origin, Vector3 point, float distance, int trustedFloorIndex)
        {
            var ray = point - origin;
            var margin = DepthNoise.MissMargin(distance);
            for (var index = 0; index < levels.Count; index++)
            {
                var level = levels[index];
                if (index == trustedFloorIndex || level.IsSuppressed)
                {
                    continue;
                }

                var originOffset = origin.y - level.Height;
                var pointOffset = point.y - level.Height;

                // Until depth points confirm the level's height, only rays that end far past it may carve.
                var needed = margin + (level.IsConfirmed ? 0f : SurfaceLevel.HeightWindow);
                if (originOffset * pointOffset >= 0f || Mathf.Abs(pointOffset) <= needed)
                {
                    continue;
                }

                var crossing = origin + ray * (originOffset / (originOffset - pointOffset));
                AddVote(index, CellOf(crossing.x, crossing.z), VoteKind.Miss);
            }
        }

        /// <summary>
        /// A hit on a level below the floor: was it seen through a solid cell of the floor? A level whose
        /// hits mostly were is what the floor mirrors (see <see cref="FloorModel"/>).
        /// </summary>
        static void NoteSeenThrough(SurfaceLevel level, SurfaceLevel reference, Vector3 origin, Vector3 point,
            float weight)
        {
            if (reference == null || level == reference || level.Height >= reference.Height
                || origin.y <= reference.Height || point.y >= reference.Height)
            {
                return;
            }

            var crossing = origin + (point - origin) * ((origin.y - reference.Height) / (origin.y - point.y));
            var cell = CellOf(crossing.x, crossing.z);
            var logOdds = reference.GetLogOdds(cell);
            var solid = logOdds >= SurfaceEvidence.ConfirmedLogOdds
                        || (logOdds >= 0f && reference.HasPrior(cell) && reference.PlaneArea >= FloorModel.MinArea
                            && level.PlaneArea < FloorModel.MinArea * 0.5f);
            level.NoteSeen(solid, weight);
        }

        void AddVote(int level, Vector2Int cell, VoteKind kind)
        {
            var key = ((long)level << 48) ^ ((long)(cell.x & 0xFFFFFF) << 24) ^ (cell.y & 0xFFFFFF);
            votes.TryGetValue(key, out var vote);
            vote.Level = level;
            vote.Cell = cell;
            switch (kind)
            {
                case VoteKind.Hit:
                    vote.Hits++;
                    break;
                case VoteKind.Miss:
                    vote.Misses++;
                    break;
                default:
                    vote.Weak = true;
                    break;
            }

            votes[key] = vote;
        }

        void ApplyVotes(float hitWeight, float frameWeight)
        {
            foreach (var vote in votes.Values)
            {
                var level = levels[vote.Level];
                if (vote.Hits > 0 && vote.Hits >= vote.Misses)
                {
                    level.ApplyVote(vote.Cell, true, hitWeight * frameWeight);
                }
                else if (vote.Misses > 0)
                {
                    level.ApplyVote(vote.Cell, false, frameWeight);
                }
                else if (vote.Weak)
                {
                    level.ApplyVote(vote.Cell, true, SurfaceEvidence.ReflectionHitWeight * frameWeight);
                }

                checkedCells.Add(((long)vote.Cell.x << 32) ^ (uint)vote.Cell.y);
            }
        }

        int NearestLevel(float height, float maxDistance)
        {
            var nearest = -1;
            var nearestDistance = maxDistance;
            for (var i = 0; i < levels.Count; i++)
            {
                var distance = Mathf.Abs(levels[i].Height - height);
                if (distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        /// <summary>The level a point at this height lies on (the nearest within the tolerance), or -1.</summary>
        int LevelHitBy(float height, float tolerance)
        {
            var nearest = -1;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < levels.Count; i++)
            {
                var distance = Mathf.Abs(levels[i].Height - height);
                if (distance <= tolerance && distance < nearestDistance && !levels[i].IsSuppressed)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        static int HeightBinOf(float y) => Mathf.FloorToInt(y / HeightBinSize);

        enum VoteKind
        {
            Hit,
            Miss,
            Weak,
        }

        struct Vote
        {
            public int Level;
            public Vector2Int Cell;
            public int Hits;
            public int Misses;
            public bool Weak;
        }
    }
}
