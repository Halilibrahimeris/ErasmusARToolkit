using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class FloorModelTests
    {
        const int RowCells = 20;
        const float RowArea = RowCells * RoomSurfaceMap.CellSize * RoomSurfaceMap.CellSize;

        double clock;

        /// <summary>A level with a detected plane and cells confirmed by depth of the given areas (square meters).</summary>
        static SurfaceLevel Level(int id, float height, float planeArea = 0f, float confirmedArea = 0f)
        {
            var level = new SurfaceLevel(id, height);
            var rows = Mathf.RoundToInt(planeArea / RowArea);
            for (var row = 0; row < rows; row++)
            {
                level.SetPriorRun(row, 0, RowCells - 1);
            }

            if (rows > 0)
            {
                level.AddPlaneSample(height, planeArea);
            }

            rows = Mathf.RoundToInt(confirmedArea / RowArea);
            for (var row = 0; row < rows; row++)
            {
                for (var x = 0; x < RowCells; x++)
                {
                    level.ApplyVote(new Vector2Int(x, row), true, 0.5f);
                }
            }

            return level;
        }

        /// <summary>Frames of a camera at a steady height.</summary>
        float? Update(FloorModel model, List<SurfaceLevel> levels, float cameraHeight, int frames)
        {
            float? found = null;
            for (var i = 0; i < frames; i++)
            {
                clock += 0.125;
                model.NoteCamera(cameraHeight, clock);
                found = model.Update(levels) ?? found;
            }

            return found;
        }

        [SetUp]
        public void SetUp()
        {
            clock = 0.0;
        }

        [Test]
        public void NoLevels_NoFloor()
        {
            var model = new FloorModel();

            Update(model, new List<SurfaceLevel>(), 1.4f, 5);

            Assert.AreEqual(FloorState.Unknown, model.State);
            Assert.IsNull(model.Level);
            Assert.IsFalse(model.IsTrusted);
        }

        [Test]
        public void DeskUnderACameraAtChestHeight_IsNeverTheFloor()
        {
            var model = new FloorModel();
            var desk = Level(0, 0.75f, planeArea: 1.75f, confirmedArea: 1.1f);

            Update(model, new List<SurfaceLevel> { desk }, 1.4f, 40);

            Assert.IsFalse(model.IsEligible(desk), "0.65 m below the camera is too close for a floor.");
            Assert.AreEqual(FloorState.Unknown, model.State);
            Assert.IsFalse(desk.IsFloor);
        }

        [Test]
        public void DeskUnderAHighCamera_IsACandidate_ButNeverTrusted()
        {
            var model = new FloorModel();
            var desk = Level(0, 0.75f, planeArea: 1.75f, confirmedArea: 1.1f);

            Update(model, new List<SurfaceLevel> { desk }, 1.7f, 40);

            Assert.AreEqual(FloorState.Candidate, model.State);
            Assert.AreSame(desk, model.Level);
            Assert.IsFalse(model.IsTrusted, "One desk is smaller than a floor has to be.");
        }

        [Test]
        public void DeskUnderAHighCamera_GivesWay_WhenTheFloorPlaneAppears()
        {
            var model = new FloorModel();
            var desk = Level(0, 0.75f, planeArea: 1.75f, confirmedArea: 1.1f);
            var levels = new List<SurfaceLevel> { desk };
            Update(model, levels, 1.7f, 20);

            var floor = Level(1, 0f, planeArea: 4f);
            levels.Add(floor);
            Update(model, levels, 1.7f, 20);

            Assert.AreSame(floor, model.Level);
            Assert.AreEqual(FloorState.Trusted, model.State);
            Assert.IsTrue(floor.IsFloor);
            Assert.IsFalse(desk.IsFloor);
            Assert.IsFalse(desk.IsSuppressed, "The desk lies above the floor: it stays a surface.");
        }

        [Test]
        public void Trust_NeedsTheSamePickForAWhile()
        {
            var model = new FloorModel();
            var levels = new List<SurfaceLevel> { Level(0, 0f, planeArea: 4f) };

            Update(model, levels, 1.4f, 5);
            Assert.AreEqual(FloorState.Candidate, model.State);

            Update(model, levels, 1.4f, FloorModel.TrustFrames);
            Assert.AreEqual(FloorState.Trusted, model.State);
        }

        [Test]
        public void Trust_NeedsTwoSquareMeters()
        {
            var model = new FloorModel();
            var levels = new List<SurfaceLevel> { Level(0, 0f, planeArea: 1.5f) };

            Update(model, levels, 1.4f, 60);

            Assert.AreEqual(FloorState.Candidate, model.State);
        }

        [Test]
        public void FloorKnownFromItsPlaneOnly_SaysSo()
        {
            var model = new FloorModel();
            var fromPlane = new List<SurfaceLevel> { Level(0, 0f, planeArea: 4f) };
            Update(model, fromPlane, 1.4f, 20);
            Assert.IsTrue(model.FromPlane);

            model.Reset();
            var fromDepth = new List<SurfaceLevel> { Level(1, 0f, planeArea: 4f, confirmedArea: 2f) };
            Update(model, fromDepth, 1.4f, 20);
            Assert.IsTrue(model.IsTrusted);
            Assert.IsFalse(model.FromPlane);
        }

        [Test]
        public void DriftedPlaneBelowTheFloor_IsNotPicked()
        {
            var model = new FloorModel();
            var floor = Level(0, 0f, planeArea: 6f, confirmedArea: 3f);
            var drifted = Level(1, -0.12f, planeArea: 1.5f);

            Update(model, new List<SurfaceLevel> { floor, drifted }, 1.4f, 20);

            Assert.AreSame(floor, model.Level);
            Assert.IsTrue(model.IsTrusted);
            Assert.IsTrue(drifted.IsSuppressed);
        }

        [Test]
        public void HigherLevel_IsOnlyPicked_WithTwiceTheSupport()
        {
            var model = new FloorModel();
            var lower = Level(0, -0.12f, planeArea: 4f);
            var upper = Level(1, 0f, planeArea: 6f);

            Update(model, new List<SurfaceLevel> { lower, upper }, 1.4f, 5);

            Assert.AreSame(lower, model.Level);
        }

        [Test]
        public void HigherLevel_MoreThanTwentyCentimetersUp_IsNotPickedInstead()
        {
            var model = new FloorModel();
            var lower = Level(0, -0.3f, planeArea: 1.5f);
            var upper = Level(1, 0f, planeArea: 8f, confirmedArea: 4f);

            Update(model, new List<SurfaceLevel> { lower, upper }, 1.4f, 5);

            Assert.AreSame(lower, model.Level, "A floor 30 cm lower is another floor (the room has a podium).");
        }

        [Test]
        public void TrustedFloor_HidesTheLevelsBelowIt()
        {
            var model = new FloorModel();
            var floor = Level(0, 0f, planeArea: 4f);
            var below = Level(1, -0.3f, confirmedArea: 0.3f);
            var justBelow = Level(2, -0.04f, confirmedArea: 0.3f);
            var desk = Level(3, 0.75f, confirmedArea: 0.8f);

            Update(model, new List<SurfaceLevel> { floor, below, justBelow, desk }, 1.4f, 20);

            Assert.IsTrue(model.IsTrusted);
            Assert.IsTrue(below.IsSuppressed);
            Assert.IsFalse(below.IsUsable);
            Assert.IsFalse(justBelow.IsSuppressed, "Within the noise of the floor.");
            Assert.IsFalse(desk.IsSuppressed);
            Assert.IsFalse(floor.IsSuppressed);
        }

        [Test]
        public void LargePlaneBelowATrustedFloor_MakesItACandidateAgain_AndShowsTheLevelsBelow()
        {
            var model = new FloorModel();
            var podium = Level(0, 0.3f, planeArea: 4f);
            var below = Level(1, 0f, confirmedArea: 0.3f);
            var levels = new List<SurfaceLevel> { podium, below };
            Update(model, levels, 1.7f, 20);
            Assert.IsTrue(model.IsTrusted);
            Assert.IsTrue(below.IsSuppressed);

            // ARCore detects the real floor beside the podium.
            for (var row = 0; row < 60; row++)
            {
                below.SetPriorRun(row, 0, RowCells - 1);
            }

            below.AddPlaneSample(0f, 3f);
            Update(model, levels, 1.7f, 1);

            Assert.AreEqual(FloorState.Candidate, model.State);
            Assert.IsFalse(below.IsSuppressed);

            Update(model, levels, 1.7f, 20);

            Assert.AreSame(below, model.Level);
            Assert.IsTrue(model.IsTrusted);
            Assert.IsFalse(podium.IsSuppressed, "The podium is a surface above the floor.");
        }

        [Test]
        public void PlaneBelowATrustedFloor_OnAPhoneWithDepth_NeedsDepthToBecomeTheFloor()
        {
            var model = new FloorModel { PlanesNeedDepth = true };
            var floor = Level(0, 0f, planeArea: 4f);
            var levels = new List<SurfaceLevel> { floor };
            Update(model, levels, 1.4f, 20);
            Assert.IsTrue(model.IsTrusted);

            // ARCore detects a plane on what the shiny floor mirrors.
            var mirrored = Level(1, -0.4f, planeArea: 3f);
            levels.Add(mirrored);
            Update(model, levels, 1.4f, 30);

            Assert.AreSame(floor, model.Level);
            Assert.IsTrue(model.IsTrusted);
            Assert.IsTrue(mirrored.IsSuppressed);

            // Depth confirms a surface there: the room has a lower floor.
            var lower = Level(2, -0.4f, planeArea: 3f, confirmedArea: 1.5f);
            levels[1] = lower;
            Update(model, levels, 1.4f, 30);

            Assert.AreSame(lower, model.Level);
        }

        [Test]
        public void DepthPointsBesideTheFloor_AtOneHeightBelowIt_ReportThatHeight()
        {
            var model = new FloorModel();
            var podium = Level(0, 0.3f, planeArea: 4f);
            var levels = new List<SurfaceLevel> { podium };
            Update(model, levels, 1.7f, 20);
            Assert.IsTrue(model.IsTrusted);

            for (var i = 0; i < 250; i++)
            {
                model.NoteBelow(0.005f * (i % 3), 1f);
            }

            var found = Update(model, levels, 1.7f, 1);

            Assert.IsTrue(found.HasValue);
            Assert.AreEqual(0f, found.Value, 0.02f);
            Assert.AreEqual(FloorState.Candidate, model.State);
        }

        [Test]
        public void FewDepthPointsBelowTheFloor_ChangeNothing()
        {
            var model = new FloorModel();
            var levels = new List<SurfaceLevel> { Level(0, 0f, planeArea: 4f) };
            Update(model, levels, 1.4f, 20);

            for (var i = 0; i < 50; i++)
            {
                model.NoteBelow(-0.4f, 1f);
            }

            var found = Update(model, levels, 1.4f, 1);

            Assert.IsFalse(found.HasValue);
            Assert.IsTrue(model.IsTrusted);
        }

        [Test]
        public void LevelSeenThroughTheFloor_IsAReflection_AndNotTheFloor()
        {
            var model = new FloorModel();
            var floor = Level(0, 0f, planeArea: 4f);
            var mirrored = Level(1, -0.4f, planeArea: 1.5f);
            for (var i = 0; i < 60; i++)
            {
                mirrored.NoteSeen(true, 1f);
            }

            Update(model, new List<SurfaceLevel> { floor, mirrored }, 1.4f, 20);

            Assert.IsTrue(mirrored.IsGhost);
            Assert.IsFalse(model.IsEligible(mirrored));
            Assert.AreSame(floor, model.Level);
            Assert.IsTrue(model.IsTrusted);
        }

        [Test]
        public void LevelSeenBesideTheFloor_IsNoReflection()
        {
            var model = new FloorModel();
            var floor = Level(0, 0.3f, planeArea: 4f);
            var lower = Level(1, 0f, planeArea: 1.5f);
            for (var i = 0; i < 60; i++)
            {
                lower.NoteSeen(i % 10 == 0, 1f);
            }

            Update(model, new List<SurfaceLevel> { floor, lower }, 1.7f, 3);

            Assert.IsFalse(lower.IsGhost);
        }

        [Test]
        public void Merge_KeepsTheFloorAndItsTrust()
        {
            var model = new FloorModel();
            var floor = Level(0, 0f, planeArea: 4f);
            var duplicate = Level(1, 0.05f, planeArea: 2f);
            var levels = new List<SurfaceLevel> { floor };
            Update(model, levels, 1.4f, 20);
            Assert.IsTrue(model.IsTrusted);

            duplicate.Absorb(floor);
            model.OnMerged(floor, duplicate);
            levels[0] = duplicate;
            Update(model, levels, 1.4f, 1);

            Assert.AreSame(duplicate, model.Level);
            Assert.IsTrue(duplicate.IsFloor);
            Assert.IsTrue(model.IsTrusted);
        }

        [Test]
        public void RemovedFloor_LeavesNoFloor()
        {
            var model = new FloorModel();
            var floor = Level(0, 0f, planeArea: 4f);
            Update(model, new List<SurfaceLevel> { floor }, 1.4f, 20);

            model.OnRemoved(floor);

            Assert.IsNull(model.Level);
            Assert.AreEqual(FloorState.Unknown, model.State);
        }

        [Test]
        public void ForceTrust_MakesAnyLevelTheFloor_UntilReset()
        {
            var model = new FloorModel();
            var small = Level(0, 0.2f, confirmedArea: 0.4f);
            var plane = Level(1, 0f, planeArea: 4f);
            var levels = new List<SurfaceLevel> { small, plane };

            model.ForceTrust(small);
            Update(model, levels, 1.6f, 30);

            Assert.AreSame(small, model.Level);
            Assert.IsTrue(model.IsTrusted);
            Assert.IsTrue(plane.IsSuppressed);

            model.Reset();
            Assert.IsNull(model.Level);
            Assert.AreEqual(FloorState.Unknown, model.State);
        }

        [Test]
        public void CameraHeight_IsWhereThePhoneIsUsuallyHeld()
        {
            var model = new FloorModel();
            for (var i = 0; i < 40; i++)
            {
                clock += 0.125;

                // Now and then the user bends down to look under a desk.
                model.NoteCamera(i % 8 == 0 ? 0.6f : 1.4f, clock);
            }

            Assert.AreEqual(1.4f, model.CameraHeight, 1e-4f);
            Assert.AreEqual(1.4f, model.CameraAbove(0f), 1e-4f);
        }

        [Test]
        public void CameraHeight_ForgetsOldFrames()
        {
            var model = new FloorModel();
            for (var i = 0; i < 40; i++)
            {
                clock += 0.125;
                model.NoteCamera(1.4f, clock);
            }

            // The user sits down and scans on.
            clock += FloorModel.CameraWindowSeconds;
            for (var i = 0; i < 8; i++)
            {
                clock += 0.125;
                model.NoteCamera(1.0f, clock);
            }

            Assert.AreEqual(1.0f, model.CameraHeight, 1e-4f);
        }

        [Test]
        public void StateChanged_IsRaisedOncePerChange()
        {
            var model = new FloorModel();
            var states = new List<FloorState>();
            model.StateChanged += states.Add;

            Update(model, new List<SurfaceLevel> { Level(0, 0f, planeArea: 4f) }, 1.4f, 30);

            CollectionAssert.AreEqual(new[] { FloorState.Candidate, FloorState.Trusted }, states);
        }
    }
}
