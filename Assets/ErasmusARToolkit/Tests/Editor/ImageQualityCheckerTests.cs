using Erasmus.ARToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class ImageQualityCheckerTests
    {
        [TestCase("100", 100)]
        [TestCase("  85\r\n", 85)]
        [TestCase("Some log line\n42\n", 42)]
        [TestCase("", -1)]
        [TestCase("Failed to get image score", -1)]
        [TestCase(null, -1)]
        [TestCase("Failed to get enough keypoints from target image.\r\n", 0)]
        public void ParseScore_ReadsLastNumberLine(string output, int expected)
        {
            Assert.AreEqual(expected, ImageQualityChecker.ParseScore(output));
        }

        [Test]
        public void ToolPath_DependsOnPlatform()
        {
            Assert.AreEqual("Tools~/Windows/arcoreimg.exe", ImageQualityChecker.GetToolRelativePath(RuntimePlatform.WindowsEditor));
            Assert.AreEqual("Tools~/MacOS/arcoreimg", ImageQualityChecker.GetToolRelativePath(RuntimePlatform.OSXEditor));
            Assert.AreEqual("Tools~/Linux/arcoreimg", ImageQualityChecker.GetToolRelativePath(RuntimePlatform.LinuxEditor));
        }

        [TestCase(100, true)]
        [TestCase(75, true)]
        [TestCase(74, false)]
        [TestCase(-1, false)]
        public void IsGoodScore_UsesArCoreRecommendation(int score, bool expected)
        {
            Assert.AreEqual(expected, ImageQualityChecker.IsGoodScore(score));
        }
    }
}
