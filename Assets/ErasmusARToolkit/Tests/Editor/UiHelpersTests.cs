using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class InfoLabelTextTests
    {
        [Test]
        public void TitleAndBody_TitleIsBoldColoredFirstLine()
        {
            var text = InfoLabel.FormatText(new InfoLabelData("Globe", "Erasmus+ connects Europe."), new Color(1f, 1f, 0f));
            Assert.AreEqual("<b><color=#FFFF00><size=125%>Globe</size></color></b>\nErasmus+ connects Europe.", text);
        }

        [Test]
        public void MissingParts_AreLeftOut()
        {
            Assert.AreEqual("Only body", InfoLabel.FormatText(new InfoLabelData("", " Only body "), Color.white));
            Assert.AreEqual("<b><color=#FFFFFF><size=125%>Title</size></color></b>",
                InfoLabel.FormatText(new InfoLabelData("Title", "  "), Color.white));
        }
    }

    public class WebcamCoverSizeTests
    {
        [Test]
        public void WiderImage_KeepsHeight_AndOverflowsWidth()
        {
            var size = CameraFeedLayout.CoverSize(10f, 1f, 2f);
            Assert.AreEqual(new Vector2(20f, 10f), size);
        }

        [Test]
        public void TallerImage_KeepsWidth_AndOverflowsHeight()
        {
            var size = CameraFeedLayout.CoverSize(10f, 2f, 1f);
            Assert.AreEqual(new Vector2(20f, 20f), size);
        }
    }

    public class UIPointerGuardTests
    {
        [Test]
        public void ScreenPosition_IsFlippedToTopLeftOrigin()
        {
            Assert.AreEqual(new Vector2(100f, 900f), UIPointerGuard.ToPanelScreenPosition(new Vector2(100f, 180f), 1080f));
        }
    }
}
