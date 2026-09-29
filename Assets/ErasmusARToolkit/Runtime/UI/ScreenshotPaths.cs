using System;
using System.Globalization;
using System.IO;

namespace Erasmus.ARToolkit
{
    /// <summary>Where screenshots are saved and how they are named.</summary>
    public static class ScreenshotPaths
    {
        const string FolderName = "Screenshots";

        public static string CreateFileName(DateTime timestamp)
        {
            return "ARToolkit_" + timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".png";
        }

        /// <summary>
        /// Mobile: the app's persistent data folder (Android/data/&lt;package&gt;/files/Screenshots).
        /// Desktop and Editor: a Screenshots folder next to the game's data folder (or the project folder).
        /// </summary>
        public static string GetDirectory(bool isMobile, string dataPath, string persistentDataPath)
        {
            if (isMobile)
            {
                return Path.Combine(persistentDataPath, FolderName);
            }

            var parent = Path.GetDirectoryName(dataPath);
            var root = string.IsNullOrEmpty(parent) ? persistentDataPath : parent;
            return Path.Combine(root, FolderName);
        }
    }
}
