using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace Erasmus.ARToolkit.Tests
{
    public class RoomScanObstacleTextTests
    {
        [Test]
        public void Progress_CountsTheObstacles_WhenThereAreAny()
        {
            var none = new RoomScanProgress(true, 2, 3.5f, 6f, true);
            var one = new RoomScanProgress(true, 2, 3.5f, 6f, true, true, 1);
            var three = new RoomScanProgress(true, 2, 3.5f, 6f, true, true, 3);

            StringAssert.DoesNotContain("obstacle", RoomScanText.Progress(none));
            StringAssert.Contains("· 1 obstacle", RoomScanText.Progress(one));
            StringAssert.DoesNotContain("1 obstacles", RoomScanText.Progress(one));
            StringAssert.Contains("· 3 obstacles", RoomScanText.Progress(three));
            Assert.AreEqual(3, three.ObstacleCount);
        }

        [Test]
        public void Progress_CountsTheWalls_WhenThereAreAny()
        {
            var none = new RoomScanProgress(true, 2, 3.5f, 6f, true, true, 3);
            var one = new RoomScanProgress(true, 2, 3.5f, 6f, true, true, 3, 1);
            var four = new RoomScanProgress(true, 2, 3.5f, 6f, true, true, 0, 4);

            StringAssert.DoesNotContain("wall", RoomScanText.Progress(none));
            StringAssert.Contains("· 3 obstacles · 1 wall", RoomScanText.Progress(one));
            StringAssert.Contains("· 4 walls", RoomScanText.Progress(four));
            StringAssert.DoesNotContain("obstacle", RoomScanText.Progress(four));
        }
    }

    public class RoomScanTextTests
    {
        [Test]
        public void ButtonText_SwitchesBetweenScanAndFinish()
        {
            Assert.AreEqual("Scan Room", RoomScanText.ButtonText(false));
            Assert.AreEqual("Finish Scan", RoomScanText.ButtonText(true));
        }

        [Test]
        public void Progress_ShowsSurfacesAndCheckedArea()
        {
            var text = RoomScanText.Progress(new RoomScanProgress(true, 3, 4.2f, 14.26f, true));

            Assert.AreEqual("Scanning · 3 surfaces · 14.3 m² checked", text);
        }

        [Test]
        public void Progress_OneSurface_IsSingular_AndTrackingPointsAreNamed()
        {
            var text = RoomScanText.Progress(new RoomScanProgress(true, 1, 1f, 2f, false));

            StringAssert.Contains("1 surface ·", text);
            StringAssert.EndsWith("(tracking points)", text);
        }

        [Test]
        public void Texts_UseDotDecimals_WhateverThePhoneLanguage()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");

                StringAssert.Contains("2.5 m²", RoomScanText.Finished(new RoomScanProgress(false, 2, 2.5f, 9f, true)));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void TouchHelp_MentionsTheRoomScan()
        {
            StringAssert.Contains("Scan Room", HelpTexts.Touch);
        }
    }
}
