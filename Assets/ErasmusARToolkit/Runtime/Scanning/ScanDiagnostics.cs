using System;
using System.Globalization;
using System.Text;

namespace Erasmus.ARToolkit
{
    /// <summary>How far the room scan got with checking how the depth image lines up with the screen.</summary>
    public enum MappingState
    {
        /// <summary>Still comparing the candidates with ARCore's depth hit test.</summary>
        Checking,

        /// <summary>One candidate is clearly the right one.</summary>
        Verified,

        /// <summary>No clear winner: the documented mapping is used, without carving.</summary>
        Assumed,

        /// <summary>The depth image cannot be used, so the scan runs on tracking points.</summary>
        Unverifiable,
    }

    /// <summary>How sure the room scan is about which surface is the floor.</summary>
    public enum FloorState
    {
        /// <summary>No surface qualifies as the floor yet.</summary>
        Unknown,

        /// <summary>A surface looks like the floor but is not confirmed.</summary>
        Candidate,

        /// <summary>The floor is confirmed; points below it count as reflections.</summary>
        Trusted,
    }

    /// <summary>
    /// What the room scan measured on this phone, for the diagnostics line in the log (and on the HUD when
    /// switched on in the config). Heights are world y in meters. Values a toolkit version does not
    /// measure stay at their defaults.
    /// </summary>
    public struct ScanDiagnostics
    {
        public MappingState Mapping;

        /// <summary>Camera frame time minus depth image time, in milliseconds (NaN when unknown).</summary>
        public float DepthLagMs;

        public int DepthWidth;
        public int DepthHeight;

        /// <summary>Share of the sampled depth pixels that were confident enough to use (0..1).</summary>
        public float ConfidentRatio;

        public float CaptureMs;
        public float IntegrateMs;
        public float RebuildMs;

        /// <summary>World y of the AR camera.</summary>
        public float CameraHeight;

        /// <summary>Height of the scanned floor (NaN when there is none).</summary>
        public float FloorHeight;

        /// <summary>Height of the floor plane ARCore detected (NaN when there is none).</summary>
        public float PlaneFloorHeight;

        public FloorState Floor;

        /// <summary>The floor is shown from the ARCore plane because depth is too weak there.</summary>
        public bool FloorFromPlane;

        public int LevelCount;
        public int DepthSlotsUsed;
        public int DepthSlots;

        /// <summary>Depth points seen through the floor in the last frame (reflections on shiny floors).</summary>
        public int Reflections;

        /// <summary>Depth points too far below the camera to be real, in the last frame.</summary>
        public int Implausible;

        public int FramesIntegrated;

        /// <summary>How the camera image is drawn ("BeforeOpaques" is what the toolkit expects).</summary>
        public string BackgroundMode;

        /// <summary>Android thermal status (0 = none ... 6 = shutdown), -1 when unknown.</summary>
        public int ThermalStatus;

        /// <summary>One line for the log: short keys, dot decimals whatever the phone's language.</summary>
        public string ToLine()
        {
            var culture = CultureInfo.InvariantCulture;
            var line = new StringBuilder(200);
            line.Append("map=").Append(Mapping);
            line.Append(" depth=").Append(DepthWidth).Append('x').Append(DepthHeight);
            line.Append(" lag=").Append(Number(DepthLagMs, "0")).Append("ms");
            line.Append(" conf=").Append(ConfidentRatio.ToString("0.00", culture));
            line.Append(" cam=").Append(Number(CameraHeight, "0.00"));
            line.Append(" floor=").Append(Number(FloorHeight, "0.000")).Append('(').Append(Floor);
            line.Append(FloorFromPlane ? ",plane)" : ")");
            line.Append(" plane=").Append(Number(PlaneFloorHeight, "0.000"));
            line.Append(" levels=").Append(LevelCount);
            line.Append(" slots=").Append(DepthSlotsUsed).Append('/').Append(DepthSlots);
            line.Append(" refl=").Append(Reflections);
            line.Append(" deep=").Append(Implausible);
            line.Append(" frames=").Append(FramesIntegrated);
            line.Append(" cap=").Append(CaptureMs.ToString("0.0", culture));
            line.Append(" int=").Append(IntegrateMs.ToString("0.0", culture));
            line.Append(" reb=").Append(RebuildMs.ToString("0.0", culture)).Append("ms");
            line.Append(" bg=").Append(string.IsNullOrEmpty(BackgroundMode) ? "?" : BackgroundMode);
            line.Append(" heat=").Append(ThermalStatus);
            return line.ToString();
        }

        static string Number(float value, string format)
        {
            return float.IsNaN(value) ? "-" : value.ToString(format, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>What a scan source can tell about the depth images it reads.</summary>
    internal interface IScanSourceDiagnostics
    {
        MappingState Mapping { get; }

        /// <summary>Camera frame time minus depth image time, in milliseconds (NaN when unknown).</summary>
        float DepthLagMs { get; }

        int DepthWidth { get; }
        int DepthHeight { get; }
        float ConfidentRatio { get; }
        float CaptureMs { get; }

        /// <summary>Mean error (meters) of every mapping candidate against the depth hit test, NaN without samples.</summary>
        float MappingError(int candidate);

        /// <summary>The mapping state changed (verified, given up, or switched to another candidate).</summary>
        event Action MappingChanged;
    }
}
