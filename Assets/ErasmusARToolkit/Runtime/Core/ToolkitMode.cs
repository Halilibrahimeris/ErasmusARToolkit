namespace Erasmus.ARToolkit
{
    /// <summary>How the toolkit is currently running.</summary>
    public enum ToolkitMode
    {
        /// <summary>AR support is still being checked.</summary>
        Undetermined,

        /// <summary>Real AR through AR Foundation (Android with ARCore, or XR Simulation in the Editor).</summary>
        AR,

        /// <summary>Mouse/keyboard simulation on a virtual ground plane (PC, Editor, unsupported phones).</summary>
        Desktop,
    }

    /// <summary>Lets the user force a mode instead of detecting it automatically.</summary>
    public enum ModeOverride
    {
        Auto,
        ForceAR,
        ForceDesktop,
    }
}
