using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class ConfigValidatorTests
    {
        GameObject prefab;
        Texture2D texture;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("TestPrefab");
            texture = new Texture2D(64, 32) { name = "Marker" };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void ValidConfig_HasNoIssues()
        {
            var issues = ConfigValidator.Validate(
                new[] { new PlaceableItem("Box", prefab) },
                new[] { new ImageTargetItem("Poster", texture, 0.2f, prefab) },
                10, new Vector2(0.5f, 2f));
            Assert.IsEmpty(issues);
        }

        [Test]
        public void NoPlaceables_IsWarningOnly()
        {
            var issues = ConfigValidator.Validate(new PlaceableItem[0], new ImageTargetItem[0], 10, Vector2.one);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(IssueSeverity.Warning, issues[0].Severity);
            Assert.IsFalse(ConfigValidator.HasErrors(issues));
        }

        [Test]
        public void PlaceableWithoutPrefab_IsError()
        {
            var issues = ConfigValidator.Validate(
                new[] { new PlaceableItem("Box", null) }, new ImageTargetItem[0], 10, Vector2.one);
            Assert.IsTrue(ConfigValidator.HasErrors(issues));
        }

        [Test]
        public void PlaceableWithNonPositiveScale_IsError()
        {
            var issues = ConfigValidator.Validate(
                new[] { new PlaceableItem("Box", prefab, 0f) }, new ImageTargetItem[0], 10, Vector2.one);
            Assert.IsTrue(ConfigValidator.HasErrors(issues));
        }

        [Test]
        public void DuplicatePlaceableNames_IsWarning()
        {
            var issues = ConfigValidator.Validate(
                new[] { new PlaceableItem("Box", prefab), new PlaceableItem("Box", prefab) },
                new ImageTargetItem[0], 10, Vector2.one);
            Assert.IsFalse(ConfigValidator.HasErrors(issues));
            Assert.IsTrue(issues.Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("Box")));
        }

        [Test]
        public void ImageTargetProblems_AreErrors()
        {
            var noTexture = ConfigValidator.Validate(new[] { new PlaceableItem("Box", prefab) },
                new[] { new ImageTargetItem("A", null, 0.2f, prefab) }, 10, Vector2.one);
            var badWidth = ConfigValidator.Validate(new[] { new PlaceableItem("Box", prefab) },
                new[] { new ImageTargetItem("A", texture, 0f, prefab) }, 10, Vector2.one);
            var duplicates = ConfigValidator.Validate(new[] { new PlaceableItem("Box", prefab) },
                new[] { new ImageTargetItem("A", texture, 0.2f, prefab), new ImageTargetItem("A", texture, 0.2f, prefab) },
                10, Vector2.one);

            Assert.IsTrue(ConfigValidator.HasErrors(noTexture));
            Assert.IsTrue(ConfigValidator.HasErrors(badWidth));
            Assert.IsTrue(ConfigValidator.HasErrors(duplicates));
        }

        [Test]
        public void ImageTargetWithoutPrefabOrLabel_IsError_ButLabelOnlyIsFine()
        {
            var nothing = ConfigValidator.Validate(new[] { new PlaceableItem("Box", prefab) },
                new[] { new ImageTargetItem("A", texture, 0.2f, null) }, 10, Vector2.one);
            var labelOnly = ConfigValidator.Validate(new[] { new PlaceableItem("Box", prefab) },
                new[] { new ImageTargetItem("A", texture, 0.2f, null, new InfoLabelData("Title", "Body")) },
                10, Vector2.one);

            Assert.IsTrue(ConfigValidator.HasErrors(nothing));
            Assert.IsEmpty(labelOnly);
        }

        [Test]
        public void ImageTargetName_FallsBackToTextureName()
        {
            var issues = ConfigValidator.Validate(new[] { new PlaceableItem("Box", prefab) },
                new[] { new ImageTargetItem("", texture, 0.2f, prefab), new ImageTargetItem("Marker", texture, 0.2f, prefab) },
                10, Vector2.one);
            Assert.IsTrue(issues.Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("Marker")));
        }

        [Test]
        public void InvalidLimits_AreErrors()
        {
            var placeables = new[] { new PlaceableItem("Box", prefab) };
            Assert.IsTrue(ConfigValidator.HasErrors(
                ConfigValidator.Validate(placeables, new ImageTargetItem[0], 0, Vector2.one)));
            Assert.IsTrue(ConfigValidator.HasErrors(
                ConfigValidator.Validate(placeables, new ImageTargetItem[0], 5, new Vector2(0f, 2f))));
            Assert.IsTrue(ConfigValidator.HasErrors(
                ConfigValidator.Validate(placeables, new ImageTargetItem[0], 5, new Vector2(3f, 2f))));
        }

        [Test]
        public void VeryWideImageTarget_IsWarning()
        {
            var issues = ConfigValidator.Validate(new[] { new PlaceableItem("Box", prefab) },
                new[] { new ImageTargetItem("Poster", texture, 1f, prefab) }, 10, Vector2.one);
            Assert.IsFalse(ConfigValidator.HasErrors(issues));
            Assert.IsTrue(issues.Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("Poster")));
        }

        [Test]
        public void NullConfig_IsError()
        {
            Assert.IsTrue(ConfigValidator.HasErrors(ConfigValidator.Validate(null)));
        }

        [Test]
        public void ConfigAsset_IsValidatedThroughItsProperties()
        {
            var config = ScriptableObject.CreateInstance<ARToolkitConfig>();
            try
            {
                config.SetContent(new[] { new PlaceableItem("Box", prefab) }, new ImageTargetItem[0]);
                Assert.IsEmpty(ConfigValidator.Validate(config));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
