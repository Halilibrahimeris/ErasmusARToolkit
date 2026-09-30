using System.Linq;
using Erasmus.ARToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class PosterLayoutTests
    {
        [Test]
        public void QuadScale_CoversTheImage_UnderAnUnscaledParent()
        {
            var scale = PosterLayout.QuadScale(new Vector2(0.3f, 0.42f), Vector3.one);
            Assert.AreEqual(0.3f, scale.x, 0.0001f);
            Assert.AreEqual(0.42f, scale.y, 0.0001f);
        }

        [Test]
        public void QuadScale_UndoesTheParentScale()
        {
            // A model scaled by 2 on the image: the video still covers the image, not twice its size.
            var scale = PosterLayout.QuadScale(new Vector2(0.3f, 0.42f), new Vector3(2f, 1f, 3f));
            Assert.AreEqual(0.15f, scale.x, 0.0001f);
            Assert.AreEqual(0.14f, scale.y, 0.0001f);
        }

        [Test]
        public void FitWidth_KeepsTheVideoShape()
        {
            var size = PosterLayout.FitWidth(1.6f, 1920, 1080);
            Assert.AreEqual(1.6f, size.x, 0.0001f);
            Assert.AreEqual(0.9f, size.y, 0.0001f);
            Assert.AreEqual(0.9f, PosterLayout.FitWidth(1.6f, 0, 0).y, 0.0001f, "Unknown size: 16:9.");
        }
    }

    public class PosterConfigTests
    {
        Texture2D texture;
        ARToolkitConfig config;

        [SetUp]
        public void SetUp()
        {
            texture = new Texture2D(512, 512) { name = "Poster" };
            config = ScriptableObject.CreateInstance<ARToolkitConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void Validate_PosterWithOnlyASound_IsFine()
        {
            var sound = AudioClip.Create("Narration", 4410, 1, 44100, false);
            try
            {
                var poster = new ImageTargetItem("Poster", texture, 0.3f, null, null, new PosterMedia(null, sound));
                var issues = ConfigValidator.Validate(new PlaceableItem[0], new[] { poster }, 10, Vector2.one);
                Assert.IsFalse(ConfigValidator.HasErrors(issues));
            }
            finally
            {
                Object.DestroyImmediate(sound);
            }
        }

        [Test]
        public void Validate_ImageWithNothingToShow_IsAnError()
        {
            var empty = new ImageTargetItem("Poster", texture, 0.3f, null);
            var issues = ConfigValidator.Validate(new PlaceableItem[0], new[] { empty }, 10, Vector2.one);
            Assert.IsTrue(ConfigValidator.HasErrors(issues));
        }

        [Test]
        public void Add_AppendsAPosterWithItsCard_AndAUniqueName()
        {
            PosterBuilder.Add(config, texture, 0.3f, null, null, true, null, "Title", "Body");
            var second = PosterBuilder.Add(config, texture, 0.3f, null, null, false, null, "", "");

            Assert.AreEqual(2, config.ImageTargets.Count);
            Assert.AreEqual("Poster", config.ImageTargets[0].ResolvedName);
            Assert.AreEqual("Poster 2", second.ResolvedName);
            Assert.AreEqual("Title", config.ImageTargets[0].Info.Title);
            Assert.IsFalse(second.Media.Loop);
        }

        [Test]
        public void Remove_TakesThePosterOut()
        {
            var poster = PosterBuilder.Add(config, texture, 0.3f, null, null, true, null, "Title", "Body");
            ConfigEdits.RemoveImageTarget(config, poster, "Remove");
            Assert.AreEqual(0, config.ImageTargets.Count);
        }

        [Test]
        public void Unique_AddsANumber()
        {
            Assert.AreEqual("Stop", ConfigEdits.Unique(new[] { "Other" }, "Stop"));
            Assert.AreEqual("Stop 3", ConfigEdits.Unique(new[] { "Stop", "Stop 2" }, "Stop"));
        }

        [Test]
        public void MediaFileRules_OnlyMp4IsPhoneFriendly()
        {
            Assert.IsTrue(MediaFileRules.IsPhoneFriendlyVideo("Assets/Clip.MP4"));
            Assert.IsFalse(MediaFileRules.IsPhoneFriendlyVideo("Assets/Clip.mov"));
            Assert.IsFalse(MediaFileRules.IsPhoneFriendlyVideo(null));
        }

        [Test]
        public void Describe_ListsWhatThePosterShows()
        {
            var sound = AudioClip.Create("Narration", 4410, 1, 44100, false);
            try
            {
                var poster = new ImageTargetItem("Poster", texture, 0.3f, null, new InfoLabelData("A", "B"),
                    new PosterMedia(null, sound));
                StringAssert.Contains("sound, card", PosterBuilder.Describe(poster));
                Assert.IsFalse(PosterBuilder.Describe(poster).Contains("video"));
            }
            finally
            {
                Object.DestroyImmediate(sound);
            }
        }
    }
}
