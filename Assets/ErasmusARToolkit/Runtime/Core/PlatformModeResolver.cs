using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit
{
    /// <summary>Decides whether the toolkit runs in AR or Desktop mode. Pure logic, no Unity state.</summary>
    public static class PlatformModeResolver
    {
        public static bool NeedsAvailabilityCheck(ModeOverride modeOverride, bool isMobileDevice)
        {
            return modeOverride == ModeOverride.Auto && isMobileDevice;
        }

        public static ToolkitMode Resolve(ModeOverride modeOverride, bool isMobileDevice, ARSessionState sessionState)
        {
            switch (modeOverride)
            {
                case ModeOverride.ForceAR:
                    return ToolkitMode.AR;
                case ModeOverride.ForceDesktop:
                    return ToolkitMode.Desktop;
            }

            if (!isMobileDevice)
            {
                return ToolkitMode.Desktop;
            }

            switch (sessionState)
            {
                case ARSessionState.CheckingAvailability:
                    return ToolkitMode.Undetermined;
                case ARSessionState.None:
                case ARSessionState.Unsupported:
                    return ToolkitMode.Desktop;
                default:
                    // NeedsInstall and Installing are handled by ARSession, which prompts for ARCore.
                    return ToolkitMode.AR;
            }
        }

        /// <summary>True when a phone could not run AR and fell back to Desktop mode.</summary>
        public static bool IsFallback(ModeOverride modeOverride, bool isMobileDevice, ToolkitMode resolvedMode)
        {
            return modeOverride == ModeOverride.Auto && isMobileDevice && resolvedMode == ToolkitMode.Desktop;
        }
    }
}
