namespace Erasmus.ARToolkit
{
    /// <summary>Where the room scan gets its camera frames from (the depth image, tracking points, or a test).</summary>
    public interface IScanFrameSource
    {
        /// <summary>True while the source delivers frames with a normal per point (depth images).</summary>
        bool IsPrecise { get; }

        /// <summary>False once the source knows it cannot work on this device.</summary>
        bool IsAvailable { get; }

        void Begin();

        void End();

        /// <summary>Fills <paramref name="frame"/> with the newest frame. False if there is nothing new.</summary>
        bool TryReadFrame(ScanFrame frame);
    }
}
