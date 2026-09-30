using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Erasmus.ARToolkit.Editor
{
    public enum BuildPlatform
    {
        Android,
        Windows,
    }

    /// <summary>Where builds are written. Pure logic.</summary>
    public static class BuildPaths
    {
        const string DefaultName = "ARToolkitApp";

        public static string GetLocationPath(BuildPlatform platform, string outputFolder, string productName)
        {
            var fileName = SanitizeFileName(productName);
            switch (platform)
            {
                case BuildPlatform.Android:
                    return Path.Combine(outputFolder, "Android", fileName + ".apk");
                default:
                    return Path.Combine(outputFolder, "Windows", fileName + ".exe");
            }
        }

        /// <summary>
        /// The first scene of the list starts on the phone. Returns a warning when that is a toolkit sample
        /// while a scene of the project comes later in the list, else null.
        /// </summary>
        public static string FirstSceneHint(IReadOnlyList<string> scenes)
        {
            if (scenes == null || scenes.Count < 2 || !IsUnder(scenes[0], ToolkitPaths.SamplesFolder))
            {
                return null;
            }

            for (var i = 1; i < scenes.Count; i++)
            {
                if (IsUnder(scenes[i], ToolkitPaths.ScenesFolder))
                {
                    return $"The app starts with the sample scene '{Path.GetFileNameWithoutExtension(scenes[0])}'. " +
                           $"Your scene '{Path.GetFileNameWithoutExtension(scenes[i])}' comes later: move it to the " +
                           "top of the scene list (File > Build Profiles) to start with it.";
                }
            }

            return null;
        }

        static bool IsUnder(string path, string folder)
        {
            return !string.IsNullOrEmpty(path)
                   && path.Replace('\\', '/').StartsWith(folder + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return DefaultName;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Trim().Length);
            foreach (var character in name.Trim())
            {
                builder.Append(invalid.Contains(character) ? '_' : character);
            }

            return builder.ToString();
        }
    }

    /// <summary>Rules for exporting the toolkit as a .unitypackage. Pure logic.</summary>
    public static class PackageExportRules
    {
        static readonly Regex VersionPattern = new Regex(@"^\d+\.\d+\.\d+$");

        public static bool IsValidVersion(string version)
        {
            return !string.IsNullOrEmpty(version) && VersionPattern.IsMatch(version);
        }

        public static string CreateFileName(string version)
        {
            return $"ErasmusARToolkit_v{version}.unitypackage";
        }

        public static IEnumerable<string> SelectPaths(IEnumerable<string> assetPaths, bool includeSamples)
        {
            var root = ToolkitPaths.ToolkitRoot + "/";
            return assetPaths
                .Where(path => path.StartsWith(root, System.StringComparison.Ordinal))
                .Where(path => !IsUnder(path, ToolkitPaths.TestsFolder))
                .Where(path => includeSamples || !IsUnder(path, ToolkitPaths.SamplesFolder))
                .Distinct();
        }

        static bool IsUnder(string path, string folder)
        {
            return path == folder || path.StartsWith(folder + "/", System.StringComparison.Ordinal);
        }
    }

    /// <summary>ARCore image requirements that can only be checked in the Editor. Pure logic.</summary>
    public static class ImageFileRules
    {
        public const int MinimumPixels = 300;

        public static bool IsSupportedFormat(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var extension = Path.GetExtension(assetPath).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg";
        }

        public static bool IsLargeEnough(int width, int height)
        {
            return width >= MinimumPixels && height >= MinimumPixels;
        }
    }

    /// <summary>Video and sound files that work on phones.</summary>
    public static class MediaFileRules
    {
        /// <summary>MP4 video (H.264 inside) plays on every Android phone.</summary>
        public static bool IsPhoneFriendlyVideo(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var extension = Path.GetExtension(assetPath).ToLowerInvariant();
            return extension == ".mp4" || extension == ".m4v";
        }
    }
}
