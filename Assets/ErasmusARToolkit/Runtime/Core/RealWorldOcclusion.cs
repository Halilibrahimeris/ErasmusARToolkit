using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Lets real objects (furniture, people, hands) hide virtual content behind them. The phone estimates
    /// the depth of the camera image (ARCore Depth) and the AR camera background writes it into the depth
    /// buffer, so anything drawn later is depth-tested against the real world. Phones without depth
    /// support get occlusion switched off and show everything, as before.
    /// </summary>
    public static class RealWorldOcclusion
    {
        const float SupportCheckSeconds = 10f;

        /// <summary>
        /// Adds (or reuses) the occlusion manager on the AR camera and switches it on or off.
        /// Call it before the camera object becomes active: ARCameraBackground looks for it in Awake.
        /// </summary>
        public static AROcclusionManager Configure(GameObject arCameraObject, bool enabled)
        {
            if (arCameraObject == null || arCameraObject.GetComponent<Camera>() == null)
            {
                return null;
            }

            var manager = arCameraObject.GetComponent<AROcclusionManager>();
            if (manager == null)
            {
                manager = arCameraObject.AddComponent<AROcclusionManager>();
            }

            // Fastest keeps the per-frame cost lowest; temporal smoothing keeps occlusion edges from flickering.
            manager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            manager.environmentDepthTemporalSmoothingRequested = true;
            manager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            manager.enabled = enabled;
            return manager;
        }

        /// <summary>
        /// While the occlusion manager is on, AR Foundation asks URP for an extra depth pass every frame.
        /// Run this once the AR rig is on: it switches occlusion off on phones without depth support.
        /// </summary>
        public static IEnumerator TurnOffIfUnsupported(AROcclusionManager manager)
        {
            var giveUpAt = Time.unscaledTime + SupportCheckSeconds;
            while (manager != null && manager.enabled && Time.unscaledTime < giveUpAt)
            {
                var descriptor = manager.descriptor;
                var unsupported = IsUnsupported(
                    descriptor != null,
                    descriptor != null ? descriptor.environmentDepthImageSupported : Supported.Unknown,
                    ARSession.state == ARSessionState.SessionTracking);

                if (unsupported.HasValue)
                {
                    if (unsupported.Value)
                    {
                        manager.enabled = false;
                        Debug.Log("[Erasmus AR Toolkit] This phone has no ARCore Depth, so real-world occlusion is off.");
                    }

                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>True: no depth support. False: supported. Null: not known yet, ask again next frame.</summary>
        internal static bool? IsUnsupported(bool hasDescriptor, Supported environmentDepth, bool sessionTracking)
        {
            switch (environmentDepth)
            {
                case Supported.Supported:
                    return false;
                case Supported.Unsupported:
                    return true;
                default:
                    // Once the session tracks, a missing occlusion subsystem means the device has none.
                    return sessionTracking && !hasDescriptor ? true : (bool?)null;
            }
        }
    }
}
