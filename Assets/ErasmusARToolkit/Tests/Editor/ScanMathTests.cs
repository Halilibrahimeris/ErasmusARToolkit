using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class DepthNoiseTests
    {
        [Test]
        public void Sigma_GrowsWithTheSquareOfTheDistance()
        {
            Assert.AreEqual(0.015f, DepthNoise.Sigma(1f), 1e-5f);
            Assert.AreEqual(0.030f, DepthNoise.Sigma(2f), 1e-5f);
            Assert.AreEqual(0.055f, DepthNoise.Sigma(3f), 1e-5f);
        }

        [Test]
        public void HitTolerance_IsTwoSigma_WithinThreeAndSixCentimeters()
        {
            Assert.AreEqual(0.03f, DepthNoise.HitTolerance(0.5f), 1e-5f);
            Assert.AreEqual(0.0425f, DepthNoise.HitTolerance(1.5f), 1e-5f);
            Assert.AreEqual(0.06f, DepthNoise.HitTolerance(3f), 1e-5f);
        }

        [Test]
        public void MissMargin_AndBand_AreWiderThanTheHitTolerance()
        {
            for (var distance = 0.3f; distance <= 4.5f; distance += 0.3f)
            {
                Assert.Greater(DepthNoise.MissMargin(distance), DepthNoise.HitTolerance(distance), "at " + distance);
                Assert.GreaterOrEqual(DepthNoise.Band(distance), DepthNoise.MinBand, "at " + distance);
                Assert.Less(DepthNoise.Band(distance), DepthNoise.MissMargin(distance) + 1e-5f, "at " + distance);
            }

            Assert.AreEqual(0.165f, DepthNoise.MissMargin(3f), 1e-5f);
        }

        [Test]
        public void HeightWeight_IsFullNearby_AndSmallFarAway()
        {
            Assert.AreEqual(1f, DepthNoise.HeightWeight(0.5f), 1e-5f);
            Assert.AreEqual(1f, DepthNoise.HeightWeight(1f), 1e-5f);
            Assert.AreEqual(0.25f, DepthNoise.HeightWeight(2f), 1e-4f);
            Assert.Less(DepthNoise.HeightWeight(3f), 0.1f);
        }
    }

    public class HeightHistogramTests
    {
        static void AddSurface(HeightHistogram histogram, float height, int samples, float spread = 0.004f)
        {
            for (var i = 0; i < samples; i++)
            {
                // Evenly spread around the height, like noise.
                var offset = ((i % 9) - 4) / 4f * spread;
                histogram.Add(height + offset, 1f, 1f);
            }
        }

        [Test]
        public void TryNearestMode_FindsTheSurfaceHeight()
        {
            var histogram = new HeightHistogram();
            AddSurface(histogram, -1.2f, 300);

            Assert.IsTrue(histogram.TryNearestMode(-1.17f, 0.15f, 30f, out var height, out var sharp));

            Assert.AreEqual(-1.2f, height, 0.003f);
            Assert.IsTrue(sharp);
        }

        [Test]
        public void TryNearestMode_TakesTheNearestPeak_NotTheLargest()
        {
            var histogram = new HeightHistogram();
            AddSurface(histogram, 0f, 900);        // the floor
            AddSurface(histogram, 0.12f, 120);     // a low step next to it

            Assert.IsTrue(histogram.TryNearestMode(0.11f, 0.15f, 30f, out var step, out _));
            Assert.IsTrue(histogram.TryNearestMode(0.02f, 0.15f, 30f, out var floor, out _));

            Assert.AreEqual(0.12f, step, 0.005f);
            Assert.AreEqual(0f, floor, 0.005f);
        }

        [Test]
        public void TryNearestMode_NeedsEnoughObservations_AndAPeakInReach()
        {
            var histogram = new HeightHistogram();
            Assert.IsFalse(histogram.TryNearestMode(0f, 0.15f, 30f, out _, out _), "Nothing was added.");

            AddSurface(histogram, 0.75f, 20);
            Assert.IsFalse(histogram.TryNearestMode(0.75f, 0.15f, 30f, out _, out _), "20 observations are too few.");

            AddSurface(histogram, 0.75f, 20);
            Assert.IsTrue(histogram.TryNearestMode(0.75f, 0.15f, 30f, out _, out _));
            Assert.IsFalse(histogram.TryNearestMode(0.45f, 0.15f, 30f, out _, out _), "The peak is 30 cm away.");
        }

        [Test]
        public void TryNearestMode_BlurredSurface_IsNotSharp()
        {
            var histogram = new HeightHistogram();
            for (var i = 0; i < 600; i++)
            {
                histogram.Add(((i % 61) - 30) * 0.0015f, 1f, 1f);   // spread over +-4.5 cm
            }

            Assert.IsTrue(histogram.TryNearestMode(0f, 0.15f, 30f, out _, out var sharp));
            Assert.IsFalse(sharp);
        }

        [Test]
        public void Decay_LetsANewHeightWin()
        {
            var histogram = new HeightHistogram();
            AddSurface(histogram, 0f, 600);

            // ARCore corrected its map: the same floor is now measured 4 cm lower.
            for (var frame = 0; frame < 120; frame++)
            {
                histogram.Decay(0.97f);
                AddSurface(histogram, -0.04f, 60);
            }

            Assert.IsTrue(histogram.TryNearestMode(0f, 0.15f, 30f, out var height, out _));
            Assert.AreEqual(-0.04f, height, 0.004f);
            Assert.Less(histogram.CountAround(0f, 0.01f), 30f, "The old height has faded.");
        }

        [Test]
        public void WeightedSamples_PullTheHeightLess()
        {
            var histogram = new HeightHistogram();
            for (var i = 0; i < 100; i++)
            {
                histogram.Add(0.000f, 1f, 1f);     // near, exact
                histogram.Add(0.012f, 0.1f, 1f);   // far, noisy
            }

            Assert.IsTrue(histogram.TryNearestMode(0f, 0.15f, 30f, out var height, out _));
            Assert.Less(height, 0.003f);
        }

        [Test]
        public void Clear_EmptiesIt_AndTheRangeFollowsTheNextFirstSample()
        {
            var histogram = new HeightHistogram();
            AddSurface(histogram, 0f, 100);

            histogram.Clear();
            Assert.IsTrue(histogram.IsEmpty);

            AddSurface(histogram, 40f, 100);
            Assert.IsTrue(histogram.TryNearestMode(40f, 0.15f, 30f, out var height, out _));
            Assert.AreEqual(40f, height, 0.003f);
        }
    }

    public class FrameCellSetTests
    {
        [Test]
        public void Add_IsTrueOncePerFrame()
        {
            var set = new FrameCellSet();

            Assert.IsTrue(set.Add(3, -7, 12));
            Assert.IsFalse(set.Add(3, -7, 12));
            Assert.IsTrue(set.Add(3, -7, 13));
            Assert.IsTrue(set.Add(-7, 3, 12));
            Assert.AreEqual(3, set.Count);

            set.NextFrame();

            Assert.AreEqual(0, set.Count);
            Assert.IsTrue(set.Add(3, -7, 12));
        }

        [Test]
        public void Add_ManyCells_AreAllKept()
        {
            var set = new FrameCellSet();
            for (var z = -40; z < 40; z++)
            {
                for (var x = -40; x < 40; x++)
                {
                    Assert.IsTrue(set.Add(x, z, x + z), $"cell {x},{z}");
                }
            }

            Assert.AreEqual(6400, set.Count);
            Assert.IsFalse(set.Add(-40, -40, -80));
            Assert.IsFalse(set.Add(39, 39, 78));
        }

        [Test]
        public void Add_WhenFull_RefusesFurtherCells_InsteadOfSearchingForever()
        {
            var set = new FrameCellSet();
            var added = 0;
            for (var i = 0; i < 20000; i++)
            {
                added += set.Add(i, 0, 0) ? 1 : 0;
            }

            Assert.AreEqual(set.Count, added);
            Assert.Less(added, 16384);
            Assert.Greater(added, 6000, "A frame of 6000 points always fits.");
        }
    }

    public class HeightLockTests
    {
        const float Redraw = 0.5f;

        [Test]
        public void FirstHeight_IsTakenAtOnce()
        {
            var height = new HeightLock();
            Assert.IsFalse(height.HasValue);

            Assert.IsTrue(height.Update(-1.2f, Redraw, null, false));

            Assert.IsTrue(height.HasValue);
            Assert.AreEqual(-1.2f, height.Value);
        }

        [Test]
        public void SmallChanges_DoNotMoveTheDrawnHeight()
        {
            var height = new HeightLock();
            height.Update(0f, Redraw, null, false);

            for (var i = 0; i < 20; i++)
            {
                Assert.IsFalse(height.Update(i % 2 == 0 ? 0.012f : -0.012f, Redraw, null, false));
            }

            Assert.AreEqual(0f, height.Value);
        }

        [Test]
        public void LastingChange_IsFollowedSlowly_AfterThreeRedraws()
        {
            var height = new HeightLock();
            height.Update(0f, Redraw, null, false);

            Assert.IsFalse(height.Update(0.05f, Redraw, null, false));
            Assert.IsFalse(height.Update(0.05f, Redraw, null, false));
            Assert.IsTrue(height.Update(0.05f, Redraw, null, false));
            Assert.AreEqual(0.01f, height.Value, 1e-5f, "2 cm per second, half a second per redraw.");

            for (var i = 0; i < 10; i++)
            {
                height.Update(0.05f, Redraw, null, false);
            }

            Assert.AreEqual(0.05f, height.Value, HeightLock.Settle);
            Assert.IsFalse(height.Update(0.05f, Redraw, null, false), "Arrived: nothing to redraw.");
        }

        [Test]
        public void OneOutlier_DoesNotStartAMove()
        {
            var height = new HeightLock();
            height.Update(0f, Redraw, null, false);

            height.Update(0.06f, Redraw, null, false);
            height.Update(0.06f, Redraw, null, false);
            height.Update(0f, Redraw, null, false);
            Assert.IsFalse(height.Update(0.06f, Redraw, null, false));

            Assert.AreEqual(0f, height.Value);
        }

        [Test]
        public void Floor_SinksAtMostThreeCentimeters_WithoutItsPlane()
        {
            var height = new HeightLock();
            height.Update(0f, Redraw, 0f, true);

            for (var i = 0; i < 40; i++)
            {
                height.Update(-0.10f, Redraw, 0f, true);
            }

            Assert.AreEqual(-HeightLock.DownLimit, height.Value, HeightLock.Settle);
        }

        [Test]
        public void Floor_KeepsItsLimit_WhenThePlaneIsLost()
        {
            var height = new HeightLock();
            height.Update(0.01f, Redraw, 0f, true);

            for (var i = 0; i < 40; i++)
            {
                height.Update(-0.10f, Redraw, null, true);
            }

            Assert.AreEqual(-HeightLock.DownLimit, height.Value, HeightLock.Settle);
        }

        [Test]
        public void Floor_WithoutAnyPlane_FollowsDepth()
        {
            var height = new HeightLock();
            height.Update(0f, Redraw, null, true);

            for (var i = 0; i < 40; i++)
            {
                height.Update(-0.10f, Redraw, null, true);
            }

            Assert.AreEqual(-0.10f, height.Value, HeightLock.Settle);
        }

        [Test]
        public void Floor_FollowsDown_WhenItsPlaneMovedDownToo()
        {
            var height = new HeightLock();
            height.Update(0f, Redraw, 0f, true);

            for (var i = 0; i < 40; i++)
            {
                height.Update(-0.10f, Redraw, -0.10f, true);
            }

            Assert.AreEqual(-0.10f, height.Value, HeightLock.Settle);
        }

        [Test]
        public void RaisedSurface_MayMoveDownFreely()
        {
            var height = new HeightLock();
            height.Update(0.78f, Redraw, 0.78f, false);

            for (var i = 0; i < 40; i++)
            {
                height.Update(0.70f, Redraw, 0.78f, false);
            }

            Assert.AreEqual(0.70f, height.Value, HeightLock.Settle);
        }

        [Test]
        public void Reset_StartsOver()
        {
            var height = new HeightLock();
            height.Update(0.4f, Redraw, null, false);

            height.Reset();

            Assert.IsFalse(height.HasValue);
            height.Update(0.9f, Redraw, null, false);
            Assert.AreEqual(0.9f, height.Value);
        }
    }

    public class CameraPoseBufferTests
    {
        static Pose At(float x, float yaw = 0f)
        {
            return new Pose(new Vector3(x, 0f, 0f), Quaternion.Euler(0f, yaw, 0f));
        }

        [Test]
        public void TrySample_NearAFrame_GivesThatPose()
        {
            var buffer = new CameraPoseBuffer();
            buffer.Push(1.000, At(1f));
            buffer.Push(1.033, At(2f));
            buffer.Push(1.066, At(3f));

            Assert.IsTrue(buffer.TrySample(1.035, out var pose));
            Assert.AreEqual(2f, pose.position.x);
            Assert.IsTrue(buffer.TrySample(1.066, out pose));
            Assert.AreEqual(3f, pose.position.x);
        }

        [Test]
        public void TrySample_BetweenFrames_Interpolates()
        {
            var buffer = new CameraPoseBuffer();
            buffer.Push(2.00, At(0f, 0f));
            buffer.Push(2.05, At(1f, 10f));

            Assert.IsTrue(buffer.TrySample(2.025, out var pose));

            Assert.AreEqual(0.5f, pose.position.x, 1e-3f);
            Assert.AreEqual(5f, pose.rotation.eulerAngles.y, 1e-2f);
        }

        [Test]
        public void TrySample_OutsideTheBuffer_OrAcrossALongGap_Fails()
        {
            var buffer = new CameraPoseBuffer();
            Assert.IsFalse(buffer.TrySample(1.0, out _), "The buffer is empty.");

            buffer.Push(1.0, At(0f));
            buffer.Push(1.5, At(1f));

            Assert.IsFalse(buffer.TrySample(0.5, out _), "Older than every pose.");
            Assert.IsFalse(buffer.TrySample(2.0, out _), "Newer than every pose.");
            Assert.IsFalse(buffer.TrySample(1.25, out _), "Half a second between the two poses.");
        }

        [Test]
        public void Push_KeepsTheLastPoses_AndStartsOverWhenTimeGoesBack()
        {
            var buffer = new CameraPoseBuffer();
            for (var i = 0; i < 100; i++)
            {
                buffer.Push(i * 0.033, At(i));
            }

            Assert.AreEqual(CameraPoseBuffer.Capacity, buffer.Count);
            Assert.IsTrue(buffer.TrySample(99 * 0.033, out var newest));
            Assert.AreEqual(99f, newest.position.x);
            Assert.IsFalse(buffer.TrySample(10 * 0.033, out _), "Dropped long ago.");

            buffer.Push(0.5, At(-1f));

            Assert.AreEqual(1, buffer.Count);
            Assert.IsTrue(buffer.TrySample(0.5, out var restarted));
            Assert.AreEqual(-1f, restarted.position.x);
        }

        [Test]
        public void Push_TheSameFrameTwice_KeepsOnePose()
        {
            var buffer = new CameraPoseBuffer();
            buffer.Push(1.0, At(1f));
            buffer.Push(1.0, At(5f));

            Assert.AreEqual(1, buffer.Count);
        }
    }

    public class MappingSelfCheckTests
    {
        static MappingSelfCheck Check() => new MappingSelfCheck(4);

        static bool Image(MappingSelfCheck check, params float[] errors)
        {
            for (var candidate = 0; candidate < errors.Length; candidate++)
            {
                for (var point = 0; point < 6; point++)
                {
                    check.AddSample(candidate, errors[candidate]);
                }
            }

            return check.EndImage(true);
        }

        [Test]
        public void ClearWinner_IsVerified()
        {
            var check = Check();

            Assert.IsTrue(Image(check, 0.30f, 0.25f, 0.02f, 0.40f));

            Assert.AreEqual(MappingState.Verified, check.State);
            Assert.AreEqual(2, check.Best);
            Assert.IsTrue(check.IsUsable);
            Assert.AreEqual(0.02f, check.MeanError(2), 1e-5f);
            Assert.IsFalse(Image(check, 0.01f, 0.5f, 0.5f, 0.5f), "A verified mapping is final.");
            Assert.AreEqual(2, check.Best);
        }

        [Test]
        public void TiedCandidates_AreAssumedAfterEnoughImages_NeverWaitedForForever()
        {
            var check = Check();
            for (var image = 1; image < MappingSelfCheck.ImagesToAssume; image++)
            {
                Assert.IsFalse(Image(check, 0.03f, 0.03f, 0.04f, 0.04f));
                Assert.AreEqual(MappingState.Checking, check.State);
                Assert.IsFalse(check.IsUsable);
            }

            Assert.IsTrue(Image(check, 0.03f, 0.03f, 0.04f, 0.04f));

            Assert.AreEqual(MappingState.Assumed, check.State);
            Assert.AreEqual(0, check.Best, "The documented mapping comes first among the tied ones.");
            Assert.IsTrue(check.IsUsable);
            Assert.IsFalse(check.IsFinal);
        }

        [Test]
        public void AssumedMapping_CanStillBeVerified_AlsoAsAnotherCandidate()
        {
            var check = Check();
            for (var image = 0; image < MappingSelfCheck.ImagesToAssume; image++)
            {
                Image(check, 0.03f, 0.03f, 0.03f, 0.03f);
            }

            Assert.AreEqual(MappingState.Assumed, check.State);
            Assert.AreEqual(0, check.Best);

            // A view with depth variety: only candidate 1 keeps matching.
            var changed = false;
            for (var image = 0; image < 40 && !changed; image++)
            {
                changed = Image(check, 0.40f, 0.02f, 0.40f, 0.40f);
            }

            Assert.IsTrue(changed);
            Assert.AreEqual(MappingState.Verified, check.State);
            Assert.AreEqual(1, check.Best);
        }

        [Test]
        public void NoCandidateClose_IsUnverifiable()
        {
            var check = Check();
            for (var image = 0; image < MappingSelfCheck.ImagesToAssume; image++)
            {
                Image(check, 0.5f, 0.6f, 0.5f, 0.6f);
            }

            Assert.AreEqual(MappingState.Unverifiable, check.State);
            Assert.IsFalse(check.IsUsable);
            Assert.IsTrue(check.IsFinal);
        }

        [Test]
        public void NoDepthHits_IsUnverifiable()
        {
            var check = Check();
            for (var image = 1; image < MappingSelfCheck.ImagesWithoutHits; image++)
            {
                Assert.IsFalse(check.EndImage(false));
            }

            Assert.IsTrue(check.EndImage(false));
            Assert.AreEqual(MappingState.Unverifiable, check.State);
        }

        [Test]
        public void TooFewPoints_AreNotJudged()
        {
            var check = Check();
            check.AddSample(1, 0.01f);
            check.AddSample(1, 0.01f);

            Assert.IsFalse(check.EndImage(true));
            Assert.AreEqual(MappingState.Checking, check.State);
        }

        [Test]
        public void Reset_StartsTheCheckAgain()
        {
            var check = Check();
            Image(check, 0.30f, 0.25f, 0.02f, 0.40f);

            check.Reset();

            Assert.AreEqual(MappingState.Checking, check.State);
            Assert.AreEqual(0, check.Best);
            Assert.AreEqual(0, check.Images);
            Assert.IsTrue(float.IsNaN(check.MeanError(2)));
        }
    }
}
