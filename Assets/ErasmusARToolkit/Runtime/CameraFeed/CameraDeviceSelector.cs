using System;
using System.Collections.Generic;

namespace Erasmus.ARToolkit
{
    /// <summary>The parts of a WebCamDevice needed to choose a camera (plain data, easy to test).</summary>
    public readonly struct CameraDeviceInfo
    {
        public CameraDeviceInfo(string name, bool isFrontFacing)
        {
            Name = name ?? string.Empty;
            IsFrontFacing = isFrontFacing;
        }

        public string Name { get; }
        public bool IsFrontFacing { get; }
    }

    /// <summary>Chooses which camera to show and in which order the Camera button cycles.</summary>
    public static class CameraDeviceSelector
    {
        /// <summary>
        /// Returns the index of the camera to use, or -1 if there is none.
        /// A configured name wins; phones prefer the back camera; PCs take the first webcam.
        /// </summary>
        public static int Pick(IReadOnlyList<CameraDeviceInfo> devices, string preferredName, bool preferBackFacing)
        {
            if (devices == null || devices.Count == 0)
            {
                return -1;
            }

            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                var wanted = preferredName.Trim();
                for (var i = 0; i < devices.Count; i++)
                {
                    if (string.Equals(devices[i].Name, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            if (preferBackFacing)
            {
                for (var i = 0; i < devices.Count; i++)
                {
                    if (!devices[i].IsFrontFacing)
                    {
                        return i;
                    }
                }
            }

            return 0;
        }

        /// <summary>Next camera for the Camera button: each device in turn, then off (-1), then the first again.</summary>
        public static int Next(int current, int count)
        {
            if (count <= 0)
            {
                return -1;
            }

            if (current < 0)
            {
                return 0;
            }

            return current + 1 < count ? current + 1 : -1;
        }
    }
}
