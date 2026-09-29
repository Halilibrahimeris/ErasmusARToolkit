using System.Linq;
using Erasmus.ARToolkit.Editor;
using NUnit.Framework;

namespace Erasmus.ARToolkit.Tests
{
    public class BuildPathsTests
    {
        [Test]
        public void Android_ProducesApkInAndroidFolder()
        {
            var path = BuildPaths.GetLocationPath(BuildPlatform.Android, "C:/Builds", "My AR App").Replace('\\', '/');
            Assert.AreEqual("C:/Builds/Android/My AR App.apk", path);
        }

        [Test]
        public void Windows_ProducesExeInWindowsFolder()
        {
            var path = BuildPaths.GetLocationPath(BuildPlatform.Windows, "C:/Builds", "My AR App").Replace('\\', '/');
            Assert.AreEqual("C:/Builds/Windows/My AR App.exe", path);
        }

        [Test]
        public void InvalidCharacters_AreReplaced_AndEmptyNamesGetDefault()
        {
            Assert.AreEqual("AR_App_", BuildPaths.SanitizeFileName("AR/App?"));
            Assert.AreEqual("ARToolkitApp", BuildPaths.SanitizeFileName("  "));
        }
    }

    public class PackageExportRulesTests
    {
        [TestCase("1.0.0", true)]
        [TestCase("2.10.3", true)]
        [TestCase("1.0", false)]
        [TestCase("v1.0.0", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void VersionFormat(string version, bool expected)
        {
            Assert.AreEqual(expected, PackageExportRules.IsValidVersion(version));
        }

        [Test]
        public void FileName_IncludesVersion()
        {
            Assert.AreEqual("ErasmusARToolkit_v1.2.3.unitypackage", PackageExportRules.CreateFileName("1.2.3"));
        }

        [Test]
        public void SelectPaths_ExcludesTests_AndOptionallySamples()
        {
            var input = new[]
            {
                "Assets/ErasmusARToolkit/Runtime/Core/ARToolkitManager.cs",
                "Assets/ErasmusARToolkit/Tests",
                "Assets/ErasmusARToolkit/Tests/Editor/SomeTests.cs",
                "Assets/ErasmusARToolkit/Samples/Prefabs/Globe.prefab",
                "Assets/ErasmusARToolkit/README.md",
                "Assets/Other/Thing.cs",
            };

            var withSamples = PackageExportRules.SelectPaths(input, true).ToArray();
            var withoutSamples = PackageExportRules.SelectPaths(input, false).ToArray();

            CollectionAssert.AreEquivalent(new[]
            {
                "Assets/ErasmusARToolkit/Runtime/Core/ARToolkitManager.cs",
                "Assets/ErasmusARToolkit/Samples/Prefabs/Globe.prefab",
                "Assets/ErasmusARToolkit/README.md",
            }, withSamples);
            CollectionAssert.DoesNotContain(withoutSamples, "Assets/ErasmusARToolkit/Samples/Prefabs/Globe.prefab");
        }
    }

    public class ReferenceLibraryPathTests
    {
        [Test]
        public void LibraryLivesInGeneratedFolderNextToItsConfig()
        {
            Assert.AreEqual("Assets/ARToolkitContent/Generated/ARToolkitConfig_ImageLibrary.asset",
                ReferenceLibraryBuilder.LibraryPathFor("Assets/ARToolkitContent/ARToolkitConfig.asset", "ARToolkitConfig"));
            Assert.AreEqual("Assets/ErasmusARToolkit/Samples/Generated/Demo_ImageLibrary.asset",
                ReferenceLibraryBuilder.LibraryPathFor("Assets/ErasmusARToolkit/Samples/Demo.asset", "Demo"));
        }

        [Test]
        public void UnsavedConfig_UsesDefaultPath()
        {
            Assert.AreEqual(ToolkitPaths.ImageLibraryAssetPath, ReferenceLibraryBuilder.LibraryPathFor("", "Any"));
        }
    }

    public class ImageFileRulesTests
    {
        [TestCase("Assets/Images/marker.png", true)]
        [TestCase("Assets/Images/marker.JPG", true)]
        [TestCase("Assets/Images/marker.jpeg", true)]
        [TestCase("Assets/Images/marker.psd", false)]
        [TestCase("", false)]
        public void SupportedFormats(string path, bool expected)
        {
            Assert.AreEqual(expected, ImageFileRules.IsSupportedFormat(path));
        }

        [Test]
        public void MinimumSize()
        {
            Assert.IsTrue(ImageFileRules.IsLargeEnough(300, 400));
            Assert.IsFalse(ImageFileRules.IsLargeEnough(299, 1000));
        }
    }
}
