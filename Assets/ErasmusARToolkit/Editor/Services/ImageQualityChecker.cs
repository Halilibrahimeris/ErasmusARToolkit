using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Scores an image target (0-100) with Google's arcoreimg tool, which ships inside the
    /// ARCore XR Plugin. Google recommends a score of at least 75.
    /// </summary>
    public static class ImageQualityChecker
    {
        public const int RecommendedScore = 75;
        const int TimeoutMilliseconds = 20000;

        // arcoreimg prints this (on stderr) instead of a score when the image has too little detail to track.
        const string NotEnoughDetailMessage = "enough keypoints";

        public static bool IsGoodScore(int score) => score >= RecommendedScore;

        public static string GetToolRelativePath(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.OSXEditor:
                    return "Tools~/MacOS/arcoreimg";
                case RuntimePlatform.LinuxEditor:
                    return "Tools~/Linux/arcoreimg";
                default:
                    return "Tools~/Windows/arcoreimg.exe";
            }
        }

        /// <summary>
        /// The score is printed on its own line; an image with too little detail scores 0.
        /// Returns -1 when there is no score.
        /// </summary>
        public static int ParseScore(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return -1;
            }

            if (output.IndexOf(NotEnoughDetailMessage, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0;
            }

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines.Reverse())
            {
                if (int.TryParse(line.Trim(), out var score) && score >= 0 && score <= 100)
                {
                    return score;
                }
            }

            return -1;
        }

        /// <summary>Returns the score, or -1 if the tool or image is unavailable.</summary>
        public static int Evaluate(Texture2D texture)
        {
            var toolPath = FindTool();
            var assetPath = texture != null ? AssetDatabase.GetAssetPath(texture) : null;
            if (toolPath == null || !ImageFileRules.IsSupportedFormat(assetPath))
            {
                return -1;
            }

            var imagePath = Path.GetFullPath(assetPath);
            if (imagePath.IndexOf('"') >= 0)
            {
                // Would break the quoted command-line argument below.
                return -1;
            }

            try
            {
                var startInfo = new ProcessStartInfo(toolPath, $"eval-img --input_image_path=\"{imagePath}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return -1;
                    }

                    // Both streams are read at once so neither can fill up and block the tool.
                    var errors = process.StandardError.ReadToEndAsync();
                    var output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(TimeoutMilliseconds))
                    {
                        process.Kill();
                        return -1;
                    }

                    return ParseScore(output + "\n" + errors.Result);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Erasmus AR Toolkit] Could not run arcoreimg: " + exception.Message);
                return -1;
            }
        }

        static string FindTool()
        {
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == ToolkitPaths.ArCorePackage);
            if (package == null)
            {
                return null;
            }

            var path = Path.Combine(package.resolvedPath, GetToolRelativePath(Application.platform));
            return File.Exists(path) ? path : null;
        }
    }
}
