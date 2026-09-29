using System;
using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Decides which surface level is the floor, and how sure that is. "The lowest surface" is not good
    /// enough: a shiny floor mirrors the room, so depth points and even ARCore planes appear below it,
    /// and a desk that is seen before the floor must not be taken for it.
    /// <list type="bullet">
    /// <item><b>Eligible</b>: the level lies where a floor can be for a phone held by a person (0.8 to
    /// 1.9 m below the usual camera height) and is large: a detected plane or confirmed cells of 1 m².
    /// Below a floor that was trusted, a plane alone is not enough on phones with depth: ARCore detects
    /// planes on what a shiny floor mirrors, too.</item>
    /// <item><b>Pick</b>: the lowest eligible level, unless a level up to 20 cm above it has twice the
    /// support (ARCore sometimes adds a second, lower plane for the same floor).</item>
    /// <item><b>Trusted</b>: the same pick for a while, with 2 m² of support. Only a trusted floor clips
    /// the depth points below it (see <see cref="RoomSurfaceMap"/>) and hides the levels below it.</item>
    /// <item><b>Back to candidate</b>: depth points beside the known floor cells form a surface of their
    /// own below it, or a lower level becomes the pick (ARCore detected a large plane there). The floor
    /// may be a podium.</item>
    /// </list>
    /// Trust hides levels but removes nothing, so a wrong decision is undone when the evidence changes.
    /// </summary>
    internal sealed class FloorModel
    {
        /// <summary>The floor is at least this far (meters) below the usual camera height...</summary>
        public const float MinCameraAbove = 0.8f;

        /// <summary>...and at most this far. Points deeper than that are reflections.</summary>
        public const float MaxCameraAbove = 1.9f;

        /// <summary>A floor has a detected plane or confirmed cells of this area (square meters).</summary>
        public const float MinArea = 1f;

        /// <summary>A level up to this far (meters) above the lowest one can be the floor instead...</summary>
        public const float PromoteWithin = 0.2f;

        /// <summary>...when it has this many times the support.</summary>
        public const float PromoteSupport = 2f;

        /// <summary>Frames in a row with the same pick before it is trusted.</summary>
        public const int TrustFrames = 12;

        /// <summary>Support (square meters) a trusted floor needs. More than one desk has.</summary>
        public const float TrustArea = 2f;

        /// <summary>Levels this far (meters) below a trusted floor are hidden.</summary>
        public const float SuppressBelow = 0.06f;

        /// <summary>Observations below the floor, at one height, that make the floor a candidate again.</summary>
        public const float BelowCells = 200f;

        /// <summary>Hits of a level below the floor that are needed to judge it...</summary>
        public const float GhostCells = 40f;

        /// <summary>...a reflection when this share of them was seen through solid floor cells...</summary>
        public const float GhostShare = 0.5f;

        /// <summary>...and a surface again when the share falls below this.</summary>
        public const float GhostClearShare = 0.3f;

        /// <summary>The usual camera height is taken from the frames of the last seconds.</summary>
        public const float CameraWindowSeconds = 20f;

        const float CameraPercentile = 0.8f;
        const int CameraSamples = 256;
        const float ConfirmedAreaOfADepthFloor = 0.5f;
        const float StatisticsDecay = 0.97f;

        readonly HeightHistogram below = new HeightHistogram();
        readonly float[] cameraHeights = new float[CameraSamples];
        readonly double[] cameraTimes = new double[CameraSamples];
        readonly float[] sorted = new float[CameraSamples];
        int cameraNext;
        int cameraCount;
        SurfaceLevel lastTrusted;
        int stableFrames;
        bool forced;

        public event Action<FloorState> StateChanged;

        public FloorState State { get; private set; }

        /// <summary>The level picked as the floor, or null.</summary>
        public SurfaceLevel Level { get; private set; }

        /// <summary>
        /// The floor that was trusted before a lower level was picked. Hits on the lower level are checked
        /// against it: if they were seen through its solid cells, the lower level is a reflection (ARCore
        /// detects planes on what a shiny floor mirrors, too).
        /// </summary>
        public SurfaceLevel Former { get; private set; }

        /// <summary>The level that hits below it are checked against.</summary>
        public SurfaceLevel Reference => Former ?? Level;

        public bool IsTrusted => State == FloorState.Trusted && Level != null;

        public float Height => Level != null ? Level.Height : float.NaN;

        public float DrawnHeight => Level != null ? Level.DrawnHeight : float.NaN;

        /// <summary>The floor is known from the plane ARCore detected; depth has confirmed little of it.</summary>
        public bool FromPlane { get; private set; }

        /// <summary>Height the camera is usually held at (80th percentile of the last seconds).</summary>
        public float CameraHeight { get; private set; } = float.NaN;

        public bool HasCameraHeight => cameraCount > 0;

        /// <summary>
        /// True (phones with depth): a level below a floor that was trusted needs cells confirmed by depth
        /// to become the floor. False: a detected plane of the size of a floor is enough.
        /// </summary>
        public bool PlanesNeedDepth { get; set; }

        public void Reset()
        {
            below.Clear();
            cameraNext = 0;
            cameraCount = 0;
            stableFrames = 0;
            forced = false;
            CameraHeight = float.NaN;
            FromPlane = false;
            Former = null;
            lastTrusted = null;
            SetLevel(null);
            SetState(FloorState.Unknown);
        }

        /// <summary>The camera height of a frame that is integrated.</summary>
        public void NoteCamera(float y, double time)
        {
            cameraHeights[cameraNext] = y;
            cameraTimes[cameraNext] = time;
            cameraNext = (cameraNext + 1) % CameraSamples;
            cameraCount = Mathf.Min(cameraCount + 1, CameraSamples);

            var count = 0;
            for (var i = 0; i < cameraCount; i++)
            {
                if (time - cameraTimes[i] <= CameraWindowSeconds && cameraTimes[i] <= time)
                {
                    sorted[count++] = cameraHeights[i];
                }
            }

            if (count == 0)
            {
                CameraHeight = y;
                return;
            }

            Array.Sort(sorted, 0, count);
            CameraHeight = sorted[Mathf.Min(count - 1, Mathf.FloorToInt(CameraPercentile * count))];
        }

        /// <summary>Distance (meters) from the usual camera height down to a height.</summary>
        public float CameraAbove(float height)
        {
            return HasCameraHeight ? CameraHeight - height : float.NaN;
        }

        /// <summary>
        /// An upward-facing depth point below the trusted floor whose ray crossed the floor plane at a cell
        /// that is not known to be floor: it may lie on a lower floor beside this one.
        /// </summary>
        public void NoteBelow(float y, float weight)
        {
            below.Add(y, weight, weight);
        }

        /// <summary>The user says which level the floor is. It stays the floor until the scan is reset.</summary>
        public void ForceTrust(SurfaceLevel level)
        {
            if (level == null)
            {
                return;
            }

            forced = true;
            Former = null;
            SetLevel(level);
            stableFrames = TrustFrames;
            level.SetGhost(false);
            level.SetSuppressed(false);
            Trust();
        }

        /// <summary>Two levels turned out to be one surface.</summary>
        public void OnMerged(SurfaceLevel from, SurfaceLevel into)
        {
            if (Level == from)
            {
                // Same floor, other object: what is known about it stays.
                Level.IsFloor = false;
                Level = into;
                Level.IsFloor = true;
            }

            if (lastTrusted == from)
            {
                lastTrusted = into;
            }

            if (Former == from)
            {
                Former = into;
            }

            if (Former == Level)
            {
                Former = null;
            }
        }

        public void OnRemoved(SurfaceLevel level)
        {
            if (lastTrusted == level)
            {
                lastTrusted = null;
            }

            if (Former == level)
            {
                Former = null;
            }

            if (Level == level)
            {
                forced = false;
                SetLevel(null);
                SetState(FloorState.Unknown);
            }
        }

        /// <summary>
        /// Call once per integrated frame, after the heights were refined. Returns the height of a surface
        /// found below the floor that needs a level of its own, or null.
        /// </summary>
        public float? Update(IReadOnlyList<SurfaceLevel> levels)
        {
            below.Decay(StatisticsDecay);
            JudgeGhosts(levels);
            if (forced && Level != null)
            {
                Suppress(levels);
                return null;
            }

            var pick = Pick(levels);
            if (pick == null)
            {
                Former = null;
                SetLevel(null);
                SetState(FloorState.Unknown);
                Restore(levels);
                return null;
            }

            if (pick != Level)
            {
                // A floor that was trusted and gives way to a lower level keeps watch over it.
                Former = lastTrusted != null && lastTrusted != pick && lastTrusted.Height > pick.Height
                    ? lastTrusted
                    : null;
                SetLevel(pick);
                stableFrames = 0;
                SetState(FloorState.Candidate);
                Restore(levels);
                return null;
            }

            stableFrames++;
            float? found = null;
            if (State == FloorState.Trusted)
            {
                if (IsContradicted(out found))
                {
                    stableFrames = 0;
                    below.Clear();
                    SetState(FloorState.Candidate);
                    Restore(levels);
                    return found;
                }
            }
            else if (stableFrames >= TrustFrames && Mathf.Max(pick.PlaneArea, pick.ConfirmedArea) >= TrustArea)
            {
                Trust();
            }

            if (State == FloorState.Trusted)
            {
                Suppress(levels);
            }

            return null;
        }

        /// <summary>The level can be the floor, as far as its height and size go.</summary>
        public bool IsEligible(SurfaceLevel level)
        {
            if (level.IsGhost || !HasCameraHeight)
            {
                return false;
            }

            var above = CameraHeight - level.Height;
            if (above < MinCameraAbove || above > MaxCameraAbove)
            {
                return false;
            }

            var planeCounts = !PlanesNeedDepth || lastTrusted == null || level == lastTrusted
                              || level.Height >= lastTrusted.Height - SuppressBelow;
            return level.ConfirmedArea >= MinArea || (planeCounts && level.PlaneArea >= MinArea);
        }

        static float Support(SurfaceLevel level)
        {
            return level.ConfirmedArea + 0.5f * level.PlaneArea;
        }

        SurfaceLevel Pick(IReadOnlyList<SurfaceLevel> levels)
        {
            SurfaceLevel pick = null;
            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                if (IsEligible(level) && (pick == null || level.Height < pick.Height))
                {
                    pick = level;
                }
            }

            if (pick == null)
            {
                return null;
            }

            var lowest = pick;
            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                var above = level.Height - lowest.Height;
                if (level != lowest && IsEligible(level) && above > 0f && above <= PromoteWithin
                    && Support(level) >= PromoteSupport * Support(lowest) && Support(level) > Support(pick))
                {
                    pick = level;
                }
            }

            return pick;
        }

        void Trust()
        {
            lastTrusted = Level;
            FromPlane = Level.ConfirmedArea < ConfirmedAreaOfADepthFloor;
            Level.ForgiveCarvedPriors();
            below.Clear();
            SetState(FloorState.Trusted);
        }

        /// <summary>
        /// A surface of its own below the floor: depth points at one height, seen beside the floor cells.
        /// (A large plane below the floor changes the pick, unless it is a drifted copy of this floor.)
        /// </summary>
        bool IsContradicted(out float? found)
        {
            found = null;
            if (below.TryStrongestMode(BelowCells, out var height) && height < Level.Height - SuppressBelow)
            {
                found = height;
                return true;
            }

            return false;
        }

        /// <summary>
        /// A level below the floor whose hits were mostly seen through solid floor cells is what the floor
        /// mirrors, not a surface.
        /// </summary>
        void JudgeGhosts(IReadOnlyList<SurfaceLevel> levels)
        {
            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                level.DecaySeen(StatisticsDecay);
                if (level.SeenCells < GhostCells)
                {
                    continue;
                }

                var share = level.ThroughCells / level.SeenCells;
                if (share >= GhostShare)
                {
                    level.SetGhost(true);
                }
                else if (share < GhostClearShare)
                {
                    level.SetGhost(false);
                }
            }
        }

        void Suppress(IReadOnlyList<SurfaceLevel> levels)
        {
            var limit = Level.Height - SuppressBelow;
            for (var i = 0; i < levels.Count; i++)
            {
                levels[i].SetSuppressed(levels[i] != Level && levels[i].Height < limit);
            }
        }

        static void Restore(IReadOnlyList<SurfaceLevel> levels)
        {
            for (var i = 0; i < levels.Count; i++)
            {
                levels[i].SetSuppressed(false);
            }
        }

        void SetLevel(SurfaceLevel level)
        {
            if (Level == level)
            {
                return;
            }

            if (Level != null)
            {
                Level.IsFloor = false;
                Level.MarkDirty();
            }

            Level = level;
            if (Level != null)
            {
                Level.IsFloor = true;
                Level.MarkDirty();
            }
        }

        void SetState(FloorState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            if (state != FloorState.Trusted)
            {
                FromPlane = false;
            }

            StateChanged?.Invoke(state);
        }
    }
}
